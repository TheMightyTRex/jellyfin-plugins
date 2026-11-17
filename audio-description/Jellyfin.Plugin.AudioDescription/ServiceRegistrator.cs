using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>Adds the shared service, the playback rule and the watchers to Jellyfin.</summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<AudioDescriptionService>();
        serviceCollection.AddHostedService<Player>();
        serviceCollection.AddHostedService<Watcher>();
    }
}
