using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service that automatically schedules DVR recordings for monitored future events.
/// Runs periodically to:
/// 1. Schedule recordings for newly monitored events
/// 2. Re-check events that may have gotten new channel mappings
/// 3. Clean up recordings for events that are no longer monitored or in the past
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
                await ScheduleUpcomingEventsAsync(stoppingToken);
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
