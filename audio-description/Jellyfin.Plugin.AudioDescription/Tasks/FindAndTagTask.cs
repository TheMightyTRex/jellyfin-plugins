using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.AudioDescription.Tasks;

/// <summary>Find and tag, in Jellyfin's Scheduled Tasks: every night at 04:20, and after each library scan.</summary>
public class FindAndTagTask : IScheduledTask
{
    private readonly AudioDescriptionService _service;

    public FindAndTagTask(AudioDescriptionService service)
    {
        _service = service;
    }

    public string Name => "Find and tag audio description";

    public string Key => "AudioDescriptionFindAndTag";

    public string Description => "Reads the sound tracks of every film and episode, and tags each one with a described track, and its series, with Audio description. While the collection is active, adds them to the Audio description collection too. Takes the tag off when the track goes.";

    public string Category => "Audio description";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var rec = await _service.Tagger.RunAsync(_service.TakeWhy(), progress, cancellationToken).ConfigureAwait(false);
        if (rec is not null && !string.IsNullOrEmpty(rec.Problem))
        {
            throw new InvalidOperationException("Find and tag did not finish: " + rec.Problem);
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = new TimeSpan(4, 20, 0).Ticks,
            MaxRuntimeTicks = TimeSpan.FromMinutes(60).Ticks
        };
    }
}
