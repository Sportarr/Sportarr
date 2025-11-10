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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Enhanced Download Monitor] Error processing download: {Title}", download.Title);

                // Mark as failed but allow retry
                download.Status = DownloadStatus.Failed;
                download.ErrorMessage = ex.Message;
                download.RetryCount = (download.RetryCount ?? 0) + 1;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
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
            // Download not found in client - might have been removed externally
            _logger.LogWarning("[Enhanced Download Monitor] Download not found in client: {DownloadId}", download.DownloadId);

            // Check if it's been missing for too long (orphaned)
            if (download.LastUpdate.HasValue && DateTime.UtcNow - download.LastUpdate.Value > TimeSpan.FromHours(1))
            {
                download.Status = DownloadStatus.Failed;
                download.ErrorMessage = "Download removed from client or orphaned";
            }
            return;
        }

        // Update download metadata
        var previousStatus = download.Status;
        var previousProgress = download.Progress;

        download.Progress = status.Progress;
        download.Downloaded = status.Downloaded;
        download.Size = status.Size;
        download.TimeRemaining = status.TimeRemaining;
        download.LastUpdate = DateTime.UtcNow;

        // Update status based on client response
        download.Status = status.Status switch
        {
            "downloading" => DownloadStatus.Downloading,
            "paused" => DownloadStatus.Paused,
            "completed" => DownloadStatus.Completed,
            "failed" or "error" => DownloadStatus.Failed,
            "queued" or "waiting" => DownloadStatus.Queued,
            "warning" => DownloadStatus.Warning,
            _ => download.Status
        };

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

        // Detect stalled downloads
        if (download.Status == DownloadStatus.Downloading)
        {
            CheckForStalledDownload(download, previousProgress, db);
        }

        // Handle completed downloads
        if (download.Status == DownloadStatus.Completed &&
            previousStatus != DownloadStatus.Completed &&
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
            if (removeCompleted && download.DownloadClient != null)
            {
                try
                {
                    await downloadClientService.RemoveDownloadAsync(
                        download.DownloadClient,
                        download.DownloadId,
                        deleteFiles: false); // Files already moved/hardlinked

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
            _logger.LogError(ex, "[Enhanced Download Monitor] ✗ Import failed: {Title}", download.Title);

            download.Status = DownloadStatus.Failed;
            download.ErrorMessage = $"Import failed: {ex.Message}";
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
        if (!string.IsNullOrEmpty(download.TorrentInfoHash))
        {
            var existingBlock = await db.Blocklist
                .FirstOrDefaultAsync(b => b.TorrentInfoHash == download.TorrentInfoHash);

            if (existingBlock == null)
            {
                var blocklistItem = new BlocklistItem
                {
                    EventId = download.EventId,
                    Title = download.Title,
                    TorrentInfoHash = download.TorrentInfoHash,
                    Indexer = download.Indexer ?? "Unknown",
                    Reason = BlocklistReason.FailedDownload,
                    Message = download.ErrorMessage ?? "Download failed",
                    BlockedAt = DateTime.UtcNow
                };

                db.Blocklist.Add(blocklistItem);
                _logger.LogInformation("[Enhanced Download Monitor] Added to blocklist: {Hash}", download.TorrentInfoHash);
            }
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
