using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>
/// When Jellyfin starts, carries on checking any pictures Start again moved aside before it stopped. With none
/// waiting, it does nothing at all.
/// </summary>
public sealed class PictureSettler : IHostedService
{
    private readonly ShrinkService _service;

    public PictureSettler(ShrinkService service) => _service = service;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _service.ArmSettling();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
