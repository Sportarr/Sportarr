namespace Sportarr.Api.Models;

/// <summary>
/// Release Profile for filtering and scoring releases based on keywords
/// Similar to Sonarr's Release Profiles feature
/// </summary>
public class ReleaseProfile
{
    public int Id { get; set; }

    /// <summary>
    /// Name of the release profile for identification
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether this profile is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Required keywords - release must contain at least one
    /// Comma-separated regex patterns
    /// </summary>
    public string Required { get; set; } = string.Empty;

    /// <summary>
    /// Ignored keywords - release will be rejected if it contains any
    /// Comma-separated regex patterns
    /// </summary>
    public string Ignored { get; set; } = string.Empty;

    /// <summary>
    /// Preferred keywords with scores
    /// JSON serialized list of {term, score} pairs
    /// </summary>
    public List<PreferredKeyword> Preferred { get; set; } = new();

    /// <summary>
    /// Whether to include preferred when renaming
    /// </summary>
    public bool IncludePreferredWhenRenaming { get; set; } = false;

    /// <summary>
    /// Tags this profile applies to (empty = all)
    /// </summary>
    public List<int> Tags { get; set; } = new();

    /// <summary>
    /// Indexer IDs this profile applies to (empty = all)
    /// </summary>
    public List<int> IndexerId { get; set; } = new();

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// Preferred keyword with associated score
/// </summary>
public class PreferredKeyword
{
    /// <summary>
    /// Regex pattern for the preferred keyword
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Score to add when this keyword is found (can be negative)
    /// </summary>
    public int Value { get; set; } = 0;
}
