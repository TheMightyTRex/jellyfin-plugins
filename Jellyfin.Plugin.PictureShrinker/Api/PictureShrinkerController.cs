using System;
using System.Linq;
using MediaBrowser.Common.Api;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.PictureShrinker.Api;

/// <summary>What the settings page reads and does: the space, the runs, Shrink now and Delete backup. Administrators only.</summary>
[ApiController]
[Route("PictureShrinker")]
[Authorize(Policy = Policies.RequiresElevation)]
[Produces("application/json")]
public class PictureShrinkerController : ControllerBase
{
    private readonly ShrinkService _service;
    private readonly ITaskManager _tasks;

    public PictureShrinkerController(ShrinkService service, ITaskManager tasks)
    {
        _service = service;
        _tasks = tasks;
    }

    /// <summary>The space now (measured as the page asks), the last runs and what is running.</summary>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> Status()
    {
        var shrinker = _service.Shrinker;
        var settings = _service.Settings();
        var state = shrinker.Store.Load();
        return new
        {
            Space = shrinker.Measure(),
            Running = shrinker.Busy,
            Progress = shrinker.Progress,
            Problem = settings.Problem(),
            MetadataFolder = settings.MetadataRoot,
            BackupFolder = settings.BackupRoot,
            DefaultBackupFolder = _service.DefaultBackupFolder,
            RulesVersion = Rules.Current.RulesVersion,
            Runs = state.Runs.AsEnumerable().Reverse().Take(10),
            state.LastRestore,
            state.LastDelete
        };
    }

    /// <summary>Starts Shrink pictures now, even with Once a month activated.</summary>
    [HttpPost("ShrinkNow")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> ShrinkNow()
    {
        if (_service.Shrinker.Running)
        {
            return new { Done = false, Said = "Already running. This page says how far it has gone." };
        }

        _service.ForceNext();
        _tasks.QueueIfNotRunning<Tasks.ShrinkPicturesTask>();
        return new { Done = true, Said = "Shrinking pictures at the lowest priority. Scheduled Tasks shows how far it has gone." };
    }

    /// <summary>Deletes the backup for good. Refused while a run, a restore or another delete is going.</summary>
    [HttpPost("DeleteBackup")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> DeleteBackup()
    {
        var result = _service.Shrinker.DeleteBackup();
        return new { result.Done, result.Files, result.Bytes, result.Said, Space = _service.Shrinker.Measure() };
    }
}
