using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>A film on Titles with audio description.</summary>
public sealed class FilmRow
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the year, 0 when Jellyfin has none.</summary>
    public int Year { get; set; }

    public string Track { get; set; } = string.Empty;

    public string Status { get; set; } = DescribedTitles.Described;
}

/// <summary>A described episode on Titles with audio description.</summary>
public sealed class EpisodeRow
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the words for it: S1 E3: Name, or the name alone when Jellyfin has no numbers.</summary>
    public string Title { get; set; } = string.Empty;

    public string Track { get; set; } = string.Empty;

    public string Status { get; set; } = DescribedTitles.Described;

    internal int? Season { get; set; }

    internal int? Number { get; set; }
}

/// <summary>A series with at least one described episode on Titles with audio description.</summary>
public sealed class SeriesRow
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Year { get; set; }

    /// <summary>Gets or sets its name and year, as its heading shows them, such as Quiet Valley (2023).</summary>
    public string Title { get; set; } = string.Empty;

    public int Described { get; set; }

    public int Episodes { get; set; }

    /// <summary>Gets or sets its status in words, such as 3 of 6 episodes described.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the first described track among its episodes.</summary>
    public string Track { get; set; } = string.Empty;

    public List<EpisodeRow> Items { get; set; } = new();
}

/// <summary>What the page Titles with audio description shows.</summary>
public sealed class TitlesAnswer
{
    public List<FilmRow> Films { get; set; } = new();

    public List<SeriesRow> Series { get; set; } = new();

    public int EpisodeCount { get; set; }

    /// <summary>Gets or sets when Find and tag last ran, or null when it has not run yet.</summary>
    public DateTimeOffset? Checked { get; set; }
}

/// <summary>
/// Titles with audio description (owner, 17 November 2026: "a page ... to list the items with audio description", the
/// same page, columns and words as the Starboard plug-in's): every film and episode with the tag that Find and tag
/// gives, and a described track, with each series' described episodes in order. It reads only the tagged items and
/// their streams, and each series' episode ids for its count, when the page asks.
/// </summary>
public static class DescribedTitles
{
    /// <summary>The status of a described film or episode, in words.</summary>
    public const string Described = "Described";

    /// <summary>S1 E3: Name, or the name alone when Jellyfin has no numbers for it, as the Starboard plug-in says it.</summary>
    public static string EpisodeWords(int? season, int? episode, string? name)
    {
        if (season is int s && episode is int e)
        {
            return string.IsNullOrEmpty(name)
                ? string.Format(CultureInfo.InvariantCulture, "S{0} E{1}", s, e)
                : string.Format(CultureInfo.InvariantCulture, "S{0} E{1}: {2}", s, e, name);
        }

        return string.IsNullOrEmpty(name) ? "An episode" : name;
    }

    /// <summary>A series' status in words: All 6 episodes described, or 3 of 6 episodes described.</summary>
    public static string SeriesWords(int described, int episodes)
    {
        if (described == episodes && episodes > 1)
        {
            return string.Format(CultureInfo.GetCultureInfo("en-GB"), "All {0:N0} episodes described", episodes);
        }

        return string.Format(CultureInfo.GetCultureInfo("en-GB"), "{0:N0} of {1:N0} {2} described", described, episodes, episodes == 1 ? "episode" : "episodes");
    }

    /// <summary>A name and its year, such as Harbour Lights (2019), or the name alone.</summary>
    public static string WithYear(string name, int year) => year > 0 ? string.Format(CultureInfo.InvariantCulture, "{0} ({1})", name, year) : name;

    /// <summary>Every tagged film and episode with a described track, films by name and series by name.</summary>
    public static TitlesAnswer Build(ILibraryManager library, Func<Guid, string?> trackOf, DateTimeOffset? checkedAt)
    {
        var answer = new TitlesAnswer { Checked = checkedAt };
        var series = new Dictionary<Guid, SeriesRow>();
        var tagged = library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
            Recursive = true,
            IsVirtualItem = false,
            Tags = new[] { Tagger.Tag }
        });
        foreach (var item in tagged)
        {
            var track = trackOf(item.Id);
            if (track is null)
            {
                continue; // tagged, but the track has gone: the next Find and tag takes the tag off
            }

            if (item is Episode ep)
            {
                var sid = ep.SeriesId;
                if (!series.TryGetValue(sid, out var row))
                {
                    var show = sid == Guid.Empty ? null : library.GetItemById(sid);
                    var name = show?.Name ?? ep.SeriesName ?? "Unknown series";
                    var year = show?.ProductionYear ?? 0;
                    row = new SeriesRow { Id = sid == Guid.Empty ? string.Empty : sid.ToString("N"), Name = name, Year = year, Title = WithYear(name, year), Track = track };
                    row.Episodes = sid == Guid.Empty ? 0 : library.GetItemIds(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.Episode },
                        AncestorIds = new[] { sid },
                        Recursive = true,
                        IsVirtualItem = false
                    }).Count;
                    series[sid] = row;
                }

                row.Items.Add(new EpisodeRow
                {
                    Id = ep.Id.ToString("N"),
                    Title = EpisodeWords(ep.ParentIndexNumber, ep.IndexNumber, ep.Name),
                    Track = track,
                    Season = ep.ParentIndexNumber,
                    Number = ep.IndexNumber
                });
            }
            else if (item is Movie)
            {
                answer.Films.Add(new FilmRow { Id = item.Id.ToString("N"), Title = item.Name ?? string.Empty, Year = item.ProductionYear ?? 0, Track = track });
            }
        }

        answer.Films = answer.Films.OrderBy(f => WithYear(f.Title, f.Year), StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var row in series.Values)
        {
            row.Items = row.Items
                .OrderBy(e => e.Season is null).ThenBy(e => e.Season ?? 0)
                .ThenBy(e => e.Number is null).ThenBy(e => e.Number ?? 0)
                .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            row.Described = row.Items.Count;
            row.Episodes = Math.Max(row.Episodes, row.Described);
            row.Status = SeriesWords(row.Described, row.Episodes);
        }

        answer.Series = series.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        answer.EpisodeCount = answer.Series.Sum(s => s.Described);
        return answer;
    }
}
