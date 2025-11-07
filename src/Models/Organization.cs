namespace Fightarr.Api.Models;

/// <summary>
/// Represents a fighting organization (e.g., UFC, Bellator, ONE Championship)
/// Similar to Sonarr's Series concept - a container for events
/// </summary>
public class Organization
{
    public int Id { get; set; }

    /// <summary>
    /// Organization name (e.g., "UFC", "Bellator")
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether this organization is monitored for automatic downloads
    /// </summary>
    public bool Monitored { get; set; } = true;

    /// <summary>
    /// Default quality profile for all events in this organization
    /// Events can override this with their own QualityProfileId
    /// </summary>
    public int? QualityProfileId { get; set; }

    /// <summary>
    /// Organization logo/poster URL
    /// </summary>
    public string? PosterUrl { get; set; }

    /// <summary>
    /// When this organization was added to the library
    /// </summary>
    public DateTime Added { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time organization settings were updated
    /// </summary>
    public DateTime? LastUpdate { get; set; }
}
