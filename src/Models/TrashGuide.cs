using System.Text.Json;
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

    /// <summary>
    /// Format items mapping: format name -> trash_id
    /// TRaSH JSON uses object/dictionary format: { "BR-DISK": "85c61753...", "LQ": "9c11cd3f..." }
    /// </summary>
    [JsonPropertyName("formatItems")]
    public Dictionary<string, string>? FormatItems { get; set; }

    [JsonPropertyName("items")]
    public List<TrashQualityItem>? Items { get; set; }
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

    /// <summary>
    /// Nested items - can be either strings (quality names) or TrashQualityItem objects
    /// TRaSH JSON uses strings for leaf items: "items": ["WEBDL-480p", "WEBRip-480p"]
    /// We store them as TrashQualityItem with just Name populated
    /// </summary>
    [JsonPropertyName("items")]
    [JsonConverter(typeof(TrashQualityItemsConverter))]
    public List<TrashQualityItem>? Items { get; set; }
}

/// <summary>
/// Custom JSON converter that handles TRaSH quality items which can be either:
/// - Array of strings: ["WEBDL-480p", "WEBRip-480p"]
/// - Array of objects: [{ "name": "Unknown", "allowed": false }]
/// </summary>
public class TrashQualityItemsConverter : JsonConverter<List<TrashQualityItem>?>
{
    public override List<TrashQualityItem>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected array but got {reader.TokenType}");
        }

        var items = new List<TrashQualityItem>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                // Simple string item - convert to TrashQualityItem with just Name
                items.Add(new TrashQualityItem
                {
                    Name = reader.GetString(),
                    Allowed = true // Default to allowed for nested string items
                });
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                // Object item - deserialize as TrashQualityItem
                var item = JsonSerializer.Deserialize<TrashQualityItem>(ref reader, options);
                if (item != null)
                {
                    items.Add(item);
                }
            }
            else
            {
                throw new JsonException($"Unexpected token {reader.TokenType} in items array");
            }
        }

        return items;
    }

    public override void Write(Utf8JsonWriter writer, List<TrashQualityItem>? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }
        writer.WriteEndArray();
    }
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

/// <summary>
/// TRaSH Guides sync settings (stored in database)
/// </summary>
public class TrashSyncSettings
{
    /// <summary>
    /// Enable automatic scheduled sync
    /// </summary>
    public bool EnableAutoSync { get; set; } = false;

    /// <summary>
    /// Auto sync interval in hours (default: 24)
    /// </summary>
    public int AutoSyncIntervalHours { get; set; } = 24;

    /// <summary>
    /// Last automatic sync timestamp
    /// </summary>
    public DateTime? LastAutoSync { get; set; }

    /// <summary>
    /// Auto-sync only sport-relevant formats
    /// </summary>
    public bool AutoSyncSportRelevantOnly { get; set; } = true;

    /// <summary>
    /// Auto-apply scores to profiles after syncing CFs
    /// </summary>
    public bool AutoApplyScoresToProfiles { get; set; } = false;

    /// <summary>
    /// Score set to use for auto-apply
    /// </summary>
    public string AutoApplyScoreSet { get; set; } = "default";
}

/// <summary>
/// Preview of changes that will be made during sync
/// </summary>
public class TrashSyncPreview
{
    public List<TrashSyncPreviewItem> ToCreate { get; set; } = new();
    public List<TrashSyncPreviewItem> ToUpdate { get; set; } = new();
    public List<TrashSyncPreviewItem> ToSkip { get; set; } = new();
    public int TotalChanges => ToCreate.Count + ToUpdate.Count;
}

public class TrashSyncPreviewItem
{
    public string TrashId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public int? DefaultScore { get; set; }
    public string? Reason { get; set; }

    /// <summary>
    /// For updates: what fields will change
    /// </summary>
    public List<string> Changes { get; set; } = new();
}

/// <summary>
/// TRaSH naming template presets
/// These are adapted from TRaSH Guides but modified for Sportarr's sports content
/// </summary>
public static class TrashNamingTemplates
{
    /// <summary>
    /// Standard naming presets for Sportarr
    /// Key: preset name, Value: (format, description)
    /// </summary>
    public static readonly Dictionary<string, (string Format, string Description, bool SupportsMultiPart)> FileNamingPresets = new()
    {
        // Plex-compatible naming (default for Sportarr)
        { "plex-standard", (
            "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}",
            "Plex TV Show style - Best for media servers. Supports multi-part episodes.",
            true
        )},

        // Plex with release group
        { "plex-with-group", (
            "{Series} - {Season}{Episode}{Part} - {Event Title} [{Quality Full}] [{Release Group}]",
            "Plex style with release group in brackets",
            true
        )},

        // Simple date-based (TRaSH style)
        { "date-based", (
            "{Event Title} ({Air Date Year}) - {Quality Full}",
            "Simple date-based naming without episode numbers",
            false
        )},

        // Sports-focused with league prefix
        { "sports-league", (
            "{Series} - {Air Date} - {Event Title}{Part} [{Quality Full}]",
            "League-first naming with date. Good for sports organization.",
            true
        )},

        // Minimal clean naming
        { "minimal", (
            "{Event Title} - {Quality}",
            "Minimal naming - event title and quality only",
            false
        )},

        // Full details (similar to TRaSH Sonarr recommended)
        { "full-details", (
            "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full} [{Release Group}]",
            "Full details with quality and release group",
            true
        )},

        // Original filename preservation
        { "original", (
            "{Original Filename}",
            "Keep original filename from release",
            false
        )},
    };

    /// <summary>
    /// Folder naming presets
    /// </summary>
    public static readonly Dictionary<string, (string Format, string Description)> FolderNamingPresets = new()
    {
        { "plex-standard", (
            "{Series}/Season {Season}",
            "Plex TV Show structure - League/Season Year"
        )},

        { "league-year", (
            "{League}/{Year}",
            "Simple League/Year structure"
        )},

        { "sport-league-year", (
            "{Sport}/{League}/{Year}",
            "Full hierarchy: Sport/League/Year"
        )},

        { "flat", (
            "{League}",
            "Flat structure - all events in league folder"
        )},
    };

    /// <summary>
    /// Get a file naming preset, ensuring {Part} token is handled correctly
    /// </summary>
    public static string GetFileNamingPreset(string presetName, bool enableMultiPartEpisodes)
    {
        if (!FileNamingPresets.TryGetValue(presetName, out var preset))
            return FileNamingPresets["plex-standard"].Format;

        var format = preset.Format;

        // If multi-part is disabled but format has {Part}, remove it
        if (!enableMultiPartEpisodes && format.Contains("{Part}"))
        {
            format = format.Replace("{Part}", "");
            // Clean up any double spaces or trailing dashes
            format = System.Text.RegularExpressions.Regex.Replace(format, @"\s+", " ");
            format = System.Text.RegularExpressions.Regex.Replace(format, @"\s*-\s*-\s*", " - ");
            format = format.Trim(' ', '-');
        }
        // If multi-part is enabled but format doesn't have {Part} and supports it, add it
        else if (enableMultiPartEpisodes && !format.Contains("{Part}") && preset.SupportsMultiPart)
        {
            // Insert after {Episode} if exists
            if (format.Contains("{Episode}"))
            {
                format = format.Replace("{Episode}", "{Episode}{Part}");
            }
        }

        return format;
    }
}

/// <summary>
/// TRaSH quality profile template info for UI
/// </summary>
public class TrashQualityProfileInfo
{
    public string TrashId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>
    /// Number of qualities in this profile
    /// </summary>
    public int QualityCount { get; set; }

    /// <summary>
    /// Number of custom format scores defined
    /// </summary>
    public int FormatScoreCount { get; set; }

    /// <summary>
    /// Minimum format score setting
    /// </summary>
    public int? MinFormatScore { get; set; }

    /// <summary>
    /// Cutoff quality name
    /// </summary>
    public string? Cutoff { get; set; }
}
