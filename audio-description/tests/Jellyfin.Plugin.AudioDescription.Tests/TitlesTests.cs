using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudioDescription.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AudioDescription.Tests;

/// <summary>
/// Titles with audio description: every tagged film and described episode by series, with its year, track and status
/// in words, the same words as the Starboard plug-in's page.
/// </summary>
public sealed class TitlesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ad-titles-" + Guid.NewGuid().ToString("N"));
    private readonly FakeJellyfin _jf = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private Tagger Make() => new(_jf.Library(), _jf.Media(), _jf.Collections(), () => new PluginConfiguration(), new StateStore(Path.Combine(_dir, "state.json")), NullLogger.Instance);

    [Fact]
    public async Task Films_and_described_episodes_by_series_after_find_and_tag()
    {
        _jf.Film("Harbour Lights", 2019, "", FakeJellyfin.Audio(1, "English"), FakeJellyfin.Audio(2, "Audio Description"));
        _jf.Film("Copper Sky", 2021);
        var valley = _jf.Show("Quiet Valley");
        valley.ProductionYear = 2023;
        var e3 = _jf.Ep(valley, 1, 3, "", FakeJellyfin.Audio(1, "English (Described)"));
        _jf.Ep(valley, 1, 2);
        var e1 = _jf.Ep(valley, 1, 1, "", FakeJellyfin.Audio(1, "English (Described)"));
        var tagger = Make();
        Assert.Empty(tagger.Titles(null).Films); // nothing is tagged before Find and tag runs

        await tagger.RunAsync("now", null, default);
        var at = DateTimeOffset.Now;
        var answer = tagger.Titles(at);

        var film = Assert.Single(answer.Films);
        Assert.Equal(("Harbour Lights", 2019, "Audio Description", "Described"), (film.Title, film.Year, film.Track, film.Status));
        var series = Assert.Single(answer.Series);
        Assert.Equal(("Quiet Valley (2023)", 2, 3), (series.Title, series.Described, series.Episodes));
        Assert.Equal("2 of 3 episodes described", series.Status);
        Assert.Equal(new[] { "S1 E1: Episode 1", "S1 E3: Episode 3" }, series.Items.Select(e => e.Title));
        Assert.Equal(new[] { e1.Id.ToString("N"), e3.Id.ToString("N") }, series.Items.Select(e => e.Id));
        Assert.Equal(2, answer.EpisodeCount);
        Assert.Equal(at, answer.Checked);
    }

    [Fact]
    public void A_tag_without_a_track_is_left_out_until_find_and_tag_takes_it_off()
    {
        var film = _jf.Film("Paper Moons", 2018);
        film.Tags = new[] { "Audio description" };
        Assert.Empty(Make().Titles(null).Films);
    }

    [Fact]
    public async Task Last_20_newest_first_by_default_and_a_series_keeps_all_its_episodes()
    {
        var day = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        for (var n = 1; n <= 25; n++)
        {
            var f = _jf.Film($"Film {n:00}", 2020, "", FakeJellyfin.Audio(1, "AD"));
            f.DateCreated = day.AddDays(n);
        }

        for (var n = 1; n <= 23; n++)
        {
            var show = _jf.Show($"Show {n:00}");
            for (var k = 1; k <= 3; k++)
            {
                var ep = _jf.Ep(show, 1, k, "", FakeJellyfin.Audio(1, "AD"));
                ep.DateCreated = day.AddDays(n).AddHours(k);
            }
        }

        var tagger = Make();
        await tagger.RunAsync("now", null, default);
        var answer = tagger.Titles(DateTimeOffset.Now);

        Assert.Equal((25, 23, 20, 20), (answer.FilmTotal, answer.SeriesTotal, answer.FilmsShown, answer.SeriesShown));
        Assert.Equal(20, answer.Films.Count);
        Assert.Equal(new[] { "Film 25", "Film 24" }, answer.Films.Take(2).Select(f => f.Title)); // newest first
        Assert.Equal(20, answer.Series.Count);
        Assert.Equal("Show 23", answer.Series[0].Name);
        Assert.All(answer.Series, s => Assert.Equal(3, s.Items.Count)); // every described episode of a listed series
        Assert.Equal(60, answer.EpisodeCount);

        var all = DescribedTitles.Build(_jf.Library(), id => "AD", null, 0, 10);
        Assert.Equal((25, 10), (all.Films.Count, all.Series.Count));
        Assert.Equal(20, DescribedTitles.Shown(7)); // an unknown value is the default
    }

    [Theory]
    [InlineData(4, 4, "All 4 episodes described")]
    [InlineData(1, 1, "1 of 1 episode described")]
    [InlineData(3, 6, "3 of 6 episodes described")]
    [InlineData(1200, 1300, "1,200 of 1,300 episodes described")]
    public void A_series_status_in_words(int described, int episodes, string words)
    {
        Assert.Equal(words, DescribedTitles.SeriesWords(described, episodes));
    }

    [Theory]
    [InlineData(1, 3, "Part three", "S1 E3: Part three")]
    [InlineData(2, 1, "", "S2 E1")]
    [InlineData(null, null, "Special", "Special")]
    [InlineData(null, 4, "", "An episode")]
    public void An_episode_in_words(int? season, int? episode, string name, string words)
    {
        Assert.Equal(words, DescribedTitles.EpisodeWords(season, episode, name));
    }
}
