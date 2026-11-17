using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>What the last Find and tag did.</summary>
public sealed class TagRecord
{
    public DateTimeOffset At { get; set; }

    /// <summary>Gets or sets what started it: "scheduled", "scan" (after a library scan), "now" or "track".</summary>
    public string Why { get; set; } = string.Empty;

    public int Checked { get; set; }

    public int Films { get; set; }

    public int Episodes { get; set; }

    public int Series { get; set; }

    public int Tagged { get; set; }

    public int Untagged { get; set; }

    public int AddedToCollection { get; set; }

    public int TakenFromCollection { get; set; }

    public string Problem { get; set; } = string.Empty;
}

/// <summary>A track in the folder: what it was matched to and what happened.</summary>
public sealed class TrackRecord
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the size and time of the file when it was last checked, so a changed file is checked again.</summary>
    public string Sig { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets new, choose, no_match, described, exists, out_of_reach, unreadable, unreadable_film, lengths, added,
    /// confirmed, taken_off or problem: the same words as Starboard's Audio description tracks plug-in.
    /// </summary>
    public string Status { get; set; } = "new";

    public string ItemId { get; set; } = string.Empty;

    public string ItemTitle { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Search { get; set; } = string.Empty;

    public double TrackSeconds { get; set; }

    public double FilmSeconds { get; set; }

    public string Said { get; set; } = string.Empty;

    public string Why { get; set; } = string.Empty;

    public List<Choice> Maybe { get; set; } = new();

    public string Added { get; set; } = string.Empty;

    public DateTimeOffset? AddedAt { get; set; }
}

/// <summary>A title a track may belong to.</summary>
public sealed class Choice
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}

/// <summary>Everything the plug-in remembers, in one small file in its data folder.</summary>
public sealed class State
{
    public TagRecord? LastTag { get; set; }

    public Dictionary<string, TrackRecord> Tracks { get; set; } = new(StringComparer.Ordinal);

    public DateTimeOffset? LastLook { get; set; }
}

/// <summary>Reads and writes the state file, one change at a time, replacing it in one step.</summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };
    private readonly string _path;
    private readonly object _lock = new();

    public StateStore(string path)
    {
        _path = path;
    }

    public State Load()
    {
        lock (_lock)
        {
            try
            {
                return File.Exists(_path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(_path), _json) ?? new State() : new State();
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                return new State();
            }
        }
    }

    public State Update(Action<State> change)
    {
        lock (_lock)
        {
            var s = Load();
            change(s);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, _json));
            File.Move(tmp, _path, true);
            return s;
        }
    }
}
