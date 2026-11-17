using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PictureShrinker.Tasks;

/// <summary>Shrink pictures, in Jellyfin's Scheduled Tasks: every Sunday at 03:30 until the person changes it.</summary>
public class ShrinkPicturesTask : IScheduledTask
{
    private readonly ShrinkService _service;
    private readonly ChoiceReminder _reminder;
    private readonly ILogger<ShrinkPicturesTask> _log;

    public ShrinkPicturesTask(ShrinkService service, ChoiceReminder reminder, ILogger<ShrinkPicturesTask> log)
    {
        _service = service;
        _reminder = reminder;
        _log = log;
    }

    public string Name => "Shrink pictures";

    public string Key => "PictureShrinkerShrink";

    public string Description => "Shrinks posters, backdrops, logos, photos of cast and crew and the other picture types to the sizes on the Picture shrinker page, at the lowest priority. A run stops itself after 115 minutes and the next one carries on.";

    public string Category => "Picture shrinker";

    /// <summary>
    /// Nothing is shrunk until the backup choice is made. The task then ends as failed, with the reason, so Scheduled
    /// Tasks shows it in red instead of looking as if it worked (1.2.2.0 and 1.2.3.0).
    /// </summary>
    internal static void FailUntilChosen(RunRecord rec)
    {
        if (rec.Stopped == Shrinker.ChooseFirst)
        {
            throw new InvalidOperationException(Shrinker.ChooseFirst + ": Dashboard, Plugins, Picture shrinker, Settings, Original pictures");
        }
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var forced = _service.TakeForce();
        var month = DateTimeOffset.Now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        if (_service.Config.OnceAMonth && !forced && _service.Shrinker.Store.Load().LastFullRunMonth == month)
        {
            _log.LogInformation("Picture shrinker: Once a month is activated and it has already run this month, so it waits for next month");
            progress.Report(100);
            return;
        }

        var limit = TimeSpan.FromMinutes(Rules.Current.RunLimitMinutes);
        var rec = await Unix.RunAtLowestPriority(() => _service.Shrinker.Run("shrink", limit, cancellationToken, progress)).ConfigureAwait(false);
        if (rec is null)
        {
            _log.LogInformation("Picture shrinker: something else is running (new pictures, a restore or deleting the backup), so this run did not start");
            return;
        }

        if (rec.Stopped == Shrinker.ChooseFirst)
        {
            _reminder.Remind();
        }

        FailUntilChosen(rec);

        if (string.IsNullOrEmpty(rec.Stopped))
        {
            _service.Shrinker.Store.Update(s => s.LastFullRunMonth = month);
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.WeeklyTrigger,
            DayOfWeek = DayOfWeek.Sunday,
            TimeOfDayTicks = new TimeSpan(3, 30, 0).Ticks,
            MaxRuntimeTicks = TimeSpan.FromMinutes(120).Ticks
        };
    }
}
