using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.PictureShrinker.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.PictureShrinker.Tests;

public sealed class ShrinkerTests : IDisposable
{
    private readonly string _dir;
    private readonly string _meta;
    private readonly string _backup;

    public ShrinkerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ps-test-" + Guid.NewGuid().ToString("N"));
        _meta = Path.Combine(_dir, "metadata");
        _backup = Path.Combine(_dir, "data", "picture-shrinker", "originals");
        Copy(Path.Combine(AppContext.BaseDirectory, "pictures", "metadata"), _meta);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private static string Here(string name) => Path.Combine(AppContext.BaseDirectory, name);

    private static void Copy(string from, string to)
    {
        foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var t = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(t)!);
            File.Copy(f, t);
        }
    }

    private ShrinkSettings Settings(bool backup = true, Func<string, long>? free = null, string? backupRoot = null) => new()
    {
        MetadataRoot = _meta,
        Backup = backup,
        BackupRoot = backupRoot ?? _backup,
        FreeSpace = free ?? Shrinker.FreeSpaceOf
    };

    private Shrinker Make(ShrinkSettings s) => new(() => s, new StateStore(Path.Combine(_dir, "state.json")), NullLogger.Instance);

    private static Dictionary<string, string> Hashes(string root)
        => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    // The build -------------------------------------------------------------------------------------------------

    [Fact]
    public void BuiltForTheRightJellyfin()
    {
        // The 10.11 build is .NET 9 against Jellyfin 10.11 and SkiaSharp 3.116; the 12 build .NET 10 against Jellyfin 12
        // and SkiaSharp 3.119, the versions each Jellyfin loads. An even third version number is 10.11, odd is 12.
        var plugin = typeof(Plugin).Assembly;
        var refs = plugin.GetReferencedAssemblies().ToDictionary(a => a.Name!, a => a.Version!);
#if JELLYFIN_10_11
        (int net, int jellyfin, int skiaMinor, int parity) = (9, 10, 116, 0);
#else
        (int net, int jellyfin, int skiaMinor, int parity) = (10, 12, 119, 1);
#endif
        Assert.Equal(net, Environment.Version.Major);
        Assert.Equal(jellyfin, refs["MediaBrowser.Controller"].Major);
        Assert.Equal(jellyfin, refs["MediaBrowser.Common"].Major);
        Assert.Equal(skiaMinor, typeof(SKBitmap).Assembly.GetName().Version!.Minor);
        Assert.Equal(parity, plugin.GetName().Version!.Build % 2);
    }

    // The rules file ---------------------------------------------------------------------------------------------

    [Fact]
    public void BuiltInRulesAreTheRulesFile()
    {
        var file = Rules.Parse(File.ReadAllText(Here("rules.json")));
        Assert.Equal(JsonSerializer.Serialize(file), JsonSerializer.Serialize(Rules.Current));
        Assert.Equal("1.1", file.RulesVersion);
    }

    [Fact]
    public void SettingDefaultsComeFromTheRulesFile()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Here("rules.json")));
        var r = doc.RootElement;
        var c = new PluginConfiguration();
        Assert.Equal(r.GetProperty("kinds").GetProperty("poster").GetProperty("default").GetInt32(), c.PosterHeight);
        Assert.Equal(r.GetProperty("kinds").GetProperty("person").GetProperty("default").GetInt32(), c.PersonWidth);
        Assert.Equal(r.GetProperty("quality").GetProperty("default").GetInt32(), c.Quality);
        Assert.Equal(r.GetProperty("backup").GetProperty("default").GetBoolean(), c.Backup);
        Assert.Equal(r.GetProperty("new_pictures").GetProperty("default").GetBoolean(), c.ShrinkNewPictures);
        Assert.Equal(r.GetProperty("new_pictures").GetProperty("min_age_seconds").GetInt32() / 60, c.NewPictureMinutes);
        Assert.Equal(115, r.GetProperty("run_limit_minutes").GetInt32());
    }

    [Theory]
    [InlineData(PictureKind.Poster, 1000, 1500, 600, 900)]
    [InlineData(PictureKind.Poster, 1001, 1800, 500, 900)] // 500.5 rounds half to even, as Python's round does
    [InlineData(PictureKind.Poster, 1003, 1800, 502, 900)] // 501.5 rounds to 502
    [InlineData(PictureKind.Person, 800, 1201, 400, 600)] // 600.5 rounds to 600
    [InlineData(PictureKind.Person, 1000, 1000, 400, 400)]
    public void SizesFollowTheRules(PictureKind kind, int w, int h, int ew, int eh)
        => Assert.Equal((ew, eh), Shrinker.Target(kind, w, h, 900, 400));

    [Fact]
    public void SmallPicturesAreLeftAlone()
    {
        Assert.Null(Shrinker.Target(PictureKind.Poster, 2000, 900, 900, 400));
        Assert.Null(Shrinker.Target(PictureKind.Person, 400, 3000, 900, 400));
    }

    // The shared test pictures -----------------------------------------------------------------------------------

    [Fact]
    public void SharedPicturesGiveTheExpectedResults()
    {
        var before = Hashes(_meta);
        var sizes = Directory.GetFiles(_meta, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(_meta, f), f => new FileInfo(f).Length);
        var rec = Make(Settings()).Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!;
        using var doc = JsonDocument.Parse(File.ReadAllText(Here(Path.Combine("pictures", "expected.json"))));
        var after = Hashes(_meta);
        var failed = rec.Failures.Select(f => f.File.Replace('\\', '/')).ToHashSet();
        var shrunk = 0;
        foreach (var p in doc.RootElement.GetProperty("pictures").EnumerateObject())
        {
            var rel = p.Name.Replace('/', Path.DirectorySeparatorChar);
            var e = p.Value;
            var outcome = e.GetProperty("outcome").GetString();
            var path = Path.Combine(_meta, rel);
            if (outcome == "shrunk")
            {
                shrunk++;
                using var codec = SKCodec.Create(path);
                Assert.True(codec is not null, p.Name);
                Assert.Equal(e.GetProperty("width").GetInt32(), codec!.Info.Width);
                Assert.Equal(e.GetProperty("height").GetInt32(), codec.Info.Height);
                Assert.Equal(e.GetProperty("format").GetString(), codec.EncodedFormat.ToString().ToUpperInvariant());
                Assert.True(new FileInfo(path).Length < sizes[rel], p.Name + " is smaller");
                Assert.True(File.Exists(Path.Combine(_backup, rel)), p.Name + " is backed up");
            }
            else
            {
                Assert.True(before[rel] == after[rel], p.Name + " is unchanged");
                Assert.Equal(outcome == "failed", failed.Contains(p.Name));
                Assert.False(File.Exists(Path.Combine(_backup, rel)), p.Name + " has no backup");
            }
        }

        Assert.Equal(shrunk, rec.Shrunk);
        Assert.Equal(2, rec.Failed);
        Assert.Equal(2, rec.Left);
        Assert.Empty(Directory.GetFiles(_dir, "*" + Rules.Current.TempSuffix, SearchOption.AllDirectories));

        var again = Make(Settings()).Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!;
        Assert.Equal(0, again.Shrunk); // pictures already shrunk are only opened
    }

    [Fact]
    public void ModeIsKept()
    {
        var path = Path.Combine(_meta, "library", "0a", "0a1b", "poster.jpg");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        var got = Shrinker.ShrinkOne(Settings(), path, PictureKind.Poster);
        Assert.Equal(Outcome.Shrunk, got.Outcome);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead, File.GetUnixFileMode(path));
    }

    // Backup and restore -----------------------------------------------------------------------------------------

    [Fact]
    public void NoBackupMeansNoReplace()
    {
        var blocker = Path.Combine(_dir, "a-file");
        File.WriteAllText(blocker, "not a folder");
        var before = Hashes(_meta);
        var rec = Make(Settings(backupRoot: Path.Combine(blocker, "originals"))).Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!;
        Assert.Equal(0, rec.Shrunk);
        Assert.Equal(7, rec.NotBackedUp);
        Assert.Contains("could not be backed up", rec.Stopped);
        Assert.Equal(before, Hashes(_meta));
    }

    [Fact]
    public void ShortOfSpaceMeansNoReplace()
    {
        var before = Hashes(_meta);
        var rec = Make(Settings(free: _ => 1000)).Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!;
        Assert.Equal(0, rec.Shrunk);
        Assert.Contains(rec.Failures, f => f.Why.Contains("backup disk has only", StringComparison.Ordinal));
        Assert.Equal(before, Hashes(_meta));
    }

    [Fact]
    public void AnExistingBackupIsNeverOverwritten()
    {
        var rel = Path.Combine("library", "0a", "0a1b", "poster.jpg");
        var copy = Path.Combine(_backup, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.WriteAllText(copy, "the first original");
        var got = Shrinker.ShrinkOne(Settings(), Path.Combine(_meta, rel), PictureKind.Poster);
        Assert.Equal(Outcome.Shrunk, got.Outcome);
        Assert.Equal("the first original", File.ReadAllText(copy));
    }

    [Fact]
    public void RestorePutsBackByteIdenticalFiles()
    {
        var original = Hashes(_meta);
        var shrinker = Make(Settings());
        Assert.True(shrinker.Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!.Shrunk > 0);
        Assert.NotEqual(original, Hashes(_meta));
        var rec = shrinker.Restore(CancellationToken.None)!;
        Assert.Equal(7, rec.Restored);
        Assert.Equal(original, Hashes(_meta));
        Assert.Equal(0, shrinker.Measure().Saved); // nothing is saved once every picture is back
        Assert.Equal(0, shrinker.Measure().ShrunkTotal);
        Assert.Equal(7, shrinker.Restore(CancellationToken.None)!.AlreadyOriginal); // the backup is kept
    }

    [Fact]
    public void BackupDeactivatedShrinksWithoutBackup()
    {
        var rec = Make(Settings(backup: false)).Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!;
        Assert.Equal(7, rec.Shrunk);
        Assert.False(Directory.Exists(_backup));
    }

    // Space and deleting the backup ------------------------------------------------------------------------------

    [Fact]
    public void SpaceIsMeasured()
    {
        var shrinker = Make(Settings());
        var start = shrinker.Measure();
        Assert.Equal(11, start.LiveCount); // the posters and photos under library and People, failed ones included
        Assert.Equal(0, start.BackupCount);
        var rec = shrinker.Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!;
        var end = shrinker.Measure();
        Assert.Equal(7, end.BackupCount);
        Assert.Equal(rec.Before, end.BackupBytes);
        Assert.Equal(rec.Before - rec.After, end.Saved);
        Assert.Equal(start.LiveBytes - end.Saved, end.LiveBytes);
    }

    [Fact]
    public void DeleteBackupDeletesOnlyInsideTheBackupFolder()
    {
        var shrinker = Make(Settings());
        shrinker.Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None);
        var size = shrinker.Measure().BackupBytes;
        var outside = Path.Combine(_dir, "keep-me.jpg");
        File.WriteAllText(outside, "outside");
        var notOurs = Path.Combine(_backup, "notes.txt");
        File.WriteAllText(notOurs, "not a picture the shrinker made");
        File.CreateSymbolicLink(Path.Combine(_backup, "library", "link.jpg"), outside);
        Directory.CreateSymbolicLink(Path.Combine(_backup, "People", "linked-folder"), _meta);
        var live = Hashes(_meta);

        var result = shrinker.DeleteBackup();

        Assert.True(result.Done);
        Assert.Equal(8, result.Files); // seven originals and the link itself
        Assert.Equal(size, result.Bytes);
        Assert.Equal("outside", File.ReadAllText(outside));
        Assert.True(File.Exists(notOurs));
        Assert.Equal(live, Hashes(_meta)); // the linked folder was not followed
        Assert.False(Directory.Exists(Path.Combine(_backup, "library", "0a")));
        Assert.Equal(0, shrinker.Measure().BackupBytes - new FileInfo(notOurs).Length);
    }

    [Fact]
    public void DeleteBackupIsRefusedDuringARun()
    {
        var shrinker = Make(Settings());
        shrinker.Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None);
        using (shrinker.Hold())
        {
            var result = shrinker.DeleteBackup();
            Assert.False(result.Done);
            Assert.Contains("running", result.Said);
        }

        Assert.Equal(7, shrinker.Measure().BackupCount);
    }

    [Fact]
    public void DeleteBackupRefusesAFolderHoldingTheMetadata()
    {
        var result = Make(Settings(backupRoot: _dir)).DeleteBackup();
        Assert.False(result.Done);
    }

    // Quiet runs -------------------------------------------------------------------------------------------------

    [Fact]
    public void AFinishedRunRaisesNoNotification()
    {
        // The only result of a run is the figures on the settings page: the plug-in never uses Jellyfin's
        // notifications, activity log or session messages.
        var banned = new[] { "Notification", "IActivityManager", "ISessionManager", "IUserDataManager" };
        var asm = typeof(Shrinker).Assembly;
        foreach (var type in asm.GetTypes())
        {
            var used = type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType.FullName ?? string.Empty)
                .Concat(type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Select(f => f.FieldType.FullName ?? string.Empty));
            foreach (var name in used)
            {
                Assert.DoesNotContain(banned, b => name.Contains(b, StringComparison.Ordinal));
            }
        }

        Assert.DoesNotContain(asm.GetReferencedAssemblies(), a => a.Name!.Contains("Notification", StringComparison.Ordinal));
    }
}
