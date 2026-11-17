using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudioDescription.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>
/// What sets the plug-in going between its scheduled runs, each a cheap check: Find and tag after each library scan
/// (Jellyfin's Scan Media Library task), a film tagged once Jellyfin has read the track added to it, and the track
/// folder checked every few minutes for new files (one listing of the folder, nothing read).
/// </summary>
public sealed class Watcher : IHostedService, IDisposable
{
    /// <summary>The key of Jellyfin's own Scan Media Library task.</summary>
    public const string LibraryScanKey = "RefreshLibrary";

    public static readonly TimeSpan FolderEvery = TimeSpan.FromMinutes(5);

    private readonly ITaskManager _tasks;
    private readonly ILibraryManager _library;
    private readonly AudioDescriptionService _service;
    private readonly ILogger<Watcher> _log;
    private Timer? _timer;

    public Watcher(ITaskManager tasks, ILibraryManager library, AudioDescriptionService service, ILogger<Watcher> log)
    {
        _tasks = tasks;
        _library = library;
        _service = service;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _tasks.TaskCompleted += OnTaskCompleted;
        _library.ItemUpdated += OnItemUpdated;
        _timer = new Timer(_ => CheckFolder(), null, TimeSpan.FromMinutes(1), FolderEvery);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _tasks.TaskCompleted -= OnTaskCompleted;
        _library.ItemUpdated -= OnItemUpdated;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    /// <summary>Whether a finished task is the library scan that Find and tag follows.</summary>
    public static bool FollowsScan(string? key, bool afterScan) => afterScan && string.Equals(key, LibraryScanKey, StringComparison.Ordinal);

    private void OnTaskCompleted(object? sender, TaskCompletionEventArgs e)
    {
        if (FollowsScan(e.Result?.Key, _service.Config.AfterLibraryScan) && !_service.Tagger.Running)
        {
            _service.StartedBy("scan");
            _tasks.QueueIfNotRunning<FindAndTagTask>();
        }
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        var item = e.Item;
        if (item is not (Movie or Episode) || !_service.Tagger.IsExpected(item.Id))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _service.Tagger.TagOneAsync(item, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Audio description: could not tag {Title}", item.Name);
            }
        });
    }

    private void CheckFolder()
    {
        try
        {
            if (_service.Config.WatchFolder && !_service.Tracks.Running && _service.Tracks.HasNew())
            {
                _tasks.QueueIfNotRunning<AddTracksTask>();
            }
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "Audio description: could not check the track folder");
        }
    }
}
