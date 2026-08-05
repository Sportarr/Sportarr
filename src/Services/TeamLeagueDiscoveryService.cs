using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for discovering all leagues a team plays in.
/// Used for cross-league team monitoring - when a user follows a team,
/// this service finds all leagues they participate in so the user can bulk-add them.
/// </summary>
public class TeamLeagueDiscoveryService
{
    private readonly SportarrApiClient _sportsDbClient;
    private readonly ILogger<TeamLeagueDiscoveryService> _logger;

    /// <summary>
    /// Sports that support team-based cross-league monitoring. Verified
    /// against the metadata catalog (2026-07-31): every sport here has real
    /// home/away team rows on its events (not just a roster association),
    /// which team-based filtering and league discovery both depend on.
    /// Deliberately excludes mixed-format buckets (Watersports, Wintersports,
    /// Esports) where "team" events are inconsistent within the category,
    /// and anything already covered by LeagueSportRules.IsTeamlessSport.
    /// </summary>
    public static readonly HashSet<string> SupportedSports = new(StringComparer.OrdinalIgnoreCase)
    {
        "Soccer",
        "Football",           // American football in the catalog taxonomy (soccer is always "Soccer")
        "Basketball",
        "Ice Hockey",
        "Hockey",              // Alternative name for Ice Hockey
        "Baseball",
        "Rugby",
        "Volleyball",
        "Handball",
        "Cricket",
        "Australian Football",
        "Netball",
        "Field Hockey",
        "Lacrosse",
        "Gaelic"
    };

    /// <summary>
    /// Check if a sport supports cross-league team monitoring.
    /// </summary>
    public static bool IsSportSupported(string? sport)
    {
        if (string.IsNullOrEmpty(sport)) return false;
        // Exact membership, not substring: with "Football" in the set a
        // Contains check would also pass "Australian Football", and "Hockey"
        // would pass "Field Hockey" - both unsupported team-mode sports.
        return SupportedSports.Contains(sport.Trim());
    }

    /// <summary>
    /// Get a user-friendly list of supported sports for display. Skips the
    /// alternate-name duplicate ("Hockey") so the UI shows one entry per
    /// sport rather than every internal alias.
    /// </summary>
    public static List<string> GetSupportedSportsList()
    {
        return new List<string>
        {
            "Soccer", "Basketball", "Ice Hockey", "Football", "Baseball",
            "Rugby", "Volleyball", "Handball", "Cricket", "Australian Football",
            "Netball", "Field Hockey", "Lacrosse", "Gaelic"
        };
    }

    public TeamLeagueDiscoveryService(SportarrApiClient sportsDbClient, ILogger<TeamLeagueDiscoveryService> logger)
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
