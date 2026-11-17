using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudioDescription.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>
/// Play the described track, for each person who chooses it (inactive for everyone until chosen). When playback starts
/// on a title with a described track, and the sound track playing is not it, the plug-in sends the app Jellyfin's own
/// SetAudioStreamIndex command. Not every app accepts it: an app that does not say it does is left alone, and the log
/// says so.
/// </summary>
public sealed class Player : IHostedService
{
    /// <summary>The outcomes, as the log and the tests read them.</summary>
    public const string Sent = "sent";
    public const string NotChosen = "not chosen";
    public const string NoTrack = "no described track";
    public const string AlreadyPlaying = "already playing it";
    public const string NotSupported = "app does not accept it";
    public const string Moved = "playback moved on";
    public const string Again = "already sent";

    private readonly ISessionManager _sessions;
    private readonly IMediaSourceManager _media;
    private readonly Func<PluginConfiguration> _config;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, DateTime> _sent = new(StringComparer.Ordinal);

    public Player(ISessionManager sessions, IMediaSourceManager media, ILogger<Player> log)
        : this(sessions, media, () => Plugin.Instance?.Configuration ?? new PluginConfiguration(), log)
    {
    }

    internal Player(ISessionManager sessions, IMediaSourceManager media, Func<PluginConfiguration> config, ILogger log)
    {
        _sessions = sessions;
        _media = media;
        _config = config;
        _log = log;
    }

    /// <summary>Gets or sets how long to wait after playback starts, so the app is ready for a command.</summary>
    internal TimeSpan Wait { get; set; } = TimeSpan.FromSeconds(Rules.Current.Play.WaitSeconds);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessions.PlaybackStart += OnPlaybackStart;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessions.PlaybackStart -= OnPlaybackStart;
        return Task.CompletedTask;
    }

    /// <summary>The described track's index when it should be chosen, or null: none, or it is already playing.</summary>
    public static int? IndexToPlay(IEnumerable<MediaStream>? streams, int? playing)
    {
        var track = Words.DescribedTrack(streams);
        return track is null || track.Index == playing ? null : track.Index;
    }

    /// <summary>Whether an app says it accepts the command to change the sound track.</summary>
    public static bool Accepts(SessionInfo session)
        => (session.Capabilities?.SupportedCommands ?? Array.Empty<GeneralCommandType>()).Contains(GeneralCommandType.SetAudioStreamIndex);

    /// <summary>The command, as Jellyfin's own apps send it: Index is the stream's number in the file.</summary>
    public static GeneralCommand Command(int index)
    {
        var command = new GeneralCommand { Name = GeneralCommandType.SetAudioStreamIndex };
        command.Arguments[Rules.Current.Play.Argument] = index.ToString(CultureInfo.InvariantCulture);
        return command;
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        if (_config().PlayUsers.Length == 0 || e.Session is null || e.Item is null)
        {
            return;
        }

        var users = (e.Users ?? new()).Select(u => u.Id).ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                await ConsiderAsync(e.Session, e.Item, users, e.MediaSourceId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Audio description: could not choose the described track for {Client}", e.Session.Client);
            }
        });
    }

    /// <summary>Decides, waits for the app, checks again and sends the command. Returns what happened.</summary>
    internal async Task<string> ConsiderAsync(SessionInfo session, BaseItem item, IReadOnlyCollection<Guid> users, string? mediaSourceId)
    {
        var chosen = _config().PlayUsers.Select(u => Guid.TryParse(u, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToHashSet();
        if (!users.Any(chosen.Contains) && !chosen.Contains(session.UserId))
        {
            return NotChosen;
        }

        var source = Guid.TryParse(mediaSourceId, out var sid) && sid != Guid.Empty ? sid : item.Id;
        var streams = _media.GetMediaStreams(source);
        if (Words.DescribedTrack(streams) is null)
        {
            return NoTrack;
        }

        if (Wait > TimeSpan.Zero)
        {
            await Task.Delay(Wait).ConfigureAwait(false);
        }

        var now = _sessions.Sessions.FirstOrDefault(s => s.Id == session.Id) ?? session;
        if (now.NowPlayingItem is { } playingItem && playingItem.Id != item.Id)
        {
            return Moved;
        }

        var index = IndexToPlay(streams, now.PlayState?.AudioStreamIndex);
        if (index is null)
        {
            return AlreadyPlaying;
        }

        if (!Accepts(now))
        {
            _log.LogInformation(
                "Audio description: {Client} on {Device} does not accept the command to change the sound track, so {Title} plays as it is. Choose the described track in the app",
                now.Client,
                now.DeviceName,
                item.Name);
            return NotSupported;
        }

        var key = now.Id + "|" + item.Id.ToString("N");
        if (_sent.TryGetValue(key, out var at) && DateTime.UtcNow - at < TimeSpan.FromMinutes(1))
        {
            return Again;
        }

        _sent[key] = DateTime.UtcNow;
        foreach (var old in _sent.Where(kv => DateTime.UtcNow - kv.Value > TimeSpan.FromHours(1)).Select(kv => kv.Key).ToList())
        {
            _sent.TryRemove(old, out _);
        }

        await _sessions.SendGeneralCommand(null!, now.Id, Command(index.Value), CancellationToken.None).ConfigureAwait(false);
        _log.LogInformation("Audio description: chose the described track (stream {Index}) for {Title} on {Client}", index, item.Name, now.Client);
        return Sent;
    }
}
