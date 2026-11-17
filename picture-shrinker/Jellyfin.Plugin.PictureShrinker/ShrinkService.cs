using System;
using System.IO;
using System.Threading;
using Jellyfin.Plugin.PictureShrinker.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>
/// The one shrinker Jellyfin's tasks, the new-picture watcher and the settings page share, so two never run at once.
/// It also checks the pictures Start again moved aside, every 10 minutes while any are waiting and not otherwise.
/// </summary>
public sealed class ShrinkService : IDisposable
{
    private readonly IServerApplicationPaths _paths;
    private readonly ILogger<ShrinkService> _log;
    private readonly Timer _settle;
    private volatile bool _force;

    public ShrinkService(IServerApplicationPaths paths, ILibraryManager library, IProviderManager providers, IFileSystem fileSystem, ILogger<ShrinkService> log)
    {
        _paths = paths;
        _log = log;
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Picture shrinker is not loaded");
        Shrinker = new Shrinker(Settings, new StateStore(Path.Combine(plugin.DataFolderPath, "state.json")), log);
        Refresher = new JellyfinRefresher(library, providers, fileSystem);
        _settle = new Timer(_ => SettleTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public Shrinker Shrinker { get; }

    public IPictureRefresher Refresher { get; }

    public PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    public string MetadataRoot => _paths.InternalMetadataPath;

    public string DefaultBackupFolder => Path.Combine(_paths.DataPath, Rules.Current.Backup.Folder);

    /// <summary>Shrink now on the settings page: the next run happens even with Once a month activated.</summary>
    public void ForceNext() => _force = true;

    public bool TakeForce()
    {
        var was = _force;
        _force = false;
        return was;
    }

    public ShrinkSettings Settings()
    {
        var c = Config;
        var r = Rules.Current;
        return new ShrinkSettings
        {
            MetadataRoot = _paths.InternalMetadataPath,
            PosterHeight = Math.Clamp(c.PosterHeight, r.Poster.Min, r.Poster.Max),
            PersonWidth = Math.Clamp(c.PersonWidth, r.Person.Min, r.Person.Max),
            BackdropWidth = Math.Clamp(c.BackdropWidth, r.Backdrop.Min, r.Backdrop.Max),
            ThumbWidth = Math.Clamp(c.ThumbWidth, r.Thumb.Min, r.Thumb.Max),
            LogoWidth = Math.Clamp(c.LogoWidth, r.Logo.Min, r.Logo.Max),
            BannerWidth = Math.Clamp(c.BannerWidth, r.Banner.Min, r.Banner.Max),
            DiscWidth = Math.Clamp(c.DiscWidth, r.Disc.Min, r.Disc.Max),
            ClearartWidth = Math.Clamp(c.ClearartWidth, r.Clearart.Min, r.Clearart.Max),
            Quality = Math.Clamp(c.Quality, r.Quality.Min, r.Quality.Max),
            Posters = c.IncludePosters,
            People = c.IncludePeople,
            Backdrops = c.IncludeBackdrops,
            Thumbs = c.IncludeThumbs,
            Logos = c.IncludeLogos,
            Banners = c.IncludeBanners,
            Discs = c.IncludeDiscs,
            Clearart = c.IncludeClearart,
            ShrinkAll = c.ShrinkAll,
            Choice = (c.BackupChoice ?? string.Empty).Trim(),
            BackupRoot = string.IsNullOrWhiteSpace(c.BackupFolder) ? DefaultBackupFolder : c.BackupFolder.Trim()
        };
    }

    /// <summary>Start again, from the settings page: the refreshes are asked for in the background.</summary>
    public StartAgainResult StartAgain(string kind)
    {
        var result = Shrinker.StartAgain(kind, Refresher, wait: false);
        if (result.Done)
        {
            ArmSettling();
        }

        return result;
    }

    /// <summary>Starts the check of the pictures moved aside when any are waiting; it stops itself when none are.</summary>
    public void ArmSettling()
    {
        if (Shrinker.Store.Load().Aside.Count > 0)
        {
            var every = TimeSpan.FromMinutes(Rules.Current.StartAgain.SettleEveryMinutes);
            _settle.Change(every, every);
        }
    }

    private void SettleTick()
    {
        try
        {
            var got = Shrinker.Settle();
            if (got is not null && got.Waiting == 0)
            {
                _settle.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "Picture shrinker: the pictures moved aside were not checked");
        }
    }

    public void Dispose() => _settle.Dispose();
}
