using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudioDescription.Tasks;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AudioDescription.Api;

/// <summary>What the settings page reads and does: the last Find and tag, the people, the tracks and their buttons. Administrators only.</summary>
[ApiController]
[Route("AudioDescription")]
[Authorize(Policy = Policies.RequiresElevation)]
[Produces("application/json")]
public class AudioDescriptionController : ControllerBase
{
    private readonly AudioDescriptionService _service;
    private readonly ITaskManager _tasks;
    private readonly IUserManager _users;

    public AudioDescriptionController(AudioDescriptionService service, ITaskManager tasks, IUserManager users)
    {
        _service = service;
        _tasks = tasks;
        _users = users;
    }

    /// <summary>The last Find and tag, the collection, the people in Jellyfin and each track in the folder.</summary>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> Status()
    {
        var state = _service.Store.Load();
        var box = _service.Tagger.FindCollection();
        var tracks = _service.Tracks;
        return new
        {
            Tag = Tagger.Tag,
            CollectionName = Tagger.CollectionName,
            CollectionId = box?.Id.ToString("N") ?? string.Empty,
            LastTag = state.LastTag,
            Tagging = _service.Tagger.Running,
            Users = AllUsers().Select(u => new { Id = u.Id.ToString("N"), Name = u.Username }).OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase),
            Folder = tracks.Inbox,
            DefaultFolder = tracks.DefaultFolder,
            Checking = tracks.Running,
            state.LastLook,
            Tracks = TrackRows.Rows(state.Tracks.Values, tracks.Language),
            Groups = TrackRows.Groups,
            Tolerance = tracks.Tolerance,
            TrackTitle = Words.TrackTitle(tracks.Language),
            Languages = Rules.Current.Tracks.Languages,
            RulesVersion = Rules.Current.RulesVersion
        };
    }

#if JELLYFIN_12
    private System.Collections.Generic.IEnumerable<Jellyfin.Database.Implementations.Entities.User> AllUsers() => _users.GetUsers();
#else
    private System.Collections.Generic.IEnumerable<Jellyfin.Database.Implementations.Entities.User> AllUsers() => _users.Users;
#endif

    /// <summary>Starts Find and tag now.</summary>
    [HttpPost("FindNow")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> FindNow()
    {
        if (_service.Tagger.Running)
        {
            return new { Done = false, Said = "Already finding and tagging. Scheduled Tasks shows how far it has got." };
        }

        _service.StartedBy("now");
        _tasks.QueueIfNotRunning<FindAndTagTask>();
        return new { Done = true, Said = "Finding and tagging. The figures here change when it ends." };
    }

    /// <summary>Checks the track folder now.</summary>
    [HttpPost("CheckFolder")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> CheckFolder()
    {
        if (_service.Tracks.Running)
        {
            return new { Done = false, Said = "Already checking the folder." };
        }

        _tasks.QueueIfNotRunning<AddTracksTask>();
        return new { Done = true, Said = "Checking the folder. Select Refresh the list in a minute." };
    }

    /// <summary>Checks a track against the title chosen for it.</summary>
    [HttpPost("Tracks/Choose")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> Choose([FromQuery] string name, [FromQuery] string itemId, CancellationToken cancellationToken)
    {
        try
        {
            return new { Done = true, Said = await _service.Tracks.ChooseAsync(name, itemId, cancellationToken).ConfigureAwait(false) + "." };
        }
        catch (ArgumentException e)
        {
            return new { Done = false, Said = e.Message + "." };
        }
    }

    /// <summary>Whether an added track plays in step: right (put the original away) or undo (take the track off).</summary>
    [HttpPost("Tracks/Outcome")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> Outcome([FromQuery] string name, [FromQuery] string value)
    {
        try
        {
            return new { Done = true, Said = _service.Tracks.Outcome(name, value) + "." };
        }
        catch (ArgumentException e)
        {
            return new { Done = false, Said = e.Message + "." };
        }
    }

    /// <summary>A track chosen on the page, sent as the request's body, then checked with the rest of the folder.</summary>
    [HttpPost("Tracks/Upload")]
    [DisableRequestSizeLimit]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> Upload([FromQuery] string name, CancellationToken cancellationToken)
    {
        try
        {
            var said = await _service.Tracks.UploadAsync(name, Request.Body, Request.ContentLength ?? 0, cancellationToken).ConfigureAwait(false);
            _tasks.QueueIfNotRunning<AddTracksTask>();
            return new { Done = true, Said = said + "." };
        }
        catch (ArgumentException e)
        {
            return new { Done = false, Said = e.Message + "." };
        }
    }
}
