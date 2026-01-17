namespace Sportarr.Api.Models;

/// <summary>
/// Represents a league excluded from import lists.
/// Maps to Sonarr's ImportListExclusionResource for Maintainerr compatibility.
/// </summary>
public class ImportListExclusion
{
    public int Id { get; set; }

    /// <summary>
    /// TheSportsDB ID (maps to Sonarr's tvdbId for Maintainerr compatibility)
    /// </summary>
    public int TvdbId { get; set; }

    /// <summary>
    /// League title at time of exclusion
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// When this exclusion was created
    /// </summary>
    public DateTime Added { get; set; } = DateTime.UtcNow;
}
