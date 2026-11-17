using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>A film on Titles with audio description.</summary>
public sealed class FilmRow
{
    internal DateTime Added { get; set; }

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

    internal DateTime Added { get; set; }
}

/// <summary>What the page Titles with audio description shows.</summary>
public sealed class TitlesAnswer
{
    public List<FilmRow> Films { get; set; } = new();

    public List<SeriesRow> Series { get; set; } = new();

    /// <summary>Gets or sets how many episodes the listed series have described.</summary>
    public int EpisodeCount { get; set; }

    /// <summary>Gets or sets how many films have the tag, listed or not.</summary>
    public int FilmTotal { get; set; }

    /// <summary>Gets or sets how many series have the tag, listed or not.</summary>
    public int SeriesTotal { get; set; }

    /// <summary>Gets or sets the Films to list setting: 0 for all.</summary>
    public int FilmsShown { get; set; }

    /// <summary>Gets or sets the Series to list setting: 0 for all.</summary>
    public int SeriesShown { get; set; }

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

    /// <summary>Films to list and Series to list: Last 20 by default.</summary>
    public const int ShownDefault = 20;

    /// <summary>The choices for Films to list and Series to list; 0 is All.</summary>
    public static readonly IReadOnlyList<int> ShownChoices = new[] { 0, 10, 20, 50, 100, 500 };

    private const int Page = 200;

    /// <summary>A setting's value made safe: one of the choices, else the default.</summary>
    public static int Shown(int value) => ShownChoices.Contains(value) ? value : ShownDefault;

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

    /// <summary>
    /// The tagged films with a described track, newest added first, and the series whose described episodes were added
    /// most recently, each with all its described episodes. Films to list and Series to list limit what is read: films
    /// with Limit and DateCreated, newest first; episodes a page at a time, newest first, until enough series are found.
    /// </summary>
    public static TitlesAnswer Build(ILibraryManager library, Func<Guid, string?> trackOf, DateTimeOffset? checkedAt, int filmsShown = ShownDefault, int seriesShown = ShownDefault)
    {
        filmsShown = Shown(filmsShown);
        seriesShown = Shown(seriesShown);
        var answer = new TitlesAnswer { Checked = checkedAt, FilmsShown = filmsShown, SeriesShown = seriesShown };
        var newest = new[] { (ItemSortBy.DateCreated, SortOrder.Descending), (ItemSortBy.SortName, SortOrder.Ascending) };
        InternalItemsQuery Tagged(BaseItemKind kind) => new()
        {
            IncludeItemTypes = new[] { kind },
            Recursive = true,
            IsVirtualItem = false,
            Tags = new[] { Tagger.Tag }
        };

        answer.FilmTotal = library.GetCount(Tagged(BaseItemKind.Movie));
        answer.SeriesTotal = library.GetCount(Tagged(BaseItemKind.Series));

        var films = Tagged(BaseItemKind.Movie);
        films.OrderBy = newest;
        films.Limit = filmsShown == 0 ? null : filmsShown;
        foreach (var item in library.GetItemList(films))
        {
            if (trackOf(item.Id) is { } track)
            {
                answer.Films.Add(new FilmRow { Id = item.Id.ToString("N"), Title = item.Name ?? string.Empty, Year = item.ProductionYear ?? 0, Track = track, Added = item.DateCreated });
            }
        }

        // The series, by their newest described episode: episodes newest first, a page at a time, until enough series.
        var chosen = new List<Guid>();
        var newestOf = new Dictionary<Guid, DateTime>();
        for (var start = 0; seriesShown == 0 || chosen.Count < seriesShown; start += Page)
        {
            var q = Tagged(BaseItemKind.Episode);
            q.OrderBy = newest;
            q.StartIndex = start;
            q.Limit = Page;
            var page = library.GetItemList(q);
            foreach (var ep in page.OfType<Episode>())
            {
                if (!newestOf.ContainsKey(ep.SeriesId) && trackOf(ep.Id) is not null)
                {
                    newestOf[ep.SeriesId] = ep.DateCreated;
                    chosen.Add(ep.SeriesId);
                    if (seriesShown != 0 && chosen.Count >= seriesShown)
                    {
                        break;
                    }
                }
            }

            if (page.Count < Page)
            {
                break;
            }
        }

        foreach (var sid in chosen)
        {
            var show = sid == Guid.Empty ? null : library.GetItemById(sid);
            var q = Tagged(BaseItemKind.Episode);
            q.AncestorIds = new[] { sid };
            var row = new SeriesRow { Id = sid == Guid.Empty ? string.Empty : sid.ToString("N"), Added = newestOf[sid] };
            foreach (var ep in library.GetItemList(q).OfType<Episode>())
            {
                if (trackOf(ep.Id) is not { } track)
                {
                    continue; // tagged, but the track has gone: the next Find and tag takes the tag off
                }

                row.Name = show?.Name ?? ep.SeriesName ?? "Unknown series";
                row.Track = row.Track.Length > 0 ? row.Track : track;
                row.Items.Add(new EpisodeRow { Id = ep.Id.ToString("N"), Title = EpisodeWords(ep.ParentIndexNumber, ep.IndexNumber, ep.Name), Track = track, Season = ep.ParentIndexNumber, Number = ep.IndexNumber });
            }

            row.Year = show?.ProductionYear ?? 0;
            row.Title = WithYear(row.Name, row.Year);
            row.Episodes = sid == Guid.Empty ? 0 : library.GetItemIds(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                AncestorIds = new[] { sid },
                Recursive = true,
                IsVirtualItem = false
            }).Count;
            row.Items = row.Items
                .OrderBy(e => e.Season is null).ThenBy(e => e.Season ?? 0)
                .ThenBy(e => e.Number is null).ThenBy(e => e.Number ?? 0)
                .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            row.Described = row.Items.Count;
            row.Episodes = Math.Max(row.Episodes, row.Described);
            row.Status = SeriesWords(row.Described, row.Episodes);
            answer.Series.Add(row);
        }

        answer.EpisodeCount = answer.Series.Sum(s => s.Described);
        return answer;
    }
}
