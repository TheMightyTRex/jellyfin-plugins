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
