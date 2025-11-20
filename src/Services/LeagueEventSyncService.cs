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

        // Get league from database with monitored teams
        var league = await _db.Leagues
            .Include(l => l.MonitoredTeams)
            .ThenInclude(lt => lt.Team)
            .FirstOrDefaultAsync(l => l.Id == leagueId);

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

        // Determine current season for MonitorType filtering
        var currentSeason = DateTime.UtcNow.Year.ToString();

        // Check for team-based filtering
        // Note: Disable team-based filtering for Fighting sports (UFC, Boxing, MMA, etc.)
        // because "teams" in these sports are weight classes, not the actual participants in fights
        var monitoredTeamIds = new HashSet<string>();

        if (league.Sport != "Fighting")
        {
            monitoredTeamIds = league.MonitoredTeams
                .Where(lt => lt.Monitored && lt.Team != null)
                .Select(lt => lt.Team!.ExternalId)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToHashSet();

            if (monitoredTeamIds.Any())
            {
                _logger.LogInformation("[League Event Sync] Team-based filtering enabled - monitoring {Count} teams: {Teams}",
                    monitoredTeamIds.Count,
                    string.Join(", ", league.MonitoredTeams.Where(lt => lt.Monitored && lt.Team != null).Select(lt => lt.Team!.Name).Take(5)));
            }
            else
            {
                _logger.LogInformation("[League Event Sync] No team filtering - will sync all events in league");
            }
        }
        else
        {
            _logger.LogInformation("[League Event Sync] Fighting sport detected - team filtering disabled (will sync all fights in league)");
        }

        // Default to smart season fetching if no seasons specified
        // Query TheSportsDB for actual available seasons instead of guessing years
        if (seasons == null || !seasons.Any())
        {
            _logger.LogInformation("[League Event Sync] Fetching available seasons from TheSportsDB for league: {LeagueName}", league.Name);

            var availableSeasons = await _theSportsDBClient.GetAllSeasonsAsync(league.ExternalId);

            if (availableSeasons != null && availableSeasons.Any())
            {
                // Use actual seasons that exist in TheSportsDB
                seasons = availableSeasons
                    .Where(s => !string.IsNullOrEmpty(s.StrSeason))
                    .Select(s => s.StrSeason!)
                    .ToList();

                // Add future years to catch upcoming events (current year + 5 years)
                var currentYear = DateTime.UtcNow.Year;
                for (int year = currentYear; year <= currentYear + 5; year++)
                {
                    var yearStr = year.ToString();
                    if (!seasons.Contains(yearStr))
                    {
                        seasons.Add(yearStr);
                    }
                }

                _logger.LogInformation("[League Event Sync] Found {Count} actual seasons from TheSportsDB (+ {FutureCount} future years): {FirstFew}...",
                    availableSeasons.Count, 5, string.Join(", ", seasons.Take(5)));
            }
            else
            {
                // Fallback to old method if API fails
                _logger.LogWarning("[League Event Sync] Could not fetch seasons from API, falling back to year range");
                seasons = GenerateSeasonRange(league.Sport);
            }
        }

        _logger.LogInformation("[League Event Sync] Syncing {Count} seasons for league: {LeagueName}",
            seasons.Count, league.Name);

        int seasonIndex = 0;
        // Sync each season
        foreach (var season in seasons)
        {
            seasonIndex++;
            var seasonStartCount = result.NewCount + result.UpdatedCount;

            _logger.LogInformation("[League Event Sync] Processing season {Current}/{Total}: {Season}",
                seasonIndex, seasons.Count, season);

            var events = await _theSportsDBClient.GetLeagueSeasonAsync(league.ExternalId, season);

            if (events == null || !events.Any())
            {
                _logger.LogInformation("[League Event Sync] Season {Season}: 0 events", season);
                continue;
            }

            // Filter events by monitored teams if team-based filtering is enabled
            var originalEventCount = events.Count;
            if (monitoredTeamIds.Any())
            {
                events = events.Where(e =>
                    (!string.IsNullOrEmpty(e.HomeTeamExternalId) && monitoredTeamIds.Contains(e.HomeTeamExternalId)) ||
                    (!string.IsNullOrEmpty(e.AwayTeamExternalId) && monitoredTeamIds.Contains(e.AwayTeamExternalId))
                ).ToList();

                _logger.LogInformation("[League Event Sync] Season {Season}: Filtered {Original} events to {Filtered} based on monitored teams",
                    season, originalEventCount, events.Count);
            }

            if (!events.Any())
            {
                _logger.LogInformation("[League Event Sync] Season {Season}: 0 events after filtering", season);
                continue;
            }

            // Process each event
            foreach (var apiEvent in events)
            {
                try
                {
                    await ProcessEventAsync(apiEvent, league, result, currentSeason);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[League Event Sync] Failed to process event: {EventTitle}",
                        apiEvent.Title);
                    result.FailedCount++;
                }
            }

            // Save changes after each season (batch save)
            await _db.SaveChangesAsync();

            var seasonEventsProcessed = (result.NewCount + result.UpdatedCount) - seasonStartCount;
            _logger.LogInformation("[League Event Sync] Season {Season}: {Count} events processed ({New} new, {Updated} updated)",
                season, seasonEventsProcessed, result.NewCount - seasonStartCount + result.UpdatedCount, result.UpdatedCount);
        }

        // Update league's last sync timestamp
        league.LastUpdate = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        result.Success = true;
        result.Message = $"Synced {result.NewCount} new events, updated {result.UpdatedCount} events, skipped {result.SkippedCount} duplicates";
        _logger.LogInformation("[League Event Sync] Completed: {Message}", result.Message);

        return result;
    }

    /// <summary>
    /// Process a single event from TheSportsDB API
    /// </summary>
    private async Task ProcessEventAsync(Event apiEvent, League league, LeagueEventSyncResult result, string currentSeason)
    {
        // Check if event already exists by ExternalId
        var existingEvent = await _db.Events
            .FirstOrDefaultAsync(e => e.ExternalId == apiEvent.ExternalId);

        if (existingEvent != null)
        {
            // Event already exists - update important fields
            _logger.LogDebug("[League Event Sync] Event already exists: {EventTitle}", apiEvent.Title);

            // Update key fields that may have changed
            bool needsUpdate = false;

            // Season (important for proper grouping/filtering)
            if (existingEvent.Season != apiEvent.Season)
            {
                _logger.LogInformation("[League Event Sync] Updating season for {EventTitle}: {Old} → {New}",
                    apiEvent.Title, existingEvent.Season ?? "null", apiEvent.Season ?? "null");
                existingEvent.Season = apiEvent.Season;
                needsUpdate = true;
            }

            // Round/Week
            if (existingEvent.Round != apiEvent.Round)
            {
                existingEvent.Round = apiEvent.Round;
                needsUpdate = true;
            }

            // Status (Scheduled, Live, Completed, etc.)
            if (existingEvent.Status != apiEvent.Status)
            {
                existingEvent.Status = apiEvent.Status;
                needsUpdate = true;
            }

            // Scores (for completed events)
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

            // Venue/Location (may change for rescheduled events)
            if (existingEvent.Venue != apiEvent.Venue)
            {
                existingEvent.Venue = apiEvent.Venue;
                needsUpdate = true;
            }
            if (existingEvent.Location != apiEvent.Location)
            {
                existingEvent.Location = apiEvent.Location;
                needsUpdate = true;
            }

            // Broadcast info (may be added later)
            if (existingEvent.Broadcast != apiEvent.Broadcast)
            {
                existingEvent.Broadcast = apiEvent.Broadcast;
                needsUpdate = true;
            }

            // Backfill Plex episode numbers for existing events (migration support)
            if (!existingEvent.SeasonNumber.HasValue && !string.IsNullOrEmpty(apiEvent.Season))
            {
                existingEvent.SeasonNumber = ParseSeasonNumber(apiEvent.Season);
                needsUpdate = true;
            }

            if (!existingEvent.EpisodeNumber.HasValue)
            {
                existingEvent.EpisodeNumber = await GetNextEpisodeNumberAsync(league.Id, apiEvent.Season);
                needsUpdate = true;
            }

            // NOTE: We do NOT update MonitoredParts for existing events during sync
            // This preserves any custom event-level MonitoredParts settings the user may have configured
            // MonitoredParts is only inherited from league when events are first created
            // If users want to bulk update MonitoredParts for existing events, they should use the
            // "Edit League" -> "Update all events" feature (future enhancement)

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

        // Try to link to existing Team entities using external IDs
        if (!string.IsNullOrEmpty(apiEvent.HomeTeamExternalId))
        {
            var homeTeam = await _db.Teams.FirstOrDefaultAsync(t => t.ExternalId == apiEvent.HomeTeamExternalId);
            homeTeamId = homeTeam?.Id;
            if (homeTeam != null)
            {
                _logger.LogDebug("[League Event Sync] Linked home team: {TeamName}", homeTeam.Name);
            }
        }

        if (!string.IsNullOrEmpty(apiEvent.AwayTeamExternalId))
        {
            var awayTeam = await _db.Teams.FirstOrDefaultAsync(t => t.ExternalId == apiEvent.AwayTeamExternalId);
            awayTeamId = awayTeam?.Id;
            if (awayTeam != null)
            {
                _logger.LogDebug("[League Event Sync] Linked away team: {TeamName}", awayTeam.Name);
            }
        }

        // Create new event entity
        var newEvent = new Event
        {
            ExternalId = apiEvent.ExternalId,
            Title = apiEvent.Title,
            Sport = apiEvent.Sport,
            LeagueId = league.Id,

            // Team relationships (internal database IDs)
            HomeTeamId = homeTeamId,
            AwayTeamId = awayTeamId,

            // Team external IDs from TheSportsDB (for filtering)
            HomeTeamExternalId = apiEvent.HomeTeamExternalId,
            AwayTeamExternalId = apiEvent.AwayTeamExternalId,
            HomeTeamName = apiEvent.HomeTeamName,
            AwayTeamName = apiEvent.AwayTeamName,

            Season = apiEvent.Season,
            SeasonNumber = ParseSeasonNumber(apiEvent.Season),
            EpisodeNumber = await GetNextEpisodeNumberAsync(league.Id, apiEvent.Season),
            Round = apiEvent.Round,
            EventDate = apiEvent.EventDate,
            Venue = apiEvent.Venue,
            Location = apiEvent.Location,
            Broadcast = apiEvent.Broadcast,
            Status = apiEvent.Status,
            HomeScore = apiEvent.HomeScore,
            AwayScore = apiEvent.AwayScore,
            Images = apiEvent.Images ?? new List<string>(),

            // Determine if event should be monitored based on league MonitorType
            Monitored = league.Monitored && ShouldMonitorEvent(league.MonitorType, apiEvent.EventDate, apiEvent.Season, currentSeason),
            QualityProfileId = league.QualityProfileId,

            // Inherit monitored parts from league (for Fighting sports with multi-part episodes)
            MonitoredParts = league.MonitoredParts,

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

        // Fallback range: Last 10 years + next 5 years
        // Only used when seasons API fails - most leagues should have season data in TheSportsDB
        // If you need more historical data, the league should be added to TheSportsDB with season info
        const int yearsBack = 10;
        const int yearsForward = 5;
        int oldestYear = currentYear - yearsBack;
        int newestYear = currentYear + yearsForward;

        // Generate in REVERSE order (newest first) to get current/recent events first
        for (int year = newestYear; year >= oldestYear; year--)
        {
            seasons.Add(year.ToString());
        }

        _logger.LogInformation("[League Event Sync] Generated fallback season range for {Sport}: {NewestYear}-{OldestYear} ({Count} seasons, newest first)",
            sport, newestYear, oldestYear, seasons.Count);

        return seasons;
    }

    /// <summary>
    /// Determines if an event should be monitored based on the league's MonitorType setting
    /// </summary>
    private static bool ShouldMonitorEvent(MonitorType monitorType, DateTime eventDate, string? eventSeason, string currentSeason)
    {
        var now = DateTime.UtcNow;

        return monitorType switch
        {
            MonitorType.All => true,
            MonitorType.Future => eventDate > now,
            MonitorType.CurrentSeason => eventSeason == currentSeason,
            MonitorType.LatestSeason => eventSeason == currentSeason, // Same as CurrentSeason for now
            MonitorType.NextSeason => !string.IsNullOrEmpty(eventSeason) &&
                                      int.TryParse(eventSeason.Split('-')[0], out var year) &&
                                      year == now.Year + 1,
            MonitorType.Recent => eventDate >= now.AddDays(-30),
            MonitorType.None => false,
            _ => true // Default to monitoring if unknown type
        };
    }

    /// <summary>
    /// Parse season string to extract year as integer for Plex compatibility
    /// Examples: "2024" -> 2024, "2023-2024" -> 2023, "2023/24" -> 2023
    /// </summary>
    private static int? ParseSeasonNumber(string? season)
    {
        if (string.IsNullOrEmpty(season))
            return null;

        // Try to parse as direct integer first (most common case: "2024")
        if (int.TryParse(season, out var year))
            return year;

        // Handle multi-year formats like "2023-2024" or "2023/24"
        var parts = season.Split(new[] { '-', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && int.TryParse(parts[0], out var startYear))
            return startYear;

        return null;
    }

    /// <summary>
    /// Get the next available episode number for a league/season combination
    /// Episode numbers are assigned sequentially within each season
    /// </summary>
    private async Task<int> GetNextEpisodeNumberAsync(int leagueId, string? season)
    {
        if (string.IsNullOrEmpty(season))
            return 1;

        // Get the highest episode number for this league+season
        var maxEpisode = await _db.Events
            .Where(e => e.LeagueId == leagueId && e.Season == season)
            .MaxAsync(e => (int?)e.EpisodeNumber);

        return (maxEpisode ?? 0) + 1;
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
