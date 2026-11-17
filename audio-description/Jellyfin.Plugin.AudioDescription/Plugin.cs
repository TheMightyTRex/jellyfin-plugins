using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.AudioDescription.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>
/// Audio description: finds and tags the films and series with a described sound track, plays it for the people who
/// choose it, and adds a description track you already have next to its film.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>The plug-in id. Starboard's own audio description plug-ins look for it to stay idle when this one is installed.</summary>
    public const string PluginId = "7a591954-9c27-4ba7-80c3-dc5f0a140692";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>The name of the page Titles with audio description, in its address: #/configurationpage?name=AudioDescriptionTitles.</summary>
    public const string TitlesPage = "AudioDescriptionTitles";

    public static Plugin? Instance { get; private set; }

    public override string Name => "Audio description";

    public override Guid Id => Guid.Parse(PluginId);

    public override string Description => "Finds the films and series with a described sound track for blind and partially sighted people, tags them, plays the described track for the people who choose it, and adds a description track you already have.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
        };

        // Titles with audio description: a second page, opened from the tabs at the top of both pages.
        yield return new PluginPageInfo
        {
            Name = TitlesPage,
            DisplayName = "Titles with audio description",
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.titlesPage.html", GetType().Namespace)
        };
    }
}
