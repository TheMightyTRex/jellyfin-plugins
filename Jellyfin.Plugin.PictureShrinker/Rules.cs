using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>
/// The rules both picture shrinkers follow, read from rules.json (embedded at build time). The Starboard plug-in has a
/// copy of the same file; SYNC.md says how the two are kept in step.
/// </summary>
public sealed class Rules
{
    private static readonly Lazy<Rules> _current = new(LoadEmbedded);

    /// <summary>Gets the rules built into this plug-in.</summary>
    public static Rules Current => _current.Value;

    [JsonPropertyName("rules_version")]
    public string RulesVersion { get; set; } = string.Empty;

    [JsonPropertyName("kinds")]
    public Dictionary<string, KindRule> Kinds { get; set; } = new();

    [JsonPropertyName("shrink_all")]
    public ShrinkAllRule ShrinkAll { get; set; } = new();

    [JsonPropertyName("extensions")]
    public List<string> Extensions { get; set; } = new();

    [JsonPropertyName("formats")]
    public List<string> Formats { get; set; } = new();

    [JsonPropertyName("quality")]
    public Range Quality { get; set; } = new();

    [JsonPropertyName("rounding")]
    public string Rounding { get; set; } = string.Empty;

    [JsonPropertyName("temp_suffix")]
    public string TempSuffix { get; set; } = string.Empty;

    [JsonPropertyName("skip_hidden")]
    public bool SkipHidden { get; set; }

    [JsonPropertyName("checks")]
    public List<string> Checks { get; set; } = new();

    [JsonPropertyName("keep_from_original")]
    public List<string> KeepFromOriginal { get; set; } = new();

    [JsonPropertyName("backup")]
    public BackupRule Backup { get; set; } = new();

    [JsonPropertyName("start_again")]
    public StartAgainRule StartAgain { get; set; } = new();

    [JsonPropertyName("new_pictures")]
    public NewPicturesRule NewPictures { get; set; } = new();

    [JsonPropertyName("run_limit_minutes")]
    public int RunLimitMinutes { get; set; }

    [JsonPropertyName("keep_runs")]
    public int KeepRuns { get; set; }

    [JsonPropertyName("keep_failures_per_run")]
    public int KeepFailuresPerRun { get; set; }

    public KindRule Poster => Kinds["poster"];

    public KindRule Person => Kinds["person"];

    public KindRule Backdrop => Kinds["backdrop"];

    public KindRule Thumb => Kinds["thumb"];

    public KindRule Logo => Kinds["logo"];

    public KindRule Banner => Kinds["banner"];

    public KindRule Disc => Kinds["disc"];

    public KindRule Clearart => Kinds["clearart"];

    public static Rules Parse(string json)
        => JsonSerializer.Deserialize<Rules>(json) ?? throw new InvalidDataException("rules.json is empty");

    private static Rules LoadEmbedded()
    {
        using var stream = typeof(Rules).Assembly.GetManifestResourceStream("Jellyfin.Plugin.PictureShrinker.rules.json")
            ?? throw new InvalidDataException("rules.json is not built into the plug-in");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public sealed class KindRule
    {
        [JsonPropertyName("folder")]
        public string Folder { get; set; } = string.Empty;

        [JsonPropertyName("name_starts_with")]
        public string NameStartsWith { get; set; } = string.Empty;

        [JsonPropertyName("limit")]
        public string Limit { get; set; } = string.Empty;

        [JsonPropertyName("default")]
        public int Default { get; set; }

        [JsonPropertyName("min")]
        public int Min { get; set; }

        [JsonPropertyName("max")]
        public int Max { get; set; }

        [JsonPropertyName("shrink_by_default")]
        public bool ShrinkByDefault { get; set; }
    }

    public sealed class ShrinkAllRule
    {
        [JsonPropertyName("default")]
        public bool Default { get; set; }
    }

    public sealed class Range
    {
        [JsonPropertyName("default")]
        public int Default { get; set; }

        [JsonPropertyName("min")]
        public int Min { get; set; }

        [JsonPropertyName("max")]
        public int Max { get; set; }
    }

    public sealed class BackupRule
    {
        /// <summary>Gets or sets the backup choice a new install starts with: null, so the person chooses.</summary>
        [JsonPropertyName("default")]
        public string? Default { get; set; }

        [JsonPropertyName("choices")]
        public List<string> Choices { get; set; } = new();

        [JsonPropertyName("ask_after_install")]
        public bool AskAfterInstall { get; set; }

        [JsonPropertyName("ask_after_update_to")]
        public string AskAfterUpdateTo { get; set; } = string.Empty;

        [JsonPropertyName("folder")]
        public string Folder { get; set; } = string.Empty;

        [JsonPropertyName("never_overwrite")]
        public bool NeverOverwrite { get; set; }

        [JsonPropertyName("keep_free_bytes")]
        public long KeepFreeBytes { get; set; }
    }

    public sealed class StartAgainRule
    {
        [JsonPropertyName("kinds")]
        public List<string> Kinds { get; set; } = new();

        [JsonPropertyName("deletes_backup")]
        public bool DeletesBackup { get; set; }

        [JsonPropertyName("clears_records")]
        public bool ClearsRecords { get; set; }

        [JsonPropertyName("keeps_backup_choice")]
        public bool KeepsBackupChoice { get; set; }

        [JsonPropertyName("aside_suffix")]
        public string AsideSuffix { get; set; } = string.Empty;

        [JsonPropertyName("settle_every_minutes")]
        public int SettleEveryMinutes { get; set; }

        [JsonPropertyName("put_back_after_hours")]
        public int PutBackAfterHours { get; set; }

        [JsonPropertyName("pause_between_refreshes_ms")]
        public int PauseBetweenRefreshesMs { get; set; }
    }

    public sealed class NewPicturesRule
    {
        [JsonPropertyName("default")]
        public bool Default { get; set; }

        [JsonPropertyName("min_age_seconds")]
        public int MinAgeSeconds { get; set; }
    }
}
