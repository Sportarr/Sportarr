namespace Sportarr.Api.Models;

/// <summary>
/// Event mapping for matching release naming patterns to official database names.
/// Enables matching when release names differ from official sports event names.
///
/// Architecture:
/// - Mappings sync from central Sportarr-API (community-curated)
/// - Local overrides can be added with Source = "local"
/// - Priority determines which mapping takes precedence for overlapping patterns
///
/// Example: Formula 1 releases use "Formula1", "F1", "Formula.1" but database has "Formula 1"
/// </summary>
public class EventMapping
{
    public int Id { get; set; }

    /// <summary>
    /// Remote ID from Sportarr-API (null for local overrides)
    /// </summary>
    public int? RemoteId { get; set; }

    /// <summary>
    /// Sport type this mapping applies to: "Fighting", "Motorsport", "Basketball", etc.
    /// </summary>
    public string SportType { get; set; } = string.Empty;

    /// <summary>
    /// Sportarr API league ID (optional - some mappings are sport-wide)
    /// </summary>
    public string? LeagueId { get; set; }

    /// <summary>
    /// Official league name from Sportarr API
    /// </summary>
    public string? LeagueName { get; set; }

    /// <summary>
    /// Alternative names used in releases.
    /// Stored as JSON array: ["Formula1", "F1", "Formula.1", "Formula.One"]
    /// </summary>
    public List<string> ReleaseNames { get; set; } = new();

    /// <summary>
    /// Session/part pattern mappings as JSON.
    /// Example for F1: { "Practice 1": ["fp1", "practice.1"], "Race": ["race", "gp"] }
    /// </summary>
    public string? SessionPatternsJson { get; set; }

    /// <summary>
    /// Query building configuration as JSON.
    /// Example: { "prefix": "Formula1", "includeYear": true, "includeRound": true }
    /// </summary>
    public string? QueryConfigJson { get; set; }

    /// <summary>
    /// Whether this mapping is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Priority for overlapping patterns (higher = checked first)
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Source of this mapping: "community" (from API), "admin" (API admin), "local" (user override)
    /// </summary>
    public string Source { get; set; } = "community";

    /// <summary>
    /// When this mapping was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this mapping was last updated (from sync or manual edit)
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this mapping was last synced from the remote API
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }
}

/// <summary>
/// Session pattern configuration for a sport/league
/// </summary>
public class SessionPatterns
{
    /// <summary>
    /// Maps official session names to alternative patterns.
    /// Key: Official name (e.g., "Practice 1")
    /// Value: List of alternative patterns (e.g., ["fp1", "practice.1", "free.practice.1"])
    /// </summary>
    public Dictionary<string, List<string>> Patterns { get; set; } = new();
}

/// <summary>
/// Query building configuration for a sport/league
/// </summary>
public class QueryConfig
{
    /// <summary>
    /// Prefix to use for search queries (e.g., "Formula1", "UFC")
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Whether to include year in search queries
    /// </summary>
    public bool IncludeYear { get; set; } = true;

    /// <summary>
    /// Whether to include round number in search queries
    /// </summary>
    public bool IncludeRound { get; set; }

    /// <summary>
    /// Date format to use in queries (null = use default format)
    /// </summary>
    public string? DateFormat { get; set; }
}

/// <summary>
/// Tracks mapping requests submitted by this Sportarr instance.
/// Used to check status and notify user when requests are approved/rejected.
/// </summary>
public class SubmittedMappingRequest
{
    public int Id { get; set; }

    /// <summary>
    /// The remote request ID from Sportarr-API
    /// </summary>
    public int RemoteRequestId { get; set; }

    /// <summary>
    /// Sport type submitted
    /// </summary>
    public string SportType { get; set; } = string.Empty;

    /// <summary>
    /// League name submitted (optional)
    /// </summary>
    public string? LeagueName { get; set; }

    /// <summary>
    /// Release names submitted (comma-separated for display)
    /// </summary>
    public string ReleaseNames { get; set; } = string.Empty;

    /// <summary>
    /// Current status: pending, approved, rejected
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Review notes from admin (if rejected, contains reason)
    /// </summary>
    public string? ReviewNotes { get; set; }

    /// <summary>
    /// When the request was submitted
    /// </summary>
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the status was last checked
    /// </summary>
    public DateTime? LastCheckedAt { get; set; }

    /// <summary>
    /// When the request was reviewed (approved/rejected)
    /// </summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>
    /// Whether the user has been notified of the status change
    /// </summary>
    public bool UserNotified { get; set; } = false;
}
