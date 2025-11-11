using System.Text.Json.Serialization;

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
    [JsonPropertyName("idLeague")]
    public string? ExternalId { get; set; }

    /// <summary>
    /// League name (e.g., "UFC", "NFL", "Premier League")
    /// </summary>
    [JsonPropertyName("strLeague")]
    public required string Name { get; set; }

    /// <summary>
    /// Sport type (e.g., "Soccer", "Fighting", "Basketball", "Baseball")
    /// </summary>
    [JsonPropertyName("strSport")]
    public required string Sport { get; set; }

    /// <summary>
    /// League country/region (e.g., "USA", "England", "International")
    /// </summary>
    [JsonPropertyName("strCountry")]
    public string? Country { get; set; }

    /// <summary>
    /// League description
    /// </summary>
    [JsonPropertyName("strDescriptionEN")]
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
    [JsonPropertyName("strBadge")]
    public string? LogoUrl { get; set; }

    /// <summary>
    /// League banner image URL
    /// </summary>
    [JsonPropertyName("strBanner")]
    public string? BannerUrl { get; set; }

    /// <summary>
    /// League poster/trophy image URL
    /// </summary>
    [JsonPropertyName("strPoster")]
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Official league website
    /// </summary>
    [JsonPropertyName("strWebsite")]
    public string? Website { get; set; }

    /// <summary>
    /// Year the league was formed
    /// </summary>
    [JsonPropertyName("intFormedYear")]
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

/// <summary>
/// DTO for adding a league from the frontend (uses camelCase)
/// Frontend sends camelCase JSON, this DTO accepts it without JsonPropertyName conflicts
/// </summary>
public class AddLeagueRequest
{
    public string? ExternalId { get; set; }
    public required string Name { get; set; }
    public required string Sport { get; set; }
    public string? Country { get; set; }
    public string? Description { get; set; }
    public bool Monitored { get; set; } = true;
    public int? QualityProfileId { get; set; }
    public string? LogoUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? PosterUrl { get; set; }
    public string? Website { get; set; }
    public int? FormedYear { get; set; }

    /// <summary>
    /// Convert DTO to League entity for database
    /// </summary>
    public League ToLeague()
    {
        return new League
        {
            ExternalId = ExternalId,
            Name = Name,
            Sport = Sport,
            Country = Country,
            Description = Description,
            Monitored = Monitored,
            QualityProfileId = QualityProfileId,
            LogoUrl = LogoUrl,
            BannerUrl = BannerUrl,
            PosterUrl = PosterUrl,
            Website = Website,
            FormedYear = FormedYear,
            Added = DateTime.UtcNow
        };
    }
}
