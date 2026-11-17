using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudioDescription.Configuration;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AudioDescription.Tests;

/// <summary>Play the described track: only for the people who choose it, only when another track plays, only to apps that accept it.</summary>
public class PlayerTests
{
    private readonly FakeJellyfin _jf = new();
    private readonly PluginConfiguration _config = new();
    private readonly List<(string Session, GeneralCommand Command)> _sent = new();
    private readonly List<SessionInfo> _sessions = new();
    private readonly ListLogger _log = new();
    private readonly Guid _sam = Guid.NewGuid();
    private readonly Guid _alex = Guid.NewGuid();

    private ISessionManager Sessions() => Fake<ISessionManager>.Make((m, a) =>
    {
        switch (m.Name)
        {
            case "get_Sessions":
                return _sessions;
            case nameof(ISessionManager.SendGeneralCommand):
                _sent.Add(((string)a[1]!, (GeneralCommand)a[2]!));
                return Task.CompletedTask;
            default:
                throw new NotSupportedException("The fake session manager has no " + m.Name);
        }
    });

    private Player Make() => new(Sessions(), _jf.Media(), () => _config, _log) { Wait = TimeSpan.Zero };

    private SessionInfo Session(Guid user, Guid item, int? playing, bool accepts = true)
    {
        var s = new SessionInfo(Sessions(), NullLogger.Instance)
        {
            Id = "session-" + _sessions.Count,
            UserId = user,
            Client = accepts ? "Jellyfin Web" : "Some TV app",
            DeviceName = "Living room",
            Capabilities = new ClientCapabilities
            {
                SupportedCommands = accepts ? new[] { GeneralCommandType.SetAudioStreamIndex, GeneralCommandType.SetSubtitleStreamIndex } : new[] { GeneralCommandType.DisplayMessage }
            },
            PlayState = new PlayerStateInfo { AudioStreamIndex = playing },
            NowPlayingItem = new BaseItemDto { Id = item }
        };
        _sessions.Add(s);
        return s;
    }

    [Fact]
    public void Inactive_for_everyone_until_chosen()
    {
        Assert.Empty(new PluginConfiguration().PlayUsers);
        Assert.False(Rules.Current.Play.Default);
    }

    [Fact]
    public async Task For_someone_who_chose_it_the_described_track_is_asked_for()
    {
        _config.PlayUsers = new[] { _sam.ToString("N") };
        var dune = _jf.Film("Dune", 2021, "", FakeJellyfin.Audio(1, "English"), FakeJellyfin.Audio(2, "English (Audio Description)"));
        var s = Session(_sam, dune.Id, 1);
        Assert.Equal(Player.Sent, await Make().ConsiderAsync(s, dune, new[] { _sam }, null));
        var (session, command) = Assert.Single(_sent);
        Assert.Equal(s.Id, session);
        Assert.Equal(GeneralCommandType.SetAudioStreamIndex, command.Name);
        Assert.Equal("2", command.Arguments["Index"]);
    }

    [Fact]
    public async Task Nothing_is_sent_when_the_described_track_already_plays()
    {
        _config.PlayUsers = new[] { _sam.ToString() };
        var dune = _jf.Film("Dune", 2021, "", FakeJellyfin.Audio(1, "English"), FakeJellyfin.Audio(2, "AD"));
        Assert.Equal(Player.AlreadyPlaying, await Make().ConsiderAsync(Session(_sam, dune.Id, 2), dune, new[] { _sam }, null));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task Nothing_is_sent_for_someone_who_did_not_choose_it_or_a_title_with_no_described_track()
    {
        _config.PlayUsers = new[] { _sam.ToString("N") };
        var dune = _jf.Film("Dune", 2021, "", FakeJellyfin.Audio(1, "English"), FakeJellyfin.Audio(2, "AD"));
        var brazil = _jf.Film("Brazil", 1985, "", FakeJellyfin.Audio(1, "English"), FakeJellyfin.Audio(2, "Director's commentary"));
        var p = Make();
        Assert.Equal(Player.NotChosen, await p.ConsiderAsync(Session(_alex, dune.Id, 1), dune, new[] { _alex }, null));
        Assert.Equal(Player.NoTrack, await p.ConsiderAsync(Session(_sam, brazil.Id, 1), brazil, new[] { _sam }, null));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task An_app_that_does_not_accept_the_command_is_left_alone_and_the_log_says_so()
    {
        _config.PlayUsers = new[] { _sam.ToString("N") };
        var dune = _jf.Film("Dune", 2021, "", FakeJellyfin.Audio(1, "English"), FakeJellyfin.Audio(2, "AD"));
        Assert.Equal(Player.NotSupported, await Make().ConsiderAsync(Session(_sam, dune.Id, 1, accepts: false), dune, new[] { _sam }, null));
        Assert.Empty(_sent);
        var line = Assert.Single(_log.Lines);
        Assert.Contains("Some TV app", line, StringComparison.Ordinal);
        Assert.Contains("does not accept the command to change the sound track", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_is_sent_once_playback_has_moved_on_or_twice_for_the_same_start()
    {
        _config.PlayUsers = new[] { _sam.ToString("N") };
        var dune = _jf.Film("Dune", 2021, "", FakeJellyfin.Audio(1, "English"), FakeJellyfin.Audio(2, "AD"));
        var brazil = _jf.Film("Brazil", 1985);
        var p = Make();
        Assert.Equal(Player.Moved, await p.ConsiderAsync(Session(_sam, brazil.Id, 1), dune, new[] { _sam }, null));
        var s = Session(_sam, dune.Id, 1);
        Assert.Equal(Player.Sent, await p.ConsiderAsync(s, dune, new[] { _sam }, null));
        Assert.Equal(Player.Again, await p.ConsiderAsync(s, dune, new[] { _sam }, null));
        Assert.Single(_sent);
    }

    [Fact]
    public void The_rule_on_its_own()
    {
        var streams = new[] { FakeJellyfin.Audio(0, "English"), FakeJellyfin.Audio(3, "Descriptive audio") };
        Assert.Equal(3, Player.IndexToPlay(streams, 0));
        Assert.Equal(3, Player.IndexToPlay(streams, null));
        Assert.Null(Player.IndexToPlay(streams, 3));
        Assert.Null(Player.IndexToPlay(streams.Take(1), 0));
        Assert.Equal("7", Player.Command(7).Arguments["Index"]);
    }
}
