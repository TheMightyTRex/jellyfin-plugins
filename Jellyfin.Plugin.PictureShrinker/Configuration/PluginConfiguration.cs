using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PictureShrinker.Configuration;

/// <summary>The settings on the plug-in's page. Every default comes from rules.json.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public int PosterHeight { get; set; } = Rules.Current.Poster.Default;

    public int PersonWidth { get; set; } = Rules.Current.Person.Default;

    public int Quality { get; set; } = Rules.Current.Quality.Default;

    public int BackdropWidth { get; set; } = Rules.Current.Backdrop.Default;

    public int ThumbWidth { get; set; } = Rules.Current.Thumb.Default;

    public int LogoWidth { get; set; } = Rules.Current.Logo.Default;

    public int BannerWidth { get; set; } = Rules.Current.Banner.Default;

    public int DiscWidth { get; set; } = Rules.Current.Disc.Default;

    public int ClearartWidth { get; set; } = Rules.Current.Clearart.Default;

    /// <summary>Gets or sets a value indicating whether every picture type is shrunk, whatever the ticks for each type.</summary>
    public bool ShrinkAll { get; set; } = Rules.Current.ShrinkAll.Default;

    public bool IncludePosters { get; set; } = Rules.Current.Poster.ShrinkByDefault;

    public bool IncludePeople { get; set; } = Rules.Current.Person.ShrinkByDefault;

    public bool IncludeBackdrops { get; set; } = Rules.Current.Backdrop.ShrinkByDefault;

    public bool IncludeThumbs { get; set; } = Rules.Current.Thumb.ShrinkByDefault;

    public bool IncludeLogos { get; set; } = Rules.Current.Logo.ShrinkByDefault;

    public bool IncludeBanners { get; set; } = Rules.Current.Banner.ShrinkByDefault;

    public bool IncludeDiscs { get; set; } = Rules.Current.Disc.ShrinkByDefault;

    public bool IncludeClearart { get; set; } = Rules.Current.Clearart.ShrinkByDefault;

    /// <summary>
    /// Gets or sets what happens to the original pictures: "keep" (a backup copy of each) or "none" (just shrink). It
    /// starts empty, and nothing is shrunk until the person chooses. The Backup setting of version 1.1 is no longer
    /// read, so after the update to 1.2 the choice is empty again and the person chooses once more.
    /// </summary>
    public string BackupChoice { get; set; } = Rules.Current.Backup.Default ?? string.Empty;

    /// <summary>Gets or sets the backup folder; empty means the default, in Jellyfin's data folder.</summary>
    public string BackupFolder { get; set; } = string.Empty;

    public bool ShrinkNewPictures { get; set; } = Rules.Current.NewPictures.Default;

    public int NewPictureMinutes { get; set; } = Rules.Current.NewPictures.MinAgeSeconds / 60;

    public bool OnceAMonth { get; set; }
}
