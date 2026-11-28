using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Moves a scheduled DVR recording to a better channel when newer EPG data
/// arrives.
///
/// A recording is often scheduled days ahead, when the EPG does not yet cover
/// the event date. The resolver then falls back to a league-channel mapping,
/// which is a guess. Closer to the event the EPG usually gains an exact
/// program match on a better channel. This service runs the resolver again
/// for those recordings and moves the recording if a clearly better channel
/// appears.
///
/// Safety rules:
/// - Only recordings in Scheduled status change. A recording that runs, or
///   that already finished, is never touched.
/// - A catchup recording moves only to a channel that can serve the catchup
///   download. Its backups follow the same rule.
/// - A recording stops changing channel Config.DvrReresolveLockMinutes before
///   it starts, so the channel is settled before the recorder opens the
///   stream.
/// - A rival channel must beat the current one by
///   Config.DvrReresolveMinImprovement confidence points. This stops two
///   near-equal channels from swapping back and forth on every pass. One
///   exception applies. When both channels have evidence and the new one has
///   at least the same confidence, the recording moves to the channel with
///   the higher mapping rank. The rank is stable, so this does not flap.
/// - A recording of an event with team channels follows the guide the same
///   way the scheduler does. When the guide lists the event on the
///   scheduler's pick and not on the current channel, the recording moves to
///   the pick. When the current channel has a listing, the recording moves
///   only up the team list. Without any listing, the recording stays where
///   it is, unless its channel cannot record. Its backups still change.
/// - Backups list the team channels by rank first, then the resolver
///   candidates.
/// - The existing row is updated. No second recording is created.
/// </summary>
public class DvrChannelReresolveService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DvrChannelReresolveService> _logger;

    // EPG refreshes are hourly at best, so a 30-minute pass reacts quickly
    // enough without re-resolving the same rows repeatedly for no gain.
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(30);

    // A recording further out than this will be re-checked on a later pass
    // anyway, and its EPG data is usually still missing.
    private readonly TimeSpan _horizon = TimeSpan.FromDays(14);

    // The timer, the manual endpoint and the pre-recording refresh can all
    // start a pass. Two passes at once could move the same row twice.
    private readonly SemaphoreSlim _passLock = new(1, 1);

    // Recordings already warned about, so each warns once.
    private readonly HashSet<int> _warnedForeign = new();

    public DvrChannelReresolveService(
        IServiceProvider serviceProvider,
        ILogger<DvrChannelReresolveService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[DVR Re-resolve] Service started");

        // Let the EPG refresh and the auto-scheduler settle first.
        await Task.Delay(TimeSpan.FromMinutes(7), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DVR Re-resolve] Error during re-resolution pass");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("[DVR Re-resolve] Service stopped");
    }

    /// <summary>
    /// Re-resolve every eligible scheduled recording once. Returns how many
    /// recordings moved to a different channel.
    /// </summary>
    public async Task<int> RunPassAsync(CancellationToken ct = default)
    {
        await _passLock.WaitAsync(ct);
        try
        {
            return await RunPassCoreAsync(ct);
        }
        finally
        {
            _passLock.Release();
        }
    }

    private async Task<int> RunPassCoreAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<EventChannelResolverService>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
        var epgScheduling = scope.ServiceProvider.GetRequiredService<EpgSchedulingService>();
        var config = await configService.GetConfigAsync();

        if (!config.DvrReresolveChannelsEnabled)
        {
            return 0;
        }

        var lockWindow = TimeSpan.FromMinutes(Math.Max(0, config.DvrReresolveLockMinutes));
        var minImprovement = Math.Max(0, config.DvrReresolveMinImprovement);

        var now = DateTime.UtcNow;
        var earliestStart = now + lockWindow;
        var latestStart = now + _horizon;

        var candidates = await db.DvrRecordings
            .Where(r => r.Status == DvrRecordingStatus.Scheduled
                        && (r.Method == DvrRecordingMethod.Live || r.Method == DvrRecordingMethod.Catchup)
                        && r.EventId != null
                        && !r.ChannelPinned
                        && r.ScheduledStart > earliestStart
                        && r.ScheduledStart <= latestStart)
            .OrderBy(r => r.ScheduledStart)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return 0;
        }

        _logger.LogDebug("[DVR Re-resolve] Checking {Count} scheduled recordings", candidates.Count);

        var moved = 0;
        foreach (var recording in candidates)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var resolution = await resolver.ResolveDetailedAsync(recording.EventId!.Value, ct);
                if (await TryReresolveAsync(db, resolution, recording, minImprovement, lockWindow, ct))
                {
                    moved++;
                }
                else
                {
                    WarnIfOnForeignChannel(recording, resolution);
                }

                // The channel is settled, so the times can follow the guide
                // on it. This covers a recording that never moved too.
                await TryFollowGuideTimesAsync(db, epgScheduling, recording, lockWindow, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[DVR Re-resolve] Recording {Id} could not be re-resolved", recording.Id);
            }
        }

        // A recording that started, ended or left the window needs no more
        // warnings, so it leaves the set.
        _warnedForeign.IntersectWith(candidates.Select(r => r.Id));

        if (moved > 0)
        {
            _logger.LogInformation("[DVR Re-resolve] Moved {Moved} of {Total} scheduled recordings to a better channel",
                moved, candidates.Count);
        }

        return moved;
    }

    /// <summary>
    /// A recording booked before the guide covered its date holds the default
    /// window: the event start and EpgSchedulingService.DefaultDurationHours
    /// after it. When the guide later lists the event on the recording's
    /// channel, the recording takes the times a booking made after the guide
    /// filled in would get (#451, #453). Only a recording still on the
    /// default window changes. Times a user edited, an extended recording and
    /// a recording the guide does not list all stay as they are. Returns true
    /// when the times changed.
    /// </summary>
    internal static async Task<bool> TryFollowGuideTimesAsync(
        SportarrDbContext db,
        EpgSchedulingService epgScheduling,
        DvrRecording recording,
        TimeSpan lockWindow,
        CancellationToken ct)
    {
        if (recording.Status != DvrRecordingStatus.Scheduled
            || recording.OriginalScheduledEnd != null
            || recording.EventId == null)
        {
            return false;
        }

        var evt = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == recording.EventId, ct);
        if (evt == null)
        {
            return false;
        }

        var defaultEnd = evt.EventDate.AddHours(EpgSchedulingService.DefaultDurationHours);
        if (recording.ScheduledStart != evt.EventDate || recording.ScheduledEnd != defaultEnd)
        {
            return false;
        }

        var channel = await db.IptvChannels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == recording.ChannelId, ct);
        if (channel == null)
        {
            return false;
        }

        var times = await epgScheduling.GetOptimizedRecordingTimesAsync(
            evt, channel, recording.PrePadding, recording.PostPadding, ct);
        if (!times.UsedEpgData)
        {
            return false;
        }

        // The scheduler books the unpadded times and the recorder adds the
        // padding when it starts, so the padding that the optimizer adds is
        // taken off again here.
        var newStart = times.OptimizedStartTime.AddMinutes(recording.PrePadding);
        var newEnd = times.OptimizedEndTime.AddMinutes(-recording.PostPadding);
        // A start inside the lock window is too close for the recorder to
        // settle on a channel, the same limit the pass uses to pick rows.
        if (newEnd <= newStart || newStart <= DateTime.UtcNow + lockWindow
            || (newStart == recording.ScheduledStart && newEnd == recording.ScheduledEnd))
        {
            return false;
        }

        // The write runs only when the row still holds the times that were
        // read, so it cannot undo an extension or an edit that came in
        // meanwhile.
        var oldStart = recording.ScheduledStart;
        var oldEnd = recording.ScheduledEnd;
        var now = DateTime.UtcNow;
        var changed = await db.DvrRecordings
            .Where(r => r.Id == recording.Id && r.Status == DvrRecordingStatus.Scheduled
                        && r.ScheduledStart == oldStart && r.ScheduledEnd == oldEnd
                        && r.OriginalScheduledEnd == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.ScheduledStart, newStart)
                .SetProperty(r => r.ScheduledEnd, newEnd)
                .SetProperty(r => r.LastUpdated, now), ct);
        if (changed != 1)
        {
            return false;
        }

        // The tracked row gets the new times as saved values, so a later
        // save in this pass does not write them again.
        var entry = db.Entry(recording);
        recording.ScheduledStart = newStart;
        recording.ScheduledEnd = newEnd;
        recording.LastUpdated = now;
        foreach (var property in new[] { "ScheduledStart", "ScheduledEnd", "LastUpdated" })
        {
            entry.Property(property).OriginalValue = entry.Property(property).CurrentValue;
            entry.Property(property).IsModified = false;
        }

        return true;
    }

    // A recording left on a slot that names another match will record the
    // wrong game. Nothing better exists, so the user must step in.
    private void WarnIfOnForeignChannel(DvrRecording recording, EventChannelResolution resolution)
    {
        if (!resolution.ForeignEventChannelIds.Contains(recording.ChannelId))
        {
            _warnedForeign.Remove(recording.Id);
            return;
        }
        // Passes run one at a time, so the set needs no lock.
        if (!_warnedForeign.Add(recording.Id)) return;
        _logger.LogWarning(
            "[DVR Re-resolve] Recording {Id} for event {EventId} is on channel {ChannelId}, whose name gives another match, and no better channel exists",
            recording.Id, recording.EventId, recording.ChannelId);
    }

    private async Task<bool> TryReresolveAsync(
        SportarrDbContext db,
        EventChannelResolution resolution,
        DvrRecording recording,
        int minImprovement,
        TimeSpan lockWindow,
        CancellationToken ct)
    {
        var ranked = resolution.Candidates;
        if (recording.Method == DvrRecordingMethod.Catchup)
        {
            // A move to a channel without an archive leaves the download
            // with nothing to fetch.
            ranked = await KeepCatchupChannelsAsync(db, ranked, ct);
        }

        // Another copy of this event already uses these channels. A move onto
        // one of them gives two copies of the same stream.
        var redundant = await RedundantChannelIdsAsync(db, recording, ct);
        ranked = ranked
            .Where(c => c.ChannelId == recording.ChannelId || !redundant.Contains(c.ChannelId))
            .ToList();
        // A team slot channel that names another match today is not a team
        // channel for this event.
        var teamChannels = (await TeamChannelsAsync(db, recording, redundant, ct))
            .Where(c => !resolution.ForeignEventChannelIds.Contains(c.Id))
            .ToList();
        var teamIds = teamChannels.Select(c => c.Id).ToList();

        // With team channels, an empty resolver list still lets an unusable
        // channel move to the first team channel. Without either, nothing can
        // replace the primary. Its backups still lose any slot that names
        // another match.
        if (ranked.Count == 0 && teamChannels.Count == 0)
        {
            await DropForeignFallbacksAsync(db, recording, resolution.ForeignEventChannelIds, ct);
            return false;
        }

        var onTeamChannel = await IsOnATeamMappedChannelAsync(db, recording, ct)
            && !resolution.ForeignEventChannelIds.Contains(recording.ChannelId);
        if (teamChannels.Count > 0)
        {
            var listed = ranked
                .Where(TeamChannelSelection.IsEvidence)
                .Select(c => c.ChannelId)
                .ToHashSet();
            var currentListed = listed.Contains(recording.ChannelId);
            var fromName = ranked.FirstOrDefault(c => c.ChannelId == recording.ChannelId)?.ChannelName;

            // Follow the guide the same way the scheduler does. When the guide
            // lists the event on the scheduler's pick and not on the current
            // channel, move to the pick. This can leave the team list or go
            // down it. The pick is stable while the guide stays the same, so
            // the next pass does not move the recording again.
            var knownBad = await KnownBadChannelIdsAsync(db, listed, ct);
            var pick = TeamChannelSelection.PickPrimaryChannelId(teamChannels, ranked, knownBad);

            // No channel has a guide listing. The current channel stays and
            // only the backups change. A guess must not replace another guess
            // here. The one exception is a current channel that cannot record.
            if (listed.Count == 0)
            {
                // Take the first channel in backup order that can record.
                // The scheduler's pick can be the current channel or a dead
                // team channel, so it is not used here.
                // A slot channel that names another match cannot record this
                // event either.
                if (resolution.ForeignEventChannelIds.Contains(recording.ChannelId)
                    || !await CanRecordAsync(db, recording, recording.ChannelId, ct))
                {
                    foreach (var candidateId in TeamChannelSelection.MergeOrder(teamChannels, ranked))
                    {
                        if (candidateId == recording.ChannelId
                            || resolution.ForeignEventChannelIds.Contains(candidateId)
                            || !await CanRecordAsync(db, recording, candidateId, ct))
                        {
                            continue;
                        }
                        var targetName = teamChannels.FirstOrDefault(c => c.Id == candidateId)?.Name
                            ?? ranked.FirstOrDefault(c => c.ChannelId == candidateId)?.ChannelName
                            ?? $"channel {candidateId}";
                        return await MoveAsync(db, recording, candidateId, targetName, fromName,
                            ranked, teamChannels, lockWindow,
                            "the current channel cannot record, and the guide lists the event on no channel", ct);
                    }
                }
                await RefreshFallbacksAsync(db, recording, ranked, teamChannels, ct);
                return false;
            }

            if (pick is int pickId && pickId != recording.ChannelId && listed.Contains(pickId) && !currentListed)
            {
                var pickName = teamChannels.FirstOrDefault(c => c.Id == pickId)?.Name
                    ?? ranked.First(c => c.ChannelId == pickId).ChannelName;
                var reason = teamIds.Contains(pickId)
                    ? "the guide lists it on a team channel and not on the current channel"
                    : "the guide lists it on a channel outside the team list, and not on the current channel or a team channel";
                return await MoveAsync(db, recording, pickId, pickName, fromName,
                    ranked, teamChannels, lockWindow, reason, ct);
            }

            // The current channel has a guide listing too. Move only up to a
            // higher ranked team channel that the guide also lists, never to
            // a lower ranked one.
            var teamTarget = TeamChannelSelection.FirstGuideMatchedTeamChannel(teamChannels, ranked);
            if (currentListed && teamTarget != null && teamTarget.Id != recording.ChannelId)
            {
                var currentRank = teamIds.IndexOf(recording.ChannelId);
                var movesUp = !onTeamChannel || currentRank < 0 || teamIds.IndexOf(teamTarget.Id) < currentRank;
                if (movesUp)
                {
                    var reason = currentRank < 0
                        ? "the guide lists it on a channel mapped to one of its teams"
                        : !onTeamChannel
                            ? "the guide lists it on a working team channel, and the current team channel is unusable"
                            : "the guide lists it on a higher ranked team channel";
                    return await MoveAsync(db, recording, teamTarget.Id, teamTarget.Name, fromName,
                        ranked, teamChannels, lockWindow, reason, ct);
                }
            }
        }

        var best = ranked[0];
        if (best.ChannelId == recording.ChannelId)
        {
            // Already on the best channel. Keep the fallback list fresh so a
            // failure rotates onto today's runners-up, not stale ones.
            await RefreshFallbacksAsync(db, recording, ranked, teamChannels, ct);
            return false;
        }

        // The resolver does not read team mappings, so it can rank the team
        // channel the scheduler chose below a rival. The user mapped that
        // channel on purpose. The primary stays and the backups still refresh.
        if (onTeamChannel)
        {
            _logger.LogDebug(
                "[DVR Re-resolve] Recording {Id} is on a channel mapped to one of its teams, leaving its channel alone",
                recording.Id);
            await RefreshFallbacksAsync(db, recording, ranked, teamChannels, ct);
            return false;
        }

        // Score the assigned channel with the same fresh data as its rivals.
        // A channel that dropped out of the list scores zero, which covers a
        // channel the user disabled or deleted since scheduling.
        var current = ranked.FirstOrDefault(c => c.ChannelId == recording.ChannelId);
        var currentConfidence = current?.Confidence ?? 0;

        // The margin stops two guesses from trading places every pass. A
        // guide listing is evidence, so a guess gives way to it as soon as
        // it scores higher, or the guess could never be left.
        var guessToGuide = EventChannelResolverService.IsEvidence(best.Source)
            && !EventChannelResolverService.IsEvidence(current?.Source);
        var requiredGain = guessToGuide ? 1 : minImprovement;

        // When both channels have evidence and the new one is at least as
        // good, the user's mapping choice decides. The rank does not change
        // between passes. A move back needs the full margin, so the
        // recording cannot flap.
        var toHigherMapping = current != null
            && EventChannelResolverService.IsEvidence(best.Source)
            && EventChannelResolverService.IsEvidence(current.Source)
            && best.Confidence >= current.Confidence
            && best.MappingRank > current.MappingRank;
        if (!toHigherMapping && best.Confidence < currentConfidence + requiredGain)
        {
            // The primary stays, so the hysteresis has done its job. The
            // backups still follow today's ranking: a channel whose guide
            // lists the event must be first in line if the primary fails.
            await RefreshFallbacksAsync(db, recording, ranked, teamChannels, ct);
            return false;
        }

        var gainReason = toHigherMapping
            ? $"both channels have evidence, the new one at confidence {best.Confidence} via {best.Source}, and your league mapping ranks it higher"
            : $"confidence rose from {currentConfidence} to {best.Confidence} via {best.Source}";
        return await MoveAsync(db, recording, best.ChannelId, best.ChannelName, current?.ChannelName,
            ranked, teamChannels, lockWindow, gainReason, ct);
    }

    private async Task<bool> MoveAsync(
        SportarrDbContext db,
        DvrRecording recording,
        int targetChannelId,
        string targetChannelName,
        string? currentChannelName,
        List<EventChannelCandidate> ranked,
        List<IptvChannel> teamChannels,
        TimeSpan lockWindow,
        string reason,
        CancellationToken ct)
    {
        // Ranking the candidates takes time, and the recording can start, be
        // cancelled or enter its lock window while that is happening. Moving
        // it then left the database describing a channel different from the
        // stream actually being recorded. Confirm it is still eligible with
        // the freshest copy of the row before touching it.
        await db.Entry(recording).ReloadAsync(ct);
        if (recording.Status != DvrRecordingStatus.Scheduled)
        {
            _logger.LogDebug(
                "[DVR Re-resolve] Recording {Id} is {Status} now, leaving its channel alone",
                recording.Id, recording.Status);
            return false;
        }

        // The user may have set the channel while the candidates were ranked.
        if (recording.ChannelPinned)
        {
            return false;
        }

        if (recording.ScheduledStart <= DateTime.UtcNow + lockWindow)
        {
            _logger.LogDebug(
                "[DVR Re-resolve] Recording {Id} has entered its lock window, leaving its channel alone",
                recording.Id);
            return false;
        }

        var previousChannelId = recording.ChannelId;
        var previousChannelName = currentChannelName ?? $"channel {previousChannelId}";

        recording.ChannelId = targetChannelId;
        recording.LastUpdated = DateTime.UtcNow;
        recording.FallbackChannelIds = BuildFallbacks(
            ranked, teamChannels, targetChannelId, await RedundantChannelIdsAsync(db, recording, ct));

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[DVR Re-resolve] Recording {Id} moved from {OldChannel} to {NewChannel} for event {EventId}: {Reason}",
            recording.Id, previousChannelName, targetChannelName, recording.EventId, reason);

        return true;
    }

    private static async Task<HashSet<int>> KnownBadChannelIdsAsync(
        SportarrDbContext db, IReadOnlyCollection<int> channelIds, CancellationToken ct)
    {
        if (channelIds.Count == 0)
        {
            return new HashSet<int>();
        }
        var ids = channelIds.ToList();
        var bad = await db.IptvChannels
            .Where(c => ids.Contains(c.Id)
                && (c.Status == IptvChannelStatus.Offline || c.Status == IptvChannelStatus.Error))
            .Select(c => c.Id)
            .ToListAsync(ct);
        return bad.ToHashSet();
    }

    // A channel cannot record when it is gone, disabled, on an inactive
    // source or found dead by a health check. A league exclusion and, for a
    // catchup recording, a channel without a usable archive also count.
    // A missing resolver entry does not count. A channel that the resolver
    // found only through its guide listing drops out when the listing goes,
    // and the recording must not move for that reason.
    private static async Task<bool> CanRecordAsync(
        SportarrDbContext db, DvrRecording recording, int channelId, CancellationToken ct)
    {
        var channel = await db.IptvChannels
            .AsNoTracking()
            .Include(c => c.Source)
            .FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel == null || !channel.IsEnabled || channel.Source?.IsActive != true
            || channel.Status == IptvChannelStatus.Offline || channel.Status == IptvChannelStatus.Error)
        {
            return false;
        }

        if (recording.Method == DvrRecordingMethod.Catchup && !DvrAssignmentService.CanServeCatchup(channel))
        {
            return false;
        }

        var leagueId = await db.Events
            .Where(e => e.Id == recording.EventId)
            .Select(e => e.LeagueId)
            .FirstOrDefaultAsync(ct);
        return !(leagueId.HasValue && await db.ChannelLeagueMappings.AnyAsync(m =>
            m.ChannelId == channel.Id && m.LeagueId == leagueId.Value && m.Priority < 0, ct));
    }

    private static async Task<List<EventChannelCandidate>> KeepCatchupChannelsAsync(
        SportarrDbContext db, List<EventChannelCandidate> ranked, CancellationToken ct)
    {
        var ids = ranked.Select(c => c.ChannelId).ToList();
        var channels = await db.IptvChannels
            .AsNoTracking()
            .Include(c => c.Source)
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(ct);
        var usable = channels
            .Where(DvrAssignmentService.CanServeCatchup)
            .Select(c => c.Id)
            .ToHashSet();
        return ranked.Where(c => usable.Contains(c.ChannelId)).ToList();
    }

    // Any usable channel the user mapped to the home or the away team counts.
    // The scheduler can pick the away team's channel, or a second channel of
    // a team when the first is unhealthy at that time.
    private static async Task<bool> IsOnATeamMappedChannelAsync(
        SportarrDbContext db, DvrRecording recording, CancellationToken ct)
    {
        var teams = await db.Events
            .Where(e => e.Id == recording.EventId)
            .Select(e => new { e.HomeTeamId, e.AwayTeamId })
            .FirstOrDefaultAsync(ct);
        if (teams == null || (teams.HomeTeamId == null && teams.AwayTeamId == null))
        {
            return false;
        }

        return await db.ChannelTeamMappings.AnyAsync(m =>
            m.ChannelId == recording.ChannelId
            && (m.TeamId == teams.HomeTeamId || m.TeamId == teams.AwayTeamId)
            && m.Channel != null && m.Channel.IsEnabled
            && m.Channel.Source != null && m.Channel.Source.IsActive
            // A team channel that a health check found dead does not hold the
            // recording, so the resolver can move it to a working channel.
            && m.Channel.Status != IptvChannelStatus.Offline
            && m.Channel.Status != IptvChannelStatus.Error, ct);
    }

    private static async Task DropForeignFallbacksAsync(
        SportarrDbContext db, DvrRecording recording, IReadOnlySet<int> foreign, CancellationToken ct)
    {
        var before = recording.FallbackChannelIds;
        if (string.IsNullOrEmpty(before) || foreign.Count == 0)
        {
            return;
        }
        List<int>? ids;
        try
        {
            ids = JsonSerializer.Deserialize<List<int>>(before);
        }
        catch (JsonException)
        {
            return;
        }
        if (ids == null || !ids.Any(foreign.Contains))
        {
            return;
        }

        var kept = ids.Where(id => !foreign.Contains(id)).ToList();
        var fresh = kept.Count > 0 ? JsonSerializer.Serialize(kept) : null;
        var now = DateTime.UtcNow;
        // The same guard as RefreshFallbacksAsync. A change in the meantime wins.
        var written = await db.DvrRecordings
            .Where(r => r.Id == recording.Id
                && r.Status == DvrRecordingStatus.Scheduled
                && !r.ChannelPinned
                && r.ChannelId == recording.ChannelId
                && r.FallbackChannelIds == before)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.FallbackChannelIds, fresh)
                .SetProperty(r => r.LastUpdated, now), ct);
        if (written == 1)
        {
            var entry = db.Entry(recording);
            entry.Property(r => r.FallbackChannelIds).OriginalValue = fresh;
            entry.Property(r => r.LastUpdated).OriginalValue = now;
            recording.FallbackChannelIds = fresh;
            recording.LastUpdated = now;
        }
    }

    private async Task RefreshFallbacksAsync(
        SportarrDbContext db,
        DvrRecording recording,
        List<EventChannelCandidate> ranked,
        List<IptvChannel> teamChannels,
        CancellationToken ct)
    {
        var before = recording.FallbackChannelIds;
        var fresh = BuildFallbacks(ranked, teamChannels, recording.ChannelId, await RedundantChannelIdsAsync(db, recording, ct));
        if (fresh == before)
            return;

        // Ranking takes time. Write only if the row is still the one that was
        // ranked: still scheduled, not pinned, same channel, same backups. A
        // pin, an edit, a start or a delete in the meantime wins.
        var now = DateTime.UtcNow;
        var written = await db.DvrRecordings
            .Where(r => r.Id == recording.Id
                && r.Status == DvrRecordingStatus.Scheduled
                && !r.ChannelPinned
                && r.ChannelId == recording.ChannelId
                && r.FallbackChannelIds == before)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.FallbackChannelIds, fresh)
                .SetProperty(r => r.LastUpdated, now), ct);
        if (written == 1)
        {
            // Keep the tracked copy in step without marking it modified.
            var entry = db.Entry(recording);
            entry.Property(r => r.FallbackChannelIds).OriginalValue = fresh;
            entry.Property(r => r.LastUpdated).OriginalValue = now;
            recording.FallbackChannelIds = fresh;
            recording.LastUpdated = now;
        }
    }

    // Channels another active copy of this event already records from. A
    // failed primary must not rotate onto a channel that is mid-recording
    // the same event, the same rule the scheduler applies.
    private static async Task<HashSet<int>> RedundantChannelIdsAsync(
        SportarrDbContext db, DvrRecording recording, CancellationToken ct)
    {
        var ids = await db.DvrRecordings
            .Where(r => r.EventId == recording.EventId && r.Id != recording.Id
                && (r.Status == DvrRecordingStatus.Scheduled || r.Status == DvrRecordingStatus.Recording))
            .Select(r => r.ChannelId)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    private static string? BuildFallbacks(
        List<EventChannelCandidate> ranked, List<IptvChannel> teamChannels, int primaryChannelId, HashSet<int> excluded)
    {
        // Use the same merge as the scheduler. Team channels come first by
        // rank, then the resolver candidates. Without the team channels, a
        // pass erased the team order the scheduler stored.
        var merged = TeamChannelSelection.MergeOrder(teamChannels, ranked);
        var backups = TeamChannelSelection.BuildFallbackIds(merged, primaryChannelId, excluded);
        return backups.Count > 0 ? JsonSerializer.Serialize(backups) : null;
    }

    // The event's usable team channels in rank order. A catchup recording
    // keeps only channels that can serve the download. Channels another copy
    // of the event records from are left out.
    private static async Task<List<IptvChannel>> TeamChannelsAsync(
        SportarrDbContext db, DvrRecording recording, HashSet<int> redundant, CancellationToken ct)
    {
        var teams = await db.Events
            .Where(e => e.Id == recording.EventId)
            .Select(e => new { e.HomeTeamId, e.AwayTeamId })
            .FirstOrDefaultAsync(ct);
        if (teams == null)
        {
            return new List<IptvChannel>();
        }

        var channels = await TeamChannelSelection.GetOrderedTeamChannelsAsync(
            db, teams.HomeTeamId, teams.AwayTeamId, ct);
        return channels
            .Where(c => c.Id == recording.ChannelId || !redundant.Contains(c.Id))
            .Where(c => recording.Method != DvrRecordingMethod.Catchup || DvrAssignmentService.CanServeCatchup(c))
            .ToList();
    }


}
