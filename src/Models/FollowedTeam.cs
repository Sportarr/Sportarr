namespace Sportarr.Api.Models;

/// <summary>
/// Represents a team the user wants to follow across all leagues.
/// When a team is followed, the system discovers all leagues they play in
/// and enables bulk-adding those leagues with shared settings.
/// </summary>
public class FollowedTeam
{
    public int Id { get; set; }

    /// <summary>
    /// TheSportsDB team ID - used to identify the team across all leagues
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>
    /// Team name (e.g., "Real Madrid", "Manchester United")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Sport type (e.g., "Soccer", "Basketball", "Ice Hockey")
    /// </summary>
    public string Sport { get; set; } = string.Empty;

    /// <summary>
    /// Team badge/logo URL
    /// </summary>
    public string? BadgeUrl { get; set; }

    /// <summary>
    /// When the team was followed
    /// </summary>
    public DateTime Added { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time leagues were discovered for this team
    /// </summary>
    public DateTime? LastLeagueDiscovery { get; set; }
}
