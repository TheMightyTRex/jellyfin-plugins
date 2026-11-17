using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AudioDescription.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>A film or episode a track may belong to, as the checks need it.</summary>
public sealed record Entry(Guid Id, string Title, string Kind, string Path, long Ticks, bool Described, string Search);

/// <summary>
/// Add a track you have: the same behaviour as Starboard's Audio description tracks plug-in. A track in the folder, or
/// uploaded on the settings page, is matched to its film or episode by its name, and its length compared with the
/// film's using Jellyfin's own ffprobe. When they agree within the tolerance, Jellyfin's own ffmpeg copies the sound,
/// without converting it, into "&lt;film&gt;.Audio Description.eng.mka" next to the film, titled "English (Audio
/// Description)", with the visual impaired flag and not the default track, and Jellyfin is asked to read the film
/// again. The original stays in the folder until the person says the track plays in step (then it moves to
/// Confirmed); if it does not, the added track moves to Taken off. Nothing is ever deleted.
/// </summary>
public sealed class TrackAdder
{
    public const long UploadMax = 1024L * 1024 * 1024;

    private static readonly Rules.TracksRule _rule = Rules.Current.Tracks;
    private readonly ILibraryManager _library;
    private readonly IMediaSourceManager _media;
    private readonly IProviderManager _providers;
    private readonly IFileSystem _fileSystem;
    private readonly IRunner _runner;
    private readonly Func<(string Probe, string Ffmpeg)> _tools;
    private readonly Func<PluginConfiguration> _config;
    private readonly string _defaultFolder;
    private readonly Tagger? _tagger;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _one = new(1, 1);

    public TrackAdder(
        ILibraryManager library,
        IMediaSourceManager media,
        IProviderManager providers,
        IFileSystem fileSystem,
        IRunner runner,
        Func<(string Probe, string Ffmpeg)> tools,
        Func<PluginConfiguration> config,
        string defaultFolder,
        StateStore store,
        Tagger? tagger,
        ILogger log)
    {
        _library = library;
        _media = media;
        _providers = providers;
        _fileSystem = fileSystem;
        _runner = runner;
        _tools = tools;
        _config = config;
        _defaultFolder = defaultFolder;
        Store = store;
        _tagger = tagger;
        _log = log;
    }

    public StateStore Store { get; }

    public string DefaultFolder => _defaultFolder;

    /// <summary>Gets the folder it watches: the one chosen in the settings, or the default in Jellyfin's data folder.</summary>
    public string Inbox => string.IsNullOrWhiteSpace(_config().TracksFolder) ? _defaultFolder : _config().TracksFolder.Trim();

    public bool Running => _one.CurrentCount == 0;

    public string Language => Words.Language(_config().Language);

    public int Tolerance => Math.Clamp(_config().ToleranceSeconds, _rule.ToleranceSeconds.Min, _rule.ToleranceSeconds.Max);

    /// <summary>The sound files and zips at the top of the folder: no hidden files, no links, nothing in its sub folders.</summary>
    public IReadOnlyList<string> InboxFiles()
    {
        try
        {
            return new DirectoryInfo(Inbox).EnumerateFiles()
                .Where(f => !f.Name.StartsWith('.') && Words.IsTrackFile(f.Name) && f.LinkTarget is null)
                .Select(f => f.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Whether the folder has files the last pass did not check: one listing, nothing read.</summary>
    public bool HasNew()
    {
        var recs = Store.Load().Tracks;
        return InboxFiles().Any(n => !recs.ContainsKey(n));
    }

    /// <summary>One pass over the folder: unzips, then matches and checks each new file. One pass at a time.</summary>
    public async Task<string> LookAsync(CancellationToken cancellationToken)
    {
        if (!await _one.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return "Already checking the folder";
        }

        try
        {
            Directory.CreateDirectory(Inbox);
            foreach (var zip in InboxFiles().Where(n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    Unzip(Path.Combine(Inbox, zip));
                }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
                {
                    Store.Update(s => s.Tracks[zip] = new TrackRecord { Name = zip, Status = "problem", Said = Short(e.Message) });
                }
            }

            var present = InboxFiles().ToHashSet(StringComparer.Ordinal);
            var todo = new List<string>();
            Store.Update(s =>
            {
                var done = s.Tracks.Where(kv => kv.Value.Status == "confirmed" && !present.Contains(kv.Key))
                    .OrderByDescending(kv => kv.Value.AddedAt ?? DateTimeOffset.MinValue).Take(50).Select(kv => kv.Key).ToHashSet();
                s.Tracks = s.Tracks.Where(kv => present.Contains(kv.Key) || done.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                foreach (var n in present.Where(n => !n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal))
                {
                    var sig = Sig(Path.Combine(Inbox, n));
                    if (!s.Tracks.TryGetValue(n, out var r) || (r.Sig != sig && r.Status != "added"))
                    {
                        s.Tracks[n] = new TrackRecord { Name = n, Sig = sig, Status = "new" };
                        todo.Add(n);
                    }
                }
            });

            var added = 0;
            foreach (var n in todo)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rec = Store.Load().Tracks[n];
                try
                {
                    rec = await LookOnceAsync(n, rec, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
                {
                    rec.Status = "problem";
                    rec.Said = Short(e.Message);
                }

                added += rec.Status == "added" ? 1 : 0;
                Store.Update(s => s.Tracks[n] = rec);
            }

            Store.Update(s => s.LastLook = DateTimeOffset.Now);
            return todo.Count == 0 ? "No new files in the folder" : $"Checked {Words.Plural(todo.Count, "new file")}, added {Words.Plural(added, "track")}";
        }
        finally
        {
            _one.Release();
        }
    }

    private async Task<TrackRecord> LookOnceAsync(string name, TrackRecord rec, CancellationToken cancellationToken)
    {
        var parsed = Words.ParseName(name);
        var (sure, maybe, why) = Find(parsed);
        rec.Maybe = maybe.Take(11).Select(m => new Choice { Id = m.Id.ToString("N"), Title = m.Title }).ToList();
        rec.Why = why;
        rec.Kind = parsed.Kind;
        rec.Search = parsed.Name;
        if (sure is null)
        {
            rec.Status = maybe.Count > 0 ? "choose" : "no_match";
            rec.ItemId = string.Empty;
            rec.ItemTitle = string.Empty;
            return rec;
        }

        return await CheckAsync(name, rec, sure, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>(the title it surely is or null, up to 11 titles it may be, words when nothing matched).</summary>
    public (Entry? Sure, List<Entry> Maybe, string Why) Find(ParsedName parsed)
    {
        if (parsed.IsEpisode)
        {
            var shows = _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Series }, Recursive = true, SearchTerm = parsed.Name, Limit = 5 });
            var show = shows.FirstOrDefault(s => Words.SameName(s.Name, parsed.Name)) ?? shows.FirstOrDefault();
            if (show is null)
            {
                return (null, new List<Entry>(), $"No series called {parsed.Name} is in Jellyfin");
            }

            var eps = _library.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                Recursive = true,
                AncestorIds = new[] { show.Id },
                ParentIndexNumber = parsed.Season
            });
            var hit = eps.FirstOrDefault(e => e.IndexNumber == parsed.Episode && (e.ParentIndexNumber ?? parsed.Season) == parsed.Season);
            if (hit is null)
            {
                return (null, new List<Entry>(), $"{show.Name} is in Jellyfin, but not series {parsed.Season} episode {parsed.Episode}");
            }

            return (ToEntry(hit, show.Name), new List<Entry>(), string.Empty);
        }

        var films = _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Movie }, Recursive = true, SearchTerm = parsed.Name, Limit = 11 });
        var maybe = films.Select(f => ToEntry(f)).ToList();
        var same = films.Where(f => Words.SameName(f.Name, parsed.Name) && (parsed.Year == 0 || f.ProductionYear == parsed.Year)).ToList();
        if (same.Count == 1)
        {
            return (ToEntry(same[0]), maybe, string.Empty);
        }

        return maybe.Count == 0 ? (null, maybe, $"No film called {parsed.Name} is in Jellyfin") : (null, maybe, string.Empty);
    }

    public Entry? ById(string id)
    {
        var item = Guid.TryParse(id, out var g) ? _library.GetItemById(g) : null;
        return item is null ? null : ToEntry(item);
    }

    private Entry ToEntry(BaseItem item, string? seriesName = null)
    {
        string title;
        if (item is Episode ep)
        {
            title = $"{seriesName ?? ep.SeriesName ?? "A series"}, series {ep.ParentIndexNumber ?? 0} episode {ep.IndexNumber ?? 0}";
        }
        else
        {
            title = Words.FilmTitle(item.Name, item.ProductionYear);
        }

        var described = Words.DescribedTrack(_media.GetMediaStreams(item.Id)) is not null;
        return new Entry(item.Id, Cut(title, 120), item is Episode ? "episode" : "film", item.Path ?? string.Empty, item.RunTimeTicks ?? 0, described,
            item is Episode e2 ? e2.SeriesName ?? seriesName ?? string.Empty : item.Name ?? string.Empty);
    }

    /// <summary>Checks one track against one title, and adds it when the lengths agree. Returns the record.</summary>
    public async Task<TrackRecord> CheckAsync(string name, TrackRecord rec, Entry item, CancellationToken cancellationToken)
    {
        var src = Path.Combine(Inbox, name);
        rec.ItemId = item.Id.ToString("N");
        rec.ItemTitle = item.Title;
        rec.Kind = item.Kind;
        rec.Search = item.Search;
        rec.Said = string.Empty;
        if (item.Described)
        {
            rec.Status = "described";
            return rec;
        }

        var video = item.Path;
        if (string.IsNullOrEmpty(video) || !File.Exists(video))
        {
            rec.Status = "out_of_reach";
            return rec;
        }

        var lang = Language;
        var target = Words.TargetFor(video, lang);
        if (File.Exists(target))
        {
            rec.Status = "exists";
            return rec;
        }

        var (_, hasAudio) = await ProbeAsync(src, cancellationToken).ConfigureAwait(false);
        if (!hasAudio)
        {
            rec.Status = "unreadable";
            return rec;
        }

        var film = (await ProbeAsync(video, cancellationToken).ConfigureAwait(false)).Seconds ?? (item.Ticks > 0 ? item.Ticks / 10_000_000.0 : null);
        if (film is null)
        {
            rec.Status = "unreadable_film";
            return rec;
        }

        // The track is copied into Matroska in the folder first: a copy counts every frame, so its length is exact even
        // for an MP3 with no length in its header. Only a copy that fits goes next to the film.
        var stem = Path.GetFileNameWithoutExtension(name);
        var part = Path.Combine(Inbox, "." + stem[..Math.Min(stem.Length, 150)] + ".check.mka");
        var hidden = Path.Combine(Path.GetDirectoryName(target)!, "." + Path.GetFileName(target) + ".part");
        try
        {
            await MuxAsync(src, lang, part, cancellationToken).ConfigureAwait(false);
            var track = (await ProbeAsync(part, cancellationToken).ConfigureAwait(false)).Seconds;
            if (track is null)
            {
                rec.Status = "unreadable";
                return rec;
            }

            rec.TrackSeconds = Math.Round(track.Value, 1);
            rec.FilmSeconds = Math.Round(film.Value, 1);
            if (Math.Abs(track.Value - film.Value) > Tolerance)
            {
                rec.Status = "lengths";
                return rec;
            }

            File.Move(part, hidden); // another disk, perhaps: copied under a hidden name, so Jellyfin never reads half a file
            if (File.Exists(target))
            {
                rec.Status = "exists";
                return rec;
            }

            File.Move(hidden, target, false); // never replaces a file that is there
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        {
            rec.Status = "problem";
            rec.Said = Short(e.Message);
            return rec;
        }
        finally
        {
            foreach (var ours in new[] { part, hidden })
            {
                if (File.Exists(ours))
                {
                    File.Delete(ours); // only the plug-in's own working copies, never the person's files
                }
            }
        }

        Refresh(item.Id);
        _tagger?.Expect(item.Id);
        rec.Status = "added";
        rec.Added = target;
        rec.AddedAt = DateTimeOffset.Now;
        _log.LogInformation("Audio description: added {Track} to {Title} as {Name}", name, item.Title, Path.GetFileName(target));
        return rec;
    }

    /// <summary>The ffprobe arguments: the file's length and its streams, as JSON.</summary>
    public static IReadOnlyList<string> ProbeArguments(string path)
        => new[] { "-v", "error", "-print_format", "json", "-show_entries", "format=duration:stream=index,codec_type", path };

    /// <summary>
    /// The ffmpeg arguments: the first sound track, copied as it is (nothing converted), with no tags or chapters from the
    /// source, its language, its title, the visual impaired flag and not the default track, so nobody gets it unless
    /// they choose it.
    /// </summary>
    public static IReadOnlyList<string> MuxArguments(string src, string lang, string output)
    {
        var flags = new List<string>();
        if (_rule.Flags.VisualImpaired)
        {
            flags.Add("visual_impaired");
        }

        if (_rule.Flags.DefaultTrack)
        {
            flags.Add("default");
        }

        return new[]
        {
            "-hide_banner", "-nostdin", "-v", "error", "-y", "-i", src, "-map", "0:a:0", "-c", "copy", "-map_metadata", "-1", "-map_chapters", "-1",
            "-metadata:s:a:0", "language=" + Words.Language(lang), "-metadata:s:a:0", "title=" + Words.TrackTitle(lang),
            "-disposition:a:0", flags.Count == 0 ? "0" : string.Join('+', flags), "-f", "matroska", output
        };
    }

    private async Task<(double? Seconds, bool HasAudio)> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var r = await _runner.RunAsync(_tools().Probe, ProbeArguments(path), TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.Output))
            {
                return (null, false);
            }

            using var doc = JsonDocument.Parse(r.Output);
            double? seconds = null;
            if (doc.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var d)
                && double.TryParse(d.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0)
            {
                seconds = s;
            }

            var audio = doc.RootElement.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array
                && streams.EnumerateArray().Any(x => x.TryGetProperty("codec_type", out var t) && t.GetString() == "audio");
            return (seconds, audio);
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            return (null, false);
        }
    }

    private async Task MuxAsync(string src, string lang, string output, CancellationToken cancellationToken)
    {
        var r = await _runner.RunAsync(_tools().Ffmpeg, MuxArguments(src, lang, output), TimeSpan.FromMinutes(15), cancellationToken).ConfigureAwait(false);
        if (r.ExitCode != 0)
        {
            var last = (r.Errors + "\n" + r.Output).Trim().Split('\n').LastOrDefault(l => l.Trim().Length > 0) ?? "ffmpeg stopped";
            throw new InvalidOperationException(Short("ffmpeg could not copy the sound: " + last.Trim()));
        }
    }

    private void Refresh(Guid id)
    {
        try
        {
            _providers.QueueRefresh(id, new MetadataRefreshOptions(new DirectoryService(_fileSystem))
            {
                MetadataRefreshMode = MetadataRefreshMode.Default,
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllMetadata = false,
                ReplaceAllImages = false
            }, RefreshPriority.High);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            _log.LogWarning(e, "Audio description: Jellyfin did not take the refresh, so the track shows after its next library scan");
        }
    }

    /// <summary>A title chosen on the settings page: checks the track against it.</summary>
    public async Task<string> ChooseAsync(string name, string itemId, CancellationToken cancellationToken)
    {
        var rec = Store.Load().Tracks.GetValueOrDefault(name) ?? throw new ArgumentException("Choose a file from the list. Reload the page if it has gone");
        if (rec.Status is "confirmed" or "added")
        {
            throw new ArgumentException("This track is added already");
        }

        var item = ById(itemId) ?? throw new ArgumentException("Choose a film or episode");
        if (!File.Exists(Path.Combine(Inbox, name)))
        {
            throw new ArgumentException("The file is not in the folder any more. Reload the page");
        }

        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            rec = await CheckAsync(name, rec, item, cancellationToken).ConfigureAwait(false);
            Store.Update(s => s.Tracks[name] = rec);
        }
        finally
        {
            _one.Release();
        }

        return rec.Status switch
        {
            "added" => $"Added to {rec.ItemTitle}. Play it in Jellyfin to check it",
            "lengths" => $"Not added: the lengths of the track and {rec.ItemTitle} differ",
            "described" => $"Not added: {rec.ItemTitle} already has a described sound track",
            _ => $"Not added to {rec.ItemTitle}"
        };
    }

    /// <summary>The person's say on an added track: right moves the original to Confirmed, undo moves the added track to Taken off.</summary>
    public string Outcome(string name, string value)
    {
        var rec = Store.Load().Tracks.GetValueOrDefault(name) ?? throw new ArgumentException("Choose a file from the list. Reload the page if it has gone");
        if (rec.Status != "added")
        {
            throw new ArgumentException("This track is not waiting for a check");
        }

        var title = string.IsNullOrEmpty(rec.ItemTitle) ? "the title" : rec.ItemTitle;
        if (value == "right")
        {
            var original = Path.Combine(Inbox, name);
            if (File.Exists(original))
            {
                MoveAside(original, _rule.Folders.Confirmed);
            }

            rec.Status = "confirmed";
            Store.Update(s => s.Tracks[name] = rec);
            return $"Confirmed for {title}. The original is in the {_rule.Folders.Confirmed} folder";
        }

        if (value == "undo")
        {
            // Only the file it added: next to a film, with the name it gives.
            if (!string.IsNullOrEmpty(rec.Added) && File.Exists(rec.Added) && rec.Added.EndsWith(".mka", StringComparison.Ordinal)
                && Path.GetFileName(rec.Added).Contains(".Audio Description.", StringComparison.Ordinal))
            {
                MoveAside(rec.Added, _rule.Folders.TakenOff);
            }

            if (Guid.TryParse(rec.ItemId, out var id))
            {
                Refresh(id);
            }

            rec.Status = "taken_off";
            rec.Added = string.Empty;
            Store.Update(s => s.Tracks[name] = rec);
            return $"Taken off {title}. The track is in the {_rule.Folders.TakenOff} folder";
        }

        throw new ArgumentException("Choose Yes or No");
    }

    /// <summary>A file chosen on the settings page, streamed into the folder a piece at a time, then checked with the rest.</summary>
    public async Task<string> UploadAsync(string name, Stream body, long length, CancellationToken cancellationToken)
    {
        var baseName = Path.GetFileName((name ?? string.Empty).Replace('\\', '/')).Trim();
        if (baseName.Length == 0 || baseName.StartsWith('.') || !Words.IsTrackFile(baseName))
        {
            throw new ArgumentException("Choose a sound file, such as an MP3 or M4A, or a zip of them");
        }

        if (length <= 0 || length > UploadMax)
        {
            throw new ArgumentException("Choose a file up to 1 GB, or put it in the folder");
        }

        Directory.CreateDirectory(Inbox);
        var final = Path.Combine(Inbox, FreeName(Inbox, baseName));
        var part = Path.Combine(Inbox, "." + Path.GetFileName(final) + ".part");
        try
        {
            await using (var f = new FileStream(part, FileMode.CreateNew, FileAccess.Write))
            {
                var buffer = new byte[1 << 20];
                long left = length;
                while (left > 0)
                {
                    var n = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), cancellationToken).ConfigureAwait(false);
                    if (n == 0)
                    {
                        throw new ArgumentException("The upload stopped before the end");
                    }

                    await f.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
                    left -= n;
                }
            }

            File.Move(part, final, false);
        }
        finally
        {
            if (File.Exists(part))
            {
                File.Delete(part); // its own unfinished copy
            }
        }

        return $"Uploaded {Path.GetFileName(final)}. It is being checked";
    }

    /// <summary>Takes the sound files out of a zip, by their names only, then moves the zip to Unzipped. Returns how many came out.</summary>
    public int Unzip(string path)
    {
        var count = 0;
        using (var zip = ZipFile.OpenRead(path))
        {
            var members = zip.Entries.Where(e => e.Name.Length > 0 && Words.IsTrackFile(e.Name, false)).ToList();
            if (members.Sum(m => m.Length) > _rule.Zip.MaxBytes || members.Count > _rule.Zip.MaxFiles)
            {
                throw new InvalidDataException("This zip is too large to unpack here. Unzip it yourself into the folder");
            }

            foreach (var m in members)
            {
                var name = Path.GetFileName(m.FullName.Replace('\\', '/'));
                if (name.Length == 0 || name.StartsWith('.'))
                {
                    continue;
                }

                var to = Path.Combine(Inbox, FreeName(Inbox, name));
                using (var src = m.Open())
                using (var dst = new FileStream(to, FileMode.CreateNew, FileAccess.Write))
                {
                    src.CopyTo(dst, 1 << 20);
                }

                count++;
            }
        }

        MoveAside(path, _rule.Folders.Unzipped);
        return count;
    }

    /// <summary>Moves a file into a folder inside the track folder (Confirmed, Taken off, Unzipped). Never deletes.</summary>
    public string MoveAside(string path, string sub)
    {
        var where = Path.Combine(Inbox, sub);
        Directory.CreateDirectory(where);
        var to = Path.Combine(where, FreeName(where, Path.GetFileName(path)));
        File.Move(path, to, false);
        return to;
    }

    private static string FreeName(string folder, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var outName = name;
        for (var n = 2; File.Exists(Path.Combine(folder, outName)) || Directory.Exists(Path.Combine(folder, outName)); n++)
        {
            outName = $"{stem} ({n}){ext}";
        }

        return outName;
    }

    private static string Sig(string path)
    {
        var f = new FileInfo(path);
        return f.Length.ToString(CultureInfo.InvariantCulture) + ":" + new DateTimeOffset(f.LastWriteTimeUtc).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }

    private static string Short(string text) => Cut(text, 150);

    private static string Cut(string text, int n) => text.Length <= n ? text : text[..n];
}
