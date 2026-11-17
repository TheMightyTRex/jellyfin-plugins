using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PictureShrinker.Tasks;

/// <summary>Restore original pictures: puts the backed-up originals back. It runs only when the person chooses it.</summary>
public class RestorePicturesTask : IScheduledTask
{
    private readonly ShrinkService _service;
    private readonly ILogger<RestorePicturesTask> _log;

    public RestorePicturesTask(ShrinkService service, ILogger<RestorePicturesTask> log)
    {
        _service = service;
        _log = log;
    }

    public string Name => "Restore original pictures";

    public string Key => "PictureShrinkerRestore";

    public string Description => "Puts back the original pictures from the Picture shrinker backup. The backup is kept. Deactivate the Shrink pictures triggers first, or the next run shrinks them again.";

    public string Category => "Picture shrinker";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var rec = await Unix.RunAtLowestPriority(() => _service.Shrinker.Restore(cancellationToken, progress)).ConfigureAwait(false);
        if (rec is null)
        {
            _log.LogInformation("Picture shrinker: a run is going, so the restore did not start. Try again when it has finished");
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();
}
