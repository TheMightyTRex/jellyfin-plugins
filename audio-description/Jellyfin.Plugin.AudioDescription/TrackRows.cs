using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>One row of the track list on the settings page.</summary>
public sealed class TrackRow
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets "check", "added" or "done": the three parts of the list.</summary>
    public string Group { get; set; } = "check";

    /// <summary>Gets or sets ok, warning, problem, off or waiting.</summary>
    public string State { get; set; } = "waiting";

    public string Label { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    /// <summary>Gets or sets the titles it may be, for Choose the title: the one it was matched to first.</summary>
    public List<Choice> Options { get; set; } = new();

    public string Chosen { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether Yes and No are offered: the track was added and waits for a check.</summary>
    public bool AskPlaysInStep { get; set; }
}

/// <summary>The words for each track's state, the same as Starboard's Audio description tracks plug-in.</summary>
public static class TrackRows
{
    public static readonly IReadOnlyDictionary<string, string> Groups = new Dictionary<string, string>
    {
        ["check"] = "To check",
        ["added"] = "Added: check each plays in step",
        ["done"] = "Done"
    };

    public static TrackRow Row(TrackRecord rec, string language)
    {
        var t = rec.ItemTitle;
        var row = new TrackRow { Name = rec.Name };
        var folders = Rules.Current.Tracks.Folders;
        switch (rec.Status)
        {
            case "added":
                row.Group = "added";
                row.State = "ok";
                row.Label = "Added";
                row.Detail = Cut($"Added to {t} as {Words.TrackTitle(language)}. Play it in Jellyfin, then say whether it plays in step. {rec.Said}".Trim(), 200);
                row.AskPlaysInStep = true;
                return row;
            case "confirmed":
                row.Group = "done";
                row.State = "ok";
                row.Label = "Confirmed";
                row.Detail = $"In {t}. The original is in the {folders.Confirmed} folder: delete it there when you like.";
                return row;
            case "taken_off":
                row.State = "off";
                row.Label = "Taken off";
                row.Detail = $"The added track is in the {folders.TakenOff} folder, and the original is still here. Choose a title to try again.";
                break;
            case "lengths":
                var diff = rec.TrackSeconds - rec.FilmSeconds;
                row.State = "warning";
                row.Label = "Lengths differ";
                row.Detail = Cut($"{t}: the track is {Words.LengthWords(diff)} {(diff > 0 ? "longer" : "shorter")} than the film, so it is probably for another cut, such as an extended one. Find one for this cut.", 200);
                break;
            case "choose":
                row.State = "warning";
                row.Label = "Choose the title";
                row.Detail = "It is not sure which title this is. Choose the right one.";
                break;
            case "no_match":
                row.State = "warning";
                row.Label = "No match";
                row.Detail = Cut($"{(string.IsNullOrEmpty(rec.Why) ? "Nothing in Jellyfin matches this name" : rec.Why)}. Rename the file as the film and year, such as Dune (2021).mp3, or a series as Slow Horses S01E03.mp3.", 200);
                break;
            case "described":
                row.State = "ok";
                row.Label = "Already described";
                row.Detail = $"{t} already has a described sound track in Jellyfin, so nothing was added. Choose another title if this is wrong.";
                break;
            case "exists":
                row.State = "ok";
                row.Label = "Already added";
                row.Detail = $"{t} already has a description track file next to it, so nothing was added or replaced.";
                break;
            case "out_of_reach":
                row.State = "problem";
                row.Label = "Out of reach";
                row.Detail = $"Jellyfin cannot open the file of {t}, so this plug-in cannot add a track next to it.";
                break;
            case "unreadable":
                row.State = "problem";
                row.Label = "Cannot read";
                row.Detail = "This file could not be read as sound. Check that it plays, then put it here again.";
                break;
            case "unreadable_film":
                row.State = "problem";
                row.Label = "Cannot read film";
                row.Detail = $"The length of {t} could not be read, so the track was not added.";
                break;
            case "new":
                row.State = "waiting";
                row.Label = "Waiting";
                row.Detail = "Waiting to be checked.";
                break;
            default:
                row.State = "problem";
                row.Label = "Not added";
                row.Detail = Cut(string.IsNullOrEmpty(rec.Said) ? "The track was not added. Check that the file plays, then put it in the folder again." : "Not added: " + rec.Said, 200);
                break;
        }

        var options = rec.Maybe.Take(11).Select(m => new Choice { Id = m.Id, Title = Cut(m.Title, 60) }).ToList();
        if (!string.IsNullOrEmpty(rec.ItemId) && options.All(o => o.Id != rec.ItemId))
        {
            options.Insert(0, new Choice { Id = rec.ItemId, Title = Cut(t, 60) });
        }

        row.Options = options.Take(11).ToList();
        row.Chosen = rec.ItemId;
        return row;
    }

    public static List<TrackRow> Rows(IEnumerable<TrackRecord> records, string language)
    {
        var order = Groups.Keys.ToList();
        return records.OrderBy(r => r.Name, System.StringComparer.OrdinalIgnoreCase)
            .Select(r => Row(r, language))
            .OrderBy(r => order.IndexOf(r.Group))
            .ToList();
    }

    private static string Cut(string text, int n) => text.Length <= n ? text : text[..n];
}
