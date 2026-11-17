using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>
/// Until the backup choice is made, nothing is shrunk, and Jellyfin has no way for a plug-in to show a pop-up. So the
/// reminder goes where an administrator looks: an entry in the Dashboard's Activity list when Jellyfin starts, and
/// again when a run or a new picture is skipped, at most once a day. Once the choice is made it writes nothing.
/// </summary>
public sealed class ChoiceReminder : IHostedService
{
    public const string Name = "Picture shrinker is waiting for you to choose";

    public const string ShortOverview = "Nothing is shrunk until you choose a backup option";

    public const string Overview = "Open Dashboard, Plugins, Picture shrinker, then Settings. Under Original pictures, choose Keep a backup copy of each original or Shrink without a backup, then save.";

    public static readonly TimeSpan Every = TimeSpan.FromDays(1);

    private readonly Func<bool> _chosen;
    private readonly IActivityManager _activity;
    private readonly ILogger _log;
    private readonly Func<DateTime> _now;
    private readonly object _lock = new();
    private DateTime? _last;

    public ChoiceReminder(ShrinkService service, IActivityManager activity, ILogger<ChoiceReminder> log)
        : this(() => service.Settings().Chosen, activity, log, () => DateTime.UtcNow)
    {
    }

    internal ChoiceReminder(Func<bool> chosen, IActivityManager activity, ILogger log, Func<DateTime> now)
    {
        _chosen = chosen;
        _activity = activity;
        _log = log;
        _now = now;
    }

    /// <summary>Writes the Activity entry when no choice is made and none was written in the last day. True when written.</summary>
    public bool Remind()
    {
        if (_chosen())
        {
            return false;
        }

        lock (_lock)
        {
            var now = _now();
            if (_last is { } last && now - last < Every)
            {
                return false;
            }

            _last = now;
        }

        try
        {
            _activity.CreateAsync(new ActivityLog(Name, "PictureShrinker", Guid.Empty)
            {
                ShortOverview = ShortOverview,
                Overview = Overview,
                LogSeverity = LogLevel.Warning
            }).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "Picture shrinker: could not add the reminder to choose to the Activity list");
            return false;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Remind();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
