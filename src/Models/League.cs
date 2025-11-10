namespace Sportarr.Api.Models;

/// <summary>
/// Represents a sports league/competition (e.g., NFL, NBA, UFC, Premier League)
/// Replaces the concept of Organization for universal sports support
/// Similar to Sonarr's Series concept - a container for events/games/matches
/// </summary>
public class League
{
    public int Id { get; set; }

    /// <summary>
    /// League ID from TheSportsDB API
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// League name (e.g., "UFC", "NFL", "Premier League")
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Sport type (e.g., "Soccer", "Fighting", "Basketball", "Baseball")
    /// </summary>
    public required string Sport { get; set; }

    /// <summary>
    /// League country/region (e.g., "USA", "England", "International")
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// League description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this league is monitored for automatic downloads
    /// </summary>
    public bool Monitored { get; set; } = true;

    /// <summary>
    /// Default quality profile for all events in this league
    /// Events can override this with their own QualityProfileId
    /// </summary>
    public int? QualityProfileId { get; set; }

    /// <summary>
    /// League logo/badge URL
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// League banner image URL
    /// </summary>
    public string? BannerUrl { get; set; }

    /// <summary>
    /// League poster/trophy image URL
    /// </summary>
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Official league website
    /// </summary>
    public string? Website { get; set; }

    /// <summary>
    /// Year the league was formed
    /// </summary>
    public int? FormedYear { get; set; }

    /// <summary>
    /// When this league was added to the library
    /// </summary>
    public DateTime Added { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time league metadata was updated from TheSportsDB
    /// </summary>
    public DateTime? LastUpdate { get; set; }
}
