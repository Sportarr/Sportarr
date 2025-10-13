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
    public List<string> Images { get; set; } = new();
    public List<Fight> Fights { get; set; } = new();
    public DateTime Added { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdate { get; set; }
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
