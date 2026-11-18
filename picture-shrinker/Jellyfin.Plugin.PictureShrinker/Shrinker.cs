using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>A picture the shrinker looks after: one of the kinds in rules.json, in the same order, by the same name.</summary>
public enum PictureKind
{
    Poster,
    Person,
    Backdrop,
    Thumb,
    Logo,
    Banner,
    Disc,
    Clearart
}

/// <summary>What happened to one picture.</summary>
public enum Outcome
{
    /// <summary>Replaced by a smaller copy that passed every check.</summary>
    Shrunk,

    /// <summary>Already no bigger than the sizes allow: only its header was read.</summary>
    SmallEnough,

    /// <summary>The smaller copy was not smaller in bytes, so the original was kept.</summary>
    NotSmaller,

    /// <summary>It could not be read, or the copy failed a check: the original was kept.</summary>
    Failed,

    /// <summary>Its original could not be backed up, so it was not shrunk.</summary>
    BackupFailed
}

/// <summary>The settings of one run, already checked against the rules.</summary>
public sealed class ShrinkSettings
{
    public string MetadataRoot { get; init; } = string.Empty;

    public int PosterHeight { get; init; } = Rules.Current.Poster.Default;

    public int PersonWidth { get; init; } = Rules.Current.Person.Default;

    public int BackdropWidth { get; init; } = Rules.Current.Backdrop.Default;

    public int ThumbWidth { get; init; } = Rules.Current.Thumb.Default;

    public int LogoWidth { get; init; } = Rules.Current.Logo.Default;

    public int BannerWidth { get; init; } = Rules.Current.Banner.Default;

    public int DiscWidth { get; init; } = Rules.Current.Disc.Default;

    public int ClearartWidth { get; init; } = Rules.Current.Clearart.Default;

    public int Quality { get; init; } = Rules.Current.Quality.Default;

    /// <summary>Gets a value indicating whether every kind is shrunk, whatever the ticks for each kind say.</summary>
    public bool ShrinkAll { get; init; } = Rules.Current.ShrinkAll.Default;

    public bool Posters { get; init; } = Rules.Current.Poster.ShrinkByDefault;

    public bool People { get; init; } = Rules.Current.Person.ShrinkByDefault;

    public bool Backdrops { get; init; } = Rules.Current.Backdrop.ShrinkByDefault;

    public bool Thumbs { get; init; } = Rules.Current.Thumb.ShrinkByDefault;

    public bool Logos { get; init; } = Rules.Current.Logo.ShrinkByDefault;

    public bool Banners { get; init; } = Rules.Current.Banner.ShrinkByDefault;

    public bool Discs { get; init; } = Rules.Current.Disc.ShrinkByDefault;

    public bool Clearart { get; init; } = Rules.Current.Clearart.ShrinkByDefault;

    /// <summary>Whether a kind of picture is shrunk: always with Shrink all picture types, otherwise by its own tick.</summary>
    public bool Wants(PictureKind kind) => ShrinkAll || kind switch
    {
        PictureKind.Poster => Posters,
        PictureKind.Person => People,
        PictureKind.Backdrop => Backdrops,
        PictureKind.Thumb => Thumbs,
        PictureKind.Logo => Logos,
        PictureKind.Banner => Banners,
        PictureKind.Disc => Discs,
        _ => Clearart
    };

    /// <summary>The size a kind of picture is shrunk to: the height for posters, the width for every other kind.</summary>
    public int Limit(PictureKind kind) => kind switch
    {
        PictureKind.Poster => PosterHeight,
        PictureKind.Person => PersonWidth,
        PictureKind.Backdrop => BackdropWidth,
        PictureKind.Thumb => ThumbWidth,
        PictureKind.Logo => LogoWidth,
        PictureKind.Banner => BannerWidth,
        PictureKind.Disc => DiscWidth,
        _ => ClearartWidth
    };

    /// <summary>Gets the backup choice: "keep", "none", or empty while the person has not chosen.</summary>
    public string Choice { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether each original is backed up before it is replaced.</summary>
    public bool Backup => Choice == Shrinker.Keep;

    /// <summary>Gets a value indicating whether the person has chosen Keep a backup copy or Shrink without a backup.</summary>
    public bool Chosen => Choice is Shrinker.Keep or Shrinker.JustShrink;

    public string BackupRoot { get; init; } = string.Empty;

    /// <summary>Gets the free space of the disk holding a folder; replaced in tests.</summary>
    public Func<string, long> FreeSpace { get; init; } = Shrinker.FreeSpaceOf;

    /// <summary>Why the shrinker cannot run with these settings, or null when it can.</summary>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(MetadataRoot) || !Directory.Exists(MetadataRoot))
        {
            return "Jellyfin's metadata folder is not there";
        }

        if (Backup)
        {
            if (string.IsNullOrWhiteSpace(BackupRoot) || !Path.IsPathRooted(BackupRoot))
            {
                return "The backup folder must be a full path, such as /config/data/picture-shrinker/originals";
            }

            var backup = Shrinker.Full(BackupRoot);
            foreach (var kind in Rules.Current.Kinds.Values)
            {
                var pictures = Shrinker.Full(Path.Combine(MetadataRoot, kind.Folder));
                if (Shrinker.IsInside(backup, pictures) || Shrinker.IsInside(pictures, backup))
                {
                    return "The backup folder cannot be inside the folders of pictures it shrinks";
                }
            }
        }

        return null;
    }
}

/// <summary>The result of one picture.</summary>
public sealed record PictureResult(Outcome Outcome, long Before, long After, string? Why = null, long BackedUpBytes = 0, int Width = 0, int Height = 0, string? Format = null);

/// <summary>A picture that could not be shrunk, and why.</summary>
public sealed class Failure
{
    public string File { get; set; } = string.Empty;

    public string Why { get; set; } = string.Empty;
}

/// <summary>One run, kept in the state file for the settings page.</summary>
public sealed class RunRecord
{
    public string Kind { get; set; } = string.Empty;

    public DateTimeOffset Start { get; set; }

    public DateTimeOffset? End { get; set; }

    public int Seen { get; set; }

    public int Shrunk { get; set; }

    public int Left { get; set; }

    public int Failed { get; set; }

    public int NotBackedUp { get; set; }

    public long Before { get; set; }

    public long After { get; set; }

    public int BackedUp { get; set; }

    public long BackedUpBytes { get; set; }

    public string Stopped { get; set; } = string.Empty;

    public int PosterHeight { get; set; }

    public int PersonWidth { get; set; }

    /// <summary>Gets or sets the size of each kind for this run, by its name in rules.json.</summary>
    public Dictionary<string, int> Sizes { get; set; } = new();

    public List<Failure> Failures { get; set; } = new();
}

/// <summary>What a restore put back.</summary>
public sealed class RestoreRecord
{
    public int Restored { get; set; }

    /// <summary>Gets or sets the size the restored pictures had while shrunk, taken off the space saved.</summary>
    public long ShrunkBytes { get; set; }

    public long Bytes { get; set; }

    public int AlreadyOriginal { get; set; }

    public int NoLongerThere { get; set; }

    public int Failed { get; set; }

    public string Stopped { get; set; } = string.Empty;
}

/// <summary>The live pictures, the backup and the space saved.</summary>
public sealed class SpaceInfo
{
    public int LiveCount { get; set; }

    public long LiveBytes { get; set; }

    public int BackupCount { get; set; }

    public long BackupBytes { get; set; }

    public long Saved { get; set; }

    public int ShrunkTotal { get; set; }

    public DateTimeOffset Measured { get; set; }
}

/// <summary>What deleting the backup did.</summary>
public sealed record DeleteResult(bool Done, int Files, long Bytes, string Said);

/// <summary>What Start again did, and what it says.</summary>
public sealed record StartAgainResult(bool Done, string Said, StartAgainRecord? Record = null);

/// <summary>What one check of the pictures moved aside did.</summary>
public sealed record SettleResult(int Replaced, int PutBack, int Waiting);

/// <summary>A film, series, season or collection (by id), or a person (by name), whose pictures Jellyfin refreshes.</summary>
public sealed record RefreshTarget(Guid Item, string? Person)
{
    public override string ToString() => Person ?? Item.ToString("N");
}

/// <summary>
/// Asks Jellyfin to download pictures again. The plug-in's version queues Jellyfin refreshes (JellyfinRefresher); the
/// tests use a stand-in that records them.
/// </summary>
public interface IPictureRefresher
{
    /// <summary>The item id of each library.</summary>
    IReadOnlyList<Guid> Libraries();

    /// <summary>Queues a refresh of an item and everything in it. False when it is no longer in Jellyfin.</summary>
    bool RefreshItem(Guid id, bool replaceAllImages);

    /// <summary>Queues a refresh of a person. False when Jellyfin has no person of that name.</summary>
    bool RefreshPerson(string name, bool replaceAllImages);
}

/// <summary>
/// Shrinks Jellyfin's posters and photos of people by the rules in rules.json. Nothing here depends on Jellyfin, so
/// the tests run it on a folder of sample pictures.
/// </summary>
public sealed class Shrinker
{
    /// <summary>The backup choice Keep a backup copy of each original.</summary>
    public const string Keep = "keep";

    /// <summary>The backup choice Shrink without a backup.</summary>
    public const string JustShrink = "none";

    /// <summary>Start again: the pictures it shrinks (posters, backdrops and photos of people, of every kind whatever is ticked).</summary>
    public const string PicturesItShrinks = "posters_and_photos";

    /// <summary>Start again for every picture of every film, series, season, collection and person.</summary>
    public const string EveryPicture = "every_picture";

    /// <summary>What a run, Shrink now and the new-picture watcher say while the backup choice is not made.</summary>
    public const string ChooseFirst = "Not shrunk: choose Keep a backup copy or Shrink without a backup, in its settings, first";

    private static readonly string[] _otherPictureExtensions = { ".gif", ".bmp" };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _log;
    private readonly Func<ShrinkSettings> _settings;

    public Shrinker(Func<ShrinkSettings> settings, StateStore store, ILogger log)
    {
        _settings = settings;
        Store = store;
        _log = log;
    }

    public StateStore Store { get; }

    /// <summary>Gets what a run has done so far, while one is going.</summary>
    public RunRecord? Progress { get; private set; }

    /// <summary>Gets what is running now: "", "shrink", "new", "restore" or "delete".</summary>
    public string Busy { get; private set; } = string.Empty;

    public bool Running => _gate.CurrentCount == 0;

    /// <summary>Holds the shrinker as a run would, for tests.</summary>
    internal IDisposable Hold(string busy = "shrink")
    {
        _gate.Wait();
        Busy = busy;
        return new Release(this);
    }

    private sealed class Release(Shrinker shrinker) : IDisposable
    {
        public void Dispose()
        {
            shrinker.Busy = string.Empty;
            shrinker._gate.Release();
        }
    }

    public ShrinkSettings Settings => _settings();

    /// <summary>Gets or sets the clock; replaced in tests.</summary>
    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;

    /// <summary>Gets or sets the pause between two refreshes; replaced in tests.</summary>
    internal Action<TimeSpan> Pause { get; set; } = Thread.Sleep;

    /// <summary>What is running, in words, for a refusal: "a run", "a restore" and so on.</summary>
    public static string BusyWords(string busy) => busy switch
    {
        "shrink" => "a run",
        "new" => "a run for new pictures",
        "restore" => "a restore",
        "delete" => "deleting the backup",
        "start-again" => "another Start again",
        "settle" => "a check of the pictures waiting for Jellyfin",
        _ => "something else"
    };

    // Rules ----------------------------------------------------------------------------------------------------

    /// <summary>Poster, person or null, from where the picture is under the metadata folder.</summary>
    public static PictureKind? KindOf(string root, string path)
    {
        var rules = Rules.Current;
        var parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
        var name = parts[^1].ToLowerInvariant();
        if (parts.Length < 2 || parts[0] == "..")
        {
            return null;
        }

        if (!rules.Extensions.Contains(Path.GetExtension(name)) || name.EndsWith(rules.TempSuffix, StringComparison.Ordinal)
            || (rules.SkipHidden && name.StartsWith('.')))
        {
            return null;
        }

        foreach (var (rule, kind) in Kinds)
        {
            if (parts[0] == rule.Folder && name.StartsWith(rule.NameStartsWith, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>Each kind the shrinker knows, with its rule, in the order of rules.json.</summary>
    public static IEnumerable<(Rules.KindRule Rule, PictureKind Kind)> Kinds
        => Rules.Current.Kinds.Select(k => (k.Value, Enum.Parse<PictureKind>(k.Key, ignoreCase: true)));

    /// <summary>
    /// The new size of a picture, or null when it is already small enough: no taller than <paramref name="limit"/> for a
    /// kind limited by height (posters), no wider for every other kind. The other side keeps the shape, rounded half to even.
    /// </summary>
    public static (int Width, int Height)? Target(PictureKind kind, int width, int height, int limit)
    {
        if (RuleOf(kind).Limit == "height")
        {
            return height > limit ? (Math.Max(1, (int)Math.Round((double)width * limit / height, MidpointRounding.ToEven)), limit) : null;
        }

        return width > limit ? (limit, Math.Max(1, (int)Math.Round((double)height * limit / width, MidpointRounding.ToEven))) : null;
    }

    /// <summary>The rule of a kind, from rules.json.</summary>
    public static Rules.KindRule RuleOf(PictureKind kind) => Rules.Current.Kinds[KindName(kind)];

    /// <summary>
    /// Each picture under library and People of the kinds ticked in What to shrink (or of every kind, with
    /// <paramref name="everyKind"/>), in folder order. Each folder is walked once, however many kinds it holds.
    /// </summary>
    public static List<(string Path, PictureKind Kind)> Pictures(ShrinkSettings s, bool everyKind = false)
    {
        var found = new List<(string, PictureKind)>();
        var wanted = Kinds.Where(k => everyKind || s.Wants(k.Kind)).ToList();
        foreach (var folder in wanted.Select(k => k.Rule.Folder).Distinct())
        {
            var top = Path.Combine(s.MetadataRoot, folder);
            if (!Directory.Exists(top))
            {
                continue;
            }

            foreach (var path in Walk(top))
            {
                var kind = KindOf(s.MetadataRoot, path);
                if (kind is not null && wanted.Any(k => k.Kind == kind))
                {
                    found.Add((path, kind.Value));
                }
            }
        }

        return found;
    }

    // One picture ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Shrinks one picture in place. The copy is written to a hidden file beside it, opened again and decoded in full,
    /// checked for its size and format, and swapped in only when it is smaller. With backup activated, the original is
    /// copied to the backup folder first (never over a backup already there); when that fails, it is not shrunk.
    /// </summary>
    public static PictureResult ShrinkOne(ShrinkSettings s, string path, PictureKind kind)
    {
        var rules = Rules.Current;
        long before;
        SKEncodedImageFormat format;
        (int Width, int Height) size;
        try
        {
            before = new FileInfo(path).Length;
            using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("it cannot be read as a picture");
            var target = Target(kind, codec.Info.Width, codec.Info.Height, s.Limit(kind));
            if (target is null)
            {
                return new PictureResult(Outcome.SmallEnough, before, before, Width: codec.Info.Width, Height: codec.Info.Height, Format: Name(codec.EncodedFormat));
            }

            format = codec.EncodedFormat;
            size = target.Value;
            if (!rules.Formats.Contains(Name(format)))
            {
                return new PictureResult(Outcome.Failed, before, before, $"{Name(format)} pictures are left as they are");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return new PictureResult(Outcome.Failed, 0, 0, e.Message);
        }

        var folder = Path.GetDirectoryName(path)!;
        var tmp = Path.Combine(folder, "." + Path.GetFileName(path) + rules.TempSuffix);
        try
        {
            WriteSmaller(path, tmp, format, size, s.Quality);
            Check(tmp, format, size);
            var after = new FileInfo(tmp).Length;
            if (after >= before)
            {
                return new PictureResult(Outcome.NotSmaller, before, before, Width: size.Width, Height: size.Height, Format: Name(format));
            }

            long backedUp = 0;
            if (s.Backup)
            {
                var (ok, bytes, why) = BackUp(s, path, before);
                if (!ok)
                {
                    return new PictureResult(Outcome.BackupFailed, before, before, why);
                }

                backedUp = bytes;
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(tmp, File.GetUnixFileMode(path));
                Unix.CopyOwner(path, tmp);
            }

            File.Move(tmp, path, overwrite: true); // a rename: Jellyfin sees the old picture or the new one, never half of one
            return new PictureResult(Outcome.Shrunk, before, after, null, backedUp, size.Width, size.Height, Name(format));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return new PictureResult(Outcome.Failed, before, before, e.Message);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static void WriteSmaller(string path, string tmp, SKEncodedImageFormat format, (int Width, int Height) size, int quality)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("it cannot be read as a picture");
        var decode = codec.Info.Size;
        if (format == SKEncodedImageFormat.Jpeg)
        {
            // Decoded at a reduced scale when the JPEG allows it, still at least the new size: less memory and time.
            for (var k = 1; k < 8; k++)
            {
                var d = codec.GetScaledDimensions(k / 8f);
                if (d.Width >= size.Width && d.Height >= size.Height && d.Width < decode.Width)
                {
                    decode = d;
                    break;
                }
            }
        }

        var gray = codec.Info.ColorType == SKColorType.Gray8;
        var opaque = format == SKEncodedImageFormat.Jpeg || codec.Info.AlphaType == SKAlphaType.Opaque;
        var colour = gray ? SKColorType.Gray8 : SKImageInfo.PlatformColorType;
        var alpha = opaque ? SKAlphaType.Opaque : SKAlphaType.Premul;
        var info = new SKImageInfo(decode.Width, decode.Height, colour, alpha);
        using var full = new SKBitmap(info);
        var result = codec.GetPixels(info, full.GetPixels());
        if (result != SKCodecResult.Success)
        {
            throw new InvalidDataException($"it could not be decoded in full ({result})");
        }

        using var small = full.Resize(new SKImageInfo(size.Width, size.Height, colour, alpha), new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidDataException("it could not be resized");
        using var image = SKImage.FromBitmap(small);
        using var data = format switch
        {
            SKEncodedImageFormat.Png => image.Encode(SKEncodedImageFormat.Png, 100),
            SKEncodedImageFormat.Webp => image.Encode(SKEncodedImageFormat.Webp, quality),
            _ => image.Encode(SKEncodedImageFormat.Jpeg, quality)
        } ?? throw new InvalidDataException("the smaller copy could not be written");
        using var output = File.Create(tmp);
        data.SaveTo(output);
    }

    /// <summary>Opens the copy again and decodes it in full: it must have the new size and the original's format.</summary>
    private static void Check(string tmp, SKEncodedImageFormat format, (int Width, int Height) size)
    {
        using var codec = SKCodec.Create(tmp) ?? throw new InvalidDataException("the smaller copy cannot be read");
        if (codec.Info.Width != size.Width || codec.Info.Height != size.Height || codec.EncodedFormat != format)
        {
            throw new InvalidDataException($"the smaller copy is {codec.Info.Width} by {codec.Info.Height} {Name(codec.EncodedFormat)}, not {size.Width} by {size.Height} {Name(format)}");
        }

        using var bitmap = new SKBitmap(codec.Info);
        var result = codec.GetPixels(codec.Info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
        {
            throw new InvalidDataException($"the smaller copy does not decode in full ({result})");
        }
    }

    /// <summary>Copies the original to the backup folder unless a backup of it is already there.</summary>
    private static (bool Ok, long Bytes, string? Why) BackUp(ShrinkSettings s, string path, long size)
    {
        var copy = BackupPathOf(s, path);
        if (File.Exists(copy))
        {
            return (true, 0, null); // never overwritten: the copy there is the original, this one may be shrunk already
        }

        var tmp = copy + Rules.Current.TempSuffix;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            var free = s.FreeSpace(Path.GetDirectoryName(copy)!);
            if (free < size + Rules.Current.Backup.KeepFreeBytes)
            {
                return (false, 0, $"not shrunk: the backup disk has only {Words.Size(free)} free");
            }

            File.Copy(path, tmp, overwrite: true);
            if (new FileInfo(tmp).Length != size)
            {
                return (false, 0, "not shrunk: the backup copy is not the same size as the original");
            }

            File.SetLastWriteTimeUtc(tmp, File.GetLastWriteTimeUtc(path));
            File.Move(tmp, copy, overwrite: false);
            return (true, size, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (false, 0, $"not shrunk: the original could not be copied to the backup folder. Check that Jellyfin can write to it. Technical details: {e.Message}");
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    public static string BackupPathOf(ShrinkSettings s, string path)
        => Path.Combine(s.BackupRoot, Path.GetRelativePath(s.MetadataRoot, path));

    // Runs -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A run over every picture (or only <paramref name="only"/>), stopping at <paramref name="limit"/>. Returns null
    /// when something else is running.
    /// </summary>
    public RunRecord? Run(string kind, TimeSpan limit, CancellationToken cancel, IProgress<double>? progress = null, IReadOnlyCollection<string>? only = null)
    {
        if (!_settings().Chosen)
        {
            // Nothing is shrunk until the person chooses; the run does not fail and leaves no record.
            _log.LogInformation("Picture shrinker: {Said}", ChooseFirst);
            return new RunRecord { Kind = kind, Start = Now(), End = Now(), Stopped = ChooseFirst };
        }

        if (!_gate.Wait(0))
        {
            return null;
        }

        try
        {
            Busy = kind;
            return RunLocked(kind, limit, cancel, progress, only);
        }
        finally
        {
            Busy = string.Empty;
            Progress = null;
            _gate.Release();
        }
    }

    private RunRecord RunLocked(string kind, TimeSpan limit, CancellationToken cancel, IProgress<double>? progress, IReadOnlyCollection<string>? only)
    {
        var s = _settings();
        var rules = Rules.Current;
        var clock = Stopwatch.StartNew();
        var rec = new RunRecord { Kind = kind, Start = DateTimeOffset.Now, PosterHeight = s.PosterHeight, PersonWidth = s.PersonWidth,
            Sizes = Kinds.ToDictionary(k => KindName(k.Kind), k => s.Limit(k.Kind)) };
        SettleLocked(s); // the pictures Start again moved aside: before a run, so it never shrinks an old one put back later
        var problem = s.Problem();
        if (problem is not null)
        {
            rec.Stopped = problem;
            rec.End = DateTimeOffset.Now;
            Store.Update(st => st.AddRun(rec));
            return rec;
        }

        Tidy(s);
        var list = only is null
            ? Pictures(s)
            : only.Select(p => (p, KindOf(s.MetadataRoot, p))).Where(x => x.Item2 is not null && File.Exists(x.p))
                .Where(x => s.Wants(x.Item2!.Value))
                .Select(x => (x.p, x.Item2!.Value)).ToList();
        Progress = rec;
        for (var i = 0; i < list.Count; i++)
        {
            if (cancel.IsCancellationRequested)
            {
                rec.Stopped = "Stopped by you; the next run carries on";
                break;
            }

            if (clock.Elapsed > limit)
            {
                rec.Stopped = "Stopped at its time limit; the next run carries on";
                break;
            }

            var (path, pkind) = list[i];
            rec.Seen++;
            var got = ShrinkOne(s, path, pkind);
            switch (got.Outcome)
            {
                case Outcome.Shrunk:
                    rec.Shrunk++;
                    rec.Before += got.Before;
                    rec.After += got.After;
                    if (got.BackedUpBytes > 0)
                    {
                        rec.BackedUp++;
                        rec.BackedUpBytes += got.BackedUpBytes;
                    }

                    break;
                case Outcome.SmallEnough:
                case Outcome.NotSmaller:
                    rec.Left++;
                    break;
                case Outcome.BackupFailed:
                    rec.NotBackedUp++;
                    Note(rec, s, path, got.Why);
                    break;
                default:
                    rec.Failed++;
                    Note(rec, s, path, got.Why);
                    break;
            }

            if (i % 25 == 0)
            {
                progress?.Report(100.0 * i / list.Count);
            }
        }

        rec.End = DateTimeOffset.Now;
        if (rec.NotBackedUp > 0 && string.IsNullOrEmpty(rec.Stopped))
        {
            rec.Stopped = $"{rec.NotBackedUp:N0} not shrunk because their originals could not be backed up";
        }

        Store.Update(st => st.AddRun(rec));
        _log.LogInformation(
            "Picture shrinker: {Kind} run shrank {Shrunk} pictures, {Before} to {After} bytes; left {Left}; could not read {Failed}; not backed up {NotBackedUp}. {Stopped}",
            kind, rec.Shrunk, rec.Before, rec.After, rec.Left, rec.Failed, rec.NotBackedUp, rec.Stopped);
        progress?.Report(100);
        ReturnMemory();
        return rec;
    }

    /// <summary>
    /// Gives the memory a run used back to the system once it ends, so Jellyfin is no larger between runs than it was
    /// before the first: a full, compacting collection (once per run), Skia's own caches emptied, and the C library's
    /// free memory handed back (on Jellyfin 12's .NET 10 the decoded pictures' native memory otherwise stays with
    /// Jellyfin).
    /// </summary>
    internal static void ReturnMemory()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        SKGraphics.PurgeAllCaches();
        Unix.TrimNativeHeap();
    }

    private static void Note(RunRecord rec, ShrinkSettings s, string path, string? why)
    {
        if (rec.Failures.Count < Rules.Current.KeepFailuresPerRun)
        {
            rec.Failures.Add(new Failure { File = Path.GetRelativePath(s.MetadataRoot, path), Why = (why ?? string.Empty)[..Math.Min((why ?? string.Empty).Length, 200)] });
        }
    }

    /// <summary>Removes its own unfinished copies, left if Jellyfin stopped part way. Nothing of Jellyfin's is touched.</summary>
    public static int Tidy(ShrinkSettings s)
    {
        var gone = 0;
        var tops = Rules.Current.Kinds.Values.Select(k => Path.Combine(s.MetadataRoot, k.Folder)).ToList();
        if (!string.IsNullOrEmpty(s.BackupRoot))
        {
            tops.Add(s.BackupRoot);
        }

        foreach (var top in tops.Where(Directory.Exists))
        {
            foreach (var path in Walk(top))
            {
                if (path.EndsWith(Rules.Current.TempSuffix, StringComparison.Ordinal))
                {
                    TryDelete(path);
                    gone++;
                }
            }
        }

        return gone;
    }

    // Restore, measure and delete the backup --------------------------------------------------------------------

    /// <summary>
    /// Puts every backed-up original back where it came from. The backup is kept, so it can be done again; a picture
    /// whose film, series or person is no longer in Jellyfin is left in the backup.
    /// </summary>
    public RestoreRecord? Restore(CancellationToken cancel, IProgress<double>? progress = null)
    {
        if (!_gate.Wait(0))
        {
            return null;
        }

        try
        {
            Busy = "restore";
            var s = _settings();
            var rec = new RestoreRecord();
            if (string.IsNullOrEmpty(s.BackupRoot) || !Directory.Exists(s.BackupRoot))
            {
                return rec;
            }

            var files = Walk(s.BackupRoot).Where(p => !p.EndsWith(Rules.Current.TempSuffix, StringComparison.Ordinal)).ToList();
            for (var i = 0; i < files.Count; i++)
            {
                if (cancel.IsCancellationRequested)
                {
                    rec.Stopped = "Stopped by you";
                    break;
                }

                var copy = files[i];
                var live = Path.Combine(s.MetadataRoot, Path.GetRelativePath(s.BackupRoot, copy));
                if (KindOf(s.MetadataRoot, live) is null || !Directory.Exists(Path.GetDirectoryName(live)))
                {
                    rec.NoLongerThere++;
                    continue;
                }

                var tmp = Path.Combine(Path.GetDirectoryName(live)!, "." + Path.GetFileName(live) + Rules.Current.TempSuffix);
                try
                {
                    if (File.Exists(live) && SameBytes(copy, live))
                    {
                        rec.AlreadyOriginal++;
                        continue;
                    }

                    var shrunkSize = File.Exists(live) ? new FileInfo(live).Length : 0;
                    File.Copy(copy, tmp, overwrite: true);
                    if (File.Exists(live) && !OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(tmp, File.GetUnixFileMode(live));
                        Unix.CopyOwner(live, tmp);
                    }

                    File.Move(tmp, live, overwrite: true);
                    rec.Restored++;
                    rec.Bytes += new FileInfo(live).Length;
                    rec.ShrunkBytes += shrunkSize;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    rec.Failed++;
                    _log.LogWarning("Picture shrinker: could not restore {File}: {Why}", live, e.Message);
                }
                finally
                {
                    TryDelete(tmp);
                }

                if (i % 25 == 0)
                {
                    progress?.Report(100.0 * i / files.Count);
                }
            }

            _log.LogInformation("Picture shrinker: restored {Restored} original pictures ({Bytes} bytes); {Already} were already the original; {Gone} are no longer in Jellyfin; {Failed} failed",
                rec.Restored, rec.Bytes, rec.AlreadyOriginal, rec.NoLongerThere, rec.Failed);
            Store.Update(st =>
            {
                // The space saved is what is saved now: a restored picture no longer counts.
                st.LastRestore = rec;
                st.Shrunk = Math.Max(0, st.Shrunk - rec.Restored);
                st.Before = Math.Max(0, st.Before - rec.Bytes);
                st.After = Math.Max(0, st.After - rec.ShrunkBytes);
            });
            progress?.Report(100);
            ReturnMemory();
            return rec;
        }
        finally
        {
            Busy = string.Empty;
            _gate.Release();
        }
    }

    /// <summary>The live pictures, the backup and the space saved, measured now (when the page opens and after each run).</summary>
    public SpaceInfo Measure()
    {
        var s = _settings();
        var info = new SpaceInfo { Measured = DateTimeOffset.Now };
        if (Directory.Exists(s.MetadataRoot))
        {
            foreach (var (path, _) in Pictures(s))
            {
                info.LiveCount++;
                info.LiveBytes += SafeLength(path);
            }
        }

        if (!string.IsNullOrEmpty(s.BackupRoot) && Directory.Exists(s.BackupRoot))
        {
            foreach (var path in Walk(s.BackupRoot))
            {
                info.BackupCount++;
                info.BackupBytes += SafeLength(path);
            }
        }

        var st = Store.Load();
        info.Saved = st.Before - st.After;
        info.ShrunkTotal = st.Shrunk;
        return info;
    }

    /// <summary>
    /// Deletes every file in the backup folder, for good: only files inside it (no link is followed out of it), then
    /// its empty folders. Refused while a run or restore is going.
    /// </summary>
    public DeleteResult DeleteBackup()
    {
        var s = _settings();
        var problem = BackupFolderProblem(s);
        if (problem is not null)
        {
            return new DeleteResult(false, 0, 0, problem);
        }

        var root = Full(s.BackupRoot);
        if (!Directory.Exists(root))
        {
            return new DeleteResult(false, 0, 0, "There is no backup to delete");
        }

        if (!_gate.Wait(0))
        {
            return new DeleteResult(false, 0, 0, "Not deleted: the shrinker is running. Try again when it has finished");
        }

        try
        {
            Busy = "delete";
            var (files, bytes) = DeleteBackupLocked(root);
            Store.Update(st => st.LastDelete = new DeleteRecord { At = DateTimeOffset.Now, Files = files, Bytes = bytes });
            return new DeleteResult(true, files, bytes, $"Deleted the backup: {files:N0} original pictures, {Words.Size(bytes)} freed. The shrunk pictures stay.");
        }
        finally
        {
            Busy = string.Empty;
            _gate.Release();
        }
    }

    /// <summary>Why the backup folder cannot be deleted, or null when it can (it may not be there).</summary>
    private static string? BackupFolderProblem(ShrinkSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.BackupRoot) || !Path.IsPathRooted(s.BackupRoot))
        {
            return "There is no backup folder to delete";
        }

        var root = Full(s.BackupRoot);
        if (root == Path.GetPathRoot(root) || IsInside(Full(s.MetadataRoot), root) || root == Full(s.MetadataRoot))
        {
            return "The backup folder is not one the shrinker can delete";
        }

        return null;
    }

    /// <summary>Deletes what the shrinker put in the backup folder. The caller holds the shrinker.</summary>
    private (int Files, long Bytes) DeleteBackupLocked(string root)
    {
        int files = 0;
        long bytes = 0;
        // Only what the shrinker itself puts there: pictures under library and People, so a backup folder set to
        // the wrong place by mistake never loses anything else.
        var tops = Rules.Current.Kinds.Values.Select(k => Path.Combine(root, k.Folder)).Where(Directory.Exists).ToList();
        foreach (var path in tops.SelectMany(Walk))
        {
            var full = Full(path);
            var ext = Path.GetExtension(full).ToLowerInvariant();
            if (!IsInside(full, root) || !Rules.Current.Extensions.Contains(ext))
            {
                continue;
            }

            var fi = new FileInfo(full);
            var size = fi.LinkTarget is null ? fi.Length : 0;
            try
            {
                File.Delete(full); // a link is removed itself, never what it points at
                files++;
                bytes += size;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning("Picture shrinker: could not delete {File} from the backup: {Why}", full, e.Message);
            }
        }

        foreach (var dir in tops.SelectMany(t => Directory.EnumerateDirectories(t, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).Append(t))
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (IsInside(Full(dir), root) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        _log.LogInformation("Picture shrinker: deleted the backup, {Files} original pictures, {Bytes} bytes, from {Root}", files, bytes, root);
        return (files, bytes);
    }

    // Start again and settling ---------------------------------------------------------------------------------

    /// <summary>
    /// Start again: deletes the backup, clears the records (the backup choice is kept) and asks Jellyfin to download
    /// pictures again. For the pictures it shrinks (every kind, whatever is ticked in What to shrink), each picture is
    /// first moved aside to a hidden file beside it, kept until
    /// Jellyfin has a new one (see <see cref="Settle"/>); for every picture, nothing is moved and Jellyfin replaces them
    /// all. Refused while anything else is going. With <paramref name="wait"/> false the refreshes are asked for in
    /// the background (one about every 50 ms) and the shrinker stays busy until they are all asked for.
    /// </summary>
    public StartAgainResult StartAgain(string kind, IPictureRefresher refresher, bool wait = true)
    {
        if (kind is not (PicturesItShrinks or EveryPicture))
        {
            return new StartAgainResult(false, "Not started again: there is no Start again of that kind");
        }

        var s = _settings();
        if (string.IsNullOrWhiteSpace(s.MetadataRoot) || !Directory.Exists(s.MetadataRoot))
        {
            return new StartAgainResult(false, "Not started again: Jellyfin's metadata folder is not there");
        }

        var folderProblem = BackupFolderProblem(s);
        if (folderProblem == "The backup folder is not one the shrinker can delete")
        {
            return new StartAgainResult(false, "Not started again: the backup folder is not one the shrinker can delete. Choose another backup folder first");
        }

        var busy = Busy;
        if (!_gate.Wait(0))
        {
            return new StartAgainResult(false, $"Not started again: {BusyWords(busy)} is going. Try again when it has finished");
        }

        var handedOn = false;
        try
        {
            Busy = "start-again";
            SettleLocked(s);
            var rec = new StartAgainRecord { Kind = kind, At = Now() };

            // 1. The backup, with the same safe rules as Delete backup.
            if (folderProblem is null && Directory.Exists(Full(s.BackupRoot)))
            {
                (rec.BackupFiles, rec.BackupBytes) = DeleteBackupLocked(Full(s.BackupRoot));
            }

            // 2. The records. The pictures still waiting from an earlier Start again stay listed, so none is lost.
            Store.Update(st =>
            {
                st.ClearRecords();
                st.LastStartAgain = rec;
            });

            // 3. What Jellyfin is asked to refresh.
            List<(RefreshTarget Target, List<string> Moved)> jobs;
            if (kind == PicturesItShrinks)
            {
                jobs = MoveAside(s, rec);
            }
            else
            {
                jobs = refresher.Libraries().Select(id => (new RefreshTarget(id, null), new List<string>()))
                    .Concat(PeopleWithFolders(s.MetadataRoot).Select(n => (new RefreshTarget(Guid.Empty, n), new List<string>())))
                    .ToList();
                rec.Libraries = jobs.Count(j => j.Item1.Person is null);
                rec.People = jobs.Count - rec.Libraries;
            }

            Store.Update(st => st.LastStartAgain = rec);
            var said = Said(rec);
            _log.LogInformation("Picture shrinker: {Said}", said);
            if (wait)
            {
                QueueRefreshes(kind, jobs, refresher, rec);
            }
            else
            {
                handedOn = true;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        QueueRefreshes(kind, jobs, refresher, rec);
                    }
                    catch (Exception e)
                    {
                        _log.LogWarning(e, "Picture shrinker: Start again stopped asking Jellyfin for pictures");
                    }
                    finally
                    {
                        Busy = string.Empty;
                        _gate.Release();
                    }
                });
            }

            return new StartAgainResult(true, said, rec);
        }
        finally
        {
            if (!handedOn)
            {
                Busy = string.Empty;
                _gate.Release();
            }
        }
    }

    private static string Said(StartAgainRecord rec)
    {
        var backup = rec.BackupFiles > 0 ? "the backup is deleted" : "there was no backup to delete";
        if (rec.Kind == PicturesItShrinks)
        {
            return $"Starting again: {backup} and Jellyfin is downloading {rec.MovedAside:N0} {(rec.MovedAside == 1 ? "picture" : "pictures")} again. "
                + "That can take some hours. Space saved, on this page, says how far it has gone";
        }

        return $"Starting again: {backup} and Jellyfin is downloading every picture of {rec.Libraries:N0} {(rec.Libraries == 1 ? "library" : "libraries")} "
            + $"and {rec.People:N0} {(rec.People == 1 ? "person" : "people")} again. That can take many hours. Scheduled Tasks and the libraries show how far it has gone";
    }

    /// <summary>
    /// Moves each poster, backdrop and photo of a person aside, to a hidden file in the same folder, whatever is ticked
    /// in What to shrink: Start again is a reset. Each is listed in the state before it is moved, so a restart part way
    /// loses none. A picture that is a link is left alone.
    /// </summary>
    private List<(RefreshTarget Target, List<string> Moved)> MoveAside(ShrinkSettings s, StartAgainRecord rec)
    {
        var planned = new List<(string Path, PictureKind Kind, RefreshTarget Target)>();
        foreach (var (path, pkind) in Pictures(s, everyKind: true)) // a reset: every kind, whatever is ticked
        {
            var target = TargetOf(s.MetadataRoot, path);
            if (target is null || IsLink(path) || File.Exists(AsidePathOf(path)) || IsLink(AsidePathOf(path)))
            {
                continue;
            }

            planned.Add((path, pkind, target));
        }

        var at = Now();
        Store.Update(st =>
        {
            var listed = st.Aside.Select(a => a.Path).ToHashSet(StringComparer.Ordinal);
            st.Aside.AddRange(planned.Where(p => !listed.Contains(p.Path))
                .Select(p => new AsideRecord { Path = p.Path, Kind = KindName(p.Kind), At = at }));
        });

        var moved = new List<(string Path, RefreshTarget Target)>();
        var failed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, _, target) in planned)
        {
            try
            {
                File.Move(path, AsidePathOf(path), overwrite: false);
                moved.Add((path, target));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failed.Add(path);
                _log.LogWarning("Picture shrinker: could not move {File} aside: {Why}", path, e.Message);
            }
        }

        if (failed.Count > 0)
        {
            Store.Update(st => st.Aside.RemoveAll(a => failed.Contains(a.Path)));
        }

        rec.MovedAside = moved.Count;
        var jobs = moved.GroupBy(m => m.Target).Select(g => (g.Key, g.Select(m => m.Path).ToList())).ToList();
        rec.Items = jobs.Count(j => j.Key.Person is null);
        rec.People = jobs.Count - rec.Items;
        return jobs;
    }

    /// <summary>
    /// Asks Jellyfin for each refresh, at low priority with a short pause between them. A film or person no longer in
    /// Jellyfin cannot get a new picture, so its old one is put back at once.
    /// </summary>
    private void QueueRefreshes(string kind, List<(RefreshTarget Target, List<string> Moved)> jobs, IPictureRefresher refresher, StartAgainRecord rec)
    {
        var replaceAll = kind == EveryPicture;
        var pause = TimeSpan.FromMilliseconds(Rules.Current.StartAgain.PauseBetweenRefreshesMs);
        var putBack = new List<string>();
        for (var i = 0; i < jobs.Count; i++)
        {
            if (i > 0)
            {
                Pause(pause);
            }

            var (target, moved) = jobs[i];
            bool found;
            try
            {
                found = target.Person is null ? refresher.RefreshItem(target.Item, replaceAll) : refresher.RefreshPerson(target.Person, replaceAll);
            }
            catch (Exception e)
            {
                found = false;
                _log.LogWarning("Picture shrinker: Jellyfin could not refresh {Target}: {Why}", target, e.Message);
            }

            if (!found)
            {
                rec.NotFound++;
                foreach (var path in moved)
                {
                    if (!File.Exists(path) && File.Exists(AsidePathOf(path)) && !IsLink(AsidePathOf(path)))
                    {
                        TryMove(AsidePathOf(path), path);
                    }

                    putBack.Add(path);
                }
            }
        }

        rec.Queued = Now();
        var done = putBack.ToHashSet(StringComparer.Ordinal);
        Store.Update(st =>
        {
            st.Aside.RemoveAll(a => done.Contains(a.Path));
            st.LastStartAgain = rec;
        });
        _log.LogInformation("Picture shrinker: asked Jellyfin for {Count} refreshes; {NotFound} are no longer in Jellyfin", jobs.Count, rec.NotFound);
    }

    /// <summary>
    /// Checks the pictures moved aside: when Jellyfin has saved a new picture in that folder, the old one is deleted;
    /// after a day with none, the old one is put back under its own name. Runs every 10 minutes while any are waiting
    /// and at the start of each run. Returns null when something else is running.
    /// </summary>
    public SettleResult? Settle()
    {
        if (!_gate.Wait(0))
        {
            return null;
        }

        try
        {
            Busy = "settle";
            return SettleLocked(_settings());
        }
        finally
        {
            Busy = string.Empty;
            _gate.Release();
        }
    }

    private SettleResult SettleLocked(ShrinkSettings s)
    {
        var waiting = Store.Load().Aside;
        if (waiting.Count == 0)
        {
            return new SettleResult(0, 0, 0);
        }

        var now = Now();
        var after = TimeSpan.FromHours(Rules.Current.StartAgain.PutBackAfterHours);
        var root = string.IsNullOrEmpty(s.MetadataRoot) ? string.Empty : Full(s.MetadataRoot);
        var gone = new HashSet<string>(StringComparer.Ordinal);
        int replaced = 0, putBack = 0;
        foreach (var a in waiting)
        {
            var aside = AsidePathOf(a.Path);
            if (root.Length == 0 || !IsInside(Full(a.Path), root))
            {
                gone.Add(a.Path); // not under this metadata folder: never touched, and no longer listed
                continue;
            }

            if (IsLink(aside) || IsLink(Path.GetDirectoryName(a.Path)!))
            {
                gone.Add(a.Path); // a link is never deleted or moved; it is no longer the shrinker's to look after
                _log.LogInformation("Picture shrinker: {File} is a link, so it is left as it is", aside);
                continue;
            }

            if (!File.Exists(aside))
            {
                gone.Add(a.Path);
                continue;
            }

            if (HasNewPicture(a))
            {
                if (TryDelete(aside))
                {
                    replaced++;
                    gone.Add(a.Path);
                }

                continue;
            }

            if (now - a.At >= after && !File.Exists(a.Path) && TryMove(aside, a.Path))
            {
                putBack++;
                gone.Add(a.Path);
            }
        }

        var left = waiting.Count - gone.Count;
        if (gone.Count > 0)
        {
            Store.Update(st =>
            {
                st.Aside.RemoveAll(x => gone.Contains(x.Path));
                st.Replaced += replaced;
                st.PutBack += putBack;
                left = st.Aside.Count;
            });
            _log.LogInformation("Picture shrinker: {Replaced} old pictures replaced by Jellyfin's new ones, {PutBack} put back, {Left} still waiting", replaced, putBack, left);
        }

        return new SettleResult(replaced, putBack, left);
    }

    /// <summary>
    /// Whether Jellyfin has saved a new picture beside one moved aside: any visible picture whose name starts as its
    /// kind's do (poster.png for a poster, backdrop1.jpg for a backdrop, landscape.jpg for a thumbnail and so on), or any
    /// visible picture for a person.
    /// </summary>
    private static bool HasNewPicture(AsideRecord a)
    {
        var folder = Path.GetDirectoryName(a.Path)!;
        if (!Directory.Exists(folder))
        {
            return false;
        }

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file).ToLowerInvariant();
            if (name.StartsWith('.') || name.EndsWith(Rules.Current.TempSuffix, StringComparison.Ordinal) || !File.Exists(file))
            {
                continue;
            }

            var picture = Rules.Current.Extensions.Contains(Path.GetExtension(name)) || _otherPictureExtensions.Contains(Path.GetExtension(name));
            var starts = Rules.Current.Kinds.TryGetValue(a.Kind, out var rule) ? rule.NameStartsWith : string.Empty;
            if (picture && name.StartsWith(starts, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The kind's name in rules.json: poster, person or backdrop.</summary>
    public static string KindName(PictureKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>The hidden name a picture is moved aside to: .poster.jpg.picture-shrinker-old.</summary>
    public static string AsidePathOf(string path)
        => Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + Rules.Current.StartAgain.AsideSuffix);

    /// <summary>
    /// The item a picture belongs to: library/&lt;2 characters&gt;/&lt;32 hex id&gt;/poster.* is that item, and
    /// People/&lt;letter&gt;/&lt;name&gt;/... (or People/&lt;name&gt;/... for a name with no letter or digit) is that person.
    /// </summary>
    public static RefreshTarget? TargetOf(string root, string path)
    {
        var parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
        if (parts.Length == 4 && parts[0] == Rules.Current.Poster.Folder && Guid.TryParseExact(parts[2], "N", out var id)
            && parts[1] == parts[2][..2])
        {
            return new RefreshTarget(id, null);
        }

        if (parts.Length >= 3 && parts[0] == Rules.Current.Person.Folder)
        {
            if (IsPrefix(parts[1]) && parts.Length >= 4)
            {
                return new RefreshTarget(Guid.Empty, parts[2]);
            }

            if (!IsPrefix(parts[1]))
            {
                return new RefreshTarget(Guid.Empty, parts[1]);
            }
        }

        return null;
    }

    /// <summary>The name of each person with a folder under People, never following a link.</summary>
    public static List<string> PeopleWithFolders(string root)
    {
        var people = new List<string>();
        var top = Path.Combine(root, Rules.Current.Person.Folder);
        if (!Directory.Exists(top))
        {
            return people;
        }

        foreach (var dir in Folders(top))
        {
            var name = Path.GetFileName(dir);
            if (!IsPrefix(name))
            {
                people.Add(name);
                continue;
            }

            people.AddRange(Folders(dir).Select(d => Path.GetFileName(d)));
        }

        return people;

        static IEnumerable<string> Folders(string folder)
        {
            try
            {
                return Directory.GetDirectories(folder).Where(d => !IsLink(d) && !Path.GetFileName(d).StartsWith('.')).OrderBy(d => d, StringComparer.Ordinal).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }
    }

    /// <summary>Jellyfin files each person under the first letter or digit of the name: a folder of one such character.</summary>
    private static bool IsPrefix(string name) => name.Length == 1 && char.IsLetterOrDigit(name[0]);

    private static bool IsLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: false);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Helpers --------------------------------------------------------------------------------------------------

    /// <summary>Every file under a folder, in sorted folder order, never following a link to a folder.</summary>
    public static IEnumerable<string> Walk(string top)
    {
        var stack = new Stack<string>();
        stack.Push(top);
        while (stack.Count > 0)
        {
            var folder = stack.Pop();
            string[] dirs, files;
            try
            {
                dirs = Directory.GetDirectories(folder);
                files = Directory.GetFiles(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files)
            {
                yield return f;
            }

            Array.Sort(dirs, StringComparer.Ordinal);
            for (var i = dirs.Length - 1; i >= 0; i--)
            {
                if (new DirectoryInfo(dirs[i]).LinkTarget is null)
                {
                    stack.Push(dirs[i]);
                }
            }
        }
    }

    public static long FreeSpaceOf(string folder)
    {
        var dir = new DirectoryInfo(folder);
        while (dir is not null && !dir.Exists)
        {
            dir = dir.Parent;
        }

        return dir is null ? 0 : new DriveInfo(dir.FullName).AvailableFreeSpace;
    }

    public static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool IsInside(string path, string folder)
        => path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static string Name(SKEncodedImageFormat f) => f switch
    {
        SKEncodedImageFormat.Jpeg => "JPEG",
        SKEncodedImageFormat.Png => "PNG",
        SKEncodedImageFormat.Webp => "WEBP",
        _ => f.ToString().ToUpperInvariant()
    };

    private static bool SameBytes(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length)
        {
            return false;
        }

        using var first = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var second = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        var firstBuffer = new byte[64 * 1024];
        var secondBuffer = new byte[64 * 1024];
        while (true)
        {
            var firstRead = ReadBlock(first, firstBuffer);
            var secondRead = ReadBlock(second, secondBuffer);
            if (firstRead != secondRead || !firstBuffer.AsSpan(0, firstRead).SequenceEqual(secondBuffer.AsSpan(0, secondRead)))
            {
                return false;
            }

            if (firstRead == 0)
            {
                return true;
            }
        }

        static int ReadBlock(Stream stream, byte[] buffer)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var count = stream.Read(buffer, read, buffer.Length - read);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            return read;
        }
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
