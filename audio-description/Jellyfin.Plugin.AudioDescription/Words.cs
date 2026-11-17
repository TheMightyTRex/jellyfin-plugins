using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>What a file name says: a film's name and year, or a series' name with its series and episode.</summary>
public sealed record ParsedName(string Kind, string Name, int Year, int Season, int Episode)
{
    public bool IsEpisode => Kind == "episode";
}

/// <summary>
/// The words of rules.json: which sound tracks are audio description, how a track's file name is read, and the name
/// and title a new track is given. The same rules as the Starboard plug-ins (SYNC.md).
/// </summary>
public static class Words
{
    private static readonly Rules _rules = Rules.Current;
    private static readonly Regex _described = Make(_rules.DescribedTrack.Pattern);
    private static readonly Regex _ruledOut = Make(_rules.DescribedTrack.RuledOutPattern);
    private static readonly Regex _episode = Make(_rules.Tracks.Names.EpisodePattern);
    private static readonly Regex _year = Make(_rules.Tracks.Names.YearPattern);
    private static readonly Regex _noise = Make(_rules.Tracks.Names.NoisePattern);

    /// <summary>True when a track's title, display title or comment says audio description, and nothing rules it out.</summary>
    public static bool IsDescribed(params string?[] texts)
    {
        var words = string.Join(' ', texts.Select(t => t ?? string.Empty));
        return _described.IsMatch(words) && !_ruledOut.IsMatch(words);
    }

    /// <summary>The first sound track that is audio description, or null. Other kinds of stream never count.</summary>
    public static MediaStream? DescribedTrack(IEnumerable<MediaStream>? streams)
        => streams?.FirstOrDefault(s => s is not null && s.Type == MediaStreamType.Audio && IsDescribed(s.Title, s.DisplayTitle, s.Comment));

    /// <summary>Reads a track's file name, as Starboard's Audio description tracks plug-in does.</summary>
    public static ParsedName ParseName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName.Replace('\\', '/')));
        var text = Regex.Replace(stem, "[._]+", " ");
        text = Regex.Replace(text, @"\[[^\]]*\]", " ");
        var ep = _episode.Match(text);
        if (ep.Success)
        {
            var season = int.Parse(ep.Groups[1].Success ? ep.Groups[1].Value : ep.Groups[3].Value, CultureInfo.InvariantCulture);
            var episode = int.Parse(ep.Groups[1].Success ? ep.Groups[2].Value : ep.Groups[4].Value, CultureInfo.InvariantCulture);
            var name = Squash(_noise.Replace(text[..ep.Index], " "));
            name = Regex.Replace(name, @"[\s()\-]+$", string.Empty);
            var noYear = _year.Replace(name, " ").Trim(' ', '-');
            name = noYear.Length > 0 ? noYear : name;
            return new ParsedName("episode", Squash(name), 0, season, episode);
        }

        var years = _year.Matches(text);
        var year = 0;
        var title = text;
        if (years.Count > 0)
        {
            var last = years[^1];
            year = int.Parse(last.Groups[1].Value, CultureInfo.InvariantCulture);
            if (text[..last.Index].Trim().Length > 0)
            {
                title = text[..last.Index];
            }
        }

        title = Squash(_noise.Replace(title, " ").Replace('(', ' ').Replace(')', ' ')).Trim(' ', '-');
        return new ParsedName("film", title.Length > 0 ? title : year.ToString(CultureInfo.InvariantCulture), year, 0, 0);
    }

    /// <summary>A name made plain for comparing: no accents, lower case, "&amp;" as "and", only letters and numbers.</summary>
    public static string Norm(string? text)
    {
        var plain = new StringBuilder();
        foreach (var c in (text ?? string.Empty).Normalize(NormalizationForm.FormKD))
        {
            if (c < 128)
            {
                plain.Append(c);
            }
        }

        var lower = plain.ToString().ToLowerInvariant().Replace("&", " and ", StringComparison.Ordinal);
        return Squash(Regex.Replace(lower, "[^a-z0-9]+", " "));
    }

    /// <summary>
    /// Whether a name in Jellyfin is the name read from a file. A film Jellyfin found no details for keeps its folder's
    /// name, such as "Dune (2021)", so a year in brackets at its end is left out.
    /// </summary>
    public static bool SameName(string? inJellyfin, string fromFile)
        => Norm(Regex.Replace(inJellyfin ?? string.Empty, @"\s*\((?:19|20)\d\d\)\s*$", string.Empty)) == Norm(fromFile);

    /// <summary>A film's name with its year, such as "Dune (2021)", without the year twice.</summary>
    public static string FilmTitle(string? name, int? year)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "A film" : name.Trim();
        var y = year is int v && v > 0 ? $" ({v})" : string.Empty;
        return y.Length > 0 && n.EndsWith(y.Trim(), StringComparison.Ordinal) ? n : n + y;
    }

    /// <summary>A length in words, such as "1 hour 58 minutes 2 seconds".</summary>
    public static string LengthWords(double seconds)
    {
        var s = (int)Math.Round(Math.Abs(seconds), MidpointRounding.ToEven);
        int h = s / 3600, m = s % 3600 / 60, sec = s % 60;
        var parts = new List<string>();
        if (h > 0)
        {
            parts.Add(Plural(h, "hour"));
        }

        if (m > 0)
        {
            parts.Add(Plural(m, "minute"));
        }

        if (sec > 0 || parts.Count == 0)
        {
            parts.Add(Plural(sec, "second"));
        }

        return string.Join(' ', parts);
    }

    public static string Plural(int n, string one, string? many = null)
        => n.ToString("N0", CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many ?? one + "s");

    /// <summary>A language code from the rules, or the default one.</summary>
    public static string Language(string? code)
        => code is not null && _rules.Tracks.Languages.ContainsKey(code) ? code : _rules.Tracks.DefaultLanguage;

    /// <summary>The new track's title, such as "English (Audio Description)".</summary>
    public static string TrackTitle(string? code)
        => _rules.Tracks.Title.Replace("{language_name}", _rules.Tracks.Languages[Language(code)], StringComparison.Ordinal);

    /// <summary>The new track's file: next to the film, with its name, so Jellyfin loads it as an extra sound track.</summary>
    public static string TargetFor(string video, string? code)
    {
        var folder = Path.GetDirectoryName(video) ?? string.Empty;
        var name = _rules.Tracks.FileName
            .Replace("{video_without_extension}", Path.GetFileNameWithoutExtension(video), StringComparison.Ordinal)
            .Replace("{language}", Language(code), StringComparison.Ordinal);
        return Path.Combine(folder, name);
    }

    /// <summary>Whether a file in the folder is one the plug-in reads: a sound file or a zip of them.</summary>
    public static bool IsTrackFile(string name, bool zipToo = true)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return _rules.Tracks.Extensions.Contains(ext) || (zipToo && ext == ".zip");
    }

    private static string Squash(string text) => Regex.Replace(text.Trim(), @"\s+", " ");

    private static Regex Make(string pattern) => new(pattern, RegexOptions.CultureInvariant);
}
