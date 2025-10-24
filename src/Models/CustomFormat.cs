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
/// A single specification within a custom format
/// Multiple specifications are combined with AND/OR logic
/// </summary>
public class FormatSpecification
{
    public int Id { get; set; }

    /// <summary>
    /// Name of this specification (e.g., "1080p Resolution", "BluRay Source")
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Type of specification (Resolution, Source, ReleaseGroup, etc.)
    /// </summary>
    public SpecificationType Type { get; set; }

    /// <summary>
    /// Whether this specification must match (true) or must NOT match (false)
    /// </summary>
    public bool Negate { get; set; }

    /// <summary>
    /// Whether this specification is required for the format to match
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Regex pattern to match against release title
    /// </summary>
    public required string Pattern { get; set; }
}

/// <summary>
/// Types of format specifications
/// </summary>
public enum SpecificationType
{
    /// <summary>Match against full release title</summary>
    ReleaseTitleRegex,

    /// <summary>Match resolution (1080p, 2160p, etc.)</summary>
    Resolution,

    /// <summary>Match source (BluRay, WEB-DL, HDTV, etc.)</summary>
    Source,

    /// <summary>Match release group</summary>
    ReleaseGroup,

    /// <summary>Match edition (Director's Cut, IMAX, etc.)</summary>
    Edition,

    /// <summary>Match audio codec (DTS, TrueHD, Atmos, etc.)</summary>
    AudioCodec,

    /// <summary>Match video codec (H.264, H.265/HEVC, AV1, etc.)</summary>
    VideoCodec,

    /// <summary>Match file size</summary>
    Size,

    /// <summary>Match indexer</summary>
    Indexer,

    /// <summary>Match language</summary>
    Language
}

/// <summary>
/// Quality definition with min/max sizes
/// </summary>
public class QualityDefinition
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>
    /// Display title for UI
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Weight/priority for this quality (higher = better)
    /// </summary>
    public int Weight { get; set; }

    /// <summary>
    /// Minimum size in MB per hour of content
    /// </summary>
    public double? MinSize { get; set; }

    /// <summary>
    /// Maximum size in MB per hour of content
    /// </summary>
    public double? MaxSize { get; set; }

    /// <summary>
    /// Preferred size in MB per hour of content
    /// </summary>
    public double? PreferredSize { get; set; }
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
