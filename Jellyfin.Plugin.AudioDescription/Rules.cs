using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>
/// The rules both audio description plug-ins follow, read from rules.json (embedded at build time). The Starboard
/// plug-ins have a copy of the same file; SYNC.md says how the two are kept in step.
/// </summary>
public sealed class Rules
{
    private static readonly Lazy<Rules> _current = new(LoadEmbedded);

    /// <summary>Gets the rules built into this plug-in.</summary>
    public static Rules Current => _current.Value;

    [JsonPropertyName("rules_version")]
    public string RulesVersion { get; set; } = string.Empty;

    [JsonPropertyName("described_track")]
    public DescribedTrackRule DescribedTrack { get; set; } = new();

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("collection")]
    public CollectionRule Collection { get; set; } = new();

    [JsonPropertyName("tagged")]
    public List<string> Tagged { get; set; } = new();

    [JsonPropertyName("after_library_scan")]
    public bool AfterLibraryScan { get; set; }

    [JsonPropertyName("play_described_track")]
    public PlayRule Play { get; set; } = new();

    [JsonPropertyName("tracks")]
    public TracksRule Tracks { get; set; } = new();

    public static Rules Parse(string json)
        => JsonSerializer.Deserialize<Rules>(json) ?? throw new InvalidDataException("rules.json is empty");

    private static Rules LoadEmbedded()
    {
        using var stream = typeof(Rules).Assembly.GetManifestResourceStream("Jellyfin.Plugin.AudioDescription.rules.json")
            ?? throw new InvalidDataException("rules.json is not built into the plug-in");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public sealed class DescribedTrackRule
    {
        [JsonPropertyName("fields")]
        public List<string> Fields { get; set; } = new();

        [JsonPropertyName("words")]
        public List<string> Words { get; set; } = new();

        [JsonPropertyName("pattern")]
        public string Pattern { get; set; } = string.Empty;

        [JsonPropertyName("ruled_out_words")]
        public List<string> RuledOutWords { get; set; } = new();

        [JsonPropertyName("ruled_out_pattern")]
        public string RuledOutPattern { get; set; } = string.Empty;

        [JsonPropertyName("examples")]
        public ExamplesRule Examples { get; set; } = new();
    }

    public sealed class ExamplesRule
    {
        [JsonPropertyName("described")]
        public List<string> Described { get; set; } = new();

        [JsonPropertyName("not_described")]
        public List<string> NotDescribed { get; set; } = new();
    }

    public sealed class CollectionRule
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("default")]
        public bool Default { get; set; }

        [JsonPropertyName("holds")]
        public List<string> Holds { get; set; } = new();
    }

    public sealed class PlayRule
    {
        [JsonPropertyName("default")]
        public bool Default { get; set; }

        [JsonPropertyName("command")]
        public string Command { get; set; } = string.Empty;

        [JsonPropertyName("argument")]
        public string Argument { get; set; } = string.Empty;

        [JsonPropertyName("wait_seconds")]
        public int WaitSeconds { get; set; }
    }

    public sealed class TracksRule
    {
        [JsonPropertyName("tolerance_seconds")]
        public RangeRule ToleranceSeconds { get; set; } = new();

        [JsonPropertyName("default_language")]
        public string DefaultLanguage { get; set; } = string.Empty;

        [JsonPropertyName("languages")]
        public Dictionary<string, string> Languages { get; set; } = new();

        [JsonPropertyName("file_name")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("flags")]
        public FlagsRule Flags { get; set; } = new();

        [JsonPropertyName("copy_without_converting")]
        public bool CopyWithoutConverting { get; set; }

        [JsonPropertyName("extensions")]
        public List<string> Extensions { get; set; } = new();

        [JsonPropertyName("zip")]
        public ZipRule Zip { get; set; } = new();

        [JsonPropertyName("folders")]
        public FoldersRule Folders { get; set; } = new();

        [JsonPropertyName("never_replace")]
        public bool NeverReplace { get; set; }

        [JsonPropertyName("never_delete")]
        public bool NeverDelete { get; set; }

        [JsonPropertyName("names")]
        public NamesRule Names { get; set; } = new();
    }

    public sealed class RangeRule
    {
        [JsonPropertyName("default")]
        public int Default { get; set; }

        [JsonPropertyName("min")]
        public int Min { get; set; }

        [JsonPropertyName("max")]
        public int Max { get; set; }
    }

    public sealed class FlagsRule
    {
        [JsonPropertyName("visual_impaired")]
        public bool VisualImpaired { get; set; }

        [JsonPropertyName("default_track")]
        public bool DefaultTrack { get; set; }
    }

    public sealed class ZipRule
    {
        [JsonPropertyName("max_bytes")]
        public long MaxBytes { get; set; }

        [JsonPropertyName("max_files")]
        public int MaxFiles { get; set; }
    }

    public sealed class FoldersRule
    {
        [JsonPropertyName("confirmed")]
        public string Confirmed { get; set; } = string.Empty;

        [JsonPropertyName("taken_off")]
        public string TakenOff { get; set; } = string.Empty;

        [JsonPropertyName("unzipped")]
        public string Unzipped { get; set; } = string.Empty;
    }

    public sealed class NamesRule
    {
        [JsonPropertyName("episode_pattern")]
        public string EpisodePattern { get; set; } = string.Empty;

        [JsonPropertyName("year_pattern")]
        public string YearPattern { get; set; } = string.Empty;

        [JsonPropertyName("noise_pattern")]
        public string NoisePattern { get; set; } = string.Empty;

        [JsonPropertyName("examples")]
        public List<NameExample> Examples { get; set; } = new();
    }

    public sealed class NameExample
    {
        [JsonPropertyName("file")]
        public string File { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("year")]
        public int Year { get; set; }

        [JsonPropertyName("season")]
        public int Season { get; set; }

        [JsonPropertyName("episode")]
        public int Episode { get; set; }
    }
}
