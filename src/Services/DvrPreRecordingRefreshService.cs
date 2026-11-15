using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Refreshes the channel list and the guide shortly before a live recording,
/// then runs the re-resolve pass, so the recording can move to a channel that
/// appeared or changed since the last scheduled refresh.
///
/// The rules below apply when Config.DvrRefreshChannelsBeforeRecordings and
/// Config.DvrReresolveChannelsEnabled are both on.
/// - The service acts only while something starts within the lookahead.
///   That is three hours, or more with a long lock window. A start is a
///   scheduled live recording of an event that is not pinned. A monitored
///   event without a recording is a start too, when a per-event name
///   matches it or a per-event slot's prefix or group names its own league.
///   Catchup recordings do not count.
/// - For each start, a playlist syncs only when it holds the recording's
///   current channel, a per-event slot for the event's own league, or a
///   per-event name that matches the event. A playlist with such slots has
///   three sync points for a recording. They are the start of the
///   lookahead, 90 minutes before the lock and 30 minutes before the lock.
///   For an event without a recording it has only the first. A playlist
///   that only holds the recording's channel syncs once, 30 minutes before
///   the lock. Starts share the points, and a playlist synced in the last
///   25 minutes waits. No sync runs once every start is inside the lock
///   window, because a later move cannot happen.
/// - The guide refreshes once, 60 to 90 minutes before a start. With a long
///   lock window, the guide window moves to just before the lock. A guide
///   source that refreshed in the last 60 minutes is skipped, so starts
///   close together share one refresh.
/// - After a playlist sync, the auto-scheduler runs, so a new per-event
///   channel can get its event a recording. After any refresh, the
///   re-resolve pass runs.
/// </summary>
public class DvrPreRecordingRefreshService : BackgroundService
{
    /// <summary>
    /// How far ahead a start begins the refreshes, at the least. A long lock
    /// window moves this out, so the guide window still fits.
    /// </summary>
    public static readonly TimeSpan MinLookahead = TimeSpan.FromHours(3);

    /// <summary>
    /// A playlist synced more recently than this does not sync again.
    /// </summary>
    public static readonly TimeSpan PlaylistMinAge = TimeSpan.FromMinutes(25);

    // The two late sync points, measured back from the start of the lock.
    private static readonly TimeSpan[] SyncPointsBeforeLock = { TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(30) };

    /// <summary>
    /// The guide refreshes for a start this far ahead, unless the lock window
    /// is longer.
    /// </summary>
    public static readonly TimeSpan GuideWindowStart = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan GuideWindowLength = TimeSpan.FromMinutes(30);

    // The guide refresh lands this long before the lock at the latest, so
    // the re-resolve pass can still act on new listings.
    private static readonly TimeSpan GuideLeadBeforeLock = TimeSpan.FromMinutes(10);

    /// <summary>
    /// A guide source refreshed this recently does not refresh again.
    /// </summary>
    public static readonly TimeSpan GuideMinAge = TimeSpan.FromMinutes(60);

    // The tick is shorter than the guide window, so every recording gets a
    // tick inside its window.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The lookahead for a lock window. It covers the lock, the lead before
    /// it, the guide window and one tick.
    /// </summary>
    public static TimeSpan LookaheadFor(TimeSpan lockWindow)
    {
        var needed = lockWindow + GuideLeadBeforeLock + GuideWindowLength + CheckInterval;
        return needed > MinLookahead ? needed : MinLookahead;
    }

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DvrPreRecordingRefreshService> _logger;

    // A guide source records its refresh time only on success. This keeps a
    // failing source from a large download on every tick.
    private readonly Dictionary<int, DateTime> _lastGuideAttempt = new();

    public DvrPreRecordingRefreshService(
        IServiceProvider serviceProvider,
        ILogger<DvrPreRecordingRefreshService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the scheduled playlist and guide refresh run first.
        await Task.Delay(TimeSpan.FromMinutes(4), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DVR Pre-Refresh] Error during refresh pass");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs one pass at the given time. Returns what it refreshed.
    /// </summary>
    public async Task<PreRecordingRefreshResult> RunOnceAsync(DateTime now, CancellationToken ct = default)
    {
        var result = new PreRecordingRefreshResult();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var config = await scope.ServiceProvider.GetRequiredService<ConfigService>().GetConfigAsync();
        // A refresh helps only when the re-resolve pass can act on it.
        if (!config.DvrRefreshChannelsBeforeRecordings || !config.DvrReresolveChannelsEnabled)
        {
            return result;
        }

        var lockWindow = TimeSpan.FromMinutes(Math.Max(0, config.DvrReresolveLockMinutes));
        var lookahead = LookaheadFor(lockWindow);
        var horizon = now + lookahead;
        var recordings = await db.DvrRecordings
            .AsNoTracking()
            .Where(r => r.Status == DvrRecordingStatus.Scheduled
                && r.Method == DvrRecordingMethod.Live
                && r.EventId != null
                && !r.ChannelPinned
                && r.ScheduledStart > now
                && r.ScheduledStart <= horizon)
            .OrderBy(r => r.ScheduledStart)
            .Select(r => new { r.Title, r.ScheduledStart, r.ChannelId, r.EventId })
            .ToListAsync(ct);
        var unscheduled = await UnscheduledTeamEventsAsync(db, now, horizon, ct);
        if (recordings.Count == 0 && unscheduled.Count == 0)
        {
            return result;
        }

        var eventChannels = await EventChannelNameMatcher.LoadAsync(db, ct);
        var vocabulary = await EventChannelVocabulary.LoadAsync(db, ct);
        var zones = EventChannelNameMatcher.CandidateZones(config.TimeZone);
        var eventIds = recordings.Select(r => r.EventId!.Value).Distinct().ToList();
        var recordingEvents = await db.Events
            .AsNoTracking()
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Where(e => eventIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);
        var channelSources = await db.IptvChannels
            .AsNoTracking()
            .Where(c => recordings.Select(r => r.ChannelId).Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.SourceId, ct);

        var upcoming = new List<UpcomingStart>();
        foreach (var recording in recordings)
        {
            var slotSources = recordingEvents.TryGetValue(recording.EventId!.Value, out var evt)
                ? SlotSources(eventChannels, vocabulary.Target(evt), zones)
                : new HashSet<int>();
            int? channelSource = channelSources.TryGetValue(recording.ChannelId, out var sourceId) ? sourceId : null;
            upcoming.Add(new UpcomingStart(recording.Title, recording.ScheduledStart, slotSources, channelSource,
                Unscheduled: false));
        }
        foreach (var evt in unscheduled)
        {
            var slotSources = SlotSources(eventChannels, vocabulary.Target(evt), zones);
            if (slotSources.Count > 0)
            {
                upcoming.Add(new UpcomingStart(evt.Title, evt.EventDate, slotSources, null, Unscheduled: true));
            }
        }
        upcoming = upcoming.OrderBy(u => u.Start).ToList();
        if (upcoming.Count == 0)
        {
            return result;
        }

        var actions = scope.ServiceProvider.GetRequiredService<IPreRecordingRefreshActions>();

        // A channel cannot change inside the lock window. Syncs stop when no
        // start can still move. Each playlist takes the latest sync point
        // that has passed among the starts that need it.
        var lockEnd = now + lockWindow;
        var syncPoints = new Dictionary<int, (DateTime Point, UpcomingStart Start)>();
        void Offer(int sourceId, IEnumerable<DateTime> points, UpcomingStart start)
        {
            var passed = points.Where(point => point <= now).ToList();
            if (passed.Count == 0) return;
            var point = passed.Max();
            if (!syncPoints.TryGetValue(sourceId, out var known) || known.Point < point)
            {
                syncPoints[sourceId] = (point, start);
            }
        }
        foreach (var start in upcoming.Where(r => r.Start > lockEnd))
        {
            var slotPoints = start.Unscheduled
                ? new[] { start.Start - lookahead }
                : SyncPointsBeforeLock.Select(gap => start.Start - lockWindow - gap).Append(start.Start - lookahead).ToArray();
            foreach (var sourceId in start.SlotSources)
            {
                Offer(sourceId, slotPoints, start);
            }
            // A playlist without slots for this start can only have a moved
            // stream, so one late sync is enough.
            if (start.ChannelSource is int channelSource && !start.SlotSources.Contains(channelSource))
            {
                Offer(channelSource, new[] { start.Start - lockWindow - SyncPointsBeforeLock[^1] }, start);
            }
        }

        if (syncPoints.Count > 0)
        {
            var wanted = syncPoints.Keys.ToList();
            var recentAfter = now - PlaylistMinAge;
            var sources = await db.IptvSources
                .AsNoTracking()
                .Where(s => s.IsActive && wanted.Contains(s.Id))
                .Select(s => new { s.Id, s.Name, s.LastUpdated })
                .ToListAsync(ct);
            foreach (var source in sources)
            {
                if (ct.IsCancellationRequested) break;
                var (point, reason) = syncPoints[source.Id];
                if (source.LastUpdated != null && (source.LastUpdated >= point || source.LastUpdated >= recentAfter))
                {
                    continue;
                }
                if (actions.IsPlaylistSyncRunning(source.Id))
                {
                    _logger.LogDebug("[DVR Pre-Refresh] Playlist '{Name}' is syncing already, skipping it", source.Name);
                    continue;
                }

                try
                {
                    var count = await actions.SyncPlaylistAsync(source.Id, ct);
                    result.PlaylistsSynced.Add(source.Id);
                    _logger.LogInformation(
                        "[DVR Pre-Refresh] Synced playlist '{Name}' ({Count} channels), because '{Title}' starts at {Start:u}",
                        source.Name, count, reason.Title, reason.Start);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[DVR Pre-Refresh] Playlist sync failed for '{Name}'", source.Name);
                }
            }
        }

        var guideStart = lockWindow + GuideLeadBeforeLock > GuideWindowStart
            ? lockWindow + GuideLeadBeforeLock
            : GuideWindowStart;
        var guideDue = upcoming.FirstOrDefault(r =>
            r.Start >= now + guideStart && r.Start <= now + guideStart + GuideWindowLength);
        if (guideDue != null)
        {
            var freshAfter = now - GuideMinAge;
            var guides = await db.EpgSources
                .AsNoTracking()
                .Where(s => s.IsActive && (s.LastUpdated == null || s.LastUpdated < freshAfter))
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(ct);
            foreach (var guide in guides)
            {
                if (ct.IsCancellationRequested) break;
                if (_lastGuideAttempt.TryGetValue(guide.Id, out var tried) && tried > freshAfter)
                {
                    continue;
                }

                _lastGuideAttempt[guide.Id] = now;
                try
                {
                    if (await actions.RefreshGuideAsync(guide.Id, ct))
                    {
                        result.GuidesRefreshed.Add(guide.Id);
                        _logger.LogInformation(
                            "[DVR Pre-Refresh] Refreshed guide '{Name}', because '{Title}' starts at {Start:u}",
                            guide.Name, guideDue.Title, guideDue.Start);
                    }
                    else
                    {
                        _logger.LogWarning("[DVR Pre-Refresh] Guide refresh failed for '{Name}'", guide.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[DVR Pre-Refresh] Guide refresh failed for '{Name}'", guide.Name);
                }
            }
        }

        if (result.PlaylistsSynced.Count > 0)
        {
            // A sync can bring a per-event channel for an event that has no
            // recording yet. A failure here must not stop the re-resolve.
            try
            {
                result.RecordingsScheduled = await actions.ScheduleAsync(ct);
                result.SchedulerRan = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DVR Pre-Refresh] Auto-scheduler pass after the refresh failed");
            }
        }

        if (result.PlaylistsSynced.Count > 0 || result.GuidesRefreshed.Count > 0)
        {
            result.RecordingsMoved = await actions.ReresolveAsync(ct);
            result.ReresolveRan = true;
            _logger.LogInformation(
                "[DVR Pre-Refresh] Re-resolve after the refresh moved {Moved} recording(s)", result.RecordingsMoved);
        }

        return result;
    }

    private sealed record UpcomingStart(
        string Title, DateTime Start, HashSet<int> SlotSources, int? ChannelSource, bool Unscheduled);

    // Monitored team events in DVR leagues that have no recording yet.
    private static Task<List<Event>> UnscheduledTeamEventsAsync(
        SportarrDbContext db, DateTime now, DateTime horizon, CancellationToken ct) =>
        db.Events
            .AsNoTracking()
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Where(e => e.Monitored
                && e.League != null && e.League.EnableDvr
                && e.HomeTeamName != null && e.AwayTeamName != null
                && e.EventDate > now && e.EventDate <= horizon
                && !db.DvrRecordings.Any(r => r.EventId == e.Id
                    && (r.Status == DvrRecordingStatus.Scheduled || r.Status == DvrRecordingStatus.Recording)))
            .ToListAsync(ct);

    // The playlists with a per-event slot for this event. A slot counts when
    // its prefix or group names the event's own league, or when its name
    // matches the event near its start. Slots often carry last week's names
    // until a sync, so the league word alone is enough. A sibling league or
    // the sport word is not, or one game would sync every soccer slot.
    private static HashSet<int> SlotSources(
        List<ParsedEventChannel> channels, EventChannelTarget target, IReadOnlyList<TimeZoneInfo> zones) =>
        channels
            .Where(c => EventChannelNameMatcher.NamesOwnLeague(c.Name, target)
                || (!c.Name.IsRerun && EventChannelNameMatcher.AnyNearMatch(new[] { c }, target, zones)))
            .Select(c => c.SourceId)
            .ToHashSet();
}

/// <summary>
/// What one pre-recording refresh pass did.
/// </summary>
public class PreRecordingRefreshResult
{
    public List<int> PlaylistsSynced { get; } = new();
    public List<int> GuidesRefreshed { get; } = new();
    public bool SchedulerRan { get; set; }
    public int RecordingsScheduled { get; set; }
    public bool ReresolveRan { get; set; }
    public int RecordingsMoved { get; set; }
}

/// <summary>
/// The refresh steps the pre-recording refresh runs. Tests replace them.
/// </summary>
public interface IPreRecordingRefreshActions
{
    bool IsPlaylistSyncRunning(int sourceId);
    Task<int> SyncPlaylistAsync(int sourceId, CancellationToken ct);
    Task<bool> RefreshGuideAsync(int epgSourceId, CancellationToken ct);
    Task<int> ScheduleAsync(CancellationToken ct);
    Task<int> ReresolveAsync(CancellationToken ct);
}

/// <summary>
/// Runs the refresh steps with the app's own services.
/// </summary>
public class PreRecordingRefreshActions : IPreRecordingRefreshActions
{
    private readonly IptvSourceService _iptv;
    private readonly EpgService _epg;
    private readonly DvrChannelReresolveService _reresolve;
    private readonly DvrAutoSchedulerService _scheduler;

    public PreRecordingRefreshActions(
        IptvSourceService iptv, EpgService epg, DvrChannelReresolveService reresolve, DvrAutoSchedulerService scheduler)
    {
        _iptv = iptv;
        _epg = epg;
        _reresolve = reresolve;
        _scheduler = scheduler;
    }

    public bool IsPlaylistSyncRunning(int sourceId) => IptvSourceService.IsSyncRunning(sourceId);

    public Task<int> SyncPlaylistAsync(int sourceId, CancellationToken ct) => _iptv.SyncChannelsAsync(sourceId);

    public async Task<bool> RefreshGuideAsync(int epgSourceId, CancellationToken ct) =>
        (await _epg.SyncSourceAsync(epgSourceId, ct)).Success;

    // The scheduler's own gate stops a second pass. A pass that is running
    // already covers the new channels or the next one does. A zero skip
    // time records no manual run, so the next timed pass still runs.
    public async Task<int> ScheduleAsync(CancellationToken ct)
    {
        var scheduled = 0;
        await _scheduler.PassGate.RunScheduledAsync(async () =>
        {
            scheduled = (await _scheduler.ScheduleUpcomingEventsAsync(ct)).RecordingsScheduled;
        }, TimeSpan.Zero, ct);
        return scheduled;
    }

    public Task<int> ReresolveAsync(CancellationToken ct) => _reresolve.RunPassAsync(ct);
}
