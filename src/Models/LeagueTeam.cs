namespace Sportarr.Api.Models;

/// <summary>
/// Join table tracking which teams are monitored for each league
/// Enables team-based filtering: Monitor specific teams instead of entire league
/// Example: Monitor only Real Madrid and Barcelona in La Liga (76 events vs 380)
/// </summary>
public class LeagueTeam
{
    public int Id { get; set; }

    /// <summary>
    /// Reference to the league
    /// </summary>
    public int LeagueId { get; set; }
    public League? League { get; set; }

    /// <summary>
    /// Reference to the team
    /// </summary>
    public int TeamId { get; set; }
    public Team? Team { get; set; }

    /// <summary>
    /// Whether this team is monitored for this league
    /// When true, all events involving this team will be monitored
    /// </summary>
    public bool Monitored { get; set; } = true;

    /// <summary>
    /// When this team was added to monitoring for this league
    /// </summary>
    public DateTime Added { get; set; } = DateTime.UtcNow;
}
