using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudioDescription.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AudioDescription.Tests;

/// <summary>Find and tag: the tag on films, episodes and their series, off again when the track goes, and the collection.</summary>
public sealed class TaggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ad-tag-" + Guid.NewGuid().ToString("N"));
    private readonly FakeJellyfin _jf = new();
    private readonly PluginConfiguration _config = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private Tagger Make() => new(_jf.Library(), _jf.Media(), _jf.Collections(), () => _config, new StateStore(Path.Combine(_dir, "state.json")), NullLogger.Instance);

    [Fact]
    public async Task Films_episodes_and_their_series_with_a_described_track_are_tagged_and_collected()
    {
        var dune = _jf.Film("Dune", 2021, "", FakeJellyfin.Audio(1, "English"), FakeJellyfin.Audio(2, "English (Audio Description)"));
        var brazil = _jf.Film("Brazil", 1985);
        var horses = _jf.Show("Slow Horses");
        var ep1 = _jf.Ep(horses, 1, 1, "", FakeJellyfin.Audio(1, "English"), FakeJellyfin.Audio(2, "AD"));
        var ep2 = _jf.Ep(horses, 1, 2);
        var other = _jf.Show("Taskmaster");
        _jf.Ep(other, 1, 1);
        dune.Tags = new[] { "Favourite" };

        var rec = await Make().RunAsync("now", null, default);

        Assert.NotNull(rec);
        Assert.Equal((1, 1, 1, 5), (rec!.Films, rec.Episodes, rec.Series, rec.Checked));
        Assert.Equal(new[] { "Favourite", "Audio description" }, dune.Tags); // other tags are kept
        Assert.Contains("Audio description", ep1.Tags);
        Assert.Contains("Audio description", horses.Tags);
        Assert.DoesNotContain("Audio description", brazil.Tags ?? Array.Empty<string>());
        Assert.DoesNotContain("Audio description", ep2.Tags ?? Array.Empty<string>());
        Assert.DoesNotContain("Audio description", other.Tags ?? Array.Empty<string>());
        Assert.Equal(3, rec.Tagged);
        Assert.Equal(new[] { dune.Id, ep1.Id, horses.Id }.OrderBy(g => g), _jf.Updated.OrderBy(g => g)); // only what changed is written
        var box = Assert.Single(_jf.Boxes);
        Assert.Equal("Audio description", box.Name);
        Assert.Equal(new[] { dune.Id, horses.Id }.OrderBy(g => g), box.LinkedChildren.Select(c => c.ItemId!.Value).OrderBy(g => g)); // films and series, not episodes
    }

    [Fact]
    public async Task The_tag_and_the_place_in_the_collection_go_when_the_track_goes()
    {
        var dune = _jf.Film("Dune", 2021, "", FakeJellyfin.Audio(2, "Descriptive audio"));
        var heat = _jf.Film("Heat", 1995, "", FakeJellyfin.Audio(2, "English AD"));
        var tagger = Make();
        await tagger.RunAsync("scheduled", null, default);
        _jf.Updated.Clear();
        _jf.Streams[dune.Id] = new() { FakeJellyfin.Audio(1, "English") };

        var rec = await tagger.RunAsync("scan", null, default);

        Assert.Equal((1, 1, 0), (rec!.Films, rec.Untagged, rec.Tagged));
        Assert.Empty(dune.Tags);
        Assert.Contains("Audio description", heat.Tags);
        Assert.Equal(new[] { dune.Id }, _jf.Updated);
        Assert.Equal(new[] { heat.Id }, Assert.Single(_jf.Boxes).LinkedChildren.Select(c => c.ItemId!.Value));
        Assert.Equal(1, rec.TakenFromCollection);
        await tagger.RunAsync("scheduled", null, default);
        Assert.Equal(new[] { "create Audio description 2", "remove 1" }, _jf.CollectionCalls); // nothing more once in step
    }

    [Fact]
    public async Task With_the_collection_inactive_only_tags_change()
    {
        _config.Collection = false;
        _jf.Film("Dune", 2021, "", FakeJellyfin.Audio(2, "Audio Description"));
        var rec = await Make().RunAsync("now", null, default);
        Assert.Equal(1, rec!.Tagged);
        Assert.Empty(_jf.CollectionCalls);
        Assert.Empty(_jf.Boxes);
    }

    [Fact]
    public async Task No_collection_is_made_while_nothing_is_described()
    {
        _jf.Film("Brazil", 1985);
        await Make().RunAsync("now", null, default);
        Assert.Empty(_jf.CollectionCalls);
    }

    [Fact]
    public async Task The_last_run_is_kept_for_the_settings_page()
    {
        _jf.Film("Dune", 2021, "", FakeJellyfin.Audio(2, "AD"));
        var tagger = Make();
        await tagger.RunAsync("scan", null, default);
        var kept = new StateStore(Path.Combine(_dir, "state.json")).Load().LastTag;
        Assert.Equal(("scan", 1), (kept!.Why, kept.Films));
    }

    [Fact]
    public async Task A_film_given_a_track_is_tagged_once_Jellyfin_reads_it_again()
    {
        var dune = _jf.Film("Dune", 2021);
        var tagger = Make();
        tagger.Expect(dune.Id);
        Assert.True(tagger.IsExpected(dune.Id));
        Assert.False(await tagger.TagOneAsync(dune, default)); // Jellyfin has not read the new track yet
        Assert.True(tagger.IsExpected(dune.Id));
        _jf.Streams[dune.Id].Add(FakeJellyfin.Audio(2, "English (Audio Description)"));
        Assert.True(await tagger.TagOneAsync(dune, default));
        Assert.Contains("Audio description", dune.Tags);
        Assert.False(tagger.IsExpected(dune.Id));
        Assert.Equal(new[] { dune.Id }, Assert.Single(_jf.Boxes).LinkedChildren.Select(c => c.ItemId!.Value));
    }

    [Fact]
    public async Task An_episode_given_a_track_tags_its_series_and_puts_the_series_in_the_collection()
    {
        var horses = _jf.Show("Slow Horses");
        var ep = _jf.Ep(horses, 1, 3, "", FakeJellyfin.Audio(2, "English (Audio Description)"));
        var existing = new BoxSet { Id = Guid.NewGuid(), Name = "Audio description", LinkedChildren = Array.Empty<MediaBrowser.Controller.Entities.LinkedChild>() };
        _jf.Items[existing.Id] = existing;
        Assert.True(await Make().TagOneAsync(ep, default));
        Assert.Contains("Audio description", horses.Tags);
        Assert.Equal(new[] { horses.Id }, existing.LinkedChildren.Select(c => c.ItemId!.Value));
        Assert.Equal(new[] { "add 1" }, _jf.CollectionCalls);
    }
}
