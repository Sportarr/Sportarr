namespace Fightarr.Api.Models;

/// <summary>
/// Custom format for matching and scoring releases (matches Sonarr/Radarr)
/// Custom formats use regex patterns to match release titles and assign scores
/// </summary>
public class CustomFormat
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>
    /// Whether this format is included in renaming
    /// </summary>
    public bool IncludeCustomFormatWhenRenaming { get; set; }

    /// <summary>
    /// Specifications that must match for this format to apply
    /// </summary>
    public List<FormatSpecification> Specifications { get; set; } = new();

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// A single condition/specification within a custom format
/// Matches Sonarr's condition system
/// </summary>
public class FormatSpecification
{
    public int Id { get; set; }

    /// <summary>
    /// Display name of this specification (user-defined)
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Implementation type: ReleaseTitle, Language, IndexerFlag, Source, Resolution, Size, ReleaseGroup, ReleaseType
    /// </summary>
    public required string Implementation { get; set; }

    /// <summary>
    /// Whether this specification must NOT match (inverts the condition)
    /// </summary>
    public bool Negate { get; set; }

    /// <summary>
    /// Whether this specification is required for the format to match
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Fields/parameters for this specification (stored as JSON)
    /// For ReleaseTitle: { "value": "regex pattern" }
    /// For Language: { "value": 1 } (language ID)
    /// For Source: { "value": 1 } (source ID)
    /// For Resolution: { "value": 1 } (resolution ID)
    /// For Size: { "min": 1000, "max": 5000 } (in MB)
    /// For ReleaseGroup: { "value": "regex pattern" }
    /// For ReleaseType: { "value": 1 } (type ID)
    /// For IndexerFlag: { "value": 1 } (flag ID)
    /// </summary>
    public Dictionary<string, object> Fields { get; set; } = new();
}

/// <summary>
/// Quality definition with min/max sizes
/// </summary>
public class QualityDefinition
{
    public int Id { get; set; }

    /// <summary>
    /// Quality level number (maps to QualityProfile quality enum)
    /// </summary>
    public int Quality { get; set; }

    /// <summary>
    /// Display title for UI (e.g., "Bluray-1080p", "WEB 720p")
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Minimum size in GB per hour of content - files smaller will be rejected
    /// </summary>
    public decimal MinSize { get; set; }

    /// <summary>
    /// Maximum size in GB per hour of content - files larger will be rejected (null = unlimited)
    /// </summary>
    public decimal? MaxSize { get; set; }

    /// <summary>
    /// Preferred/target size in GB per hour - Fightarr will prefer releases closer to this
    /// </summary>
    public decimal PreferredSize { get; set; }

    public DateTime Created { get; set; }
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// Maps custom formats to scores within a quality profile
/// </summary>
public class ProfileFormatItem
{
    public int Id { get; set; }

    /// <summary>
    /// The custom format this score applies to
    /// </summary>
    public int FormatId { get; set; }
    public CustomFormat? Format { get; set; }

    /// <summary>
    /// Score to add when this format matches (-10000 to +10000)
    /// Positive = preferred, Negative = avoid
    /// </summary>
    public int Score { get; set; }
}

/// <summary>
/// Result of evaluating a release against a quality profile
/// Includes scoring, rejection reasons, and matched formats
/// </summary>
public class ReleaseEvaluation
{
    /// <summary>
    /// Whether this release is approved for download
    /// </summary>
    public bool Approved { get; set; }

    /// <summary>
    /// Total calculated score (quality + custom formats)
    /// </summary>
    public int TotalScore { get; set; }

    /// <summary>
    /// Base quality score
    /// </summary>
    public int QualityScore { get; set; }

    /// <summary>
    /// Sum of all custom format scores
    /// </summary>
    public int CustomFormatScore { get; set; }

    /// <summary>
    /// Detected quality level
    /// </summary>
    public string? Quality { get; set; }

    /// <summary>
    /// Custom formats that matched this release
    /// </summary>
    public List<MatchedFormat> MatchedFormats { get; set; } = new();

    /// <summary>
    /// Reasons why this release was rejected (empty if approved)
    /// </summary>
    public List<string> Rejections { get; set; } = new();
}

/// <summary>
/// A custom format that matched a release
/// </summary>
public class MatchedFormat
{
    public required string Name { get; set; }
    public int Score { get; set; }
}
