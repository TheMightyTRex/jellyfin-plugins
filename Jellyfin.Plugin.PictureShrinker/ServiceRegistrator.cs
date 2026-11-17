using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>Adds the shared shrinker, the new-picture watcher and the check of pictures moved aside to Jellyfin.</summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ShrinkService>();
        serviceCollection.AddHostedService<NewPictureWatcher>();
        serviceCollection.AddHostedService<PictureSettler>();
    }
}
