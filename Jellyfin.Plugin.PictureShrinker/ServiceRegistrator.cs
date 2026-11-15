using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>Adds the shared shrinker and the new-picture watcher to Jellyfin.</summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ShrinkService>();
        serviceCollection.AddHostedService<NewPictureWatcher>();
    }
}
