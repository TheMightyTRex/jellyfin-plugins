using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudioDescription.Configuration;
using MediaBrowser.Model.IO;
using Xunit;

namespace Jellyfin.Plugin.AudioDescription.Tests;

/// <summary>
/// Jellyfin's ffprobe and ffmpeg, answering from what each test gives: the true length of each file by its name. A copy
/// made by ffmpeg has its source's length; a sound file read straight has none, like an MP3 with no length in its header.
/// </summary>
public sealed class FakeTools : IRunner
{
    public Dictionary<string, double> Lengths { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Made { get; } = new(StringComparer.Ordinal);

    public List<IReadOnlyList<string>> Ffmpeg { get; } = new();

    public Task<RunResult> RunAsync(string program, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (program == "ffprobe")
        {
            var path = arguments[^1];
            var name = Made.GetValueOrDefault(path) ?? Path.GetFileName(path);
            if (!Lengths.TryGetValue(name, out var seconds))
            {
                return Task.FromResult(new RunResult(1, string.Empty, "Invalid data found when processing input"));
            }

            var video = name.EndsWith(".mkv", StringComparison.Ordinal) || name.EndsWith(".mp4", StringComparison.Ordinal);
            var duration = video || Made.ContainsKey(path) ? $"\"duration\": \"{seconds.ToString(CultureInfo.InvariantCulture)}\"" : string.Empty;
            var streams = (video ? "{\"index\": 0, \"codec_type\": \"video\"}, " : string.Empty) + "{\"index\": 1, \"codec_type\": \"audio\"}";
            return Task.FromResult(new RunResult(0, "{\"streams\": [" + streams + "], \"format\": {" + duration + "}}", string.Empty));
        }

        Assert.Equal("ffmpeg", program);
        Ffmpeg.Add(arguments);
        var output = arguments[^1];
        File.WriteAllText(output, "matroska");
        Made[output] = Path.GetFileName(arguments[arguments.ToList().IndexOf("-i") + 1]);
        return Task.FromResult(new RunResult(0, string.Empty, string.Empty));
    }
}

/// <summary>Add a track you have: names, the length check, the file it writes, the person's say, zips and uploads.</summary>
public sealed class TrackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ad-tracks-" + Guid.NewGuid().ToString("N"));
    private readonly FakeJellyfin _jf = new();
    private readonly FakeTools _tools = new();
    private readonly PluginConfiguration _config = new();

    public TrackTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "films"));
        Directory.CreateDirectory(Inbox);
    }

    private string Inbox => Path.Combine(_dir, "audio-description");

    public void Dispose() => Directory.Delete(_dir, true);

    private TrackAdder Make() => new(_jf.Library(), _jf.Media(), _jf.Providers(), Fake<IFileSystem>.Make((_, _) => null), _tools,
        () => ("ffprobe", "ffmpeg"), () => _config, Inbox, new StateStore(Path.Combine(_dir, "state.json")), null, new ListLogger());

    private MediaBrowser.Controller.Entities.Movies.Movie Film(string name, int year, params MediaBrowser.Model.Entities.MediaStream[] streams)
    {
        var folder = Path.Combine(_dir, "films", $"{name} ({year})");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{name} ({year}).mkv");
        File.WriteAllText(path, "film");
        return _jf.Film(name, year, path, streams);
    }

    private void Drop(string name, string body = "ID3") => File.WriteAllText(Path.Combine(Inbox, name), body);

    private TrackRecord Rec(TrackAdder t, string name) => t.Store.Load().Tracks[name];

    [Fact]
    public async Task A_matching_track_is_added_next_to_the_film_without_converting_and_the_original_kept()
    {
        var dune = Film("Dune", 2021);
        _tools.Lengths["Dune (2021).mp3"] = 9302.4;
        _tools.Lengths["Dune (2021).mkv"] = 9300.0;
        Drop("Dune (2021).mp3");
        var t = Make();

        Assert.True(t.HasNew());
        Assert.Equal("Checked 1 new file, added 1 track", await t.LookAsync(default));

        var added = Path.Combine(_dir, "films", "Dune (2021)", "Dune (2021).Audio Description.eng.mka");
        Assert.True(File.Exists(added));
        var args = Assert.Single(_tools.Ffmpeg);
        foreach (var part in new[] { "-c", "copy", "-map", "0:a:0", "language=eng", "title=English (Audio Description)", "-disposition:a:0", "visual_impaired", "matroska" })
        {
            Assert.Contains(part, args);
        }

        Assert.True(File.Exists(Path.Combine(Inbox, "Dune (2021).mp3"))); // kept until the person says it plays in step
        Assert.Empty(Directory.GetFiles(Inbox, ".*")); // no working copies left
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(added)!, ".*"));
        Assert.Equal(new[] { dune.Id }, _jf.Refreshed);
        var rec = Rec(t, "Dune (2021).mp3");
        Assert.Equal(("added", added, 9302.4, 9300.0), (rec.Status, rec.Added, rec.TrackSeconds, rec.FilmSeconds));
        var row = TrackRows.Row(rec, "eng");
        Assert.Equal(("added", "Added", true), (row.Group, row.Label, row.AskPlaysInStep));
        Assert.Equal("No new files in the folder", await t.LookAsync(default)); // checked once only
        Assert.False(t.HasNew());
    }

    [Fact]
    public async Task A_different_cut_is_explained_and_nothing_is_written_next_to_the_film()
    {
        Film("Dune", 2021);
        _tools.Lengths["Dune (2021).mp3"] = 9508.0;
        _tools.Lengths["Dune (2021).mkv"] = 9300.0;
        Drop("Dune (2021).mp3");
        var t = Make();
        await t.LookAsync(default);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "films", "Dune (2021)"), "*.mka"));
        Assert.Empty(Directory.GetFiles(Inbox, ".*")); // its check copy removed
        var row = TrackRows.Row(Rec(t, "Dune (2021).mp3"), "eng");
        Assert.Equal(("warning", "Lengths differ"), (row.State, row.Label));
        Assert.Contains("3 minutes 28 seconds longer than the film", row.Detail, StringComparison.Ordinal);
        Assert.Contains("another cut", row.Detail, StringComparison.Ordinal);
        Assert.True(row.Detail.Length <= 200);
        Assert.Empty(_jf.Refreshed);
    }

    [Fact]
    public async Task The_tolerance_is_used_and_held_to_its_range()
    {
        Film("Dune", 2021);
        _tools.Lengths["Dune (2021).mp3"] = 9325.0;
        _tools.Lengths["Dune (2021).mkv"] = 9300.0;
        Drop("Dune (2021).mp3");
        _config.ToleranceSeconds = 30;
        Assert.Contains("added 1 track", await Make().LookAsync(default), StringComparison.Ordinal);
        _config.ToleranceSeconds = 900;
        Assert.Equal(30, Make().Tolerance);
        _config.ToleranceSeconds = 0;
        Assert.Equal(1, Make().Tolerance);
    }

    [Fact]
    public async Task Exactly_at_the_tolerance_is_added_and_just_past_it_is_not()
    {
        Film("Dune", 2021);
        Film("Heat", 1995);
        _tools.Lengths["Dune (2021).mp3"] = 9305.0;
        _tools.Lengths["Dune (2021).mkv"] = 9300.0;
        _tools.Lengths["Heat (1995).mp3"] = 10205.1;
        _tools.Lengths["Heat (1995).mkv"] = 10200.0;
        Drop("Dune (2021).mp3");
        Drop("Heat (1995).mp3");
        var t = Make();
        await t.LookAsync(default);
        Assert.Equal("added", Rec(t, "Dune (2021).mp3").Status);
        Assert.Equal("lengths", Rec(t, "Heat (1995).mp3").Status);
    }

    [Fact]
    public async Task A_film_with_a_described_track_or_a_track_added_already_is_left_alone()
    {
        Film("Dune", 2021, FakeJellyfin.Audio(1, "English"), FakeJellyfin.Audio(2, "English (Audio Description) - AAC"));
        _tools.Lengths["Dune (2021).mp3"] = 9300;
        _tools.Lengths["Dune (2021).mkv"] = 9300;
        Drop("Dune (2021).mp3");
        var brazil = Film("Brazil", 1985);
        var theirs = Path.Combine(Path.GetDirectoryName(brazil.Path)!, "Brazil (1985).Audio Description.eng.mka");
        File.WriteAllText(theirs, "theirs");
        _tools.Lengths["Brazil (1985).mp3"] = 8000;
        _tools.Lengths["Brazil (1985).mkv"] = 8000;
        Drop("Brazil (1985).mp3");
        var t = Make();
        await t.LookAsync(default);
        Assert.Equal("Already described", TrackRows.Row(Rec(t, "Dune (2021).mp3"), "eng").Label);
        Assert.Equal("Already added", TrackRows.Row(Rec(t, "Brazil (1985).mp3"), "eng").Label);
        Assert.Equal("theirs", File.ReadAllText(theirs)); // never replaced
        Assert.Empty(_tools.Ffmpeg);
    }

    [Fact]
    public async Task Choosing_between_two_titles_then_adding()
    {
        Film("Heat", 1995);
        var old = Film("Heat", 1986);
        _tools.Lengths["Heat.mp3"] = 6000;
        _tools.Lengths["Heat (1986).mkv"] = 6001;
        _tools.Lengths["Heat (1995).mkv"] = 10200;
        Drop("Heat.mp3");
        var t = Make();
        await t.LookAsync(default);
        var row = TrackRows.Row(Rec(t, "Heat.mp3"), "eng");
        Assert.Equal("Choose the title", row.Label);
        Assert.Equal(2, row.Options.Count);
        Assert.Equal("Added to Heat (1986). Play it in Jellyfin to check it", await t.ChooseAsync("Heat.mp3", old.Id.ToString("N"), default));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(old.Path)!, "Heat (1986).Audio Description.eng.mka")));
    }

    [Fact]
    public async Task Episodes_are_found_by_series_and_number()
    {
        var horses = _jf.Show("Slow Horses");
        var folder = Path.Combine(_dir, "tv", "Slow Horses");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Slow Horses S01E03.mkv");
        File.WriteAllText(path, "episode");
        _jf.Ep(horses, 1, 2, Path.Combine(folder, "Slow Horses S01E02.mkv"));
        var ep = _jf.Ep(horses, 1, 3, path);
        _tools.Lengths["Slow Horses S01E03.mp3"] = 2700;
        _tools.Lengths["Slow Horses S01E03.mkv"] = 2702;
        Drop("Slow Horses S01E03.mp3");
        var t = Make();
        await t.LookAsync(default);
        var rec = Rec(t, "Slow Horses S01E03.mp3");
        Assert.Equal(("added", "Slow Horses, series 1 episode 3", ep.Id.ToString("N")), (rec.Status, rec.ItemTitle, rec.ItemId));
        Assert.True(File.Exists(Path.Combine(folder, "Slow Horses S01E03.Audio Description.eng.mka")));
    }

    [Fact]
    public async Task A_name_that_matches_nothing_says_how_to_name_it()
    {
        Drop("Something Else (2020).mp3");
        var t = Make();
        await t.LookAsync(default);
        var row = TrackRows.Row(Rec(t, "Something Else (2020).mp3"), "eng");
        Assert.Equal("No match", row.Label);
        Assert.StartsWith("No film called Something Else is in Jellyfin. Rename the file", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Yes_puts_the_original_away_and_No_takes_the_track_off_and_nothing_is_deleted()
    {
        var dune = Film("Dune", 2021);
        var heat = Film("Heat", 1995);
        foreach (var (n, s) in new[] { ("Dune (2021).mp3", 9300.0), ("Dune (2021).mkv", 9300.0), ("Heat (1995).mp3", 10200.0), ("Heat (1995).mkv", 10200.0) })
        {
            _tools.Lengths[n] = s;
        }

        Drop("Dune (2021).mp3");
        Drop("Heat (1995).mp3");
        var t = Make();
        await t.LookAsync(default);

        Assert.Equal("Confirmed for Dune (2021). The original is in the Confirmed folder", t.Outcome("Dune (2021).mp3", "right"));
        Assert.True(File.Exists(Path.Combine(Inbox, "Confirmed", "Dune (2021).mp3")));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(dune.Path)!, "Dune (2021).Audio Description.eng.mka")));
        Assert.Equal(("done", "Confirmed"), (TrackRows.Row(Rec(t, "Dune (2021).mp3"), "eng").Group, TrackRows.Row(Rec(t, "Dune (2021).mp3"), "eng").Label));

        Assert.Equal("Taken off Heat (1995). The track is in the Taken off folder", t.Outcome("Heat (1995).mp3", "undo"));
        Assert.True(File.Exists(Path.Combine(Inbox, "Taken off", "Heat (1995).Audio Description.eng.mka")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(heat.Path)!, "Heat (1995).Audio Description.eng.mka")));
        Assert.True(File.Exists(Path.Combine(Inbox, "Heat (1995).mp3"))); // the original stays, to try again
        Assert.Equal("taken_off", Rec(t, "Heat (1995).mp3").Status);
        Assert.Contains(heat.Id, _jf.Refreshed.Skip(2)); // Jellyfin reads it again without the track
        Assert.Throws<ArgumentException>(() => t.Outcome("Heat (1995).mp3", "right"));
    }

    [Fact]
    public async Task A_zip_is_unpacked_by_names_only_and_kept()
    {
        using (var zip = ZipFile.Open(Path.Combine(Inbox, "Slow Horses.zip"), ZipArchiveMode.Create))
        {
            foreach (var n in new[] { "Season 1/Slow Horses S01E01.mp3", "../../Slow Horses S01E02.mp3", "notes.txt" })
            {
                using var w = new StreamWriter(zip.CreateEntry(n).Open());
                w.Write("ID3");
            }
        }

        var t = Make();
        await t.LookAsync(default);
        Assert.True(File.Exists(Path.Combine(Inbox, "Slow Horses S01E01.mp3")));
        Assert.True(File.Exists(Path.Combine(Inbox, "Slow Horses S01E02.mp3"))); // inside the folder, whatever the zip says
        Assert.False(File.Exists(Path.Combine(Inbox, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(Inbox, "Unzipped", "Slow Horses.zip")));
        Assert.False(File.Exists(Path.Combine(_dir, "Slow Horses S01E02.mp3")));
    }

    [Fact]
    public async Task An_upload_is_streamed_into_the_folder_and_a_second_with_the_same_name_kept_apart()
    {
        var t = Make();
        var body = Encoding.ASCII.GetBytes("ID3 sound");
        Assert.Equal("Uploaded Dune (2021).mp3. It is being checked", await t.UploadAsync("Dune (2021).mp3", new MemoryStream(body), body.Length, default));
        Assert.Equal("Uploaded Dune (2021) (2).mp3. It is being checked", await t.UploadAsync("C:\\Users\\x\\Dune (2021).mp3", new MemoryStream(body), body.Length, default));
        Assert.Equal("ID3 sound", File.ReadAllText(Path.Combine(Inbox, "Dune (2021).mp3")));
        await Assert.ThrowsAsync<ArgumentException>(() => t.UploadAsync("notes.txt", new MemoryStream(body), body.Length, default));
        await Assert.ThrowsAsync<ArgumentException>(() => t.UploadAsync("Dune.mp3", new MemoryStream(body), body.Length + 10, default));
        Assert.Empty(Directory.GetFiles(Inbox, ".*")); // its unfinished copy removed
    }

    [Fact]
    public async Task A_film_Jellyfin_found_no_details_for_keeps_its_year_in_its_name_and_still_matches()
    {
        var folder = Path.Combine(_dir, "films", "Quiet Harbour (2019)");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Quiet Harbour (2019).mkv");
        File.WriteAllText(path, "film");
        _jf.Film("Quiet Harbour (2019)", 2019, path);
        _tools.Lengths["Quiet Harbour (2019).mp3"] = 61;
        _tools.Lengths["Quiet Harbour (2019).mkv"] = 60;
        Drop("Quiet Harbour (2019).mp3");
        var t = Make();
        await t.LookAsync(default);
        var rec = Rec(t, "Quiet Harbour (2019).mp3");
        Assert.Equal(("added", "Quiet Harbour (2019)"), (rec.Status, rec.ItemTitle)); // the year once, not twice
        Assert.True(Words.SameName("Dune (2021)", "Dune"));
        Assert.False(Words.SameName("Dune Part Two", "Dune"));
    }

    [Fact]
    public void Hidden_files_links_and_other_files_in_the_folder_are_not_read()
    {
        Drop(".hidden.mp3");
        Drop("notes.txt");
        Drop("Dune (2021).m4a");
        File.CreateSymbolicLink(Path.Combine(Inbox, "link.mp3"), Path.Combine(Inbox, "Dune (2021).m4a"));
        Assert.Equal(new[] { "Dune (2021).m4a" }, Make().InboxFiles());
    }

    [Fact]
    public void The_folder_chosen_in_the_settings_is_used()
    {
        Assert.Equal(Inbox, Make().Inbox);
        _config.TracksFolder = " /media/audio-description ";
        Assert.Equal("/media/audio-description", Make().Inbox);
    }
}
