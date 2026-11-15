using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>A picture the shrinker looks after.</summary>
public enum PictureKind
{
    Poster,
    Person
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

    public int Quality { get; init; } = Rules.Current.Quality.Default;

    public bool Posters { get; init; } = true;

    public bool People { get; init; } = true;

    public bool Backup { get; init; } = Rules.Current.Backup.Default;

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

/// <summary>
/// Shrinks Jellyfin's posters and photos of people by the rules in rules.json. Nothing here depends on Jellyfin, so
/// the tests run it on a folder of sample pictures.
/// </summary>
public sealed class Shrinker
{
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
    internal IDisposable Hold()
    {
        _gate.Wait();
        return new Release(_gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    public ShrinkSettings Settings => _settings();

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

        if (parts[0] == rules.Person.Folder && name.StartsWith(rules.Person.NameStartsWith, StringComparison.Ordinal))
        {
            return PictureKind.Person;
        }

        if (parts[0] == rules.Poster.Folder && name.StartsWith(rules.Poster.NameStartsWith, StringComparison.Ordinal))
        {
            return PictureKind.Poster;
        }

        return null;
    }

    /// <summary>The new size of a picture, or null when it is already small enough.</summary>
    public static (int Width, int Height)? Target(PictureKind kind, int width, int height, int posterHeight, int personWidth)
    {
        if (kind == PictureKind.Person && width > personWidth)
        {
            return (personWidth, Math.Max(1, (int)Math.Round((double)height * personWidth / width, MidpointRounding.ToEven)));
        }

        if (kind == PictureKind.Poster && height > posterHeight)
        {
            return (Math.Max(1, (int)Math.Round((double)width * posterHeight / height, MidpointRounding.ToEven)), posterHeight);
        }

        return null;
    }

    /// <summary>Each picture under library and People, in folder order.</summary>
    public static List<(string Path, PictureKind Kind)> Pictures(ShrinkSettings s)
    {
        var found = new List<(string, PictureKind)>();
        foreach (var (rule, kind, wanted) in new[] { (Rules.Current.Poster, PictureKind.Poster, s.Posters), (Rules.Current.Person, PictureKind.Person, s.People) })
        {
            var top = Path.Combine(s.MetadataRoot, rule.Folder);
            if (!wanted || !Directory.Exists(top))
            {
                continue;
            }

            foreach (var path in Walk(top))
            {
                if (KindOf(s.MetadataRoot, path) == kind)
                {
                    found.Add((path, kind));
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
            var target = Target(kind, codec.Info.Width, codec.Info.Height, s.PosterHeight, s.PersonWidth);
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
            return (false, 0, $"not shrunk: the original could not be backed up ({e.Message})");
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
        var rec = new RunRecord { Kind = kind, Start = DateTimeOffset.Now, PosterHeight = s.PosterHeight, PersonWidth = s.PersonWidth };
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
                .Where(x => (x.Item2 == PictureKind.Poster && s.Posters) || (x.Item2 == PictureKind.Person && s.People))
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
            foreach (var (path, _) in Pictures(new ShrinkSettings { MetadataRoot = s.MetadataRoot }))
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
        if (string.IsNullOrWhiteSpace(s.BackupRoot) || !Path.IsPathRooted(s.BackupRoot))
        {
            return new DeleteResult(false, 0, 0, "There is no backup folder to delete");
        }

        var root = Full(s.BackupRoot);
        if (root == Path.GetPathRoot(root) || IsInside(Full(s.MetadataRoot), root) || root == Full(s.MetadataRoot))
        {
            return new DeleteResult(false, 0, 0, "The backup folder is not one the shrinker can delete");
        }

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
            Store.Update(st => st.LastDelete = new DeleteRecord { At = DateTimeOffset.Now, Files = files, Bytes = bytes });
            return new DeleteResult(true, files, bytes, $"Deleted the backup: {files:N0} original pictures, {Words.Size(bytes)} freed. The shrunk pictures stay.");
        }
        finally
        {
            Busy = string.Empty;
            _gate.Release();
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

        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
