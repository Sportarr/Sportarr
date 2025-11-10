using System.Text.Json.Serialization;

namespace Fightarr.Api.Models.Metadata;

/// <summary>
/// Metadata models for Sportarr API integration (matches sportarr.net schema)
/// </summary>

public class EventsResponse
{
    [JsonPropertyName("events")]
    public List<MetadataEvent> Events { get; set; } = new();

    [JsonPropertyName("pagination")]
    public Pagination? Pagination { get; set; }
}

public class MetadataEvent
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("eventNumber")]
    public string? EventNumber { get; set; }

    [JsonPropertyName("eventDate")]
    public DateTime EventDate { get; set; }

    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("venue")]
    public string? Venue { get; set; }

    [JsonPropertyName("broadcaster")]
    public string? Broadcaster { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("posterUrl")]
    public string? PosterUrl { get; set; }

    [JsonPropertyName("organization")]
    public MetadataOrganization? Organization { get; set; }

    [JsonPropertyName("fights")]
    public List<MetadataFight>? Fights { get; set; }

    [JsonPropertyName("_count")]
    public EventCount? Count { get; set; }
}

public class EventCount
{
    [JsonPropertyName("fights")]
    public int Fights { get; set; }
}

public class MetadataFight
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("fighter1")]
    public MetadataFighter Fighter1 { get; set; } = new();

    [JsonPropertyName("fighter2")]
    public MetadataFighter Fighter2 { get; set; } = new();

    [JsonPropertyName("weightClass")]
    public string? WeightClass { get; set; }

    [JsonPropertyName("isTitleFight")]
    public bool IsTitleFight { get; set; }

    [JsonPropertyName("isMainEvent")]
    public bool IsMainEvent { get; set; }

    [JsonPropertyName("fightOrder")]
    public int FightOrder { get; set; }

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("round")]
    public int? Round { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }
}

public class MetadataFighter
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    [JsonPropertyName("weightClass")]
    public string? WeightClass { get; set; }

    [JsonPropertyName("nationality")]
    public string? Nationality { get; set; }

    [JsonPropertyName("wins")]
    public int Wins { get; set; }

    [JsonPropertyName("losses")]
    public int Losses { get; set; }

    [JsonPropertyName("draws")]
    public int Draws { get; set; }

    [JsonPropertyName("noContests")]
    public int NoContests { get; set; }

    [JsonPropertyName("birthDate")]
    public DateTime? BirthDate { get; set; }

    [JsonPropertyName("height")]
    public string? Height { get; set; }

    [JsonPropertyName("reach")]
    public string? Reach { get; set; }

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("bio")]
    public string? Bio { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}

public class MetadataOrganization
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("logoUrl")]
    public string? LogoUrl { get; set; }

    [JsonPropertyName("website")]
    public string? Website { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}

public class Pagination
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("totalEvents")]
    public int TotalEvents { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }
}

public class SearchResponse
{
    [JsonPropertyName("events")]
    public List<MetadataEvent>? Events { get; set; }

    [JsonPropertyName("fighters")]
    public List<MetadataFighter>? Fighters { get; set; }

    [JsonPropertyName("organizations")]
    public List<MetadataOrganization>? Organizations { get; set; }
}

public class HealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("database")]
    public string? Database { get; set; }
}
