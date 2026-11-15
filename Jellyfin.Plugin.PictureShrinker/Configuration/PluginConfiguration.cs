using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PictureShrinker.Configuration;

/// <summary>The settings on the plug-in's page. Every default comes from rules.json.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public int PosterHeight { get; set; } = Rules.Current.Poster.Default;

    public int PersonWidth { get; set; } = Rules.Current.Person.Default;

    public int Quality { get; set; } = Rules.Current.Quality.Default;

    public bool IncludePosters { get; set; } = true;

    public bool IncludePeople { get; set; } = true;

    public bool Backup { get; set; } = Rules.Current.Backup.Default;

    /// <summary>Gets or sets the backup folder; empty means the default, in Jellyfin's data folder.</summary>
    public string BackupFolder { get; set; } = string.Empty;

    public bool ShrinkNewPictures { get; set; } = Rules.Current.NewPictures.Default;

    public int NewPictureMinutes { get; set; } = Rules.Current.NewPictures.MinAgeSeconds / 60;

    public bool OnceAMonth { get; set; }
}
