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
    private readonly FileRenameService _fileRenameService;
    private readonly ILogger<LeagueEventSyncService> _logger;

    // Track seasons that need episode renumbering due to date changes
    private readonly HashSet<(int LeagueId, string Season)> _seasonsNeedingRenumber = new();

    public LeagueEventSyncService(
        SportarrDbContext db,
        TheSportsDBClient theSportsDBClient,
        FileRenameService fileRenameService,
        ILogger<LeagueEventSyncService> logger)
    {
        _db = db;
        _theSportsDBClient = theSportsDBClient;
        _fileRenameService = fileRenameService;
        _logger = logger;
    }

    /// <summary>
    /// Sync events for a league from TheSportsDB API
    /// </summary>
    /// <param name="leagueId">Internal Sportarr league ID</param>
    /// <param name="seasons">Seasons to sync (e.g., ["2024", "2025"]). If null, uses smart defaults.</param>
    /// <param name="fullHistoricalSync">If true, syncs ALL historical seasons (for initial league add).
    /// If false (default), only syncs current/future seasons (for scheduled refreshes).</param>
    /// <returns>Result with counts of new, updated, and skipped events</returns>
    public async Task<LeagueEventSyncResult> SyncLeagueEventsAsync(int leagueId, List<string>? seasons = null, bool fullHistoricalSync = false)
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
        // Note: Disable team-based filtering for certain sports where events don't have home/away teams:
        // - Fighting (UFC, Boxing, MMA): "teams" are weight classes, not fight participants
        // - Cycling: races don't have home/away teams, all teams participate in each race
        // - Motorsport: races don't have home/away teams
        // - Golf: tournaments have all players competing together, not home/away teams
        // - Darts: matches are between individual players, not teams
        // - Climbing: individual climbers compete, not teams
        // - Gambling (Poker, WSOP): individual players compete in tournaments, not teams
        // - Individual Tennis (ATP, WTA): matches are between players, not teams
        //   Note: Team-based tennis (Fed Cup, Davis Cup, Olympics) still needs team filtering
        var monitoredTeamIds = new HashSet<string>();
        var sportsWithoutTeamFiltering = new[] { "Fighting", "Cycling", "Motorsport", "Golf", "Darts", "Climbing", "Gambling" };
        var isIndividualTennis = IsIndividualTennisLeague(league.Sport, league.Name);

        if (!sportsWithoutTeamFiltering.Contains(league.Sport, StringComparer.OrdinalIgnoreCase) && !isIndividualTennis)
        {
            monitoredTeamIds = league.MonitoredTeams
                .Where(lt => lt.Monitored && lt.Team != null)
                .Select(lt => lt.Team!.ExternalId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
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
        else if (isIndividualTennis)
        {
            _logger.LogInformation("[League Event Sync] Individual tennis league (ATP/WTA) - team filtering disabled (matches between players, not teams)");
        }
        else
        {
            _logger.LogInformation("[League Event Sync] {Sport} sport detected - team filtering disabled (events don't have home/away teams)", league.Sport);
        }

        // Default to smart season fetching if no seasons specified
        // Query TheSportsDB for actual available seasons instead of guessing years
        if (seasons == null || !seasons.Any())
        {
            _logger.LogInformation("[League Event Sync] Fetching available seasons from TheSportsDB for league: {LeagueName} (fullHistoricalSync: {FullSync})",
                league.Name, fullHistoricalSync);

            var availableSeasons = await _theSportsDBClient.GetAllSeasonsAsync(league.ExternalId);

            if (availableSeasons != null && availableSeasons.Any())
            {
                // Get all seasons from TheSportsDB
                var allSeasons = availableSeasons
                    .Where(s => !string.IsNullOrEmpty(s.StrSeason))
                    .Select(s => s.StrSeason!)
                    .ToList();

                if (fullHistoricalSync)
                {
                    // FULL SYNC: Include ALL historical seasons (for initial league add)
                    // This ensures users have complete event history for the league
                    seasons = allSeasons;
                    _logger.LogInformation("[League Event Sync] Full historical sync - including ALL {Count} seasons: {FirstFew}...",
                        allSeasons.Count, string.Join(", ", allSeasons.Take(10)));
                }
                else
                {
                    // OPTIMIZED SYNC: Only current/future seasons (for scheduled refreshes)
                    // Past seasons are finalized and don't need re-syncing
                    seasons = allSeasons
                        .Where(s => IsCurrentOrFutureSeason(s))
                        .ToList();

                    var skippedCount = allSeasons.Count - seasons.Count;
                    _logger.LogInformation("[League Event Sync] Optimized sync - {Count} current/future seasons (skipped {Skipped} historical): {Seasons}",
                        seasons.Count, skippedCount, string.Join(", ", seasons));
                }

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

            // Fetch episode numbers from sportarr.net API for this season
            // This ensures episode numbering matches Plex metadata (sequential across ALL events in the league)
            var apiEpisodeMap = await _theSportsDBClient.GetEpisodeNumbersFromApiAsync(league.ExternalId, season);
            if (apiEpisodeMap != null && apiEpisodeMap.Any())
            {
                _logger.LogInformation("[League Event Sync] Season {Season}: Loaded {Count} episode numbers from API",
                    season, apiEpisodeMap.Count);
            }
            else
            {
                _logger.LogDebug("[League Event Sync] Season {Season}: No API episode numbers available, will calculate locally",
                    season);
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
                    await ProcessEventAsync(apiEvent, league, result, currentSeason, apiEpisodeMap);
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

            // Only recalculate episode numbers for:
            // 1. Current or future seasons (old seasons are finalized and won't change)
            // 2. Seasons where we actually added/updated events
            // This prevents unnecessary API calls and processing for historical data
            var isCurrentOrFutureSeason = IsCurrentOrFutureSeason(season);
            var hadChanges = seasonEventsProcessed > 0;

            if (isCurrentOrFutureSeason || hadChanges)
            {
                _seasonsNeedingRenumber.Add((league.Id, season));
                _logger.LogDebug("[League Event Sync] Season {Season} marked for episode recalculation (current/future: {IsCurrent}, changes: {HasChanges})",
                    season, isCurrentOrFutureSeason, hadChanges);
            }
            else
            {
                _logger.LogDebug("[League Event Sync] Season {Season} skipped for episode recalculation (past season with no changes)",
                    season);
            }
            _logger.LogInformation("[League Event Sync] Season {Season}: {Count} events processed ({New} new, {Updated} updated)",
                season, seasonEventsProcessed, result.NewCount - seasonStartCount + result.UpdatedCount, result.UpdatedCount);
        }

        // Update league's last sync timestamp
        league.LastUpdate = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Process all synced seasons - recalculate episode numbers and rename files to match current naming format
        // This ensures all files have correct episode numbers and follow the standard event format
        if (_seasonsNeedingRenumber.Any())
        {
            _logger.LogInformation("[League Event Sync] Processing {Count} seasons for episode number sync and file renaming",
                _seasonsNeedingRenumber.Count);

            int totalRenumbered = 0;
            int totalRenamed = 0;

            foreach (var (seasonLeagueId, seasonStr) in _seasonsNeedingRenumber)
            {
                try
                {
                    // Recalculate episode numbers from API (ensures DB matches Plex metadata)
                    var renumberedCount = await _fileRenameService.RecalculateEpisodeNumbersAsync(seasonLeagueId, seasonStr);
                    totalRenumbered += renumberedCount;

                    if (renumberedCount > 0)
                    {
                        _logger.LogInformation("[League Event Sync] Renumbered {Count} episodes in season {Season}",
                            renumberedCount, seasonStr);
                    }

                    // ALWAYS scan and rename files to ensure they match current naming format
                    // This catches files that were imported with wrong episode numbers or old naming format
                    var renamedCount = await _fileRenameService.RenameAllFilesInSeasonAsync(seasonLeagueId, seasonStr);
                    totalRenamed += renamedCount;

                    if (renamedCount > 0)
                    {
                        _logger.LogInformation("[League Event Sync] Renamed {Count} files in season {Season} to match naming format",
                            renamedCount, seasonStr);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[League Event Sync] Failed to renumber/rename season {Season}", seasonStr);
                }
            }

            if (totalRenumbered > 0 || totalRenamed > 0)
            {
                _logger.LogInformation("[League Event Sync] File sync complete: {Renumbered} episodes renumbered, {Renamed} files renamed",
                    totalRenumbered, totalRenamed);
            }

            // Clear the set for next sync
            _seasonsNeedingRenumber.Clear();
        }

        result.Success = true;
        result.Message = $"Synced {result.NewCount} new events, updated {result.UpdatedCount} events, skipped {result.SkippedCount} duplicates";
        _logger.LogInformation("[League Event Sync] Completed: {Message}", result.Message);

        return result;
    }

    /// <summary>
    /// Process a single event from TheSportsDB API
    /// </summary>
    /// <param name="apiEpisodeMap">Episode numbers from sportarr.net API (ExternalId -> EpisodeNumber). If null, falls back to local calculation.</param>
    private async Task ProcessEventAsync(Event apiEvent, League league, LeagueEventSyncResult result, string currentSeason, Dictionary<string, int>? apiEpisodeMap = null)
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
            bool dateChanged = false;
            bool titleChanged = false;
            bool episodeNumberChanged = false;

            // Event Date (CRITICAL: triggers episode renumbering if changed)
            if (existingEvent.EventDate.Date != apiEvent.EventDate.Date)
            {
                _logger.LogInformation("[League Event Sync] Event date changed for '{EventTitle}': {OldDate} → {NewDate}",
                    apiEvent.Title,
                    existingEvent.EventDate.ToString("yyyy-MM-dd"),
                    apiEvent.EventDate.ToString("yyyy-MM-dd"));
                existingEvent.EventDate = apiEvent.EventDate;
                dateChanged = true;
                needsUpdate = true;

                // Mark this season for episode renumbering
                if (!string.IsNullOrEmpty(apiEvent.Season))
                {
                    _seasonsNeedingRenumber.Add((league.Id, apiEvent.Season));
                }
            }

            // Event Title (triggers file rename if changed)
            if (existingEvent.Title != apiEvent.Title)
            {
                _logger.LogInformation("[League Event Sync] Event title changed: '{OldTitle}' → '{NewTitle}'",
                    existingEvent.Title, apiEvent.Title);
                existingEvent.Title = apiEvent.Title;
                titleChanged = true;
                needsUpdate = true;
            }

            // Season (important for proper grouping/filtering)
            if (existingEvent.Season != apiEvent.Season)
            {
                _logger.LogInformation("[League Event Sync] Updating season for {EventTitle}: {Old} → {New}",
                    apiEvent.Title, existingEvent.Season ?? "null", apiEvent.Season ?? "null");

                // If event moved to a different season, both seasons need renumbering
                if (!string.IsNullOrEmpty(existingEvent.Season))
                {
                    _seasonsNeedingRenumber.Add((league.Id, existingEvent.Season));
                }
                if (!string.IsNullOrEmpty(apiEvent.Season))
                {
                    _seasonsNeedingRenumber.Add((league.Id, apiEvent.Season));
                }

                existingEvent.Season = apiEvent.Season;
                existingEvent.SeasonNumber = ParseSeasonNumber(apiEvent.Season);
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

            // Update images if new ones are available from API (backfill for events with missing images)
            var newImages = CollectEventImages(apiEvent);
            if (newImages.Count > 0 && (existingEvent.Images == null || existingEvent.Images.Count == 0 ||
                !newImages.SequenceEqual(existingEvent.Images)))
            {
                existingEvent.Images = newImages;
                needsUpdate = true;
                _logger.LogDebug("[League Event Sync] Updated images for {EventTitle}: {Count} images",
                    apiEvent.Title, newImages.Count);
            }

            // Backfill Plex episode numbers for existing events (migration support)
            if (!existingEvent.SeasonNumber.HasValue && !string.IsNullOrEmpty(apiEvent.Season))
            {
                existingEvent.SeasonNumber = ParseSeasonNumber(apiEvent.Season);
                needsUpdate = true;
            }

            // Get episode number from API (matches Plex metadata) or fall back to local calculation
            var correctEpisodeNumber = GetEpisodeNumberFromApiOrCalculate(
                apiEpisodeMap, existingEvent.ExternalId, league.Id, apiEvent.Season, existingEvent.EventDate);

            // Update episode number if missing or if it differs from the API
            if (!existingEvent.EpisodeNumber.HasValue || existingEvent.EpisodeNumber != correctEpisodeNumber)
            {
                var oldEpisodeNumber = existingEvent.EpisodeNumber;
                existingEvent.EpisodeNumber = correctEpisodeNumber;
                needsUpdate = true;

                if (oldEpisodeNumber.HasValue && oldEpisodeNumber != correctEpisodeNumber)
                {
                    episodeNumberChanged = true;
                    _logger.LogInformation("[League Event Sync] Corrected episode number for {Title}: E{Old} -> E{New} (synced with API)",
                        apiEvent.Title, oldEpisodeNumber, correctEpisodeNumber);
                }
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
                _logger.LogInformation("[League Event Sync] Updated event: {EventTitle}{DateNote}{TitleNote}{EpisodeNote}",
                    apiEvent.Title,
                    dateChanged ? " (date changed)" : "",
                    titleChanged ? " (title changed)" : "",
                    episodeNumberChanged ? " (episode corrected)" : "");

                // Note: File renaming is handled at the end of sync via RenameAllFilesInSeasonAsync
                // which scans all files and renames any that don't match the expected naming format
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
            // Use API episode number (matches Plex metadata) or fall back to local calculation
            EpisodeNumber = GetEpisodeNumberFromApiOrCalculate(
                apiEpisodeMap, apiEvent.ExternalId, league.Id, apiEvent.Season, apiEvent.EventDate),
            Round = apiEvent.Round,
            EventDate = apiEvent.EventDate,
            Venue = apiEvent.Venue,
            Location = apiEvent.Location,
            Broadcast = apiEvent.Broadcast,
            Status = apiEvent.Status,
            HomeScore = apiEvent.HomeScore,
            AwayScore = apiEvent.AwayScore,
            Images = CollectEventImages(apiEvent),

            // Determine if event should be monitored based on league MonitorType
            // For motorsports, also check if the event matches the monitored session types
            // For UFC-style fighting leagues, also check if the event matches monitored event types
            Monitored = league.Monitored
                && ShouldMonitorEvent(league.MonitorType, apiEvent.EventDate, apiEvent.Season, currentSeason)
                && ShouldMonitorMotorsportSession(league.Sport, league.Name, apiEvent.Title, league.MonitoredSessionTypes)
                && ShouldMonitorFightingEventType(league.Sport, league.Name, apiEvent.Title, league.MonitoredEventTypes),
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
    /// Determines if a motorsport session should be monitored based on the league's MonitoredSessionTypes setting
    /// For non-motorsport leagues, this always returns true
    /// For motorsports, checks if the event's session type matches the monitored session types
    /// - null = all sessions monitored (default, no explicit selection)
    /// - "" (empty) = NO sessions monitored (user explicitly deselected all)
    /// - "Race,Qualifying" = only those session types monitored
    /// </summary>
    private static bool ShouldMonitorMotorsportSession(string sport, string leagueName, string eventTitle, string? monitoredSessionTypes)
    {
        // Only apply session type filtering for Motorsport
        if (sport != "Motorsport")
            return true;

        // null = no filter applied, monitor all sessions (default behavior)
        if (monitoredSessionTypes == null)
            return true;

        // Use EventPartDetector to check if this session type should be monitored
        // This handles: "" = none, "Race,Qualifying" = specific sessions
        return EventPartDetector.IsMotorsportSessionMonitored(eventTitle, leagueName, monitoredSessionTypes);
    }

    /// <summary>
    /// Determines if a fighting event should be monitored based on the league's MonitoredEventTypes setting
    /// For non-fighting leagues or fighting leagues without event type definitions, this always returns true
    /// For UFC-style leagues, checks if the event's type (PPV, FightNight, ContenderSeries) matches monitored types
    /// - null = all event types monitored (default, no explicit selection)
    /// - "" (empty) = NO event types monitored (user explicitly deselected all)
    /// - "PPV,FightNight" = only those event types monitored
    /// </summary>
    private static bool ShouldMonitorFightingEventType(string sport, string leagueName, string eventTitle, string? monitoredEventTypes)
    {
        // Only apply event type filtering for Fighting sports
        if (!EventPartDetector.IsFightingSport(sport))
            return true;

        // Only apply to leagues that have event type definitions (UFC-style)
        var availableTypes = EventPartDetector.GetFightingEventTypes(leagueName);
        if (availableTypes.Count == 0)
            return true;

        // null = no filter applied, monitor all event types (default behavior)
        if (monitoredEventTypes == null)
            return true;

        // Use EventPartDetector to check if this event type should be monitored
        // This handles: "" = none, "PPV,FightNight" = specific event types
        return EventPartDetector.IsFightingEventTypeMonitored(eventTitle, monitoredEventTypes);
    }

    /// <summary>
    /// Collect all available event images from API response fields into Images list
    /// TheSportsDB provides images in separate strPoster, strThumb, strBanner, strFanart fields
    /// </summary>
    private static List<string> CollectEventImages(Event apiEvent)
    {
        var images = new List<string>();

        // Add poster first (highest priority for display)
        if (!string.IsNullOrEmpty(apiEvent.PosterUrl))
            images.Add(apiEvent.PosterUrl);

        // Add thumbnail
        if (!string.IsNullOrEmpty(apiEvent.ThumbUrl))
            images.Add(apiEvent.ThumbUrl);

        // Add banner
        if (!string.IsNullOrEmpty(apiEvent.BannerUrl))
            images.Add(apiEvent.BannerUrl);

        // Add fanart
        if (!string.IsNullOrEmpty(apiEvent.FanartUrl))
            images.Add(apiEvent.FanartUrl);

        // Also include any images from the existing Images list (in case API passes them differently)
        if (apiEvent.Images != null && apiEvent.Images.Count > 0)
        {
            foreach (var img in apiEvent.Images)
            {
                if (!string.IsNullOrEmpty(img) && !images.Contains(img))
                    images.Add(img);
            }
        }

        return images;
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
    /// Get episode number from the sportarr.net API map, or fall back to local calculation.
    /// Using API episode numbers ensures files match Plex metadata regardless of which teams are monitored locally.
    /// </summary>
    /// <param name="apiEpisodeMap">Dictionary mapping ExternalId to episode number from API. Can be null.</param>
    /// <param name="externalId">TheSportsDB event ID</param>
    /// <param name="leagueId">Internal league ID for fallback calculation</param>
    /// <param name="season">Season string for fallback calculation</param>
    /// <param name="eventDate">Event date for fallback calculation</param>
    /// <returns>Episode number from API if available, otherwise locally calculated</returns>
    private int GetEpisodeNumberFromApiOrCalculate(
        Dictionary<string, int>? apiEpisodeMap,
        string? externalId,
        int leagueId,
        string? season,
        DateTime eventDate)
    {
        // Try to get episode number from API first (preferred - matches Plex metadata)
        if (apiEpisodeMap != null && !string.IsNullOrEmpty(externalId) && apiEpisodeMap.TryGetValue(externalId, out var apiEpisodeNumber))
        {
            _logger.LogDebug("[League Event Sync] Using API episode number E{EpisodeNumber} for event {ExternalId}",
                apiEpisodeNumber, externalId);
            return apiEpisodeNumber;
        }

        // Fall back to local calculation (for events not in API, or if API fetch failed)
        // Note: This uses a synchronous count since we're in a non-async context
        // For new events without API data, use a simple sequential number based on existing count
        if (string.IsNullOrEmpty(season))
            return 1;

        var existingCount = _db.Events
            .Where(e => e.LeagueId == leagueId && e.Season == season &&
                       (e.EventDate < eventDate ||
                        (e.EventDate == eventDate && externalId != null &&
                         string.Compare(e.ExternalId, externalId) < 0)))
            .Count();

        var localEpisodeNumber = existingCount + 1;
        _logger.LogDebug("[League Event Sync] Using local episode number E{EpisodeNumber} for event {ExternalId} (API data not available)",
            localEpisodeNumber, externalId);
        return localEpisodeNumber;
    }

    /// <summary>
    /// Get the episode number for an event based on its chronological position within the season.
    /// Episode numbers are assigned based on event date+time order, not insertion order.
    /// This ensures proper ordering for same-day events (e.g., multiple NBA games on one date).
    /// For events with the exact same date+time, ExternalId is used as a stable tiebreaker.
    /// NOTE: This method is now primarily used as a fallback. Prefer GetEpisodeNumberFromApiOrCalculate.
    /// </summary>
    private async Task<int> GetEpisodeNumberByDateAsync(int leagueId, string? season, DateTime eventDate, string? externalId = null)
    {
        if (string.IsNullOrEmpty(season))
            return 1;

        // Count how many events in this season have an earlier date/time than this event
        // For events at the exact same time, use ExternalId as a tiebreaker
        // This gives us the correct episode number based on chronological order
        var earlierEventsCount = await _db.Events
            .Where(e => e.LeagueId == leagueId && e.Season == season &&
                       (e.EventDate < eventDate ||
                        (e.EventDate == eventDate && externalId != null &&
                         string.Compare(e.ExternalId, externalId) < 0)))
            .CountAsync();

        return earlierEventsCount + 1;
    }

    /// <summary>
    /// Determines if a season string represents a current or future season.
    /// Past seasons (more than 1 year old) don't need episode recalculation during sync
    /// because their data is finalized and won't change.
    ///
    /// Examples of current/future seasons (assuming current year is 2025):
    /// - "2025" -> true
    /// - "2024-2025" -> true (contains current year)
    /// - "2025-2026" -> true
    /// - "2023-2024" -> false (ended before current year)
    /// - "2020" -> false
    /// </summary>
    private static bool IsCurrentOrFutureSeason(string season)
    {
        if (string.IsNullOrEmpty(season))
            return false;

        var currentYear = DateTime.UtcNow.Year;

        // Extract year(s) from season string
        // Handles: "2025", "2024-2025", "2024/25", "2024-25"
        var parts = season.Split(new[] { '-', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            // Try to parse each part as a year
            if (int.TryParse(part, out var year))
            {
                // Handle 2-digit year abbreviations (e.g., "24" -> 2024)
                if (year < 100)
                    year += 2000;

                // Season is current/future if any year in it is >= last year
                // We use (currentYear - 1) to include seasons that just ended
                // (e.g., in January 2025, we still want to process 2024-2025)
                if (year >= currentYear - 1)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if a tennis league is individual-based (ATP, WTA) vs team-based (Fed Cup, Davis Cup, Olympics)
    /// Individual tennis leagues don't have meaningful team data - matches are between players, not teams
    /// </summary>
    private static bool IsIndividualTennisLeague(string sport, string leagueName)
    {
        if (!sport.Equals("Tennis", StringComparison.OrdinalIgnoreCase)) return false;

        var nameLower = leagueName.ToLowerInvariant();

        // Team-based tennis competitions - these DO need team filtering
        var teamBased = new[] { "fed cup", "davis cup", "olympic", "billie jean king" };
        if (teamBased.Any(t => nameLower.Contains(t))) return false;

        // Individual tours - no team selection needed, sync all events
        var individualTours = new[] { "atp", "wta" };
        return individualTours.Any(t => nameLower.Contains(t));
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
