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
        var removeCompleted = config.RemoveCompletedDownloads;
        var enableFailedHandling = config.EnableFailedDownloadHandling;
        var redownloadFailed = config.RedownloadFailedDownloads;
        var removeFailedDownloads = config.RemoveFailedDownloads;

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
                    removeCompleted,
                    enableFailedHandling,
                    redownloadFailed,
                    removeFailedDownloads);

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
        bool removeCompleted,
        bool enableFailedHandling,
        bool redownloadFailed,
        bool removeFailedDownloads)
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
                removeCompleted);
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
            _logger.LogDebug("[Enhanced Download Monitor] Download not found by ID {DownloadId}, trying title match for: {Title}",
                download.DownloadId, download.Title);

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

                // Track consecutive "not found" checks to avoid removing on transient issues
                download.MissingFromClientCount = (download.MissingFromClientCount ?? 0) + 1;

                if (download.MissingFromClientCount >= 3)
                {
                    // After 3 consecutive checks (~15 seconds), remove from queue
                    // This matches Sonarr behavior: downloads removed from client are removed from queue
                    _logger.LogInformation("[Enhanced Download Monitor] Download removed from client externally, removing from queue: {Title}",
                        download.Title);

                    // Remove from queue (Sonarr-style auto-cleanup)
                    db.DownloadQueue.Remove(download);
                    await db.SaveChangesAsync();
                    return;
                }
                else
                {
                    // First few "not found" - could be transient, log at debug level
                    _logger.LogDebug("[Enhanced Download Monitor] Download not found in client (check {Count}/3): {Title}",
                        download.MissingFromClientCount, download.Title);
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
            if (download.Status != DownloadStatus.Imported &&
                download.Status != DownloadStatus.Failed)
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
                removeCompleted);
        }

        // Handle failed downloads
        if (download.Status == DownloadStatus.Failed &&
            previousStatus != DownloadStatus.Failed &&
            enableFailedHandling)
        {
            await HandleFailedDownload(
                download,
                downloadClientService,
                db,
                redownloadFailed,
                removeFailedDownloads);
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

    private async Task HandleCompletedDownload(
        DownloadQueueItem download,
        DownloadClientService downloadClientService,
        FileImportService fileImportService,
        bool removeCompleted)
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

            // Remove from download client if configured
            // Pass deleteFiles: true to also remove the download folder from disk (matches Sonarr/Radarr behavior)
            // The video files have already been moved/hardlinked to the library, but non-video files (nfo, srr, etc.)
            // and the folder itself may remain - the download client should clean these up
            if (removeCompleted && download.DownloadClient != null)
            {
                try
                {
                    await downloadClientService.RemoveDownloadAsync(
                        download.DownloadClient,
                        download.DownloadId,
                        deleteFiles: true); // Delete files/folder from download client (Sonarr behavior)

                    _logger.LogDebug("[Enhanced Download Monitor] Removed completed download from client: {Title}", download.Title);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Enhanced Download Monitor] Failed to remove download from client: {Title}", download.Title);
                    // Don't fail the import if we can't remove from client
                }
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
        bool removeFailedDownloads)
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

        // Remove from download client if configured
        if (removeFailedDownloads && download.DownloadClient != null)
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

        // Retry if enabled and under retry limit
        if (redownloadFailed && download.RetryCount < 3)
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
}
