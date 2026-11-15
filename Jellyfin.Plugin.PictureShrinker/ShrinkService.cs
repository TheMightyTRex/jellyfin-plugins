using System;
using System.IO;
using Jellyfin.Plugin.PictureShrinker.Configuration;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>The one shrinker Jellyfin's tasks, the new-picture watcher and the settings page share, so two never run at once.</summary>
public sealed class ShrinkService
{
    private readonly IServerApplicationPaths _paths;
    private volatile bool _force;

    public ShrinkService(IServerApplicationPaths paths, ILogger<ShrinkService> log)
    {
        _paths = paths;
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Picture shrinker is not loaded");
        Shrinker = new Shrinker(Settings, new StateStore(Path.Combine(plugin.DataFolderPath, "state.json")), log);
    }

    public Shrinker Shrinker { get; }

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
            Quality = Math.Clamp(c.Quality, r.Quality.Min, r.Quality.Max),
            Posters = c.IncludePosters,
            People = c.IncludePeople,
            Backup = c.Backup,
            BackupRoot = string.IsNullOrWhiteSpace(c.BackupFolder) ? DefaultBackupFolder : c.BackupFolder.Trim()
        };
    }
}
