using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.PictureShrinker.Tasks;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.PictureShrinker.Tests;

/// <summary>Until the backup choice is made: an entry in the Activity list, at most once a day, and a failed task (1.2.2.0 and 1.2.3.0).</summary>
public class ChoiceReminderTests
{
    private readonly List<ActivityLog> _written = new();
    private DateTime _now = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
    private bool _chosen;

    private ChoiceReminder Make()
    {
        var activity = Fake<IActivityManager>.Make((m, a) =>
        {
            if (m.Name == "CreateAsync")
            {
                _written.Add((ActivityLog)a[0]!);
                return Task.CompletedTask;
            }

            throw new NotSupportedException(m.Name);
        });
        return new ChoiceReminder(() => _chosen, activity, NullLogger.Instance, () => _now);
    }

    [Fact]
    public async Task Jellyfin_starting_with_no_choice_adds_a_warning_to_the_activity_list()
    {
        await Make().StartAsync(default);
        var entry = Assert.Single(_written);
        Assert.Equal(ChoiceReminder.Name, entry.Name);
        Assert.Equal(ChoiceReminder.ShortOverview, entry.ShortOverview);
        Assert.Contains("Original pictures", entry.Overview, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Warning, entry.LogSeverity);
    }

    [Fact]
    public void At_most_once_a_day()
    {
        var r = Make();
        Assert.True(r.Remind());
        _now = _now.AddHours(23);
        Assert.False(r.Remind());
        _now = _now.AddHours(2);
        Assert.True(r.Remind());
        Assert.Equal(2, _written.Count);
    }

    [Fact]
    public async Task Nothing_is_written_once_the_choice_is_made()
    {
        _chosen = true;
        var r = Make();
        await r.StartAsync(default);
        Assert.False(r.Remind());
        Assert.Empty(_written);
    }

    [Fact]
    public void The_shrink_task_fails_with_the_reason_until_the_choice_is_made()
    {
        var e = Assert.Throws<InvalidOperationException>(() => ShrinkPicturesTask.FailUntilChosen(new RunRecord { Stopped = Shrinker.ChooseFirst }));
        Assert.StartsWith(Shrinker.ChooseFirst, e.Message, StringComparison.Ordinal);
        ShrinkPicturesTask.FailUntilChosen(new RunRecord()); // a run that shrank: no failure
        ShrinkPicturesTask.FailUntilChosen(new RunRecord { Stopped = "Stopped after 115 minutes" });
    }
}
