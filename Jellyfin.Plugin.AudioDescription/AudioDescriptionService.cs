using System;
using System.IO;
using Jellyfin.Plugin.AudioDescription.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>The one Find and tag and the one track adder that Jellyfin's tasks, the watchers and the settings page share.</summary>
public sealed class AudioDescriptionService
{
    private volatile string _nextWhy = "scheduled";

    public AudioDescriptionService(
        IServerApplicationPaths paths,
        ILibraryManager library,
        IMediaSourceManager media,
        ICollectionManager collections,
        IProviderManager providers,
        IFileSystem fileSystem,
        IMediaEncoder encoder,
        ILogger<AudioDescriptionService> log)
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Audio description is not loaded");
        Store = new StateStore(Path.Combine(plugin.DataFolderPath, "state.json"));
        Tagger = new Tagger(library, media, collections, () => Config, Store, log);
        Tracks = new TrackAdder(library, media, providers, fileSystem, new ProcessRunner(), () => (encoder.ProbePath, encoder.EncoderPath),
            () => Config, Path.Combine(paths.DataPath, "audio-description"), Store, Tagger, log);
    }

    public StateStore Store { get; }

    public Tagger Tagger { get; }

    public TrackAdder Tracks { get; }

    public PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>What starts the next Find and tag: a library scan or the button set it, the schedule is the default.</summary>
    public void StartedBy(string why) => _nextWhy = why;

    public string TakeWhy()
    {
        var why = _nextWhy;
        _nextWhy = "scheduled";
        return why;
    }
}
