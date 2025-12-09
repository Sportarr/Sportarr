using System.Text.Json.Serialization;

namespace Sportarr.Api.Models;

/// <summary>
/// TRaSH Guides metadata file structure (metadata.json)
/// </summary>
public class TrashMetadata
{
    [JsonPropertyName("sonarr")]
    public TrashAppMetadata? Sonarr { get; set; }

    [JsonPropertyName("radarr")]
    public TrashAppMetadata? Radarr { get; set; }
}

public class TrashAppMetadata
{
    [JsonPropertyName("custom_formats")]
    public string CustomFormats { get; set; } = "";

    [JsonPropertyName("quality_profiles")]
    public string QualityProfiles { get; set; } = "";

    [JsonPropertyName("cf_groups")]
    public string CfGroups { get; set; } = "";

    [JsonPropertyName("qualities")]
    public string Qualities { get; set; } = "";

    [JsonPropertyName("naming")]
    public string Naming { get; set; } = "";
}

/// <summary>
/// TRaSH Custom Format JSON structure
/// </summary>
public class TrashCustomFormat
{
    [JsonPropertyName("trash_id")]
    public string TrashId { get; set; } = "";

    [JsonPropertyName("trash_scores")]
    public Dictionary<string, int>? TrashScores { get; set; }

    [JsonPropertyName("trash_description")]
    public string? TrashDescription { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("includeCustomFormatWhenRenaming")]
    public bool IncludeCustomFormatWhenRenaming { get; set; }

    [JsonPropertyName("specifications")]
    public List<TrashSpecification> Specifications { get; set; } = new();

    /// <summary>
    /// Category derived from file path (e.g., "streaming", "audio", "hdr")
    /// Set during sync, not from JSON
    /// </summary>
    [JsonIgnore]
    public string? Category { get; set; }

    /// <summary>
    /// Original filename (without .json extension)
    /// Set during sync, not from JSON
    /// </summary>
    [JsonIgnore]
    public string? FileName { get; set; }
}

public class TrashSpecification
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("implementation")]
    public string Implementation { get; set; } = "";

    [JsonPropertyName("negate")]
    public bool Negate { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("fields")]
    public Dictionary<string, object>? Fields { get; set; }
}

/// <summary>
/// TRaSH Quality Profile JSON structure
/// </summary>
public class TrashQualityProfile
{
    [JsonPropertyName("trash_id")]
    public string TrashId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("trash_description")]
    public string? TrashDescription { get; set; }

    [JsonPropertyName("upgradeAllowed")]
    public bool UpgradeAllowed { get; set; } = true;

    [JsonPropertyName("cutoff")]
    public string? Cutoff { get; set; }

    [JsonPropertyName("minFormatScore")]
    public int? MinFormatScore { get; set; }

    [JsonPropertyName("cutoffFormatScore")]
    public int? CutoffFormatScore { get; set; }

    [JsonPropertyName("formatItems")]
    public List<TrashProfileFormatItem>? FormatItems { get; set; }

    [JsonPropertyName("qualities")]
    public List<TrashQualityItem>? Qualities { get; set; }
}

public class TrashProfileFormatItem
{
    [JsonPropertyName("trash_id")]
    public string TrashId { get; set; } = "";

    [JsonPropertyName("score")]
    public int Score { get; set; }
}

public class TrashQualityItem
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("allowed")]
    public bool Allowed { get; set; }

    [JsonPropertyName("items")]
    public List<TrashQualityItem>? Items { get; set; }
}

/// <summary>
/// Result of a TRaSH sync operation
/// </summary>
public class TrashSyncResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    /// <summary>
    /// Number of custom formats created
    /// </summary>
    public int Created { get; set; }

    /// <summary>
    /// Number of custom formats updated
    /// </summary>
    public int Updated { get; set; }

    /// <summary>
    /// Number of custom formats skipped (customized by user)
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>
    /// Number of custom formats that failed to sync
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    /// List of errors encountered during sync
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// List of custom format names that were synced
    /// </summary>
    public List<string> SyncedFormats { get; set; } = new();

    /// <summary>
    /// Timestamp when sync completed
    /// </summary>
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Available TRaSH custom format info for UI selection
/// </summary>
public class TrashCustomFormatInfo
{
    public string TrashId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Category { get; set; }
    public int? DefaultScore { get; set; }

    /// <summary>
    /// True if this CF is already synced to Sportarr
    /// </summary>
    public bool IsSynced { get; set; }

    /// <summary>
    /// True if this CF is recommended for sports content
    /// </summary>
    public bool IsRecommended { get; set; }
}

/// <summary>
/// Categories of custom formats relevant for sports
/// </summary>
public static class TrashCategories
{
    // Include these categories for sports
    public static readonly HashSet<string> SportRelevant = new(StringComparer.OrdinalIgnoreCase)
    {
        // Audio quality
        "audio",
        "truehd-atmos",
        "dts-x",
        "dts-hd-ma",
        "truehd",
        "flac",
        "pcm",
        "ddplus-atmos",
        "ddplus",
        "dts",
        "aac",

        // Audio channels
        "10-mono",
        "20-stereo",
        "30-sound",
        "40-sound",
        "51-surround",
        "61-surround",
        "71-surround",

        // Video quality
        "x264",
        "x265",
        "x265-hd",
        "av1",
        "mpeg2",
        "vc-1",
        "vp9",
        "10bit",

        // HDR
        "hdr",
        "hdr10",
        "hdr10plus",
        "dv",
        "dv-hdr10",
        "dv-hlg",
        "dv-sdr",
        "hlg",
        "pq",
        "sdr",

        // Resolution
        "1080p",
        "720p",
        "2160p",
        "480p",

        // Source/streaming services
        "amzn",
        "nf",
        "dsnp",
        "hmax",
        "atvp",
        "pcok",
        "hulu",
        "max",
        "roku",
        "web-tier",

        // Release types
        "remux",
        "repack",
        "proper",
        "repack2",
        "repack3",
        "hybrid",
        "remaster",

        // Unwanted
        "br-disk",
        "lq",
        "lq-release-title",
        "extras",
        "upscaled",
        "x265-no-hdrdv",
        "3d",
        "bad-dual-groups",
        "dv-webdl",
        "evo-no-webdl",
        "line-mic-dubbed",
        "no-rlsgroup",
        "obfuscated",
        "retags",
        "scene",
        "web-scene",

        // Languages
        "multi",
        "multi-audio",
        "multi-french",
        "french-audio",
        "german-audio",
        "spanish-audio",
        "italian-audio",
        "portuguese-audio",
        "japanese-audio",
        "korean-audio",
        "chinese-audio",
        "dutch-audio",
        "nordic-audio",
        "polish-audio",
        "russian-audio",
        "arabic-audio",
        "hindi-audio",
        "turkish-audio",
        "thai-audio",
        "vietnamese-audio",
        "english-audio",
        "original-audio",

        // Language subtitles
        "french-sub",
        "german-sub",
        "spanish-sub",
        "vostfr",
    };

    // Exclude these patterns (anime-specific, etc.)
    public static readonly HashSet<string> ExcludePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "anime",
        "fansub",
        "seadex",
        "dual-audio",
        "uncensored",
        "v0",
        "v1",
        "v2",
        "v3",
        "v4",
    };

    /// <summary>
    /// Check if a filename/category is relevant for sports content
    /// </summary>
    public static bool IsRelevantForSports(string filename)
    {
        var lower = filename.ToLowerInvariant();

        // Exclude anime-related
        foreach (var pattern in ExcludePatterns)
        {
            if (lower.Contains(pattern))
                return false;
        }

        // Check if it matches any sport-relevant category
        foreach (var category in SportRelevant)
        {
            if (lower.Contains(category.ToLowerInvariant()))
                return true;
        }

        // Include general quality/source CFs
        if (lower.Contains("web") || lower.Contains("bluray") || lower.Contains("hdtv") ||
            lower.Contains("hdr") || lower.Contains("remux") || lower.Contains("repack") ||
            lower.Contains("audio") || lower.Contains("surround") ||
            lower.Contains("x264") || lower.Contains("x265") || lower.Contains("hevc") ||
            lower.Contains("dts") || lower.Contains("atmos") || lower.Contains("truehd") ||
            lower.Contains("lq") || lower.Contains("br-disk") || lower.Contains("extras"))
        {
            return true;
        }

        return false;
    }
}

/// <summary>
/// Available score sets from TRaSH Guides
/// </summary>
public static class TrashScoreSets
{
    public const string Default = "default";
    public const string FrenchMulti = "french-multi";
    public const string FrenchVostfr = "french-vostfr";
    public const string German = "german";
    public const string GermanMulti = "german-multi";

    public static readonly Dictionary<string, string> DisplayNames = new()
    {
        { Default, "Default" },
        { FrenchMulti, "French (Multi-Audio)" },
        { FrenchVostfr, "French (VOSTFR)" },
        { German, "German" },
        { GermanMulti, "German (Multi-Audio)" },
    };
}
