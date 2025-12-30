using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service that automatically schedules DVR recordings for monitored future events.
/// Runs periodically to:
/// 1. Schedule recordings for newly monitored events with league-channel mappings
/// 2. Schedule recordings for monitored events detected in EPG data
/// 3. Re-check events that may have gotten new channel mappings
/// 4. Clean up recordings for events that are no longer monitored or in the past
/// </summary>
public class DvrAutoSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DvrAutoSchedulerService> _logger;

    // Check every 15 minutes for new events to schedule
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(15);

    // Only schedule recordings for events within this window (next 14 days)
    private readonly TimeSpan _schedulingWindow = TimeSpan.FromDays(14);

    public DvrAutoSchedulerService(
        IServiceProvider serviceProvider,
        ILogger<DvrAutoSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[DVR Auto-Scheduler] Service started");

        // Wait 5 minutes after startup before first check (let other services initialize)
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // First, try to schedule events using league-channel mappings
                var leagueResult = await ScheduleUpcomingEventsAsync(stoppingToken);

                // Then, try to schedule events by matching EPG programs to monitored events
                var epgResult = await ScheduleEventsFromEpgAsync(stoppingToken);

                if (epgResult.RecordingsScheduled > 0)
                {
                    _logger.LogInformation("[DVR Auto-Scheduler] EPG-based scheduling: Scheduled {Count} additional recordings",
                        epgResult.RecordingsScheduled);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DVR Auto-Scheduler] Error during automatic scheduling");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("[DVR Auto-Scheduler] Service stopped");
    }

    /// <summary>
    /// Schedule DVR recordings for all monitored future events that don't have recordings yet.
    /// </summary>
    public async Task<DvrSchedulingResult> ScheduleUpcomingEventsAsync(CancellationToken cancellationToken = default)
    {
        var result = new DvrSchedulingResult();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var eventDvrService = scope.ServiceProvider.GetRequiredService<EventDvrService>();
        var iptvService = scope.ServiceProvider.GetRequiredService<IptvSourceService>();

        var now = DateTime.UtcNow;
        var schedulingCutoff = now.Add(_schedulingWindow);

        _logger.LogDebug("[DVR Auto-Scheduler] Checking for events to schedule (now to {Cutoff})", schedulingCutoff);

        // Get all monitored future events that:
        // 1. Are monitored
        // 2. Have a start date in the future (but within scheduling window)
        // 3. Have a league assigned
        // 4. Don't already have an active/scheduled recording
        var eventsToSchedule = await db.Events
            .Include(e => e.League)
            .Where(e => e.Monitored)
            .Where(e => e.EventDate > now && e.EventDate <= schedulingCutoff)
            .Where(e => e.LeagueId != null)
            .Where(e => !db.DvrRecordings.Any(r =>
                r.EventId == e.Id &&
                (r.Status == DvrRecordingStatus.Scheduled ||
                 r.Status == DvrRecordingStatus.Recording)))
            .ToListAsync(cancellationToken);

        if (eventsToSchedule.Count == 0)
        {
            _logger.LogDebug("[DVR Auto-Scheduler] No new events to schedule");
            return result;
        }

        _logger.LogInformation("[DVR Auto-Scheduler] Found {Count} monitored events to check for DVR scheduling",
            eventsToSchedule.Count);

        // Check which leagues have channel mappings
        var leagueIds = eventsToSchedule
            .Where(e => e.LeagueId.HasValue)
            .Select(e => e.LeagueId!.Value)
            .Distinct()
            .ToList();

        var leaguesWithChannels = new HashSet<int>();
        foreach (var leagueId in leagueIds)
        {
            var channel = await iptvService.GetPreferredChannelForLeagueAsync(leagueId);
            if (channel != null)
            {
                leaguesWithChannels.Add(leagueId);
            }
        }

        _logger.LogDebug("[DVR Auto-Scheduler] {Count}/{Total} leagues have channel mappings",
            leaguesWithChannels.Count, leagueIds.Count);

        // Schedule recordings for events with channel mappings
        foreach (var evt in eventsToSchedule)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            result.EventsChecked++;

            if (!evt.LeagueId.HasValue || !leaguesWithChannels.Contains(evt.LeagueId.Value))
            {
                result.SkippedNoChannel++;
                continue;
            }

            try
            {
                var recording = await eventDvrService.ScheduleRecordingForEventAsync(evt.Id);
                if (recording != null)
                {
                    result.RecordingsScheduled++;
                    _logger.LogInformation("[DVR Auto-Scheduler] Scheduled recording for: {Title} on {Date}",
                        evt.Title, evt.EventDate);
                }
                else
                {
                    result.SkippedAlreadyScheduled++;
                }
            }
            catch (Exception ex)
            {
                result.Errors++;
                _logger.LogWarning(ex, "[DVR Auto-Scheduler] Failed to schedule recording for event {EventId}: {Title}",
                    evt.Id, evt.Title);
            }
        }

        // Also clean up cancelled/orphaned recordings for past events
        await CleanupPastRecordingsAsync(db, cancellationToken);

        _logger.LogInformation(
            "[DVR Auto-Scheduler] Scheduling complete - Checked: {Checked}, Scheduled: {Scheduled}, " +
            "Already Scheduled: {Already}, No Channel: {NoChannel}, Errors: {Errors}",
            result.EventsChecked, result.RecordingsScheduled, result.SkippedAlreadyScheduled,
            result.SkippedNoChannel, result.Errors);

        return result;
    }

    /// <summary>
    /// Clean up scheduled recordings for events that are now in the past or unmonitored.
    /// </summary>
    private async Task CleanupPastRecordingsAsync(SportarrDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // Find scheduled recordings for events that have passed or are no longer monitored
        var recordingsToCancel = await db.DvrRecordings
            .Include(r => r.Event)
            .Where(r => r.Status == DvrRecordingStatus.Scheduled)
            .Where(r => r.Event == null || // Event was deleted
                       r.Event.EventDate < now.AddHours(-6) || // Event is more than 6 hours in the past
                       !r.Event.Monitored) // Event is no longer monitored
            .ToListAsync(cancellationToken);

        if (recordingsToCancel.Count > 0)
        {
            _logger.LogInformation("[DVR Auto-Scheduler] Cancelling {Count} obsolete scheduled recordings",
                recordingsToCancel.Count);

            foreach (var recording in recordingsToCancel)
            {
                recording.Status = DvrRecordingStatus.Cancelled;
                recording.ErrorMessage = recording.Event == null
                    ? "Event was deleted"
                    : recording.Event.EventDate < now
                        ? "Event has passed"
                        : "Event is no longer monitored";
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Schedule DVR recordings by matching EPG programs to monitored events.
    /// This handles cases where:
    /// 1. League doesn't have a direct channel mapping, but EPG shows a matching program
    /// 2. Event can be matched to EPG by team names in the program title
    /// </summary>
    public async Task<DvrSchedulingResult> ScheduleEventsFromEpgAsync(CancellationToken cancellationToken = default)
    {
        var result = new DvrSchedulingResult();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var dvrService = scope.ServiceProvider.GetRequiredService<DvrRecordingService>();

        var now = DateTime.UtcNow;
        var schedulingCutoff = now.Add(_schedulingWindow);

        // Get monitored future events that don't have recordings yet
        // (including those without league-channel mappings - EPG might have them)
        var eventsWithoutRecordings = await db.Events
            .Include(e => e.League)
            .Where(e => e.Monitored)
            .Where(e => e.EventDate > now && e.EventDate <= schedulingCutoff)
            .Where(e => !db.DvrRecordings.Any(r =>
                r.EventId == e.Id &&
                (r.Status == DvrRecordingStatus.Scheduled ||
                 r.Status == DvrRecordingStatus.Recording)))
            .ToListAsync(cancellationToken);

        if (eventsWithoutRecordings.Count == 0)
        {
            return result;
        }

        _logger.LogDebug("[DVR Auto-Scheduler] Checking {Count} monitored events for EPG matches",
            eventsWithoutRecordings.Count);

        // Get all IPTV channels with TvgIds (needed for EPG matching)
        var channels = await db.IptvChannels
            .Include(c => c.Source)
            .Where(c => c.IsEnabled && !string.IsNullOrEmpty(c.TvgId))
            .Where(c => c.Source != null && c.Source.IsActive)
            .ToListAsync(cancellationToken);

        if (channels.Count == 0)
        {
            _logger.LogDebug("[DVR Auto-Scheduler] No IPTV channels with EPG mapping available");
            return result;
        }

        // Get all sports EPG programs in our scheduling window
        var tvgIds = channels.Select(c => c.TvgId!).ToList();
        var sportsPrograms = await db.EpgPrograms
            .Where(p => tvgIds.Contains(p.ChannelId))
            .Where(p => p.StartTime >= now && p.StartTime <= schedulingCutoff)
            .Where(p => p.IsSportsProgram || p.Category != null &&
                (p.Category.ToLower().Contains("sport") || p.Category.ToLower().Contains("live")))
            .ToListAsync(cancellationToken);

        if (sportsPrograms.Count == 0)
        {
            _logger.LogDebug("[DVR Auto-Scheduler] No sports programs found in EPG for the scheduling window");
            return result;
        }

        _logger.LogDebug("[DVR Auto-Scheduler] Found {Count} sports programs in EPG to check", sportsPrograms.Count);

        // Try to match events to EPG programs
        foreach (var evt in eventsWithoutRecordings)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            result.EventsChecked++;

            // Try to find a matching EPG program for this event
            var matchingProgram = FindMatchingEpgProgram(evt, sportsPrograms);
            if (matchingProgram == null)
            {
                continue;
            }

            // Find the channel for this EPG program
            var channel = channels.FirstOrDefault(c => c.TvgId == matchingProgram.ChannelId);
            if (channel == null)
            {
                continue;
            }

            try
            {
                // Schedule recording using the EPG program times
                var recording = await dvrService.ScheduleRecordingAsync(new ScheduleDvrRecordingRequest
                {
                    EventId = evt.Id,
                    ChannelId = channel.Id,
                    ScheduledStart = matchingProgram.StartTime.AddMinutes(-5), // Pre-padding
                    ScheduledEnd = matchingProgram.EndTime.AddMinutes(30), // Post-padding
                    PrePadding = 5,
                    PostPadding = 30
                });

                if (recording != null)
                {
                    result.RecordingsScheduled++;
                    _logger.LogInformation("[DVR Auto-Scheduler] EPG match: Scheduled recording for '{Title}' on {Channel} " +
                        "(matched EPG program: '{EpgTitle}' at {Start})",
                        evt.Title, channel.Name, matchingProgram.Title, matchingProgram.StartTime);

                    // Update the EPG program to link it to the event
                    matchingProgram.MatchedEventId = evt.Id;
                    matchingProgram.MatchConfidence = 80; // EPG-based match confidence
                }
            }
            catch (Exception ex)
            {
                result.Errors++;
                _logger.LogWarning(ex, "[DVR Auto-Scheduler] Failed to schedule EPG-based recording for event {EventId}",
                    evt.Id);
            }
        }

        if (result.RecordingsScheduled > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Find an EPG program that matches a monitored event.
    /// Matches based on:
    /// 1. Time proximity (program start within 1 hour of event start)
    /// 2. Title/team name matching
    /// </summary>
    private EpgProgram? FindMatchingEpgProgram(Event evt, List<EpgProgram> programs)
    {
        // Filter programs by time proximity (within 1 hour)
        var timeWindow = TimeSpan.FromHours(1);
        var candidatePrograms = programs
            .Where(p => Math.Abs((p.StartTime - evt.EventDate).TotalMinutes) <= timeWindow.TotalMinutes)
            .ToList();

        if (candidatePrograms.Count == 0)
            return null;

        // Try to find a match based on team names or event title
        var searchTerms = new List<string>();

        // Add team names as search terms
        if (!string.IsNullOrEmpty(evt.HomeTeamName))
            searchTerms.Add(NormalizeForSearch(evt.HomeTeamName));
        if (!string.IsNullOrEmpty(evt.AwayTeamName))
            searchTerms.Add(NormalizeForSearch(evt.AwayTeamName));

        // Also extract team names from the event title if structured like "Team A vs Team B"
        var titleParts = evt.Title.Split(new[] { " vs ", " v ", " @ ", " at " }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in titleParts)
        {
            var normalized = NormalizeForSearch(part);
            if (!string.IsNullOrEmpty(normalized) && !searchTerms.Contains(normalized))
                searchTerms.Add(normalized);
        }

        if (searchTerms.Count == 0)
        {
            // If no specific terms, try matching the full title
            searchTerms.Add(NormalizeForSearch(evt.Title));
        }

        // Score each candidate program
        EpgProgram? bestMatch = null;
        int bestScore = 0;

        foreach (var program in candidatePrograms)
        {
            var normalizedProgramTitle = NormalizeForSearch(program.Title);
            var score = 0;

            // Score based on matching search terms
            foreach (var term in searchTerms)
            {
                if (normalizedProgramTitle.Contains(term))
                {
                    score += 30; // Each matching term adds 30 points
                }
            }

            // Bonus for time proximity (closer = better)
            var timeDiff = Math.Abs((program.StartTime - evt.EventDate).TotalMinutes);
            if (timeDiff <= 5) score += 30;
            else if (timeDiff <= 15) score += 20;
            else if (timeDiff <= 30) score += 10;

            // Bonus for sports category
            if (program.IsSportsProgram)
                score += 10;

            // Minimum threshold of 40 points to be considered a match
            if (score > bestScore && score >= 40)
            {
                bestScore = score;
                bestMatch = program;
            }
        }

        if (bestMatch != null)
        {
            _logger.LogDebug("[DVR Auto-Scheduler] EPG match found: Event '{EventTitle}' -> Program '{ProgramTitle}' (score: {Score})",
                evt.Title, bestMatch.Title, bestScore);
        }

        return bestMatch;
    }

    /// <summary>
    /// Normalize a string for search matching (lowercase, remove special chars)
    /// </summary>
    private static string NormalizeForSearch(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), @"[^\w\s]", " ")
            .Replace("  ", " ")
            .Trim();
    }
}

/// <summary>
/// Result of automatic DVR scheduling operation
/// </summary>
public class DvrSchedulingResult
{
    public int EventsChecked { get; set; }
    public int RecordingsScheduled { get; set; }
    public int SkippedAlreadyScheduled { get; set; }
    public int SkippedNoChannel { get; set; }
    public int Errors { get; set; }
}
