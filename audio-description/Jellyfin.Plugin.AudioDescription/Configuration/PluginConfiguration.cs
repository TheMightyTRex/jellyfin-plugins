using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AudioDescription.Configuration;

/// <summary>The settings on the plug-in's page. Every default comes from rules.json.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets a value indicating whether each title with a described track also goes into the collection.</summary>
    public bool Collection { get; set; } = Rules.Current.Collection.Default;

    /// <summary>Gets or sets a value indicating whether Find and tag runs after each library scan.</summary>
    public bool AfterLibraryScan { get; set; } = Rules.Current.AfterLibraryScan;

    /// <summary>
    /// Gets or sets the people who chose Play the described track, by their Jellyfin user id. Empty until someone is
    /// chosen: it is inactive for everyone by default.
    /// </summary>
    public string[] PlayUsers { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the folder for description tracks; empty means the default, in Jellyfin's data folder.</summary>
    public string TracksFolder { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the folder is checked for new tracks every few minutes.</summary>
    public bool WatchFolder { get; set; } = true;

    public string Language { get; set; } = Rules.Current.Tracks.DefaultLanguage;

    public int ToleranceSeconds { get; set; } = Rules.Current.Tracks.ToleranceSeconds.Default;

    /// <summary>Gets or sets how many films Titles with audio description lists, newest added first: 0 for all, else 10, 20, 50, 100 or 500.</summary>
    public int FilmsShown { get; set; } = DescribedTitles.ShownDefault;

    /// <summary>Gets or sets how many series Titles with audio description lists, by their newest described episode: 0 for all, else 10, 20, 50, 100 or 500.</summary>
    public int SeriesShown { get; set; } = DescribedTitles.ShownDefault;
}
