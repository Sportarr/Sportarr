namespace Fightarr.Api.Models;

/// <summary>
/// System event log entry for audit trail and troubleshooting
/// </summary>
public class SystemEvent
{
    public int Id { get; set; }

    /// <summary>
    /// Timestamp when the event occurred
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Type of event (Info, Warning, Error, Success)
    /// </summary>
    public EventType Type { get; set; } = EventType.Info;

    /// <summary>
    /// Category/source of the event
    /// </summary>
    public EventCategory Category { get; set; }

    /// <summary>
    /// Brief message describing the event
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Detailed information about the event
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Related entity ID (e.g., Event ID, Download ID)
    /// </summary>
    public int? RelatedEntityId { get; set; }

    /// <summary>
    /// Related entity type (e.g., "Event", "Download", "Indexer")
    /// </summary>
    public string? RelatedEntityType { get; set; }

    /// <summary>
    /// User who triggered the action (if applicable)
    /// </summary>
    public string? User { get; set; }
}

/// <summary>
/// Type/severity of system event
/// </summary>
public enum EventType
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

/// <summary>
/// Category/source of system event
/// </summary>
public enum EventCategory
{
    System = 0,
    Database = 1,
    Download = 2,
    Import = 3,
    Indexer = 4,
    Search = 5,
    Quality = 6,
    Backup = 7,
    Update = 8,
    Settings = 9,
    Authentication = 10,
    Task = 11,
    Notification = 12,
    Metadata = 13
}
