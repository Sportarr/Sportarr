using System.Text.Json.Serialization;

namespace Sportarr.Api.Models;

/// <summary>
/// Monitoring type for league events (similar to Sonarr's monitor types)
/// Determines which events are automatically monitored when syncing
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MonitorType
{
    /// <summary>
    /// Monitor all events (past, present, and future)
    /// </summary>
    All,

    /// <summary>
    /// Monitor only future events (events that haven't occurred yet)
    /// </summary>
    Future,

    /// <summary>
    /// Monitor only events in the current season
    /// </summary>
    CurrentSeason,

    /// <summary>
    /// Monitor only events in the latest/most recent season
    /// </summary>
    LatestSeason,

    /// <summary>
    /// Monitor only events in the next upcoming season
    /// </summary>
    NextSeason,

    /// <summary>
    /// Monitor recent events (last 30 days)
    /// </summary>
    Recent,

    /// <summary>
    /// Do not monitor any events (manual monitoring only)
    /// </summary>
    None
}

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
    /// How events should be monitored when syncing (All, Future, CurrentSeason, etc.)
    /// </summary>
    public MonitorType MonitorType { get; set; } = MonitorType.Future;

    /// <summary>
    /// Default quality profile for all events in this league
    /// Events can override this with their own QualityProfileId
    /// </summary>
    public int? QualityProfileId { get; set; }

    /// <summary>
    /// Automatically search for missing events when league is added or settings are updated
    /// This is a one-time search, not an ongoing background search
    /// </summary>
    public bool SearchForMissingEvents { get; set; } = false;

    /// <summary>
    /// Automatically search for quality upgrades when league is added or settings are updated
    /// This is a one-time search, not an ongoing background search
    /// </summary>
    public bool SearchForCutoffUnmetEvents { get; set; } = false;

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
    /// Year the league was formed (stored as string to match TheSportsDB API format)
    /// </summary>
    [JsonPropertyName("intFormedYear")]
    public string? FormedYear { get; set; }

    /// <summary>
    /// When this league was added to the library
    /// </summary>
    public DateTime Added { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time league metadata was updated from TheSportsDB
    /// </summary>
    public DateTime? LastUpdate { get; set; }

    /// <summary>
    /// Monitored teams for this league (for team-based filtering)
    /// </summary>
    public List<LeagueTeam> MonitoredTeams { get; set; } = new();
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

    /// <summary>
    /// How events should be monitored (All, Future, CurrentSeason, LatestSeason, etc.)
    /// </summary>
    public MonitorType MonitorType { get; set; } = MonitorType.Future;

    public int? QualityProfileId { get; set; }

    /// <summary>
    /// Automatically search for missing events when league is added or settings are updated
    /// This is a one-time search, not an ongoing background search
    /// </summary>
    public bool SearchForMissingEvents { get; set; } = false;

    /// <summary>
    /// Automatically search for quality upgrades when league is added or settings are updated
    /// This is a one-time search, not an ongoing background search
    /// </summary>
    public bool SearchForCutoffUnmetEvents { get; set; } = false;

    public string? LogoUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? PosterUrl { get; set; }
    public string? Website { get; set; }
    public string? FormedYear { get; set; }

    /// <summary>
    /// List of team external IDs to monitor for this league
    /// If empty/null, all teams in the league are monitored (default behavior)
    /// If specified, only events involving these teams will be synced
    /// </summary>
    public List<string>? MonitoredTeamIds { get; set; }

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
            MonitorType = MonitorType,
            QualityProfileId = QualityProfileId,
            SearchForMissingEvents = SearchForMissingEvents,
            SearchForCutoffUnmetEvents = SearchForCutoffUnmetEvents,
            LogoUrl = LogoUrl,
            BannerUrl = BannerUrl,
            PosterUrl = PosterUrl,
            Website = Website,
            FormedYear = FormedYear,
            Added = DateTime.UtcNow
        };
    }
}

/// <summary>
/// DTO for returning leagues to the frontend (uses camelCase without JsonPropertyName)
/// Avoids JsonPropertyName conflicts when serializing to frontend
/// </summary>
public class LeagueResponse
{
    public int Id { get; set; }
    public string? ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sport { get; set; } = string.Empty;
    public string? Country { get; set; }
    public string? Description { get; set; }
    public bool Monitored { get; set; }
    public MonitorType MonitorType { get; set; }
    public int? QualityProfileId { get; set; }
    public bool SearchForMissingEvents { get; set; }
    public bool SearchForCutoffUnmetEvents { get; set; }
    public string? LogoUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? PosterUrl { get; set; }
    public string? Website { get; set; }
    public string? FormedYear { get; set; }
    public DateTime Added { get; set; }
    public DateTime? LastUpdate { get; set; }

    /// <summary>
    /// Total number of events in this league (calculated field, not stored in DB)
    /// </summary>
    public int EventCount { get; set; }

    /// <summary>
    /// Number of monitored events in this league (calculated field, not stored in DB)
    /// </summary>
    public int MonitoredEventCount { get; set; }

    /// <summary>
    /// Number of downloaded/imported events (calculated field, not stored in DB)
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Convert League entity to response DTO
    /// </summary>
    public static LeagueResponse FromLeague(League league, int eventCount = 0, int monitoredEventCount = 0, int fileCount = 0)
    {
        return new LeagueResponse
        {
            Id = league.Id,
            ExternalId = league.ExternalId,
            Name = league.Name,
            Sport = league.Sport,
            Country = league.Country,
            Description = league.Description,
            Monitored = league.Monitored,
            MonitorType = league.MonitorType,
            QualityProfileId = league.QualityProfileId,
            SearchForMissingEvents = league.SearchForMissingEvents,
            SearchForCutoffUnmetEvents = league.SearchForCutoffUnmetEvents,
            LogoUrl = league.LogoUrl,
            BannerUrl = league.BannerUrl,
            PosterUrl = league.PosterUrl,
            Website = league.Website,
            FormedYear = league.FormedYear,
            Added = league.Added,
            LastUpdate = league.LastUpdate,
            EventCount = eventCount,
            MonitoredEventCount = monitoredEventCount,
            FileCount = fileCount
        };
    }
}

/// <summary>
/// Request model for refreshing events from TheSportsDB
/// </summary>
public class RefreshEventsRequest
{
    /// <summary>
    /// Seasons to refresh (e.g., ["2024", "2025"]). If null, defaults to current year.
    /// </summary>
    public List<string>? Seasons { get; set; }
}

/// <summary>
/// Request model for updating monitored teams for a league
/// </summary>
public class UpdateMonitoredTeamsRequest
{
    /// <summary>
    /// External team IDs (from TheSportsDB) to monitor
    /// If null or empty, league will be set as not monitored
    /// </summary>
    public List<string>? MonitoredTeamIds { get; set; }
}
