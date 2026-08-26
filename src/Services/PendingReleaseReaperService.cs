using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Walks PendingReleases whose delay window has expired and promotes the best
/// release per event into the download queue, cancelling the rest.
///
/// Implements the delay-profile feature: when a release shows up but a delay
/// is configured, hold it briefly so a higher-quality release can supersede it.
/// Without this reaper, RSS sync would have to grab the first matching release
/// immediately — which is what it did before this service existed.
/// </summary>
public class PendingReleaseReaperService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PendingReleaseReaperService> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    public PendingReleaseReaperService(
        IServiceProvider serviceProvider,
        ILogger<PendingReleaseReaperService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Pending Release Reaper] Service started (poll interval: {Interval})", PollInterval);

        // Allow the host to fully initialize before first pass.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReapAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Pending Release Reaper] Pass failed - retrying in {Interval}", PollInterval);
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("[Pending Release Reaper] Service stopped");
    }

    private async Task ReapAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var downloadClientService = scope.ServiceProvider.GetRequiredService<DownloadClientService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        var qualityProfiles = await db.QualityProfiles.ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        // Pull all expired pending releases. The DB index on (Status, ReleasableAt)
        // keeps this cheap even with many entries.
        var ready = await db.PendingReleases
            .Include(p => p.Event)
                .ThenInclude(e => e!.League)
                .ThenInclude(l => l!.RootFolder)
            .Include(p => p.Event)
                .ThenInclude(e => e!.Files)
            .Where(p => p.Status == PendingReleaseStatus.Pending && p.ReleasableAt <= now)
            .ToListAsync(cancellationToken);

        if (ready.Count == 0) return;

        // Group by event and part. The winner is the highest combined score
        // in its group and the losers are cancelled so they do not all get
        // grabbed. Grouping by event alone put a fight card's prelims and
        // main card in one group, so the main card's answer settled the
        // prelims too: cancelled as superseded by a download for a part they
        // were never competing with.
        var groups = ready.GroupBy(p => (p.EventId, p.Part));

        foreach (var group in groups)
        {
            if (cancellationToken.IsCancellationRequested) return;

            var evt = group.First().Event;
            if (evt == null)
            {
                // Orphan - mark all as cancelled and move on.
                foreach (var orphan in group) orphan.Status = PendingReleaseStatus.Cancelled;
                continue;
            }

            // If the event already has a file or is no longer monitored, drop everything.
            if (!evt.Monitored || evt.HasFile)
            {
                foreach (var p in group)
                {
                    p.Status = PendingReleaseStatus.Cancelled;
                    p.Reason = evt.HasFile ? "Event already has file" : "Event no longer monitored";
                }
                continue;
            }

            var winner = group
                .OrderByDescending(p => p.QualityScore)
                .ThenByDescending(p => p.CustomFormatScore)
                .ThenByDescending(p => p.Score)
                .ThenByDescending(p => p.MatchScore)
                .ThenByDescending(p => p.Seeders ?? 0)
                .First();

            // The event flag only turns true once every monitored part has a
            // file. A part that gained its own file during the hold is decided
            // by that file, as RSS sync decides it: the hold stands only if it
            // still outscores what the part has. Left to the event flag, a held
            // prelims release was grabbed after a better prelims file had
            // already imported, downloaded in full, and was refused at import
            // as not an upgrade. Dropping on any file at all went too far the
            // other way and cancelled the upgrades RSS sync had let through.
            var groupPart = group.Key.Part;
            var partFile = string.IsNullOrEmpty(groupPart)
                ? null
                : evt.Files.FirstOrDefault(f => f.Exists && string.Equals(f.PartName, groupPart, StringComparison.OrdinalIgnoreCase));
            if (partFile != null)
            {
                // The same decision RSS sync makes, from the same helper.
                // Comparing scores alone here grabbed over a file whose
                // quality nobody could read, ignored a profile that forbids
                // upgrades, took a trivial custom-format bump as an upgrade,
                // and dropped a proper at equal score.
                // The same profile RSS sync would pick: the event's own, else the
                // league's, else the default, else the first. A separate lookup
                // here skipped the default and gave the gate a null profile for
                // an event pointing at a profile that no longer exists, which
                // switched off the upgrades-allowed and increment rules.
                var profile = RssSyncService.ResolveQualityProfile(evt, qualityProfiles);

                var refusal = Helpers.ExistingFileUpgradeGate.RefusalReason(
                    partFile, winner.Title, winner.Quality, winner.CustomFormatScore, profile, config);
                if (refusal != null)
                {
                    foreach (var p in group)
                    {
                        p.Status = PendingReleaseStatus.Cancelled;
                        p.Reason = refusal;
                    }
                    continue;
                }
            }

            // The whole group is settled before the grab runs, so the save
            // inside it commits these statuses with the queue row. Setting
            // them afterwards left a window where the download was already
            // queued while the group still read as pending, and the next pass
            // grabbed the same event a second time. Nothing is saved unless
            // the grab succeeds, so the revert below is enough on failure.
            var losers = group.Where(p => p.Id != winner.Id).ToList();
            winner.Status = PendingReleaseStatus.Released;
            foreach (var loser in losers)
            {
                loser.Status = PendingReleaseStatus.Cancelled;
                loser.Reason = $"Superseded by {winner.Title}";
            }

            var outcome = await TryGrabPendingAsync(db, downloadClientService, notificationService, evt, winner, cancellationToken);

            if (outcome == GrabOutcome.Grabbed)
            {
                _logger.LogInformation(
                    "[Pending Release Reaper] Released best-of-window for '{Event}': {Winner} (score {Score})",
                    evt.Title, winner.Title, winner.QualityScore + winner.CustomFormatScore);
            }
            else if (outcome == GrabOutcome.Superseded || outcome == GrabOutcome.Importing)
            {
                // Something better is already queued or being imported, the
                // same answer RSS sync gives. Riding the failure path here
                // marked the winner failed, put the losers back, and promoted
                // the next one to hit the same download a minute later, one
                // bogus grab failed warning per held release per pass.
                var reason = outcome == GrabOutcome.Importing
                    ? "Event already being imported"
                    : "Better or equal release already queued";
                foreach (var p in group)
                {
                    p.Status = PendingReleaseStatus.Cancelled;
                    p.Reason = reason;
                }
                _logger.LogInformation("[Pending Release Reaper] Dropped {Count} held release(s) for '{Event}': {Reason}",
                    group.Count(), evt.Title, reason);
            }
            else
            {
                winner.Status = PendingReleaseStatus.Failed;
                winner.Reason = "Grab attempt failed";
                foreach (var loser in losers)
                {
                    loser.Status = PendingReleaseStatus.Pending;
                    loser.Reason = "DelayProfile";
                }
                _logger.LogWarning("[Pending Release Reaper] Grab failed for '{Title}' - leaving losers pending for next pass",
                    winner.Title);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private enum GrabOutcome { Grabbed, Failed, Superseded, Importing }

    private async Task<GrabOutcome> TryGrabPendingAsync(
        SportarrDbContext db,
        DownloadClientService downloadClientService,
        NotificationService notificationService,
        Event evt,
        PendingRelease pending,
        CancellationToken cancellationToken)
    {
        var supportedTypes = DownloadClientService.GetClientTypesForProtocol(pending.Protocol);
        if (supportedTypes.Count == 0)
        {
            _logger.LogWarning("[Pending Release Reaper] Unknown protocol '{Protocol}' for '{Title}'", pending.Protocol, pending.Title);
            return GrabOutcome.Failed;
        }

        // Indexer record first - its assigned download client (if any) takes
        // precedence over priority/tag-based selection.
        var indexerRecord = await db.Indexers
            .FirstOrDefaultAsync(i => i.Name == pending.Indexer, cancellationToken);

        var leagueTags = evt.League?.Tags ?? new List<int>();
        var allClients = await db.DownloadClients
            .Where(dc => dc.Enabled && supportedTypes.Contains(dc.Type))
            .OrderBy(dc => dc.Priority)
            .ToListAsync(cancellationToken);
        var downloadClient =
            DownloadClientService.PickAssignedClient(allClients, indexerRecord?.DownloadClientId, _logger, "[Pending Release Reaper]")
            ?? allClients.FirstOrDefault(dc => Helpers.TagHelper.TagsMatch(dc.Tags, leagueTags));

        if (downloadClient == null)
        {
            _logger.LogWarning("[Pending Release Reaper] No {Protocol} download client for '{Event}'",
                pending.Protocol, evt.Title);
            return GrabOutcome.Failed;
        }

        // Per-root override beats the download client's default category.
        var reaperGrabCategory = !string.IsNullOrWhiteSpace(evt.League?.RootFolder?.DefaultDownloadClientCategory)
            ? evt.League.RootFolder.DefaultDownloadClientCategory!
            : downloadClient.Category;

        // The same rules RSS sync applies before it replaces a queued
        // download. A hold ends here instead, where nothing knew about
        // what was queued meanwhile, so both downloads ran and the event
        // got two copies. Only a queued or running download is a loser,
        // only when this release outscores it, and never while a finished
        // one is being imported. A blanket cancel here tore out a manual
        // 2160p grab in favour of a held 1080p release.
        var importing = await db.DownloadQueue
            .Where(q => q.EventId == evt.Id && q.Part == pending.Part)
            .AnyAsync(q => q.Status == DownloadStatus.Completed || q.Status == DownloadStatus.Importing,
                cancellationToken);
        if (importing)
        {
            _logger.LogInformation("[Pending Release Reaper] A download for '{Event}' is being imported; '{Title}' is dropped",
                evt.Title, pending.Title);
            return GrabOutcome.Importing;
        }

        var losers = await db.DownloadQueue
            .Where(q => q.EventId == evt.Id && q.Part == pending.Part)
            .Where(q => q.Status == DownloadStatus.Queued || q.Status == DownloadStatus.Downloading)
            .ToListAsync(cancellationToken);

        var pendingScore = ReleaseEvaluator.CalculateQualityScoreFromName(pending.Quality) + pending.CustomFormatScore;
        foreach (var queued in losers)
        {
            var queuedScore = ReleaseEvaluator.CalculateQualityScoreFromName(queued.Quality) + queued.CustomFormatScore;
            if (queuedScore >= pendingScore)
            {
                _logger.LogInformation("[Pending Release Reaper] '{Queued}' already queued for '{Event}' scores {QueuedScore} against {PendingScore}; '{Title}' is dropped",
                    queued.Title, evt.Title, queuedScore, pendingScore, pending.Title);
                return GrabOutcome.Superseded;
            }
        }

        var downloadId = await downloadClientService.AddDownloadAsync(
            downloadClient,
            pending.DownloadUrl,
            reaperGrabCategory,
            pending.Title,
            indexerRecord?.SeedRatio,
            indexerRecord?.SeedTime);

        if (string.IsNullOrEmpty(downloadId))
        {
            _logger.LogError("[Pending Release Reaper] Download client refused '{Title}'", pending.Title);
            return GrabOutcome.Failed;
        }

        foreach (var loser in losers)
        {
            var loserClient = await db.DownloadClients
                .FirstOrDefaultAsync(dc => dc.Id == loser.DownloadClientId, cancellationToken);
            if (loserClient != null && !string.IsNullOrEmpty(loser.DownloadId))
            {
                try
                {
                    await downloadClientService.RemoveDownloadAsync(loserClient, loser.DownloadId, deleteFiles: true);
                    _logger.LogInformation("[Pending Release Reaper] Cancelled queued download {DownloadId}; {Title} replaces it",
                        loser.DownloadId, pending.Title);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Pending Release Reaper] Could not cancel download {DownloadId}; removing its queue row anyway",
                        loser.DownloadId);
                }
            }
            db.DownloadQueue.Remove(loser);
        }

        // Recent/older event queue priority (issue #220) - same logic as the
        // other grab paths, duplicated here since the reaper grabs
        // independently instead of going through AutomaticSearchService.
        var isRecentReaperEvent = evt.EventDate >= DateTime.UtcNow.AddDays(-14);
        var requestedReaperPriority = isRecentReaperEvent ? downloadClient.RecentPriority : downloadClient.OlderPriority;

        try
        {
            var prioritySet = await downloadClientService.ApplyQueuePriorityAsync(downloadClient, downloadId, requestedReaperPriority);
            if (!prioritySet)
            {
                _logger.LogWarning("[Pending Release Reaper] Failed to set queue priority for {DownloadId} on {Client}",
                    downloadId, downloadClient.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Pending Release Reaper] Error setting queue priority for {DownloadId}", downloadId);
        }

        db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = evt.Id,
            Title = pending.Title,
            DownloadId = downloadId,
            DownloadClientId = downloadClient.Id,
            GrabCategory = reaperGrabCategory,
            Status = DownloadStatus.Queued,
            Quality = pending.Quality,
            Codec = pending.Codec,
            Source = pending.Source,
            Size = pending.Size,
            Downloaded = 0,
            Progress = 0,
            Indexer = pending.Indexer,
            IndexerId = indexerRecord?.Id,
            Protocol = pending.Protocol,
            TorrentInfoHash = pending.TorrentInfoHash,
            RetryCount = 0,
            LastUpdate = DateTime.UtcNow,
            QualityScore = pending.QualityScore,
            CustomFormatScore = pending.CustomFormatScore,
            Part = pending.Part,
            IsManualSearch = false
        });

        // Save before anything optional runs. The client already holds this
        // download, and the caller saved once after the whole loop, so a
        // failure there orphaned every download the loop had added. Nothing
        // would import them and the client would keep them for ever.
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await notificationService.SendNotificationAsync(
                NotificationTrigger.OnGrab,
                $"Grabbed: {pending.Title}",
                $"Event: {evt.Title}\nQuality: {pending.Quality ?? "Unknown"}\nIndexer: {pending.Indexer}\nSize: {pending.Size / 1024.0 / 1024.0 / 1024.0:F2} GB\nReleased from delay profile hold.",
                new NotificationEventData
                {
                    EventId = evt.Id,
                    EventExternalId = evt.ExternalId,
                    EventTitle = evt.Title ?? "",
                    League = evt.League?.Name,
                    Sport = evt.Sport,
                    Indexer = pending.Indexer ?? "",
                    Quality = pending.Quality ?? "",
                    Size = pending.Size,
                    DownloadId = downloadId,
                },
                evt.League?.Tags);
        }
        catch (Exception notifyEx)
        {
            _logger.LogWarning(notifyEx, "[Pending Release Reaper] Failed to send grab notification");
        }

        return GrabOutcome.Grabbed;
    }
}
