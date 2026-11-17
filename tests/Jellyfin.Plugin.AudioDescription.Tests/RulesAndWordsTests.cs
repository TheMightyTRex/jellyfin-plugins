using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.AudioDescription.Configuration;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.AudioDescription.Tests;

/// <summary>The rules file both versions share, and the words that come from it: matching, file names and lengths.</summary>
public class RulesAndWordsTests
{
    private static Rules FromFile() => Rules.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules.json")));

    [Fact]
    public void The_built_in_rules_are_the_rules_file()
    {
        Assert.Equal(JsonSerializer.Serialize(FromFile()), JsonSerializer.Serialize(Rules.Current));
        Assert.Equal("1.0", Rules.Current.RulesVersion);
        Assert.Equal("Audio description", Rules.Current.Tag);
    }

    [Fact]
    public void Every_default_comes_from_the_rules()
    {
        var c = new PluginConfiguration();
        var r = FromFile();
        Assert.True(r.Collection.Default);
        Assert.Equal(r.Collection.Default, c.Collection);
        Assert.Equal(r.AfterLibraryScan, c.AfterLibraryScan);
        Assert.False(r.Play.Default);
        Assert.Empty(c.PlayUsers); // Play the described track is inactive for everyone until chosen
        Assert.Equal(5, r.Tracks.ToleranceSeconds.Default);
        Assert.Equal(r.Tracks.ToleranceSeconds.Default, c.ToleranceSeconds);
        Assert.Equal("eng", c.Language);
        Assert.Equal("SetAudioStreamIndex", r.Play.Command);
    }

    [Fact]
    public void The_shared_examples_of_described_tracks_match()
    {
        foreach (var t in Rules.Current.DescribedTrack.Examples.Described)
        {
            Assert.True(Words.IsDescribed(t), t);
        }
    }

    [Fact]
    public void The_shared_examples_of_other_tracks_do_not()
    {
        foreach (var t in Rules.Current.DescribedTrack.Examples.NotDescribed)
        {
            Assert.False(Words.IsDescribed(t), t);
        }
    }

    [Fact]
    public void Only_sound_tracks_count_and_the_first_described_one_is_chosen()
    {
        var streams = new[]
        {
            new MediaStream { Type = MediaStreamType.Subtitle, Index = 0, Title = "English (Audio Description)" },
            new MediaStream { Type = MediaStreamType.Audio, Index = 1, Title = "English" },
            new MediaStream { Type = MediaStreamType.Audio, Index = 2, Title = "Director's commentary" },
            new MediaStream { Type = MediaStreamType.Audio, Index = 3, Comment = "Audio description" },
            new MediaStream { Type = MediaStreamType.Audio, Index = 4, Title = "English AD" }
        };
        Assert.Equal(3, Words.DescribedTrack(streams)!.Index);
        Assert.Null(Words.DescribedTrack(streams.Take(3)));
        Assert.Null(Words.DescribedTrack(null));
    }

    [Fact]
    public void The_shared_examples_of_file_names_are_read_the_same_way()
    {
        Assert.NotEmpty(Rules.Current.Tracks.Names.Examples);
        foreach (var e in Rules.Current.Tracks.Names.Examples)
        {
            var p = Words.ParseName(e.File);
            Assert.Equal((e.Kind, e.Name, e.Year, e.Season, e.Episode), (p.Kind, p.Name, p.Year, p.Season, p.Episode));
        }
    }

    [Fact]
    public void Lengths_in_words()
    {
        Assert.Equal("1 hour 58 minutes 2 seconds", Words.LengthWords(7082));
        Assert.Equal("3 minutes 28 seconds", Words.LengthWords(-208));
        Assert.Equal("0 seconds", Words.LengthWords(0.2));
    }

    [Fact]
    public void The_new_track_is_named_after_the_film_and_its_language()
    {
        Assert.Equal(Path.Combine("/media/films/Dune (2021)", "Dune (2021).Audio Description.eng.mka"), Words.TargetFor("/media/films/Dune (2021)/Dune (2021).mkv", "eng"));
        Assert.Equal(Path.Combine("/tv/Slow Horses", "Slow Horses S01E03.Audio Description.fra.mka"), Words.TargetFor("/tv/Slow Horses/Slow Horses S01E03.mp4", "fra"));
        Assert.Equal("English (Audio Description)", Words.TrackTitle("eng"));
        Assert.Equal("French (Audio Description)", Words.TrackTitle("fra"));
        Assert.Equal("English (Audio Description)", Words.TrackTitle("xxx")); // an unknown language falls back to the default
    }

    [Fact]
    public void Names_are_compared_plainly()
    {
        Assert.Equal(Words.Norm("Amélie"), Words.Norm("Amelie"));
        Assert.Equal("fast and furious", Words.Norm("Fast & Furious"));
        Assert.Equal(Words.Norm("Spider-Man: No Way Home"), Words.Norm("Spider Man No Way Home"));
    }

    [Fact]
    public void Find_and_tag_follows_the_library_scan_only_while_active()
    {
        Assert.True(Watcher.FollowsScan("RefreshLibrary", true));
        Assert.False(Watcher.FollowsScan("RefreshLibrary", false));
        Assert.False(Watcher.FollowsScan("RefreshChapterImages", true));
        Assert.False(Watcher.FollowsScan(null, true));
    }
}
