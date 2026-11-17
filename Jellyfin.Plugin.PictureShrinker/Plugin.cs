using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.PictureShrinker.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>Picture shrinker: shrinks the posters and photos of people Jellyfin keeps, so they take less space.</summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>The plug-in id. Starboard's own picture shrinker looks for it to stay idle when this one is installed.</summary>
    public const string PluginId = "80bf9325-d299-4e24-ac74-c087aeec3011";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Paths = applicationPaths;
    }

    public static Plugin? Instance { get; private set; }

    public IApplicationPaths Paths { get; }

    public override string Name => "Picture shrinker";

    public override Guid Id => Guid.Parse(PluginId);

    public override string Description => "Shrinks the posters, backdrops, logos, photos of people and other pictures Jellyfin keeps, so they take less space. Each smaller copy is checked before it replaces the original, and you choose whether each original is backed up first.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
        };
    }
}
