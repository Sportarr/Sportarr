using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for discovering all leagues a team plays in.
/// Used for cross-league team monitoring - when a user follows a team,
/// this service finds all leagues they participate in so the user can bulk-add them.
/// </summary>
public class TeamLeagueDiscoveryService
{
    private readonly TheSportsDBClient _sportsDbClient;
    private readonly ILogger<TeamLeagueDiscoveryService> _logger;

    /// <summary>
    /// Sports that support team-based cross-league monitoring.
    /// These are sports where teams compete across multiple leagues/competitions.
    /// </summary>
    public static readonly HashSet<string> SupportedSports = new(StringComparer.OrdinalIgnoreCase)
    {
        "Soccer",
        "Football",           // Alternative name for Soccer in some regions
        "Basketball",
        "Ice Hockey",
        "Hockey"              // Alternative name for Ice Hockey
    };

    /// <summary>
    /// Check if a sport supports cross-league team monitoring.
    /// </summary>
    public static bool IsSportSupported(string? sport)
    {
        if (string.IsNullOrEmpty(sport)) return false;
        return SupportedSports.Any(s => sport.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get a user-friendly list of supported sports for display.
    /// </summary>
    public static List<string> GetSupportedSportsList()
    {
        return new List<string> { "Soccer", "Basketball", "Ice Hockey" };
    }

    public TeamLeagueDiscoveryService(TheSportsDBClient sportsDbClient, ILogger<TeamLeagueDiscoveryService> logger)
    {
        _sportsDbClient = sportsDbClient;
        _logger = logger;
    }

    /// <summary>
    /// Discover all leagues a team plays in using comprehensive event history (up to 250 events).
    /// Returns leagues sorted by event count (most active leagues first).
    /// </summary>
    public async Task<List<DiscoveredLeague>> DiscoverLeaguesForTeamAsync(string teamExternalId)
    {
        _logger.LogInformation("[TeamLeagueDiscovery] Discovering leagues for team {TeamId}", teamExternalId);

        // Use the new comprehensive endpoint
        var teamLeagues = await _sportsDbClient.GetTeamLeaguesAsync(teamExternalId);

        if (teamLeagues == null || !teamLeagues.Any())
        {
            _logger.LogWarning("[TeamLeagueDiscovery] No leagues found for team {TeamId}", teamExternalId);
            return new List<DiscoveredLeague>();
        }

        _logger.LogInformation("[TeamLeagueDiscovery] Found {Count} leagues for team {TeamId}", teamLeagues.Count, teamExternalId);

        // Fetch full league details for each discovered league
        var discoveredLeagues = new List<DiscoveredLeague>();

        foreach (var tl in teamLeagues)
        {
            try
            {
                var league = await _sportsDbClient.LookupLeagueAsync(tl.Id);
                discoveredLeagues.Add(new DiscoveredLeague
                {
                    ExternalId = tl.Id,
                    Name = league?.Name ?? tl.Name,
                    Sport = league?.Sport ?? tl.Sport,
                    Country = league?.Country,
                    BadgeUrl = league?.LogoUrl,
                    EventCount = tl.EventCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TeamLeagueDiscovery] Failed to lookup league details for {LeagueId}, using basic info", tl.Id);
                discoveredLeagues.Add(new DiscoveredLeague
                {
                    ExternalId = tl.Id,
                    Name = tl.Name,
                    Sport = tl.Sport,
                    EventCount = tl.EventCount
                });
            }
        }

        return discoveredLeagues.OrderByDescending(l => l.EventCount).ToList();
    }
}

/// <summary>
/// Represents a league discovered for a team through event history analysis.
/// </summary>
public class DiscoveredLeague
{
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Sport { get; set; } = string.Empty;
    public string? Country { get; set; }
    public string? BadgeUrl { get; set; }

    /// <summary>
    /// Number of events found for this team in this league (helps prioritize active leagues)
    /// </summary>
    public int EventCount { get; set; }
}
