using System;
using System.ComponentModel.DataAnnotations;

namespace Sportarr.Api.Models;

/// <summary>
/// One row per resource change, consumed by the SSE feed (/api/stream).
/// The Id doubles as the client cursor: a reconnecting consumer sends
/// ?since=&lt;last seen Id&gt; and replays what it missed instead of doing
/// a full resync. Rows are pruned by housekeeping after 7 days.
/// </summary>
public class StreamEvent
{
    [Key]
    public int Id { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>"event" or "file".</summary>
    public required string ResourceType { get; set; }

    /// <summary>"added", "updated", "removed", "imported".</summary>
    public required string Action { get; set; }

    public int? EventId { get; set; }

    public string? ExternalId { get; set; }

    public int? LeagueId { get; set; }

    public string? Path { get; set; }
}
