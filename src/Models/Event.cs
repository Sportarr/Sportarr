namespace Fightarr.Api.Models;

public class Event
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Organization { get; set; }
    public DateTime EventDate { get; set; }
    public string? Venue { get; set; }
    public string? Location { get; set; }
    public bool Monitored { get; set; } = true;
    public bool HasFile { get; set; }
    public string? FilePath { get; set; }
    public long? FileSize { get; set; }
    public string? Quality { get; set; }
    public int? QualityProfileId { get; set; }
    public List<string> Images { get; set; } = new();
    public List<Fight> Fights { get; set; } = new();
    public List<FightCard> FightCards { get; set; } = new();
    public DateTime Added { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdate { get; set; }
}

/// <summary>
/// Represents a portion of an event (similar to Sonarr's Episode concept)
/// Examples: Main Card, Prelims, Early Prelims
/// </summary>
public class FightCard
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }

    /// <summary>
    /// Type of fight card: MainCard, Prelims, EarlyPrelims
    /// </summary>
    public required string CardType { get; set; }

    /// <summary>
    /// Display order (1 = Early Prelims, 2 = Prelims, 3 = Main Card)
    /// </summary>
    public int CardNumber { get; set; }

    /// <summary>
    /// Whether this fight card is monitored for automatic downloads
    /// </summary>
    public bool Monitored { get; set; } = true;

    /// <summary>
    /// Whether a file has been downloaded for this fight card
    /// </summary>
    public bool HasFile { get; set; }

    /// <summary>
    /// Path to the downloaded file for this specific fight card
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long? FileSize { get; set; }

    /// <summary>
    /// Quality of the downloaded file (e.g., "1080p", "720p")
    /// </summary>
    public string? Quality { get; set; }

    /// <summary>
    /// Air/stream time for this specific card (may differ from main event)
    /// </summary>
    public DateTime? AirDate { get; set; }
}

public class Fight
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }
    public required string Fighter1 { get; set; }
    public required string Fighter2 { get; set; }
    public string? WeightClass { get; set; }
    public string? Result { get; set; }
    public string? Method { get; set; }
    public int? Round { get; set; }
    public string? Time { get; set; }
    public bool IsMainEvent { get; set; }
}
