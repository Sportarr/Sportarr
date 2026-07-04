namespace Sportarr.Api.Models;

/// <summary>
/// Per-season poster artwork for a league, from TheSportsDB's season art
/// archive (synced via the /list/seasonposters proxy endpoint). Lets each
/// season carry its own poster in media servers instead of every season
/// falling back to the parent league's poster.
/// </summary>
public class SeasonPoster
{
    public int Id { get; set; }

    /// <summary>
    /// League this season poster belongs to
    /// </summary>
    public int LeagueId { get; set; }
    public League? League { get; set; }

    /// <summary>
    /// Season label as TheSportsDB stores it, e.g. "2026" or "2025-2026"
    /// </summary>
    public required string Season { get; set; }

    /// <summary>
    /// URL of the season's dedicated poster image
    /// </summary>
    public required string PosterUrl { get; set; }

    /// <summary>
    /// When this row was last refreshed from the upstream archive
    /// </summary>
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
}
