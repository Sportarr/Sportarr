namespace Sportarr.Api.Models;

/// <summary>
/// An athlete the user follows across events. Mirrors FollowedTeam, but at
/// person level: the metadata API carries per-person event participation for
/// fighting sports (a fight card has a participant row per fighter), so
/// following an athlete means monitoring every event they appear on. Team
/// sports link participants at team level - a team-sport athlete is served
/// by following their team instead.
/// </summary>
public class FollowedAthlete
{
    public int Id { get; set; }

    /// <summary>
    /// Sportarr API person ID (pn- short id) - stable across events.
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>
    /// Athlete name (e.g., "Dan Ige").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Sport type (fighting sports in v1, e.g., "Combat").
    /// </summary>
    public string Sport { get; set; } = string.Empty;

    /// <summary>
    /// Athlete photo/thumb URL.
    /// </summary>
    public string? ThumbUrl { get; set; }

    /// <summary>
    /// When the athlete was followed.
    /// </summary>
    public DateTime Added { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time their event list was fetched for league discovery.
    /// </summary>
    public DateTime? LastEventDiscovery { get; set; }

    /// <summary>
    /// For team-sport athletes: the current team resolved from the metadata
    /// API's roster data at discovery time. Team-sport athletes have no
    /// per-event participation rows, so their events are their team's
    /// events; re-resolved on each discovery so trades follow the player.
    /// Null for combat athletes (person-level participation covers them).
    /// </summary>
    public string? ResolvedTeamExternalId { get; set; }

    /// <summary>
    /// Display name of the resolved current team.
    /// </summary>
    public string? ResolvedTeamName { get; set; }
}
