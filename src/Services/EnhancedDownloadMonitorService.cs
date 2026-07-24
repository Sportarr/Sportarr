using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Enhanced background service that monitors download clients with comprehensive features:
/// - Download progress tracking
/// - Completed download handling and auto-import
/// - Failed download detection and auto-retry
/// - Stalled download detection
/// - Blocklist management
/// - Remove completed downloads option
/// </summary>
public class EnhancedDownloadMonitorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EnhancedDownloadMonitorService> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _stalledTimeout = TimeSpan.FromMinutes(10); // Default stalled timeout

    // Hard cap on import retries. After this many failed import attempts the
    // row is marked Failed permanently and the monitor stops touching it —
    // otherwise the download client (e.g. SABnzbd) will keep reporting the
    // item as 100% complete on every poll, the monitor will keep flipping
    // Failed→Completed, and HandleCompletedDownload will keep retrying the
    // same broken import forever. Without this cap we've seen ImportRetryCount
    // climb past 1000 in production.
    private const int MaxImportRetries = 3;

    // How long to keep retrying a completed download whose files are still packed
    // archives before giving up. An external extractor (unpackerr) or the usenet
    // client's own post-processing can take many minutes on a large release, so the
    // monitor holds the item as ImportPending for this long instead of failing it.
    private static readonly TimeSpan ExtractionGracePeriod = TimeSpan.FromMinutes(30);

    public EnhancedDownloadMonitorService(
        IServiceProvider serviceProvider,
        ILogger<EnhancedDownloadMonitorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Enhanced Download Monitor] Service started - Poll interval: {Interval}s", _pollInterval.TotalSeconds);

        // Wait before starting to allow app to fully initialize
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        // Reset MissingFromClientCount for all active downloads on startup.
        // This prevents stale counts from a previous shutdown from causing false "removed externally" removals.
        // Counts are only meaningful within a single continuous run.
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            var activeDownloads = await db.DownloadQueue
                .Where(d => d.MissingFromClientCount > 0 &&
                            d.Status != DownloadStatus.Imported &&
                            d.Status != DownloadStatus.Failed)
                .ToListAsync(stoppingToken);

            if (activeDownloads.Count > 0)
            {
                _logger.LogInformation("[Enhanced Download Monitor] Resetting MissingFromClientCount for {Total} download(s) on startup (prevents false removal after restart)",
                    activeDownloads.Count);
                foreach (var d in activeDownloads)
                {
                    _logger.LogInformation("[Enhanced Download Monitor] Resetting MissingFromClientCount={Count} for '{Title}' on startup",
                        d.MissingFromClientCount, d.Title);
                    d.MissingFromClientCount = 0;
                }
                await db.SaveChangesAsync(stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Enhanced Download Monitor] Failed to reset MissingFromClientCount on startup");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorDownloadsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Enhanced Download Monitor] Error monitoring downloads");
            }

            // Detect external downloads (added to client outside of Sportarr)
            try
            {
                await DetectExternalDownloadsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Enhanced Download Monitor] Error detecting external downloads");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }

        _logger.LogInformation("[Enhanced Download Monitor] Service stopped");
    }

    private async Task MonitorDownloadsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var downloadClientService = scope.ServiceProvider.GetRequiredService<DownloadClientService>();
        var fileImportService = scope.ServiceProvider.GetRequiredService<FileImportService>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();

        // Hygiene: drop queue and grab-history rows whose event no longer exists.
        // These orphans appear when an event is removed (its DownloadQueue cascade
        // isn't enforced on every legacy DB), leaving stale "Completed" rows that
        // clutter the Activity queue and that the dedup (keyed on event id) can't
        // match — so the same release looks un-grabbed and gets re-grabbed. Cheap
        // anti-join, normally deletes nothing once events are stable.
        try
        {
            var removedQueue = await db.DownloadQueue
                .Where(d => !db.Events.Any(e => e.Id == d.EventId))
                .ExecuteDeleteAsync(cancellationToken);
            var removedGrabs = await db.GrabHistory
                .Where(g => !db.Events.Any(e => e.Id == g.EventId))
                .ExecuteDeleteAsync(cancellationToken);
            if (removedQueue > 0 || removedGrabs > 0)
            {
                _logger.LogInformation(
                    "[Enhanced Download Monitor] Cleaned up orphaned rows whose event no longer exists: {Queue} queue, {Grabs} grab-history",
                    removedQueue, removedGrabs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Enhanced Download Monitor] Orphaned-row cleanup failed");
        }

        // Get all active downloads (not completed, not imported, not failed permanently).
        //
        // Two separate retry counters gate exclusion:
        //   - RetryCount         : incremented when the *download* itself fails
        //                          (HandleFailedDownload). Re-grab is allowed up to 3 attempts.
        //   - ImportRetryCount   : incremented when the *import* of a completed
        //                          download fails. After MaxImportRetries the row is
        //                          permanently Failed and we must NOT pick it up again,
        //                          even though SAB will still happily report it as
        //                          100% complete on every poll.
        //
        // Without the ImportRetryCount gate, the previous query kept pulling Failed
        // rows whose RetryCount was 0 (download succeeded, only import broke), the
        // monitor flipped Failed→Completed because the client said "completed", and
        // HandleCompletedDownload retried the same broken import forever.
        var activeDownloads = await db.DownloadQueue
            .Include(d => d.DownloadClient)
            .Include(d => d.Event)
            .Where(d => d.Status != DownloadStatus.Imported &&
                       (d.Status != DownloadStatus.Failed
                            || (d.RetryCount < 3 && (d.ImportRetryCount ?? 0) < MaxImportRetries)))
            .ToListAsync(cancellationToken);

        if (activeDownloads.Count == 0)
            return;

        _logger.LogDebug("[Enhanced Download Monitor] Checking {Count} active downloads", activeDownloads.Count);

        // Load settings once
        var config = await configService.GetConfigAsync();
        var enableCompletedHandling = config.EnableCompletedDownloadHandling;
        var redownloadFailed = config.RedownloadFailedDownloads;
        var stalledFailMinutes = config.StalledDownloadTimeoutMinutes;
        var redownloadFailedFromInteractive = config.RedownloadFailedFromInteractiveSearch;
        // Note: RemoveCompletedDownloads and RemoveFailedDownloads are now per-client settings
        // accessed via download.DownloadClient.RemoveCompletedDownloads/RemoveFailedDownloads

        foreach (var download in activeDownloads)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await ProcessDownloadAsync(
                    download,
                    downloadClientService,
                    fileImportService,
                    db,
                    enableCompletedHandling,
                    redownloadFailed,
                    redownloadFailedFromInteractive,
                    stalledFailMinutes,
                    cancellationToken);

                // Save changes after each successful download to prevent data loss
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Enhanced Download Monitor] Error processing download: {Title}", download.Title);

                // Mark as failed but allow retry
                download.Status = DownloadStatus.Failed;
                download.ErrorMessage = ex.Message;
                download.RetryCount = (download.RetryCount ?? 0) + 1;

                // Save the error state immediately
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "[Enhanced Download Monitor] Failed to save error state for download: {Title}", download.Title);
                }
            }
        }
    }

    private async Task ProcessDownloadAsync(
        DownloadQueueItem download,
        DownloadClientService downloadClientService,
        FileImportService fileImportService,
        SportarrDbContext db,
        bool enableCompletedHandling,
        bool redownloadFailed,
        bool redownloadFailedFromInteractive,
        int stalledFailMinutes,
        CancellationToken cancellationToken)
    {
        // For ImportPending downloads, skip the download client check and just retry import
        // The download already completed on the client, we're just waiting for the file to be accessible
        if (download.Status == DownloadStatus.ImportPending && enableCompletedHandling)
        {
            _logger.LogDebug("[Enhanced Download Monitor] Retrying import for pending download: {Title} (attempt {Count})",
                download.Title, (download.ImportRetryCount ?? 0) + 1);

            // Re-check the download client first. The data on disk may
            // have disappeared between the time we last saw "complete"
            // and now (qbit's missingFiles state for torrents whose
            // content was moved/deleted; SAB removing the history
            // entry; debrid orphan-cleanup running). Without this
            // check we waste 3 import retries (~2 minutes) per cycle
            // attempting to read a path the client has already given
            // up on. If the client now reports the download as failed
            // or no longer present, route to HandleFailedDownload
            // immediately so the Blocklist entry, qbit removal, and
            // re-search trigger fire on the FIRST poll instead of the
            // 4th.
            if (download.DownloadClient != null && !string.IsNullOrEmpty(download.DownloadId))
            {
                try
                {
                    var clientStatus = await downloadClientService.GetDownloadStatusAsync(
                        download.DownloadClient,
                        download.DownloadId);

                    var clientReportsFailure = clientStatus != null &&
                        string.Equals(clientStatus.Status, "failed", StringComparison.OrdinalIgnoreCase);

                    if (clientReportsFailure)
                    {
                        _logger.LogWarning(
                            "[Enhanced Download Monitor] Download client now reports {Title} as failed ({Reason}); short-circuiting import retry loop.",
                            download.Title, clientStatus!.ErrorMessage ?? "no detail");
                        download.Status = DownloadStatus.Failed;
                        download.ErrorMessage = clientStatus.ErrorMessage ?? "Download client reports data missing";
                        await HandleFailedDownload(
                            download,
                            downloadClientService,
                            db,
                            redownloadFailed,
                            redownloadFailedFromInteractive);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Client query failed - log but fall through to
                    // the import retry. We don't want a transient
                    // download-client outage to mark every pending
                    // import as failed.
                    _logger.LogDebug(ex,
                        "[Enhanced Download Monitor] Could not re-query download client for {Title} during import retry; proceeding with retry anyway",
                        download.Title);
                }
            }

            // Capture the status before the import retry so we can
            // detect the same Failed transition the long path
            // handles below. Without this, when attempt N/N flips
            // status to Failed inside HandleCompletedDownload, the
            // early `return` on the next line skips the
            // HandleFailedDownload call at line 414 and the
            // download silently rots in Failed state with no
            // blocklist entry, no re-search, and no notification.
            var previousImportPendingStatus = download.Status;
            await HandleCompletedDownload(
                download,
                downloadClientService,
                fileImportService,
                db);

            if (download.Status == DownloadStatus.Failed
                && previousImportPendingStatus != DownloadStatus.Failed)
            {
                await HandleFailedDownload(
                    download,
                    downloadClientService,
                    db,
                    redownloadFailed,
                    redownloadFailedFromInteractive);
            }
            return;
        }

        if (download.DownloadClient == null)
        {
            _logger.LogWarning("[Enhanced Download Monitor] Download {Title} has no download client assigned", download.Title);
            download.Status = DownloadStatus.Failed;
            download.ErrorMessage = "No download client assigned";
            return;
        }

        // Query download client for current status
        var status = await downloadClientService.GetDownloadStatusAsync(
            download.DownloadClient,
            download.DownloadId);

        if (status == null)
        {
            // Download not found by ID - try finding by title (Decypharr/debrid proxy compatibility)
            // Debrid proxies may change the download ID/hash after processing
            _logger.LogInformation("[Enhanced Download Monitor] Download not found by ID {DownloadId}, trying title match for: {Title} (MissingCount so far: {Count})",
                download.DownloadId, download.Title, download.MissingFromClientCount ?? 0);

            var (titleMatchStatus, newDownloadId) = await downloadClientService.FindDownloadByTitleAsync(
                download.DownloadClient,
                download.Title,
                download.DownloadClient.Category);

            if (titleMatchStatus != null && newDownloadId != null)
            {
                _logger.LogInformation("[Enhanced Download Monitor] Found download by title match. Updating ID: {OldId} → {NewId}",
                    download.DownloadId, newDownloadId);

                // Update the download ID to the new one (debrid proxy changed it)
                download.DownloadId = newDownloadId;
                status = titleMatchStatus;
            }
            else
            {
                // Download not found in client: auto-remove from queue.
                // This happens when user deletes from download client directly instead of through Sportarr.

                // Do NOT count this as "missing" if we're shutting down — the null could be from a cancelled HTTP request
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("[Enhanced Download Monitor] Download status check cancelled for '{Title}' - skipping missing count increment", download.Title);
                    return;
                }

                // Grace period: newly added downloads may not be visible in client yet
                // Transmission/other clients can take several minutes to register a torrent
                var gracePeriod = TimeSpan.FromMinutes(3);
                if (download.Added > DateTime.UtcNow - gracePeriod)
                {
                    _logger.LogDebug("[Enhanced Download Monitor] Download recently added ({Age:F0}s ago), skipping missing check during grace period: {Title}",
                        (DateTime.UtcNow - download.Added).TotalSeconds, download.Title);
                    return;
                }

                // Track consecutive "not found" checks to avoid removing on transient issues
                download.MissingFromClientCount = (download.MissingFromClientCount ?? 0) + 1;

                if (download.MissingFromClientCount >= 10)
                {
                    // After 10 consecutive checks (e.g. ~5 minutes at 30s poll interval), remove from queue.
                    // Downloads removed from the client are removed from the queue.
                    _logger.LogWarning("[Enhanced Download Monitor] Download not found in client for {Count} consecutive checks, removing from queue: {Title} (DownloadId: {DownloadId})",
                        download.MissingFromClientCount, download.Title, download.DownloadId);

                    // Remove from queue (auto-cleanup).
                    db.DownloadQueue.Remove(download);
                    await db.SaveChangesAsync();
                    return;
                }
                else
                {
                    // First few "not found" checks — log at Warning so they are visible in production
                    _logger.LogWarning("[Enhanced Download Monitor] Download not found in client (check {Count}/10): {Title} (DownloadId: {DownloadId})",
                        download.MissingFromClientCount, download.Title, download.DownloadId);
                }
                return;
            }
        }

        // Download found - reset "missing from client" counter
        download.MissingFromClientCount = 0;

        // Update download metadata
        var previousStatus = download.Status;
        var previousProgress = download.Progress;

        download.Progress = status.Progress;
        download.Downloaded = status.Downloaded;
        download.Size = status.Size;
        download.TimeRemaining = status.TimeRemaining;
        download.LastUpdate = DateTime.UtcNow;

        // Update status based on client response
        // Special handling for Decypharr: "paused" with 100% progress means completed
        // Decypharr pauses torrents when complete since debrid services don't seed
        var isDecypharrCompleted = status.Status == "paused" && status.Progress >= 99.9;

        download.Status = status.Status switch
        {
            "downloading" => DownloadStatus.Downloading,
            "paused" when isDecypharrCompleted => DownloadStatus.Completed,
            "paused" => DownloadStatus.Paused,
            "completed" => DownloadStatus.Completed,
            "failed" or "error" => DownloadStatus.Failed,
            "queued" or "waiting" => DownloadStatus.Queued,
            "warning" => DownloadStatus.Warning,
            _ => download.Status
        };

        if (isDecypharrCompleted)
        {
            _logger.LogInformation("[Enhanced Download Monitor] Detected Decypharr-style completion (paused at 100%): {Title}", download.Title);
        }

        if (!string.IsNullOrEmpty(status.ErrorMessage))
        {
            download.ErrorMessage = status.ErrorMessage;
        }

        // Log status changes
        if (previousStatus != download.Status)
        {
            _logger.LogInformation("[Enhanced Download Monitor] '{Title}' status: {Old} → {New} ({Progress:F1}%)",
                download.Title, previousStatus, download.Status, download.Progress);
        }

        // Warn if the event is no longer monitored.
        // This applies when user unmonitors an event/league/season while download is in progress.
        if (download.Event != null && !download.Event.Monitored)
        {
            // Only set warning status if not already completed/imported/failed
            // AND only if this was NOT a manual grab — manual grabs should always import
            // regardless of event monitoring status (matches AutomaticSearchService behavior)
            if (download.Status != DownloadStatus.Imported &&
                download.Status != DownloadStatus.Failed &&
                !download.IsManualSearch)
            {
                download.Status = DownloadStatus.Warning;

                // Add unmonitored warning to StatusMessages if not already present
                var unmonitoredMessage = "Event is no longer monitored";
                if (!download.StatusMessages.Contains(unmonitoredMessage))
                {
                    download.StatusMessages.Add(unmonitoredMessage);
                    _logger.LogWarning("[Enhanced Download Monitor] '{Title}' - Event is no longer monitored, download marked as warning",
                        download.Title);
                }
            }
        }
        else
        {
            // Remove unmonitored warning if event is now monitored again
            var unmonitoredMessage = "Event is no longer monitored";
            if (download.StatusMessages.Contains(unmonitoredMessage))
            {
                download.StatusMessages.Remove(unmonitoredMessage);
                _logger.LogInformation("[Enhanced Download Monitor] '{Title}' - Event is now monitored again, warning removed",
                    download.Title);

                // Reset status to previous state if the only warning was unmonitored
                if (download.StatusMessages.Count == 0 && download.Status == DownloadStatus.Warning)
                {
                    download.Status = status.Status switch
                    {
                        "downloading" => DownloadStatus.Downloading,
                        "paused" => DownloadStatus.Paused,
                        "completed" => DownloadStatus.Completed,
                        "queued" or "waiting" => DownloadStatus.Queued,
                        _ => DownloadStatus.Downloading
                    };
                }
            }
        }

        // Detect stalled downloads
        if (download.Status == DownloadStatus.Downloading)
        {
            CheckForStalledDownload(download, stalledFailMinutes);
        }

        // Handle completed downloads
        // Import if: (1) status just changed to Completed, OR (2) already Completed but not yet imported
        // The second case handles downloads that arrive already completed (common with debrid services)
        if (download.Status == DownloadStatus.Completed &&
            download.Status != DownloadStatus.Imported &&
            (previousStatus != DownloadStatus.Completed || download.ImportedAt == null) &&
            enableCompletedHandling)
        {
            await HandleCompletedDownload(
                download,
                downloadClientService,
                fileImportService,
                db);
        }

        // Always handle failed downloads (no global disable — Radarr parity)
        if (download.Status == DownloadStatus.Failed &&
            previousStatus != DownloadStatus.Failed)
        {
            await HandleFailedDownload(
                download,
                downloadClientService,
                db,
                redownloadFailed,
                redownloadFailedFromInteractive);
        }
    }

    // Last observed progress and when it last MOVED, per queue item. The
    // old check compared against the download's Added time, which flagged
    // any slow-but-moving torrent older than the threshold; this tracks
    // actual movement. In-memory: an app restart just restarts the timers.
    private readonly Dictionary<int, (double Progress, DateTime Since)> _stallProgress = new();

    /// <summary>
    /// Two-stage stalled handling for torrents in Downloading state. After
    /// the soft window with no progress movement the row is flagged
    /// Warning (visible in Activity). After the configured hard timeout
    /// (Settings > Download Clients; 0 disables) the row is marked Failed,
    /// which flows through the same pipeline as a client-reported failure:
    /// removed from the client, blocklisted, and re-searched. Dead
    /// torrents (no seeders, dead tracker) stop squatting in the queue
    /// forever. Usenet is exempt - it downloads at line speed or errors.
    /// </summary>
    private void CheckForStalledDownload(
        DownloadQueueItem download,
        int stalledFailMinutes)
    {
        var isStallable = download.Protocol == "Torrent" &&
                          download.Status == DownloadStatus.Downloading &&
                          download.Progress < 99.9;

        if (!isStallable)
        {
            _stallProgress.Remove(download.Id);
            return;
        }

        if (!_stallProgress.TryGetValue(download.Id, out var tracked) ||
            Math.Abs(tracked.Progress - download.Progress) >= 0.1)
        {
            _stallProgress[download.Id] = (download.Progress, DateTime.UtcNow);
            return;
        }

        var stalledFor = DateTime.UtcNow - tracked.Since;

        if (stalledFailMinutes > 0 && stalledFor > TimeSpan.FromMinutes(stalledFailMinutes))
        {
            _logger.LogWarning(
                "[Enhanced Download Monitor] Download stalled at {Progress:F1}% for {Minutes:F0}m; failing for blocklist + re-search: {Title}",
                download.Progress, stalledFor.TotalMinutes, download.Title);

            download.Status = DownloadStatus.Failed;
            download.ErrorMessage = $"Stalled: no progress for {(int)stalledFor.TotalMinutes} minutes";
            _stallProgress.Remove(download.Id);
        }
        else if (stalledFor > _stalledTimeout && download.ErrorMessage?.Contains("stalled", StringComparison.OrdinalIgnoreCase) != true)
        {
            _logger.LogWarning("[Enhanced Download Monitor] Download appears stalled: {Title} (Progress: {Progress:F1}%)",
                download.Title, download.Progress);

            download.Status = DownloadStatus.Warning;
            download.ErrorMessage = $"Download stalled at {download.Progress:F1}% for {(int)stalledFor.TotalMinutes} minutes";
        }
    }

    /// <summary>
    /// Check if a torrent has reached its seed limits (ratio and/or time) from the indexer settings.
    /// Returns true if all configured limits are met, or if no limits are configured.
    /// </summary>
    private static bool HasReachedSeedLimit(DownloadClientStatus status, Indexer indexer)
    {
        // Check ratio limit
        if (indexer.SeedRatio.HasValue && indexer.SeedRatio.Value > 0)
        {
            if ((status.Ratio ?? 0) < indexer.SeedRatio.Value)
                return false;
        }

        // Check time limit (SeedTime is in minutes)
        if (indexer.SeedTime.HasValue && indexer.SeedTime.Value > 0)
        {
            var seedingMinutes = status.CompletedAt.HasValue
                ? (DateTime.UtcNow - status.CompletedAt.Value).TotalMinutes
                : 0;

            if (seedingMinutes < indexer.SeedTime.Value)
                return false;
        }

        return true;
    }

    private async Task HandleCompletedDownload(
        DownloadQueueItem download,
        DownloadClientService downloadClientService,
        FileImportService fileImportService,
        SportarrDbContext? db = null)
    {
        // Set-once: CompletedAt is the moment the download first finished, not the
        // last poll. Re-imports (ImportPending retries) re-enter this method every
        // poll; resetting CompletedAt here would keep the extraction grace window
        // (below) from ever elapsing.
        download.CompletedAt ??= DateTime.UtcNow;

        // Defensive guard: even though MonitorDownloadsAsync filters out rows
        // with ImportRetryCount >= MaxImportRetries, the status flip from
        // Failed→Completed earlier in this method's call stack can let a row
        // reach here that has already exhausted its retries. Don't burn another
        // attempt on it — pin it to Failed and walk away.
        if ((download.ImportRetryCount ?? 0) >= MaxImportRetries)
        {
            download.Status = DownloadStatus.Failed;
            if (string.IsNullOrEmpty(download.ErrorMessage))
            {
                download.ErrorMessage = $"Import failed after {MaxImportRetries} attempts; not retrying";
            }
            return;
        }

        _logger.LogInformation("[Enhanced Download Monitor] Download completed, starting import: {Title}", download.Title);

        try
        {
            download.Status = DownloadStatus.Importing;

            // Import the download
            await fileImportService.ImportDownloadAsync(download);

            download.Status = DownloadStatus.Imported;
            download.ImportedAt = DateTime.UtcNow;

            _logger.LogInformation("[Enhanced Download Monitor] ✓ Import successful: {Title}", download.Title);

            // Remove from download client if configured in the client's settings
            // Pass deleteFiles: true to also remove the download folder from disk
            // The video files have already been moved/hardlinked to the library, but non-video files (nfo, srr, etc.)
            // and the folder itself may remain - the download client should clean these up
            //
            // Uses per-client RemoveCompletedDownloads setting which allows users to configure
            // differently for each client (e.g., remove for Usenet, preserve for seeding torrents)
            if (download.DownloadClient?.RemoveCompletedDownloads == true)
            {
                // For torrents with indexer seed settings, check if seeding goals are met before removal.
                // Torrents seed until ratio/time limits are reached.
                if (download.Protocol == "Torrent" && db != null)
                {
                    var indexer = download.IndexerId != null
                        ? await db.Indexers.FindAsync(download.IndexerId)
                        : !string.IsNullOrEmpty(download.Indexer)
                            ? await db.Indexers.FirstOrDefaultAsync(i => i.Name == download.Indexer)
                            : null;

                    if (indexer != null && (indexer.SeedRatio.HasValue || indexer.SeedTime.HasValue))
                    {
                        var status = await downloadClientService.GetDownloadStatusAsync(
                            download.DownloadClient, download.DownloadId);

                        if (status != null && !HasReachedSeedLimit(status, indexer))
                        {
                            _logger.LogInformation(
                                "[Enhanced Download Monitor] Torrent still seeding, skipping removal: {Title} " +
                                "(Ratio: {Ratio:F2}/{Target}, Time: {Time})",
                                download.Title,
                                status.Ratio ?? 0,
                                indexer.SeedRatio?.ToString("F1") ?? "N/A",
                                indexer.SeedTime.HasValue ? $"{indexer.SeedTime}min" : "N/A");

                            // Mark as imported but don't remove — monitor will re-check on next poll
                            return;
                        }
                    }
                }

                try
                {
                    await downloadClientService.RemoveDownloadAsync(
                        download.DownloadClient,
                        download.DownloadId,
                        deleteFiles: true);

                    // Info, not Debug: whether the download-dir folder was
                    // cleaned up is the question every "empty folders left
                    // behind" support thread turns on, and at Debug the
                    // default log couldn't answer it in either direction.
                    _logger.LogInformation("[Enhanced Download Monitor] Removed completed download and its files from client: {Title}", download.Title);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Enhanced Download Monitor] Failed to remove download from client: {Title}", download.Title);
                    // Don't fail the import if we can't remove from client
                }
            }
            else if (download.DownloadClient == null)
            {
                // Log when download client removal is skipped due to missing client association
                // This helps diagnose why folders might not be removed from the download client
                _logger.LogInformation("[Enhanced Download Monitor] Skipped removal from download client: No download client associated with {Title}",
                    download.Title);
            }
            else
            {
                // The toggle-off case was completely silent, which made every
                // "empty folders left behind" report unanswerable from a log:
                // no line existed in either direction. One info line per
                // import names the setting that decides the behavior.
                _logger.LogInformation("[Enhanced Download Monitor] Leaving download in client (Remove Completed Downloads is off for '{Client}'): {Title}",
                    download.DownloadClient!.Name, download.Title);
            }
        }
        catch (IndexerFailDownloadException ex)
        {
            // FailDownloads policy match. Skip the retry-count loop —
            // pin to Failed so the next monitor pass takes the
            // status-transition path in HandleFailedDownload, which
            // adds to the blocklist and (if redownloadFailed is on)
            // schedules a re-search. Bumping ImportRetryCount here
            // would burn the retry budget on a release that's never
            // going to import successfully.
            _logger.LogWarning(
                "[Enhanced Download Monitor] ✗ FailDownloads policy fired ({Reason}) for {Title}: {Message}",
                ex.Reason, download.Title, ex.Message);
            download.Status = DownloadStatus.Failed;
            download.ErrorMessage = ex.Message;
        }
        catch (ExtractionPendingException ex)
        {
            // Completed download still contains packed archives. An external extractor
            // (unpackerr) or the client's post-processing may still be running, so retry
            // within a grace window rather than failing. Crucially this burns no terminal
            // retry budget and never blocklists or removes the download, so a healthy,
            // still-seeding torrent is left completely intact while extraction finishes.
            var now = DateTime.UtcNow;
            var waited = now - (download.CompletedAt ?? download.Added);

            if (DownloadFailurePolicy.IsWithinExtractionGrace(download.CompletedAt, download.Added, now, ExtractionGracePeriod))
            {
                var waitedMin = (int)waited.TotalMinutes;
                var graceMin = (int)ExtractionGracePeriod.TotalMinutes;
                _logger.LogInformation(
                    "[Enhanced Download Monitor] Waiting for extraction (attempt after {Waited}m/{Grace}m): {Title}",
                    waitedMin, graceMin, download.Title);

                download.Status = DownloadStatus.ImportPending;
                download.ErrorMessage = $"Waiting for archive extraction to finish ({waitedMin}m/{graceMin}m): {ex.Message}";
            }
            else
            {
                // Extraction never happened (no extractor, or it failed). Give up on the
                // import, but leave the download in place: HandleFailedDownload will
                // blocklist the packed release so a non-packed one can be grabbed, and the
                // CompletedAt guard there keeps the still-seeding torrent and its data.
                _logger.LogWarning(
                    "[Enhanced Download Monitor] ✗ Archives never extracted within {Grace}m for {Title}: {Message}",
                    (int)ExtractionGracePeriod.TotalMinutes, download.Title, ex.Message);
                download.Status = DownloadStatus.Failed;
                download.ErrorMessage = ex.Message;
            }
        }
        catch (DownloadFailedException ex)
        {
            // The download client itself flagged this as failed (e.g.
            // SAB renamed the folder _FAILED_<x> after a par2/unpack
            // post-processing failure). Same routing as the
            // FailDownloads policy match: skip retries and pin to
            // Failed so HandleFailedDownload's status-transition path
            // blocklists the release and schedules a re-search. Without
            // this branch the import would re-attempt 3× into an empty
            // folder, never blocklist, and the next RSS sync would
            // re-grab the same broken NZB indefinitely.
            _logger.LogWarning(
                "[Enhanced Download Monitor] ✗ Download client reported failure for {Title}: {Message}",
                download.Title, ex.Message);
            download.Status = DownloadStatus.Failed;
            download.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            download.ImportRetryCount = (download.ImportRetryCount ?? 0) + 1;

            // Check if this is a path accessibility issue (file not ready yet)
            var isPathError = ex.Message.Contains("not found") ||
                             ex.Message.Contains("not accessible") ||
                             ex.Message.Contains("does not exist");

            if (isPathError)
            {
                // For path accessibility issues, keep retrying indefinitely
                // The file might just be delayed (still extracting, moving, etc.)
                _logger.LogWarning("[Enhanced Download Monitor] Import path not accessible (attempt {Count}): {Title} - Will retry on next poll",
                    download.ImportRetryCount, download.Title);

                download.Status = DownloadStatus.ImportPending;
                download.ErrorMessage = $"Waiting for path to be accessible (attempt {download.ImportRetryCount}): {ex.Message}";
            }
            else
            {
                // For other import errors, treat as failed after MaxImportRetries attempts.
                _logger.LogError(ex, "[Enhanced Download Monitor] ✗ Import failed (attempt {Count}/{Max}): {Title}",
                    download.ImportRetryCount, MaxImportRetries, download.Title);

                if (download.ImportRetryCount >= MaxImportRetries)
                {
                    download.Status = DownloadStatus.Failed;
                    download.ErrorMessage = $"Import failed after {MaxImportRetries} attempts: {ex.Message}";
                }
                else
                {
                    download.Status = DownloadStatus.ImportPending;
                    download.ErrorMessage = $"Import failed (attempt {download.ImportRetryCount}/{MaxImportRetries}): {ex.Message}";
                }
            }
        }
    }

    private async Task HandleFailedDownload(
        DownloadQueueItem download,
        DownloadClientService downloadClientService,
        SportarrDbContext db,
        bool redownloadFailed,
        bool redownloadFailedFromInteractive)
    {
        download.RetryCount = (download.RetryCount ?? 0) + 1;

        _logger.LogWarning("[Enhanced Download Monitor] Download failed: {Title} (Attempt {Retry}/3) - {Error}",
            download.Title, download.RetryCount, download.ErrorMessage ?? "Unknown error");

        // Add to blocklist to prevent re-grabbing the same release
        // For torrents: use TorrentInfoHash
        // For Usenet: use Title + Indexer combination
        BlocklistItem? existingBlock = null;

        if (!string.IsNullOrEmpty(download.TorrentInfoHash))
        {
            existingBlock = await db.Blocklist
                .FirstOrDefaultAsync(b => b.TorrentInfoHash == download.TorrentInfoHash);
        }
        else if (!string.IsNullOrEmpty(download.Title))
        {
            // No hash to match on (usenet, or a torrent whose infohash was
            // never captured): dedupe by title+indexer. Filtering on protocol
            // here let hashless torrents slip past the check and re-add the
            // same entry on every failed retry, which is how blocklists grew
            // to thousands of duplicate rows.
            existingBlock = await db.Blocklist
                .FirstOrDefaultAsync(b => b.Title == download.Title &&
                                         b.Indexer == (download.Indexer ?? "Unknown"));
        }

        if (existingBlock == null)
        {
            var blocklistItem = new BlocklistItem
            {
                EventId = download.EventId,
                Title = download.Title,
                TorrentInfoHash = download.TorrentInfoHash, // null for Usenet
                Indexer = download.Indexer ?? "Unknown",
                Protocol = download.Protocol ?? (string.IsNullOrEmpty(download.TorrentInfoHash) ? "Usenet" : "Torrent"),
                Reason = BlocklistReason.FailedDownload,
                Message = download.ErrorMessage ?? "Download failed",
                BlockedAt = DateTime.UtcNow
            };

            db.Blocklist.Add(blocklistItem);
            _logger.LogInformation("[Enhanced Download Monitor] Added to blocklist: {Title} ({Protocol})",
                download.Title, blocklistItem.Protocol);
        }

        // Remove from download client only for a genuine DOWNLOAD failure - never for an
        // IMPORT failure. If CompletedAt is set the data downloaded fine and the failure is
        // purely on Sportarr's import side (e.g. archives that never extracted). Removing
        // it here with deleteFiles:true would destroy a healthy, still-seeding torrent and,
        // on a private tracker, cause a hit-and-run. Leave it fully intact so it keeps
        // seeding and can be re-imported after manual extraction. The blocklist entry above
        // still steers RSS/search toward a non-packed release.
        var wasDownloaded = download.CompletedAt != null;
        if (wasDownloaded)
        {
            _logger.LogInformation(
                "[Enhanced Download Monitor] Import failed but download succeeded - leaving '{Title}' in the client intact (not removing, not deleting data)",
                download.Title);
        }
        else if (DownloadFailurePolicy.ShouldRemoveDataOnFailure(wasDownloaded, download.DownloadClient?.RemoveFailedDownloads == true))
        {
            try
            {
                await downloadClientService.RemoveDownloadAsync(
                    download.DownloadClient,
                    download.DownloadId,
                    deleteFiles: true); // Clean up files from a download that never completed

                _logger.LogDebug("[Enhanced Download Monitor] Removed failed download from client: {Title}", download.Title);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Enhanced Download Monitor] Failed to remove failed download from client: {Title}", download.Title);
            }
        }

        // Retry if enabled and under retry limit (respects interactive vs automatic search setting)
        var shouldRedownload = download.IsManualSearch ? redownloadFailedFromInteractive : redownloadFailed;
        if (shouldRedownload && download.RetryCount < 3)
        {
            _logger.LogInformation("[Enhanced Download Monitor] Will retry download on next search cycle: {Title}", download.Title);
            // The automatic search service will pick this up
            download.Status = DownloadStatus.Failed; // Keep as failed but allow retry
        }
        else if (download.RetryCount >= 3)
        {
            _logger.LogWarning("[Enhanced Download Monitor] Max retries reached for: {Title}", download.Title);
            download.ErrorMessage = $"Max retries (3) reached. {download.ErrorMessage}";
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Detect completed downloads in download clients that were added externally (not through Sportarr).
    /// Creates PendingImport records so users can review and accept/reject them in the Activity page.
    /// </summary>
    private async Task DetectExternalDownloadsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var downloadClientService = scope.ServiceProvider.GetRequiredService<DownloadClientService>();
        var libraryImport = scope.ServiceProvider.GetRequiredService<LibraryImportService>();

        // Get all enabled download clients
        var clients = await db.DownloadClients
            .Where(c => c.Enabled)
            .ToListAsync(cancellationToken);

        if (clients.Count == 0) return;

        // Get all known download IDs to filter out:
        // 1. Active downloads in queue (Sportarr-initiated, currently downloading/importing)
        // CASE-INSENSITIVE comparer: qBittorrent/SABnzbd can return the torrent hash or nzb id
        // in a different case between the initial add response and later /info polls. Without
        // OrdinalIgnoreCase the HashSet would miss the match and Sportarr-grabbed downloads
        // would re-appear as "external" PendingImport rows.
        var knownDownloadIds = new HashSet<string>(
            await db.DownloadQueue.Select(d => d.DownloadId).ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        // 2. ALL pending imports (any status — prevents re-detection of completed/rejected imports)
        var pendingDownloadIds = new HashSet<string>(
            await db.PendingImports
                .Select(pi => pi.DownloadId)
                .ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        // 3. Grab history (Sportarr-initiated downloads that have been imported and removed from queue)
        var grabbedDownloadIds = new HashSet<string>(
            await db.GrabHistory
                .Where(g => g.DownloadId != null)
                .Select(g => g.DownloadId!)
                .Distinct()
                .ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        // Hash-based fallback dedup. Real-Debrid uncached downloads can return
        // a different DownloadId from Decypharr at grab-time vs poll-time (the
        // ID changes once RD finishes caching the torrent), so DownloadId alone
        // misses the duplicate. The torrent info hash stays stable.
        var knownHashes = new HashSet<string>(
            (await db.DownloadQueue
                .Where(d => d.TorrentInfoHash != null)
                .Select(d => d.TorrentInfoHash!)
                .Concat(db.PendingImports
                    .Where(pi => pi.TorrentInfoHash != null)
                    .Select(pi => pi.TorrentInfoHash!))
                .Concat(db.GrabHistory
                    .Where(g => g.TorrentInfoHash != null)
                    .Select(g => g.TorrentInfoHash!))
                .ToListAsync(cancellationToken))
            .Select(h => h.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        // Blocklist dedup. When the user clicks Remove on a
        // pending import, the row is hard-deleted and a Blocklist entry is
        // written. If the download client silently fails to actually delete
        // the download (SABnzbd's queue-delete returns success even for
        // history-only ids; some torrent clients keep completed torrents in
        // a history view), the next poll would otherwise re-detect it as a
        // brand-new external download and recreate the PendingImport row,
        // producing the infinite re-add loop the user reported.
        var blocklistedHashes = new HashSet<string>(
            (await db.Blocklist
                .Where(b => b.TorrentInfoHash != null)
                .Select(b => b.TorrentInfoHash!)
                .ToListAsync(cancellationToken))
            .Select(h => h.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
        var blocklistedTitles = new HashSet<string>(
            await db.Blocklist
                .Select(b => b.Title)
                .ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        foreach (var client in clients)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var allDownloads = await downloadClientService.GetAllDownloadsByCategoryAsync(client, client.Category);

                // Reconcile stale detections: an auto-detected external PendingImport
                // should disappear once its download is no longer present in the
                // client's category-filtered list (the item was deleted, or its
                // label/category was removed). Only reconcile on a clearly successful
                // poll — an empty list can also mean the client was unreachable, so a
                // non-empty response is required before purging, to avoid wiping the
                // Activity list on a transient outage.
                if (allDownloads.Count > 0)
                {
                    var currentIds = new HashSet<string>(
                        allDownloads.Select(d => d.DownloadId),
                        StringComparer.OrdinalIgnoreCase);
                    var currentHashes = new HashSet<string>(
                        allDownloads.Where(d => !string.IsNullOrEmpty(d.TorrentInfoHash))
                            .Select(d => d.TorrentInfoHash!.ToLowerInvariant()),
                        StringComparer.OrdinalIgnoreCase);

                    var clientPending = await db.PendingImports
                        .Where(pi => pi.DownloadClientId == client.Id && pi.Status == PendingImportStatus.Pending)
                        .ToListAsync(cancellationToken);

                    var stale = clientPending
                        .Where(pi => !currentIds.Contains(pi.DownloadId)
                                     && (string.IsNullOrEmpty(pi.TorrentInfoHash)
                                         || !currentHashes.Contains(pi.TorrentInfoHash.ToLowerInvariant())))
                        .ToList();

                    if (stale.Count > 0)
                    {
                        db.PendingImports.RemoveRange(stale);
                        await db.SaveChangesAsync(cancellationToken);
                        foreach (var pi in stale)
                            pendingDownloadIds.Remove(pi.DownloadId);
                        _logger.LogInformation(
                            "[Enhanced Download Monitor] Removed {Count} stale pending import(s) for '{Client}' no longer present with its category",
                            stale.Count, client.Name);
                    }
                }

                foreach (var download in allDownloads)
                {
                    // Skip downloads we already know about (queue, pending imports,
                    // or grab history). Match by DownloadId first, then by torrent
                    // hash as a fallback for Real-Debrid uncached downloads where
                    // Decypharr returns a different DownloadId at grab vs poll time.
                    if (knownDownloadIds.Contains(download.DownloadId))
                    {
                        _logger.LogDebug("[Enhanced Download Monitor] Skipping '{Title}' (id {Id}) — active in DownloadQueue",
                            download.Title, download.DownloadId);
                        continue;
                    }
                    if (pendingDownloadIds.Contains(download.DownloadId))
                    {
                        _logger.LogDebug("[Enhanced Download Monitor] Skipping '{Title}' (id {Id}) — already a PendingImport awaiting user resolution",
                            download.Title, download.DownloadId);
                        continue;
                    }
                    if (grabbedDownloadIds.Contains(download.DownloadId))
                    {
                        _logger.LogDebug("[Enhanced Download Monitor] Skipping '{Title}' (id {Id}) — Sportarr-grabbed (in GrabHistory)",
                            download.Title, download.DownloadId);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(download.TorrentInfoHash) &&
                        knownHashes.Contains(download.TorrentInfoHash))
                    {
                        _logger.LogDebug(
                            "[Enhanced Download Monitor] Skipping '{Title}' — hash {Hash} already tracked under a different DownloadId (Real-Debrid id mutation case)",
                            download.Title, download.TorrentInfoHash);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(download.TorrentInfoHash) &&
                        blocklistedHashes.Contains(download.TorrentInfoHash))
                    {
                        _logger.LogDebug(
                            "[Enhanced Download Monitor] Skipping '{Title}' — hash {Hash} is blocklisted (user previously rejected)",
                            download.Title, download.TorrentInfoHash);
                        continue;
                    }
                    if (blocklistedTitles.Contains(download.Title))
                    {
                        _logger.LogDebug(
                            "[Enhanced Download Monitor] Skipping '{Title}' — title is blocklisted (user previously rejected)",
                            download.Title);
                        continue;
                    }

                    // Match with the real import engine when the completed
                    // files are reachable on disk - same engine and
                    // auto-import floor as the library rescan and the file
                    // watcher, so an externally-added finished job imports
                    // itself instead of requiring a manual library scan.
                    int? suggestedEventId = null;
                    int confidence = 0;
                    var imported = false;

                    // Only completed downloads get engine analysis: a
                    // still-downloading torrent's folder holds partial
                    // files that must never be imported.
                    string? scanFolder = null;
                    if (download.IsCompleted && !string.IsNullOrEmpty(download.FilePath))
                    {
                        if (Directory.Exists(download.FilePath))
                            scanFolder = download.FilePath;
                        else if (File.Exists(download.FilePath))
                            scanFolder = Path.GetDirectoryName(download.FilePath);
                    }

                    if (scanFolder != null)
                    {
                        try
                        {
                            var scan = await libraryImport.ScanFolderAsync(scanFolder, includeSubfolders: true);
                            var best = scan.MatchedFiles
                                .OrderByDescending(f => f.MatchConfidence ?? 0)
                                .FirstOrDefault();
                            if (best != null)
                            {
                                suggestedEventId = best.MatchedEventId;
                                confidence = best.MatchConfidence ?? 0;

                                if (best.MatchedEventId.HasValue &&
                                    confidence >= LibraryImportService.AutoImportConfidenceFloor &&
                                    best.ExistingEventId == null)
                                {
                                    var importResult = await libraryImport.ImportFilesAsync(new List<FileImportRequest>
                                    {
                                        new()
                                        {
                                            FilePath = best.FilePath,
                                            EventId = best.MatchedEventId,
                                            Quality = best.Quality
                                        }
                                    });
                                    imported = importResult.Imported.Count + importResult.Created.Count > 0;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex,
                                "[Enhanced Download Monitor] Match-engine analysis failed for '{Title}', falling back to title suggestion",
                                download.Title);
                        }
                    }

                    // Fallback suggestion when the files aren't reachable
                    // from this process (remote download client, unmapped
                    // path): a coarse title search so the pending row at
                    // least carries a candidate.
                    if (!imported && suggestedEventId == null)
                    {
                        var cleanTitle = CleanDownloadTitle(download.Title);
                        if (!string.IsNullOrEmpty(cleanTitle))
                        {
                            var pattern = $"%{cleanTitle}%";
                            var matchedEvent = await db.Events
                                .Where(e => !e.HasFile)
                                .Where(e => EF.Functions.Like(e.Title, pattern) ||
                                           e.Title != null && cleanTitle.Contains(e.Title))
                                .FirstOrDefaultAsync(cancellationToken);

                            if (matchedEvent != null)
                            {
                                suggestedEventId = matchedEvent.Id;
                                confidence = 50; // Basic title match
                            }
                        }
                    }

                    // Record the detection either way: a Completed row is the
                    // audit trail for the auto-import AND what suppresses
                    // re-detection of this download id on the next poll.
                    var pendingImport = new PendingImport
                    {
                        DownloadClientId = client.Id,
                        DownloadId = download.DownloadId,
                        Title = download.Title,
                        FilePath = download.FilePath,
                        Size = download.Size,
                        Protocol = download.Protocol,
                        TorrentInfoHash = download.TorrentInfoHash,
                        SuggestedEventId = suggestedEventId,
                        SuggestionConfidence = confidence,
                        Detected = DateTime.UtcNow,
                        Status = imported ? PendingImportStatus.Completed : PendingImportStatus.Pending
                    };

                    db.PendingImports.Add(pendingImport);
                    pendingDownloadIds.Add(download.DownloadId); // Prevent duplicates within this scan
                    if (!string.IsNullOrEmpty(download.TorrentInfoHash))
                        knownHashes.Add(download.TorrentInfoHash);

                    if (imported)
                    {
                        _logger.LogInformation(
                            "[Enhanced Download Monitor] Auto-imported external completed download: {Title} (Client: {Client}, Confidence: {Confidence}%, Event: {EventId})",
                            download.Title, client.Name, confidence, suggestedEventId);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[Enhanced Download Monitor] Detected external download: {Title} (Client: {Client}, Id: {Id}, Hash: {Hash}, Confidence: {Confidence}%) — no match in DownloadQueue, PendingImports, GrabHistory, knownHashes, or Blocklist",
                            download.Title, client.Name, download.DownloadId,
                            download.TorrentInfoHash ?? "(none)", confidence);

                        try
                        {
                            using var notifyScope = _serviceProvider.CreateScope();
                            var notificationService = notifyScope.ServiceProvider.GetRequiredService<NotificationService>();
                            await notificationService.SendNotificationAsync(
                                NotificationTrigger.OnManualInteractionRequired,
                                $"Manual import required: {download.Title}",
                                $"An external download finished in {client.Name} and could not be matched automatically (confidence {confidence}%). Review it under Activity.",
                                new Dictionary<string, object>
                                {
                                    { "downloadTitle", download.Title },
                                    { "client", client.Name },
                                    { "confidence", confidence },
                                });
                        }
                        catch (Exception notifyEx)
                        {
                            _logger.LogWarning(notifyEx, "[Enhanced Download Monitor] Failed to send pending-import notification");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[Enhanced Download Monitor] Error checking external downloads for client: {Client}", client.Name);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Clean a download title for basic matching by removing quality tags, dots, etc.
    /// </summary>
    private static string CleanDownloadTitle(string title)
    {
        // Remove common quality/source tags
        var cleaned = System.Text.RegularExpressions.Regex.Replace(title,
            @"[\.\-_](1080p|720p|2160p|4K|WEB-DL|WEBRip|BluRay|HDTV|x264|x265|HEVC|AAC|DDP?\d?\.\d|AMZN|NF|HULU).*$",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Replace dots and underscores with spaces
        cleaned = cleaned.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');

        return cleaned.Trim();
    }
}
