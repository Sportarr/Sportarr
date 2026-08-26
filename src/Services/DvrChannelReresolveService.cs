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
/// - Only live recordings change. Catchup rows belong to
///   CatchupDownloadService.
/// - A recording stops changing channel Config.DvrReresolveLockMinutes before
///   it starts, so the channel is settled before the recorder opens the
///   stream.
/// - A rival channel must beat the current one by
///   Config.DvrReresolveMinImprovement confidence points. This stops two
///   near-equal channels from swapping back and forth on every pass.
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
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<EventChannelResolverService>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
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
                        && r.Method == DvrRecordingMethod.Live
                        && r.EventId != null
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
                if (await TryReresolveAsync(db, resolver, recording, minImprovement, lockWindow, ct))
                {
                    moved++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[DVR Re-resolve] Recording {Id} could not be re-resolved", recording.Id);
            }
        }

        if (moved > 0)
        {
            _logger.LogInformation("[DVR Re-resolve] Moved {Moved} of {Total} scheduled recordings to a better channel",
                moved, candidates.Count);
        }

        return moved;
    }

    private async Task<bool> TryReresolveAsync(
        SportarrDbContext db,
        EventChannelResolverService resolver,
        DvrRecording recording,
        int minImprovement,
        TimeSpan lockWindow,
        CancellationToken ct)
    {
        var ranked = await resolver.ResolveAsync(recording.EventId!.Value, ct);
        if (ranked.Count == 0)
        {
            return false;
        }

        var best = ranked[0];
        if (best.ChannelId == recording.ChannelId)
        {
            // Already on the best channel. Keep the fallback list fresh so a
            // failure rotates onto today's runners-up, not stale ones.
            await RefreshFallbacksAsync(db, recording, ranked, ct);
            return false;
        }

        // Score the assigned channel with the same fresh data as its rivals.
        // A channel that dropped out of the list scores zero, which covers a
        // channel the user disabled or deleted since scheduling.
        var current = ranked.FirstOrDefault(c => c.ChannelId == recording.ChannelId);
        var currentConfidence = current?.Confidence ?? 0;

        if (best.Confidence < currentConfidence + minImprovement)
        {
            return false;
        }

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

        if (recording.ScheduledStart <= DateTime.UtcNow + lockWindow)
        {
            _logger.LogDebug(
                "[DVR Re-resolve] Recording {Id} has entered its lock window, leaving its channel alone",
                recording.Id);
            return false;
        }

        var previousChannelId = recording.ChannelId;
        var previousChannelName = current?.ChannelName ?? $"channel {previousChannelId}";

        recording.ChannelId = best.ChannelId;
        recording.LastUpdated = DateTime.UtcNow;
        ApplyFallbacks(recording, ranked, best.ChannelId);

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[DVR Re-resolve] Recording {Id} moved from {OldChannel} to {NewChannel} for event {EventId}. " +
            "Confidence rose from {OldConfidence} to {NewConfidence} via {Source}.",
            recording.Id, previousChannelName, best.ChannelName, recording.EventId,
            currentConfidence, best.Confidence, best.Source);

        return true;
    }

    private async Task RefreshFallbacksAsync(
        SportarrDbContext db,
        DvrRecording recording,
        List<EventChannelCandidate> ranked,
        CancellationToken ct)
    {
        var before = recording.FallbackChannelIds;
        ApplyFallbacks(recording, ranked, recording.ChannelId);

        if (before != recording.FallbackChannelIds)
        {
            recording.LastUpdated = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private static void ApplyFallbacks(
        DvrRecording recording,
        List<EventChannelCandidate> ranked,
        int primaryChannelId)
    {
        // Four backups matches what the auto-scheduler stores and stays
        // inside the rotation cap in DvrRecordingService.
        var backups = ranked
            .Where(c => c.ChannelId != primaryChannelId)
            .Take(4)
            .Select(c => c.ChannelId)
            .ToList();

        recording.FallbackChannelIds = backups.Count > 0 ? JsonSerializer.Serialize(backups) : null;
    }
}
