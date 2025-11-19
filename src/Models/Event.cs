using System.Text.Json.Serialization;

namespace Sportarr.Api.Models;

/// <summary>
/// Request model for creating a new event (universal for all sports)
/// </summary>
public class CreateEventRequest
{
    /// <summary>
    /// Event ID from TheSportsDB API
    /// </summary>
    public string? ExternalId { get; set; }

    public required string Title { get; set; }

    /// <summary>
    /// Sport type (e.g., "Soccer", "Fighting", "Basketball", "Baseball")
    /// </summary>
    public required string Sport { get; set; }

    /// <summary>
    /// League/competition ID (REQUIRED for TheSportsDB alignment)
    /// UFC, Premier League, NBA are all leagues in TheSportsDB
    /// </summary>
    public int? LeagueId { get; set; }

    /// <summary>
    /// Home team ID (for team sports)
    /// </summary>
    public int? HomeTeamId { get; set; }

    /// <summary>
    /// Away team ID (for team sports)
    /// </summary>
    public int? AwayTeamId { get; set; }

    /// <summary>
    /// Season identifier (e.g., "2024", "2024-25")
    /// </summary>
    public string? Season { get; set; }

    /// <summary>
    /// Plex-compatible season number
    /// </summary>
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Plex-compatible episode number
    /// </summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>
    /// Round/week number (e.g., "Week 10", "Round 32")
    /// </summary>
    public string? Round { get; set; }

    public DateTime EventDate { get; set; }
    public string? Venue { get; set; }
    public string? Location { get; set; }

    /// <summary>
    /// TV broadcast information (network, channel)
    /// </summary>
    public string? Broadcast { get; set; }

    /// <summary>
    /// Event status from TheSportsDB (Scheduled, Live, Completed, etc.)
    /// </summary>
    public string? Status { get; set; }

    public bool Monitored { get; set; } = true;
    public int? QualityProfileId { get; set; }
    public List<string>? Images { get; set; }
}

/// <summary>
/// Universal Event model for all sports
/// Aligns with TheSportsDB V2 API structure
/// </summary>
public class Event
{
    public int Id { get; set; }

    /// <summary>
    /// Event ID from TheSportsDB API
    /// </summary>
    [JsonPropertyName("idEvent")]
    public string? ExternalId { get; set; }

    [JsonPropertyName("strEvent")]
    public required string Title { get; set; }

    /// <summary>
    /// Sport type (e.g., "Soccer", "Fighting", "Basketball")
    /// </summary>
    [JsonPropertyName("strSport")]
    public required string Sport { get; set; }

    /// <summary>
    /// League/competition this event belongs to
    /// TheSportsDB treats UFC, Premier League, NBA all as Leagues
    /// </summary>
    public int? LeagueId { get; set; }
    public League? League { get; set; }

    /// <summary>
    /// Home team external ID from TheSportsDB API
    /// Used for team-based filtering during event sync
    /// </summary>
    [JsonPropertyName("idHomeTeam")]
    public string? HomeTeamExternalId { get; set; }

    /// <summary>
    /// Away team external ID from TheSportsDB API
    /// Used for team-based filtering during event sync
    /// </summary>
    [JsonPropertyName("idAwayTeam")]
    public string? AwayTeamExternalId { get; set; }

    /// <summary>
    /// Home team name from TheSportsDB API
    /// </summary>
    [JsonPropertyName("strHomeTeam")]
    public string? HomeTeamName { get; set; }

    /// <summary>
    /// Away team name from TheSportsDB API
    /// </summary>
    [JsonPropertyName("strAwayTeam")]
    public string? AwayTeamName { get; set; }

    /// <summary>
    /// Home team (for team sports and combat sports)
    /// In combat sports: Fighter 1 or "Red Corner"
    /// </summary>
    public int? HomeTeamId { get; set; }
    public Team? HomeTeam { get; set; }

    /// <summary>
    /// Away team (for team sports and combat sports)
    /// In combat sports: Fighter 2 or "Blue Corner"
    /// </summary>
    public int? AwayTeamId { get; set; }
    public Team? AwayTeam { get; set; }

    /// <summary>
    /// Season year or identifier (e.g., "2024", "2024-25")
    /// </summary>
    [JsonPropertyName("strSeason")]
    public string? Season { get; set; }

    /// <summary>
    /// Plex-compatible season number (extracted from Season string)
    /// For year-based seasons, this is the year as an integer (2024)
    /// For multi-year seasons like "2023-2024", this is the start year (2023)
    /// </summary>
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Plex-compatible episode number within the season
    /// Auto-assigned sequentially when events are synced
    /// Allows Plex to display events as episodes in a TV show structure
    /// </summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>
    /// Round/week number (e.g., "Week 10", "Round 32", "Quarterfinals")
    /// </summary>
    [JsonPropertyName("intRound")]
    public string? Round { get; set; }

    [JsonPropertyName("dateEvent")]
    public DateTime EventDate { get; set; }

    [JsonPropertyName("strVenue")]
    public string? Venue { get; set; }

    [JsonPropertyName("strCountry")]
    public string? Location { get; set; }

    /// <summary>
    /// TV broadcast information (network, channel, streaming service)
    /// Populated from TheSportsDB TV schedule
    /// </summary>
    public string? Broadcast { get; set; }

    public bool Monitored { get; set; } = true;

    /// <summary>
    /// Which fight card parts to monitor for Fighting sports (comma-separated: "Early Prelims,Prelims,Main Card")
    /// If null or empty, uses league's MonitoredParts setting as default
    /// Only applies when EnableMultiPartEpisodes is true in config and Sport is Fighting/MMA/UFC/Boxing/etc.
    /// </summary>
    public string? MonitoredParts { get; set; }

    public bool HasFile { get; set; }
    public string? FilePath { get; set; }
    public long? FileSize { get; set; }
    public string? Quality { get; set; }
    public int? QualityProfileId { get; set; }
    public List<string> Images { get; set; } = new();

    public DateTime Added { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdate { get; set; }


    // Results (populated after event completion)
    /// <summary>
    /// Home team/fighter score (for completed events)
    /// </summary>
    /// TheSportsDB sometimes returns scores as strings, so we store as string
    [JsonPropertyName("intHomeScore")]
    public string? HomeScore { get; set; }

    /// <summary>
    /// Away team/fighter score (for completed events)
    /// TheSportsDB sometimes returns scores as strings, so we store as string
    /// </summary>
    [JsonPropertyName("intAwayScore")]
    public string? AwayScore { get; set; }

    /// <summary>
    /// Event status from TheSportsDB (Scheduled, Live, Completed, Postponed, Cancelled)
    /// </summary>
    [JsonPropertyName("strStatus")]
    public string? Status { get; set; }
}

/// <summary>
/// DTO for returning events to the frontend (uses camelCase without JsonPropertyName)
/// Avoids JsonPropertyName conflicts when serializing to frontend
/// Similar to LeagueResponse pattern
/// </summary>
public class EventResponse
{
    public int Id { get; set; }
    public string? ExternalId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Sport { get; set; } = string.Empty;
    public int? LeagueId { get; set; }
    public string? LeagueName { get; set; }
    public int? HomeTeamId { get; set; }
    public string? HomeTeamName { get; set; }
    public int? AwayTeamId { get; set; }
    public string? AwayTeamName { get; set; }
    public string? Season { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? Round { get; set; }
    public DateTime EventDate { get; set; }
    public string? Venue { get; set; }
    public string? Location { get; set; }
    public string? Broadcast { get; set; }
    public bool Monitored { get; set; }
    public string? MonitoredParts { get; set; }
    public bool HasFile { get; set; }
    public string? FilePath { get; set; }
    public long? FileSize { get; set; }
    public string? Quality { get; set; }
    public int? QualityProfileId { get; set; }
    public List<string> Images { get; set; } = new();
    public DateTime Added { get; set; }
    public DateTime? LastUpdate { get; set; }
    public string? HomeScore { get; set; }
    public string? AwayScore { get; set; }
    public string? Status { get; set; }

    /// <summary>
    /// Convert Event entity to response DTO
    /// </summary>
    public static EventResponse FromEvent(Event evt)
    {
        return new EventResponse
        {
            Id = evt.Id,
            ExternalId = evt.ExternalId,
            Title = evt.Title,
            Sport = evt.Sport,
            LeagueId = evt.LeagueId,
            LeagueName = evt.League?.Name,
            HomeTeamId = evt.HomeTeamId,
            HomeTeamName = evt.HomeTeam?.Name,
            AwayTeamId = evt.AwayTeamId,
            AwayTeamName = evt.AwayTeam?.Name,
            Season = evt.Season,
            SeasonNumber = evt.SeasonNumber,
            EpisodeNumber = evt.EpisodeNumber,
            Round = evt.Round,
            EventDate = evt.EventDate,
            Venue = evt.Venue,
            Location = evt.Location,
            Broadcast = evt.Broadcast,
            Monitored = evt.Monitored,
            MonitoredParts = evt.MonitoredParts,
            HasFile = evt.HasFile,
            FilePath = evt.FilePath,
            FileSize = evt.FileSize,
            Quality = evt.Quality,
            QualityProfileId = evt.QualityProfileId,
            Images = evt.Images,
            Added = evt.Added,
            LastUpdate = evt.LastUpdate,
            HomeScore = evt.HomeScore,
            AwayScore = evt.AwayScore,
            Status = evt.Status,
        };
    }
}
