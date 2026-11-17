using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>
/// Shrink new pictures soon after Jellyfin saves them (deactivated by default). Jellyfin's library events name the
/// pictures; each waits until it is a few minutes old, as Jellyfin may still be writing it, then is shrunk with the
/// same checks and backup as a scheduled run. While deactivated it only notes nothing: one check of a setting per event.
/// Until the backup choice is made, it says so in the log and shrinks nothing.
/// </summary>
public sealed class NewPictureWatcher : IHostedService, IDisposable
{
    private readonly ILibraryManager _library;
    private readonly ShrinkService _service;
    private readonly ChoiceReminder _reminder;
    private readonly ILogger<NewPictureWatcher> _log;
    private readonly ConcurrentDictionary<string, DateTime> _waiting = new();
    private Timer? _timer;
    private int _busy;

    public NewPictureWatcher(ILibraryManager library, ShrinkService service, ChoiceReminder reminder, ILogger<NewPictureWatcher> log)
    {
        _library = library;
        _service = service;
        _reminder = reminder;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded += OnItemChanged;
        _library.ItemUpdated += OnItemChanged;
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded -= OnItemChanged;
        _library.ItemUpdated -= OnItemChanged;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (!_service.Config.ShrinkNewPictures || e.Item?.ImageInfos is null)
        {
            return;
        }

        var root = _service.MetadataRoot;
        foreach (var image in e.Item.ImageInfos.Where(i => i.IsLocalFile && !string.IsNullOrEmpty(i.Path)))
        {
            if (Shrinker.KindOf(root, image.Path) is not null)
            {
                _waiting[image.Path] = DateTime.UtcNow;
            }
        }
    }

    private void Tick()
    {
        if (_waiting.IsEmpty || !_service.Config.ShrinkNewPictures || _service.Shrinker.Running || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            if (!_service.Config.ShrinkNewPictures)
            {
                _waiting.Clear();
            }

            return;
        }

        try
        {
            if (!_service.Settings().Chosen)
            {
                _log.LogInformation("Picture shrinker: {Said}", Shrinker.ChooseFirst);
                _reminder.Remind();
                _waiting.Clear();
                return;
            }

            var age = TimeSpan.FromMinutes(Math.Max(1, _service.Config.NewPictureMinutes));
            var ready = _waiting.Where(kv => DateTime.UtcNow - kv.Value >= age && Older(kv.Key, age)).Select(kv => kv.Key).ToList();
            if (ready.Count == 0)
            {
                return;
            }

            var limit = TimeSpan.FromMinutes(Rules.Current.RunLimitMinutes);
            var rec = Unix.RunAtLowestPriority(() => _service.Shrinker.Run("new", limit, CancellationToken.None, null, ready)).GetAwaiter().GetResult();
            if (rec is not null)
            {
                foreach (var path in ready)
                {
                    _waiting.TryRemove(path, out _);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Picture shrinker: new pictures were not shrunk");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static bool Older(string path, TimeSpan age)
    {
        try
        {
            return !File.Exists(path) || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= age;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
