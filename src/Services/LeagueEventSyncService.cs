using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for syncing events from TheSportsDB API to populate league events
/// Similar to Sonarr's series monitoring and episode discovery
/// </summary>
public class LeagueEventSyncService
{
    private readonly SportarrDbContext _db;
    private readonly TheSportsDBClient _theSportsDBClient;
    private readonly ILogger<LeagueEventSyncService> _logger;

    public LeagueEventSyncService(
        SportarrDbContext db,
        TheSportsDBClient theSportsDBClient,
        ILogger<LeagueEventSyncService> logger)
    {
        _db = db;
        _theSportsDBClient = theSportsDBClient;
        _logger = logger;
    }

    /// <summary>
    /// Sync events for a league from TheSportsDB API
    /// </summary>
    /// <param name="leagueId">Internal Sportarr league ID</param>
    /// <param name="seasons">Seasons to sync (e.g., ["2024", "2025"]). If null, defaults to current year.</param>
    /// <returns>Result with counts of new, updated, and skipped events</returns>
    public async Task<LeagueEventSyncResult> SyncLeagueEventsAsync(int leagueId, List<string>? seasons = null)
    {
        var result = new LeagueEventSyncResult { LeagueId = leagueId };

        _logger.LogInformation("[League Event Sync] Starting sync for league ID: {LeagueId}", leagueId);

        // Get league from database
        var league = await _db.Leagues.FindAsync(leagueId);
        if (league == null)
        {
            result.Success = false;
            result.Message = "League not found";
            _logger.LogWarning("[League Event Sync] League not found: {LeagueId}", leagueId);
            return result;
        }

        // If ExternalId is missing, we can't sync from TheSportsDB
        if (string.IsNullOrEmpty(league.ExternalId))
        {
            result.Success = false;
            result.Message = "League is missing TheSportsDB External ID";
            _logger.LogWarning("[League Event Sync] League missing External ID: {LeagueName}", league.Name);
            return result;
        }

        // Default to comprehensive season range if no seasons specified
        // Fetch past, present, and future events to ensure complete coverage
        if (seasons == null || !seasons.Any())
        {
            seasons = GenerateSeasonRange(league.Sport);
            _logger.LogInformation("[League Event Sync] No seasons specified, using comprehensive range: {Seasons}",
                string.Join(", ", seasons));
        }

        _logger.LogInformation("[League Event Sync] Syncing seasons: {Seasons} for league: {LeagueName}",
            string.Join(", ", seasons), league.Name);

        // Track consecutive empty seasons for early termination
        int consecutiveEmptySeasons = 0;
        const int maxConsecutiveEmpty = 10; // Stop after 10 consecutive seasons with no data

        // Sync each season
        foreach (var season in seasons)
        {
            _logger.LogInformation("[League Event Sync] Fetching events for season: {Season}", season);

            var events = await _theSportsDBClient.GetLeagueSeasonAsync(league.ExternalId, season);

            if (events == null || !events.Any())
            {
                consecutiveEmptySeasons++;
                _logger.LogInformation("[League Event Sync] No events found for season: {Season} (consecutive empty: {Consecutive})",
                    season, consecutiveEmptySeasons);

                // Early termination: if we hit too many consecutive empty seasons, stop
                if (consecutiveEmptySeasons >= maxConsecutiveEmpty)
                {
                    _logger.LogInformation("[League Event Sync] Stopping sync after {Count} consecutive empty seasons. League may not have older data.",
                        consecutiveEmptySeasons);
                    break;
                }
                continue;
            }

            // Reset counter when we find data
            consecutiveEmptySeasons = 0;

            _logger.LogInformation("[League Event Sync] Found {Count} events from TheSportsDB for season: {Season}",
                events.Count, season);

            // Process each event
            foreach (var apiEvent in events)
            {
                try
                {
                    await ProcessEventAsync(apiEvent, league, result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[League Event Sync] Failed to process event: {EventTitle}",
                        apiEvent.Title);
                    result.FailedCount++;
                }
            }
        }

        // Save all changes
        await _db.SaveChangesAsync();

        result.Success = true;
        result.Message = $"Synced {result.NewCount} new events, updated {result.UpdatedCount} events, skipped {result.SkippedCount} duplicates";
        _logger.LogInformation("[League Event Sync] Completed: {Message}", result.Message);

        return result;
    }

    /// <summary>
    /// Process a single event from TheSportsDB API
    /// </summary>
    private async Task ProcessEventAsync(Event apiEvent, League league, LeagueEventSyncResult result)
    {
        // Check if event already exists by ExternalId
        var existingEvent = await _db.Events
            .FirstOrDefaultAsync(e => e.ExternalId == apiEvent.ExternalId);

        if (existingEvent != null)
        {
            // Event already exists - optionally update it
            _logger.LogDebug("[League Event Sync] Event already exists: {EventTitle}", apiEvent.Title);

            // Update status and scores if changed (for completed events)
            bool needsUpdate = false;
            if (existingEvent.Status != apiEvent.Status)
            {
                existingEvent.Status = apiEvent.Status;
                needsUpdate = true;
            }
            if (existingEvent.HomeScore != apiEvent.HomeScore)
            {
                existingEvent.HomeScore = apiEvent.HomeScore;
                needsUpdate = true;
            }
            if (existingEvent.AwayScore != apiEvent.AwayScore)
            {
                existingEvent.AwayScore = apiEvent.AwayScore;
                needsUpdate = true;
            }

            if (needsUpdate)
            {
                existingEvent.LastUpdate = DateTime.UtcNow;
                result.UpdatedCount++;
                _logger.LogInformation("[League Event Sync] Updated event: {EventTitle}", apiEvent.Title);
            }
            else
            {
                result.SkippedCount++;
            }

            return;
        }

        // Event doesn't exist - create new one
        _logger.LogInformation("[League Event Sync] Creating new event: {EventTitle}", apiEvent.Title);

        // Handle team relationships (for team sports)
        int? homeTeamId = null;
        int? awayTeamId = null;

        // Note: TheSportsDB returns team names in the event data
        // We would need to look them up or create them
        // For now, we'll skip team creation and just store the event
        // Team syncing can be a separate service/feature

        // Create new event entity
        var newEvent = new Event
        {
            ExternalId = apiEvent.ExternalId,
            Title = apiEvent.Title,
            Sport = apiEvent.Sport,
            LeagueId = league.Id,
            HomeTeamId = homeTeamId,
            AwayTeamId = awayTeamId,
            Season = apiEvent.Season,
            Round = apiEvent.Round,
            EventDate = apiEvent.EventDate,
            Venue = apiEvent.Venue,
            Location = apiEvent.Location,
            Broadcast = apiEvent.Broadcast,
            Status = apiEvent.Status,
            HomeScore = apiEvent.HomeScore,
            AwayScore = apiEvent.AwayScore,
            Images = apiEvent.Images ?? new List<string>(),

            // Inherit monitoring and quality profile from league
            Monitored = league.Monitored,
            QualityProfileId = league.QualityProfileId,

            // File tracking
            HasFile = false,
            FilePath = null,
            Quality = null,

            // Timestamps
            Added = DateTime.UtcNow,
            LastUpdate = DateTime.UtcNow
        };

        _db.Events.Add(newEvent);
        result.NewCount++;

        _logger.LogInformation("[League Event Sync] Added event: {EventTitle} on {EventDate}",
            newEvent.Title, newEvent.EventDate.ToString("yyyy-MM-dd"));
    }

    /// <summary>
    /// Generate comprehensive season range for a sport
    /// Returns ALL seasons to ensure complete event history is discovered
    /// Similar to Sonarr showing all seasons of a TV show, not just recent ones
    /// </summary>
    private List<string> GenerateSeasonRange(string sport)
    {
        var seasons = new List<string>();
        var currentYear = DateTime.UtcNow.Year;

        // Fetch ALL historical events + future events
        // Start from 1900: Captures complete sports history including early leagues (NFL 1920, MLB 1903, etc.)
        // End at current year + 5: Catches all scheduled future events
        // If TheSportsDB has no events for a year, the API returns empty (handled gracefully)
        const int startYear = 1900;
        int endYear = currentYear + 5;

        for (int year = startYear; year <= endYear; year++)
        {
            seasons.Add(year.ToString());
        }

        _logger.LogInformation("[League Event Sync] Generated comprehensive season range for {Sport}: {StartYear}-{EndYear} ({Count} seasons)",
            sport, startYear, endYear, seasons.Count);

        return seasons;
    }
}

/// <summary>
/// Result of league event sync operation
/// </summary>
public class LeagueEventSyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int LeagueId { get; set; }
    public int NewCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalCount => NewCount + UpdatedCount + SkippedCount;
}
