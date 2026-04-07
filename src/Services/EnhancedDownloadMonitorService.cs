using Sportarr.Api.Data;
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

        // Get all active downloads (not completed, not imported, not failed permanently)
        var activeDownloads = await db.DownloadQueue
            .Include(d => d.DownloadClient)
            .Include(d => d.Event)
            .Where(d => d.Status != DownloadStatus.Imported &&
                       (d.Status != DownloadStatus.Failed || d.RetryCount < 3)) // Allow retries
            .ToListAsync(cancellationToken);

        if (activeDownloads.Count == 0)
            return;

        _logger.LogDebug("[Enhanced Download Monitor] Checking {Count} active downloads", activeDownloads.Count);

        // Load settings once
        var config = await configService.GetConfigAsync();
        var enableCompletedHandling = config.EnableCompletedDownloadHandling;
        var redownloadFailed = config.RedownloadFailedDownloads;
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
        CancellationToken cancellationToken)
    {
        // For ImportPending downloads, skip the download client check and just retry import
        // The download already completed on the client, we're just waiting for the file to be accessible
        if (download.Status == DownloadStatus.ImportPending && enableCompletedHandling)
        {
            _logger.LogDebug("[Enhanced Download Monitor] Retrying import for pending download: {Title} (attempt {Count})",
                download.Title, (download.ImportRetryCount ?? 0) + 1);

            await HandleCompletedDownload(
                download,
                downloadClientService,
                fileImportService,
                db);
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
                // Download not found in client - Sonarr behavior: auto-remove from queue
                // This happens when user deletes from download client directly instead of through Sportarr
                // Sonarr removes the queue item immediately when the download disappears from the client

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
                    // After 10 consecutive checks (e.g. ~5 minutes at 30s poll interval), remove from queue
                    // This matches Sonarr behavior: downloads removed from client are removed from queue
                    _logger.LogWarning("[Enhanced Download Monitor] Download not found in client for {Count} consecutive checks, removing from queue: {Title} (DownloadId: {DownloadId})",
                        download.MissingFromClientCount, download.Title, download.DownloadId);

                    // Remove from queue (Sonarr-style auto-cleanup)
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

        // Check if event is no longer monitored (Sonarr-style warning)
        // This applies when user unmonitors an event/league/season while download is in progress
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
            CheckForStalledDownload(download, previousProgress, db);
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

    private void CheckForStalledDownload(
        DownloadQueueItem download,
        double previousProgress,
        SportarrDbContext db)
    {
        // If progress hasn't changed and we've been downloading for a while
        if (Math.Abs(download.Progress - previousProgress) < 0.1 && download.Added < DateTime.UtcNow - _stalledTimeout)
        {
            // Check if this is the first time we've detected stalled state
            if (!download.ErrorMessage?.Contains("stalled") == true)
            {
                _logger.LogWarning("[Enhanced Download Monitor] Download appears stalled: {Title} (Progress: {Progress:F1}%)",
                    download.Title, download.Progress);

                download.Status = DownloadStatus.Warning;
                download.ErrorMessage = $"Download stalled at {download.Progress:F1}% for {_stalledTimeout.TotalMinutes} minutes";
            }
        }
    }

    /// <summary>
    /// Check if a torrent has reached its seed limits (ratio and/or time) from the indexer settings.
    /// Returns true if all configured limits are met, or if no limits are configured.
    /// Matches Sonarr's HasReachedSeedLimit behavior.
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
        download.CompletedAt = DateTime.UtcNow;

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
                // For torrents with indexer seed settings, check if seeding goals are met before removal
                // This matches Sonarr's behavior: torrents seed until ratio/time limits are reached
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

                    _logger.LogDebug("[Enhanced Download Monitor] Removed completed download from client: {Title}", download.Title);
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
                _logger.LogDebug("[Enhanced Download Monitor] Skipped removal from download client: No download client associated with {Title}",
                    download.Title);
            }
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
                // For other import errors, treat as failed after 3 attempts
                _logger.LogError(ex, "[Enhanced Download Monitor] ✗ Import failed (attempt {Count}/3): {Title}",
                    download.ImportRetryCount, download.Title);

                if (download.ImportRetryCount >= 3)
                {
                    download.Status = DownloadStatus.Failed;
                    download.ErrorMessage = $"Import failed after 3 attempts: {ex.Message}";
                }
                else
                {
                    download.Status = DownloadStatus.ImportPending;
                    download.ErrorMessage = $"Import failed (attempt {download.ImportRetryCount}/3): {ex.Message}";
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
            // For Usenet, match by title and indexer
            existingBlock = await db.Blocklist
                .FirstOrDefaultAsync(b => b.Title == download.Title &&
                                         b.Indexer == (download.Indexer ?? "Unknown") &&
                                         b.Protocol == "Usenet");
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

        // Remove from download client if configured in the client's settings
        if (download.DownloadClient?.RemoveFailedDownloads == true)
        {
            try
            {
                await downloadClientService.RemoveDownloadAsync(
                    download.DownloadClient,
                    download.DownloadId,
                    deleteFiles: true); // Clean up failed download files

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

        // Get all enabled download clients
        var clients = await db.DownloadClients
            .Where(c => c.Enabled)
            .ToListAsync(cancellationToken);

        if (clients.Count == 0) return;

        // Get all known download IDs to filter out:
        // 1. Active downloads in queue (Sportarr-initiated, currently downloading/importing)
        var knownDownloadIds = new HashSet<string>(
            await db.DownloadQueue.Select(d => d.DownloadId).ToListAsync(cancellationToken));

        // 2. ALL pending imports (any status — prevents re-detection of completed/rejected imports)
        var pendingDownloadIds = new HashSet<string>(
            await db.PendingImports
                .Select(pi => pi.DownloadId)
                .ToListAsync(cancellationToken));

        // 3. Grab history (Sportarr-initiated downloads that have been imported and removed from queue)
        var grabbedDownloadIds = new HashSet<string>(
            await db.GrabHistory
                .Where(g => g.DownloadId != null)
                .Select(g => g.DownloadId!)
                .Distinct()
                .ToListAsync(cancellationToken));

        foreach (var client in clients)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var allDownloads = await downloadClientService.GetAllDownloadsByCategoryAsync(client, client.Category);

                foreach (var download in allDownloads)
                {
                    // Skip downloads we already know about (queue, pending imports, or grab history)
                    if (knownDownloadIds.Contains(download.DownloadId) ||
                        pendingDownloadIds.Contains(download.DownloadId) ||
                        grabbedDownloadIds.Contains(download.DownloadId))
                        continue;

                    // Try to match to an event by title
                    int? suggestedEventId = null;
                    int confidence = 0;

                    // Simple title matching: search for events whose title contains key words from download title
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

                    // Create pending import
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
                        Status = PendingImportStatus.Pending
                    };

                    db.PendingImports.Add(pendingImport);
                    pendingDownloadIds.Add(download.DownloadId); // Prevent duplicates within this scan

                    _logger.LogInformation(
                        "[Enhanced Download Monitor] Detected external download: {Title} (Client: {Client}, Confidence: {Confidence}%)",
                        download.Title, client.Name, confidence);
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
