using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>When the backup was last deleted.</summary>
public sealed class DeleteRecord
{
    public DateTimeOffset At { get; set; }

    public int Files { get; set; }

    public long Bytes { get; set; }
}

/// <summary>
/// A picture Start again moved aside to a hidden file, waiting for Jellyfin to download a new one. It is kept in the
/// state, so the list survives a restart.
/// </summary>
public sealed class AsideRecord
{
    /// <summary>Gets or sets the picture's own full path; the old one is beside it, hidden.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets "poster", "person" or "backdrop".</summary>
    public string Kind { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }
}

/// <summary>What the last Start again did.</summary>
public sealed class StartAgainRecord
{
    public string Kind { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }

    /// <summary>Gets or sets when every refresh had been asked for; empty while it is still asking.</summary>
    public DateTimeOffset? Queued { get; set; }

    public int BackupFiles { get; set; }

    public long BackupBytes { get; set; }

    /// <summary>Gets or sets the pictures moved aside (the pictures it shrinks only).</summary>
    public int MovedAside { get; set; }

    public int Libraries { get; set; }

    public int Items { get; set; }

    public int People { get; set; }

    /// <summary>Gets or sets the films, series and people no longer in Jellyfin; their pictures were put back at once.</summary>
    public int NotFound { get; set; }
}

/// <summary>The totals and the last runs, kept in the plug-in's own folder.</summary>
public sealed class State
{
    public int Shrunk { get; set; }

    public long Before { get; set; }

    public long After { get; set; }

    public List<RunRecord> Runs { get; set; } = new();

    public string LastFullRunMonth { get; set; } = string.Empty;

    public RestoreRecord? LastRestore { get; set; }

    public DeleteRecord? LastDelete { get; set; }

    public StartAgainRecord? LastStartAgain { get; set; }

    /// <summary>Gets or sets the pictures moved aside, waiting for Jellyfin to download a new one.</summary>
    public List<AsideRecord> Aside { get; set; } = new();

    /// <summary>Gets or sets how many old pictures were deleted because Jellyfin saved a new one, since the last Start again.</summary>
    public int Replaced { get; set; }

    /// <summary>Gets or sets how many old pictures were put back after a day with no new one, since the last Start again.</summary>
    public int PutBack { get; set; }

    /// <summary>
    /// Clears the records for Start again: the runs and their failures, the space saved and the month of the last full
    /// run. The pictures still waiting are kept, so none is lost; the backup choice is in the settings and is not here.
    /// </summary>
    public void ClearRecords()
    {
        Shrunk = 0;
        Before = 0;
        After = 0;
        Runs.Clear();
        LastFullRunMonth = string.Empty;
        LastRestore = null;
        LastDelete = null;
        Replaced = 0;
        PutBack = 0;
    }

    public void AddRun(RunRecord rec)
    {
        Shrunk += rec.Shrunk;
        Before += rec.Before;
        After += rec.After;
        Runs.Add(rec);
        if (Runs.Count > Rules.Current.KeepRuns)
        {
            Runs.RemoveRange(0, Runs.Count - Rules.Current.KeepRuns);
        }
    }
}

/// <summary>Reads and writes state.json; each write goes to a new file swapped in whole.</summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly object _lock = new();
    private readonly string _path;

    public StateStore(string path) => _path = path;

    public State Load()
    {
        lock (_lock)
        {
            try
            {
                return File.Exists(_path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(_path)) ?? new State() : new State();
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                return new State();
            }
        }
    }

    public void Update(Action<State> change)
    {
        lock (_lock)
        {
            var s = Load();
            change(s);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, _json));
            File.Move(tmp, _path, overwrite: true);
        }
    }
}

/// <summary>Sizes in words, as the settings page shows them.</summary>
public static class Words
{
    public static string Size(long n)
    {
        double v = n;
        foreach (var (unit, by) in new[] { ("GB", 1e9), ("MB", 1e6), ("KB", 1e3) })
        {
            if (Math.Abs(v) >= by)
            {
                return $"{v / by:0.0} {unit}";
            }
        }

        return $"{n:N0} bytes";
    }
}
