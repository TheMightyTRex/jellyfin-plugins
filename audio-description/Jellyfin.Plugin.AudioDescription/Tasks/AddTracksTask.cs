using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioDescription.Tasks;

/// <summary>Add description tracks, in Jellyfin's Scheduled Tasks: every night at 04:40, and when the folder has a new file.</summary>
public class AddTracksTask : IScheduledTask
{
    private readonly AudioDescriptionService _service;
    private readonly ILogger<AddTracksTask> _log;

    public AddTracksTask(AudioDescriptionService service, ILogger<AddTracksTask> log)
    {
        _service = service;
        _log = log;
    }

    public string Name => "Add description tracks";

    public string Key => "AudioDescriptionAddTracks";

    public string Description => "Checks the folder for description tracks. Each new track is matched to its film or episode by its name, its length is compared with the film's, and when they agree it is added next to the film as English (Audio Description).";

    public string Category => "Audio description";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var said = await _service.Tracks.LookAsync(cancellationToken).ConfigureAwait(false);
        _log.LogInformation("Audio description: {Said}", said);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = new TimeSpan(4, 40, 0).Ticks,
            MaxRuntimeTicks = TimeSpan.FromMinutes(60).Ticks
        };
    }
}
