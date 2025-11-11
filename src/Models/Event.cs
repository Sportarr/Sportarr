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
    public string Sport { get; set; } = "Fighting";

    /// <summary>
    /// League/competition this event belongs to
    /// TheSportsDB treats UFC, Premier League, NBA all as Leagues
    /// </summary>
    public int? LeagueId { get; set; }
    public League? League { get; set; }

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
    public bool HasFile { get; set; }
    public string? FilePath { get; set; }
    public long? FileSize { get; set; }
    public string? Quality { get; set; }
    public int? QualityProfileId { get; set; }
    public List<string> Images { get; set; } = new();

    public DateTime Added { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdate { get; set; }

    /// <summary>
    /// Fight details for combat sports events (DISPLAY ONLY - not for monitoring)
    /// </summary>
    public List<Fight> Fights { get; set; } = new();

    // Results (populated after event completion)
    /// <summary>
    /// Home team/fighter score (for completed events)
    /// </summary>
    [JsonPropertyName("intHomeScore")]
    public int? HomeScore { get; set; }

    /// <summary>
    /// Away team/fighter score (for completed events)
    /// </summary>
    [JsonPropertyName("intAwayScore")]
    public int? AwayScore { get; set; }

    /// <summary>
    /// Event status from TheSportsDB (Scheduled, Live, Completed, Postponed, Cancelled)
    /// </summary>
    [JsonPropertyName("strStatus")]
    public string? Status { get; set; }
}

/// <summary>
/// Individual fight within a combat sports event
/// NOTE: This is for DISPLAY purposes only, not for monitoring subdivisions
/// TheSportsDB provides fight details, but monitoring happens at Event level
/// </summary>
public class Fight
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }

    /// <summary>
    /// Fighter 1 name
    /// </summary>
    public required string Fighter1 { get; set; }

    /// <summary>
    /// Fighter 2 name
    /// </summary>
    public required string Fighter2 { get; set; }

    /// <summary>
    /// Weight class (e.g., "Lightweight", "Heavyweight")
    /// </summary>
    public string? WeightClass { get; set; }

    /// <summary>
    /// Whether this is the main event fight
    /// </summary>
    public bool IsMainEvent { get; set; }

    /// <summary>
    /// Whether this is a title fight
    /// </summary>
    public bool IsTitleFight { get; set; }

    /// <summary>
    /// Display order on the card (1 = first fight)
    /// </summary>
    public int FightOrder { get; set; }

    /// <summary>
    /// Fight result (e.g., "KO", "Decision", "Submission")
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Winning fighter name
    /// </summary>
    public string? Winner { get; set; }
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
    public string? Round { get; set; }
    public DateTime EventDate { get; set; }
    public string? Venue { get; set; }
    public string? Location { get; set; }
    public string? Broadcast { get; set; }
    public bool Monitored { get; set; }
    public bool HasFile { get; set; }
    public string? FilePath { get; set; }
    public long? FileSize { get; set; }
    public string? Quality { get; set; }
    public int? QualityProfileId { get; set; }
    public List<string> Images { get; set; } = new();
    public DateTime Added { get; set; }
    public DateTime? LastUpdate { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }
    public string? Status { get; set; }

    // Combat sports only
    public List<FightResponse> Fights { get; set; } = new();

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
            Round = evt.Round,
            EventDate = evt.EventDate,
            Venue = evt.Venue,
            Location = evt.Location,
            Broadcast = evt.Broadcast,
            Monitored = evt.Monitored,
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
            Fights = evt.Fights.Select(FightResponse.FromFight).ToList()
        };
    }
}

/// <summary>
/// DTO for fight details (combat sports only)
/// </summary>
public class FightResponse
{
    public int Id { get; set; }
    public string Fighter1 { get; set; } = string.Empty;
    public string Fighter2 { get; set; } = string.Empty;
    public string? WeightClass { get; set; }
    public bool IsMainEvent { get; set; }
    public bool IsTitleFight { get; set; }
    public int FightOrder { get; set; }
    public string? Result { get; set; }
    public string? Winner { get; set; }

    public static FightResponse FromFight(Fight fight)
    {
        return new FightResponse
        {
            Id = fight.Id,
            Fighter1 = fight.Fighter1,
            Fighter2 = fight.Fighter2,
            WeightClass = fight.WeightClass,
            IsMainEvent = fight.IsMainEvent,
            IsTitleFight = fight.IsTitleFight,
            FightOrder = fight.FightOrder,
            Result = fight.Result,
            Winner = fight.Winner
        };
    }
}
