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

    private ShrinkSettings Settings(bool backup = true, Func<string, long>? free = null, string? backupRoot = null, string? choice = null) => new()
    {
        MetadataRoot = _meta,
        Choice = choice ?? (backup ? Shrinker.Keep : Shrinker.JustShrink),
        BackupRoot = backupRoot ?? _backup,
        FreeSpace = free ?? Shrinker.FreeSpaceOf
    };

    private Shrinker Make(ShrinkSettings s) => Make(() => s);

    private Shrinker Make(Func<ShrinkSettings> s) => new(s, new StateStore(Path.Combine(_dir, "state.json")), NullLogger.Instance);

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
        Assert.Equal("1.2", file.RulesVersion);
    }

    [Fact]
    public void SettingDefaultsComeFromTheRulesFile()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Here("rules.json")));
        var r = doc.RootElement;
        var c = new PluginConfiguration();
        var kinds = r.GetProperty("kinds");
        Assert.Equal(new[] { "poster", "person", "backdrop", "thumb", "logo", "banner", "disc", "clearart" }, kinds.EnumerateObject().Select(k => k.Name));
        Assert.Equal(Enum.GetNames<PictureKind>().Select(n => n.ToLowerInvariant()), kinds.EnumerateObject().Select(k => k.Name));
        var sizes = new Dictionary<string, int>
        {
            ["poster"] = c.PosterHeight, ["person"] = c.PersonWidth, ["backdrop"] = c.BackdropWidth, ["thumb"] = c.ThumbWidth,
            ["logo"] = c.LogoWidth, ["banner"] = c.BannerWidth, ["disc"] = c.DiscWidth, ["clearart"] = c.ClearartWidth
        };
        var ticks = new Dictionary<string, bool>
        {
            ["poster"] = c.IncludePosters, ["person"] = c.IncludePeople, ["backdrop"] = c.IncludeBackdrops, ["thumb"] = c.IncludeThumbs,
            ["logo"] = c.IncludeLogos, ["banner"] = c.IncludeBanners, ["disc"] = c.IncludeDiscs, ["clearart"] = c.IncludeClearart
        };
        var settings = new ShrinkSettings();
        foreach (var k in kinds.EnumerateObject())
        {
            Assert.Equal(k.Value.GetProperty("default").GetInt32(), sizes[k.Name]);
            Assert.Equal(k.Value.GetProperty("shrink_by_default").GetBoolean(), ticks[k.Name]);
            Assert.True(ticks[k.Name]); // every kind is on by default
            Assert.Equal(sizes[k.Name], settings.Limit(Enum.Parse<PictureKind>(k.Name, true)));
        }

        Assert.Equal((1920, 640, 3840), (c.BackdropWidth, Rules.Current.Backdrop.Min, Rules.Current.Backdrop.Max));
        Assert.Equal(r.GetProperty("shrink_all").GetProperty("default").GetBoolean(), c.ShrinkAll);
        Assert.True(c.ShrinkAll);
        Assert.True(settings.ShrinkAll);
        Assert.Equal(r.GetProperty("quality").GetProperty("default").GetInt32(), c.Quality);
        Assert.Equal(JsonValueKind.Null, r.GetProperty("backup").GetProperty("default").ValueKind);
        Assert.Equal(string.Empty, c.BackupChoice); // no default: the person chooses
        Assert.Equal(new[] { Shrinker.Keep, Shrinker.JustShrink }, Rules.Current.Backup.Choices);
        Assert.Equal(new[] { Shrinker.PicturesItShrinks, Shrinker.EveryPicture }, Rules.Current.StartAgain.Kinds);
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
    [InlineData(PictureKind.Backdrop, 3840, 2160, 1920, 1080)]
    [InlineData(PictureKind.Backdrop, 2560, 1067, 1920, 800)] // 800.25 rounds to 800
    [InlineData(PictureKind.Thumb, 1920, 1080, 1280, 720)]
    [InlineData(PictureKind.Logo, 1000, 388, 800, 310)] // 310.4 rounds to 310
    [InlineData(PictureKind.Banner, 1000, 185, 1000, 185)] // already 1000 wide: left alone
    [InlineData(PictureKind.Disc, 1000, 1000, 500, 500)]
    [InlineData(PictureKind.Clearart, 1000, 562, 800, 450)] // 449.6 rounds to 450
    public void SizesFollowTheRules(PictureKind kind, int w, int h, int ew, int eh)
        => Assert.Equal((ew, eh), Shrinker.Target(kind, w, h, new ShrinkSettings().Limit(kind)) ?? (w, h));

    [Theory]
    [InlineData("library/0a/0a1b/backdrop.jpg", PictureKind.Backdrop)]
    [InlineData("library/0a/0a1b/backdrop1.jpg", PictureKind.Backdrop)]
    [InlineData("library/0a/0a1b/backdrop12.webp", PictureKind.Backdrop)]
    [InlineData("library/0a/0a1b/poster.jpg", PictureKind.Poster)]
    [InlineData("library/0a/0a1b/landscape.jpg", PictureKind.Thumb)]
    [InlineData("library/0a/0a1b/logo.png", PictureKind.Logo)]
    [InlineData("library/0a/0a1b/banner.jpg", PictureKind.Banner)]
    [InlineData("library/0a/0a1b/disc.png", PictureKind.Disc)]
    [InlineData("library/0a/0a1b/clearart.png", PictureKind.Clearart)]
    [InlineData("People/A/Alice Example/folder.jpg", PictureKind.Person)]
    public void EachKindIsFound(string rel, PictureKind kind)
        => Assert.Equal(kind, Shrinker.KindOf(_meta, Path.Combine(_meta, rel.Replace('/', Path.DirectorySeparatorChar))));

    [Theory]
    [InlineData("library/0a/0a1b/folder.jpg")]
    [InlineData("library/0a/0a1b/.backdrop.jpg")]
    [InlineData("library/0a/0a1b/backdrop.gif")]
    [InlineData("collections/x/backdrop.jpg")]
    [InlineData("library/0a/0a1b/backdrop.jpg.picture-shrinker-tmp")]
    public void OtherFilesAreLeftAlone(string rel)
        => Assert.Null(Shrinker.KindOf(_meta, Path.Combine(_meta, rel.Replace('/', Path.DirectorySeparatorChar))));

    [Fact]
    public void ShrinkAllShrinksEveryKindWhateverTheTicks()
    {
        var path = BigBackdrop();
        var noneTicked = new ShrinkSettings
        {
            MetadataRoot = _meta, Choice = Shrinker.Keep, BackupRoot = _backup, ShrinkAll = true,
            Posters = false, People = false, Backdrops = false, Thumbs = false, Logos = false, Banners = false, Discs = false, Clearart = false
        };
        var rec = Make(noneTicked).Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!;
        Assert.Equal(11, rec.Shrunk); // the ten shared pictures and the big backdrop
        Assert.Equal(1920, rec.Sizes["backdrop"]);
        using var codec = SKCodec.Create(path);
        Assert.Equal((1920, 1080), (codec.Info.Width, codec.Info.Height));
    }

    [Fact]
    public void ABackdropIsShrunkToTheWidthWhenTickedAndLeftAloneWhenNot()
    {
        var path = BigBackdrop();
        var before = File.ReadAllBytes(path);
        var off = new ShrinkSettings { MetadataRoot = _meta, Choice = Shrinker.Keep, BackupRoot = _backup, ShrinkAll = false, Backdrops = false };
        var rec = Make(off).Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!;
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(9, rec.Shrunk); // every other kind is still ticked; neither backdrop is shrunk
        Assert.False(File.Exists(Path.Combine(_backup, "library", "0a", "0a1b", "backdrop1.jpg")));

        var on = new ShrinkSettings { MetadataRoot = _meta, Choice = Shrinker.Keep, BackupRoot = _backup, ShrinkAll = false, Backdrops = true };
        Assert.Equal(2, Make(on).Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!.Shrunk); // both backdrops
        using var codec = SKCodec.Create(path);
        Assert.Equal((1920, 1080), (codec.Info.Width, codec.Info.Height));
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_backup, "library", "0a", "0a1b", "backdrop1.jpg")));
    }

    [Fact]
    public void WithShrinkAllOffOnlyTheTickedKindsAreShrunk()
    {
        var onlyLogos = new ShrinkSettings
        {
            MetadataRoot = _meta, Choice = Shrinker.JustShrink, ShrinkAll = false,
            Posters = false, People = false, Backdrops = false, Thumbs = false, Logos = true, Banners = false, Discs = false, Clearart = false
        };
        var rec = Make(onlyLogos).Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!;
        Assert.Equal(1, rec.Shrunk);
        using var codec = SKCodec.Create(Path.Combine(_meta, "library", "1c", "1c2d", "logo.png"));
        Assert.Equal((800, 267), (codec.Info.Width, codec.Info.Height));
    }

    [Fact]
    public void APngLogoKeepsItsTransparency()
    {
        var path = Path.Combine(_meta, "library", "1c", "1c2d", "logo.png");
        Assert.Equal(Outcome.Shrunk, Shrinker.ShrinkOne(Settings(backup: false), path, PictureKind.Logo).Outcome);
        using var bitmap = SKBitmap.Decode(path);
        Assert.Equal((800, 267), (bitmap.Width, bitmap.Height));
        Assert.Equal(0, bitmap.GetPixel(20, 130).Alpha); // the left quarter is fully transparent
        Assert.Equal(255, bitmap.GetPixel(780, 130).Alpha); // the right is opaque
    }

    private string BigBackdrop()
    {
        var path = Path.Combine(_meta, "library", "0a", "0a1b", "backdrop1.jpg");
        using var bitmap = new SKBitmap(3840, 2160);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(3840, 2160), new[] { SKColors.SteelBlue, SKColors.Orange }, SKShaderTileMode.Clamp) };
        canvas.DrawRect(0, 0, 3840, 2160, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Fact]
    public void SmallPicturesAreLeftAlone()
    {
        Assert.Null(Shrinker.Target(PictureKind.Poster, 2000, 900, 900));
        Assert.Null(Shrinker.Target(PictureKind.Person, 400, 3000, 400));
        Assert.Null(Shrinker.Target(PictureKind.Backdrop, 1920, 4000, 1920));
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
        Assert.Equal(10, rec.NotBackedUp);
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
        Assert.Equal(10, rec.Restored);
        Assert.Equal(original, Hashes(_meta));
        Assert.Equal(0, shrinker.Measure().Saved); // nothing is saved once every picture is back
        Assert.Equal(0, shrinker.Measure().ShrunkTotal);
        Assert.Equal(10, shrinker.Restore(CancellationToken.None)!.AlreadyOriginal); // the backup is kept
    }

    [Fact]
    public void JustShrinkShrinksWithoutBackup()
    {
        var rec = Make(Settings(backup: false)).Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!;
        Assert.Equal(10, rec.Shrunk);
        Assert.False(Directory.Exists(_backup));
    }

    [Fact]
    public void KeepBacksUpEachOriginal()
    {
        var rec = Make(Settings(choice: Shrinker.Keep)).Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!;
        Assert.Equal(10, rec.Shrunk);
        Assert.Equal(10, rec.BackedUp);
        Assert.Equal(10, Directory.GetFiles(_backup, "*", SearchOption.AllDirectories).Length);
    }

    // The backup choice ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("on")]
    public void NothingIsShrunkUntilTheChoiceIsMade(string choice)
    {
        var before = Hashes(_meta);
        var shrinker = Make(Settings(choice: choice));
        var rec = shrinker.Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!;
        Assert.Equal("Not shrunk: choose Keep a backup copy or Just shrink, in its settings, first", rec.Stopped);
        Assert.Equal(0, rec.Shrunk);
        var only = Directory.GetFiles(Path.Combine(_meta, "library"), "poster.*", SearchOption.AllDirectories);
        Assert.Equal(Shrinker.ChooseFirst, shrinker.Run("new", TimeSpan.FromMinutes(5), CancellationToken.None, null, only)!.Stopped);
        Assert.Equal(before, Hashes(_meta));
        Assert.False(Directory.Exists(_backup));
        Assert.Empty(shrinker.Store.Load().Runs); // it does nothing, and does not fail
    }

    [Fact]
    public void RestoreAndDeleteBackupWorkBeforeTheChoiceIsMade()
    {
        var original = Hashes(_meta);
        var s = Settings(choice: Shrinker.Keep);
        var shrinker = Make(() => s);
        shrinker.Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None);
        s = Settings(choice: string.Empty);
        Assert.Equal(10, shrinker.Restore(CancellationToken.None)!.Restored);
        Assert.Equal(original, Hashes(_meta));
        Assert.True(shrinker.DeleteBackup().Done);
        Assert.Equal(0, shrinker.Measure().BackupCount);
    }

    [Fact]
    public void TheUpdateFrom11DropsTheOldSettingAndKeepsTheBackup()
    {
        // A 1.1 settings file, with the old on and off backup setting activated.
        const string old = """
            <?xml version="1.0" encoding="utf-8"?>
            <PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <PosterHeight>800</PosterHeight>
              <PersonWidth>400</PersonWidth>
              <Quality>85</Quality>
              <IncludePosters>true</IncludePosters>
              <IncludePeople>true</IncludePeople>
              <Backup>true</Backup>
              <BackupFolder>/srv/originals</BackupFolder>
              <ShrinkNewPictures>false</ShrinkNewPictures>
              <NewPictureMinutes>10</NewPictureMinutes>
              <OnceAMonth>false</OnceAMonth>
            </PluginConfiguration>
            """;
        var xml = new System.Xml.Serialization.XmlSerializer(typeof(PluginConfiguration));
        var c = (PluginConfiguration)xml.Deserialize(new StringReader(old))!;
        Assert.Equal(string.Empty, c.BackupChoice); // unset, so the person chooses again
        Assert.Equal("/srv/originals", c.BackupFolder);
        Assert.Equal(800, c.PosterHeight);
        var saved = new StringWriter();
        xml.Serialize(saved, c);
        Assert.DoesNotContain("<Backup>", saved.ToString(), StringComparison.Ordinal);

        // The backup made by 1.1 is kept while the choice is unset, and whatever is chosen.
        var s = Settings(choice: Shrinker.Keep);
        var shrinker = Make(() => s);
        shrinker.Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None);
        var backup = Hashes(_backup);
        s = Settings(choice: string.Empty);
        shrinker.Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(backup, Hashes(_backup));
        s = Settings(choice: Shrinker.JustShrink);
        shrinker.Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(backup, Hashes(_backup));
        Assert.Equal(10, shrinker.Measure().BackupCount);
    }

    // Space and deleting the backup ------------------------------------------------------------------------------

    [Fact]
    public void SpaceIsMeasured()
    {
        var shrinker = Make(Settings());
        var start = shrinker.Measure();
        Assert.Equal(14, start.LiveCount); // every kind under library and People, failed ones included
        Assert.Equal(0, start.BackupCount);
        var rec = shrinker.Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!;
        var end = shrinker.Measure();
        Assert.Equal(10, end.BackupCount);
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
        Assert.Equal(11, result.Files); // ten originals and the link itself
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

        Assert.Equal(10, shrinker.Measure().BackupCount);
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
        // notifications or session messages. The Activity list is used only by the reminder to choose a backup
        // option, before anything is shrunk (1.2.2.0 and 1.2.3.0); a finished run writes nothing there.
        var banned = new[] { "Notification", "IActivityManager", "ISessionManager", "IUserDataManager" };
        var asm = typeof(Shrinker).Assembly;
        foreach (var type in asm.GetTypes().Where(t => t != typeof(ChoiceReminder)))
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
