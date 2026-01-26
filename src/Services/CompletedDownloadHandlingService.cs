using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service that monitors download clients for completed downloads
/// Implements Sonarr/Radarr-style Completed Download Handling
/// Polls download clients every 60 seconds to check for completed downloads
/// </summary>
public class CompletedDownloadHandlingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CompletedDownloadHandlingService> _logger;
    private const int POLL_INTERVAL_SECONDS = 60;

    public CompletedDownloadHandlingService(
        IServiceProvider serviceProvider,
        ILogger<CompletedDownloadHandlingService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Completed Download Handler] Service started");

        // Wait 30 seconds on startup before first check
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckCompletedDownloadsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Completed Download Handler] Error checking completed downloads");
            }

            // Wait before next check
            await Task.Delay(TimeSpan.FromSeconds(POLL_INTERVAL_SECONDS), stoppingToken);
        }

        _logger.LogInformation("[Completed Download Handler] Service stopped");
    }

    private async Task CheckCompletedDownloadsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var downloadClientService = scope.ServiceProvider.GetRequiredService<DownloadClientService>();
        var fileImportService = scope.ServiceProvider.GetRequiredService<FileImportService>();

        _logger.LogDebug("[Completed Download Handler] Checking for completed downloads");

        // Get all downloads that are currently downloading or queued
        var activeDownloads = await db.DownloadQueue
            .Where(d => d.Status == DownloadStatus.Downloading || d.Status == DownloadStatus.Queued)
            .ToListAsync();

        if (!activeDownloads.Any())
        {
            _logger.LogDebug("[Completed Download Handler] No active downloads to check");
            return;
        }

        _logger.LogInformation("[Completed Download Handler] Checking {Count} active downloads", activeDownloads.Count);

        foreach (var download in activeDownloads)
        {
            try
            {
                await ProcessDownloadAsync(db, downloadClientService, fileImportService, download);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Completed Download Handler] Error processing download {DownloadId}", download.DownloadId);
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task ProcessDownloadAsync(
        SportarrDbContext db,
        DownloadClientService downloadClientService,
        FileImportService fileImportService,
        DownloadQueueItem download)
    {
        // Get the download client for this download
        var downloadClient = await db.DownloadClients.FindAsync(download.DownloadClientId);
        if (downloadClient == null)
        {
            _logger.LogWarning("[Completed Download Handler] Download client {ClientId} not found for download {DownloadId}",
                download.DownloadClientId, download.DownloadId);
            return;
        }

        // Get status from download client
        var status = await downloadClientService.GetDownloadStatusAsync(downloadClient, download.DownloadId);
        if (status == null)
        {
            _logger.LogWarning("[Completed Download Handler] Could not get status for download {DownloadId} from {Client}",
                download.DownloadId, downloadClient.Name);
            return;
        }

        // Update progress in database
        download.Progress = status.Progress;
        download.Downloaded = status.Downloaded;
        download.LastUpdate = DateTime.UtcNow;

        // Status is a string from DownloadClientStatus, convert to enum
        download.Status = status.Status.ToLower() switch
        {
            "downloading" => DownloadStatus.Downloading,
            "queued" => DownloadStatus.Queued,
            "completed" => DownloadStatus.Completed,
            "failed" => DownloadStatus.Failed,
            "paused" => DownloadStatus.Paused,
            "warning" => DownloadStatus.Warning,
            _ => DownloadStatus.Downloading
        };

        // Track status messages (warnings, errors from download client)
        if (!string.IsNullOrEmpty(status.ErrorMessage))
        {
            if (!download.StatusMessages.Contains(status.ErrorMessage))
            {
                download.StatusMessages.Add(status.ErrorMessage);
            }
        }

        _logger.LogDebug("[Completed Download Handler] Download {Title}: {Progress}% ({Status})",
            download.Title, status.Progress, status.Status);

        // If completed, process for import
        if (status.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(status.SavePath))
        {
            _logger.LogInformation("[Completed Download Handler] Download completed: {Title} at {Path}",
                download.Title, status.SavePath);

            try
            {
                // Import the completed download using FileImportService
                // This uses the correct folder structure with episode numbers
                await fileImportService.ImportDownloadAsync(download, status.SavePath);

                // FileImportService already sets download.Status to Imported
                _logger.LogInformation("[Completed Download Handler] Successfully imported: {Title}", download.Title);

                // Move to post-import category if configured (Sonarr feature)
                // This allows users to separate active downloads from completed/seeding torrents
                if (!string.IsNullOrEmpty(downloadClient.PostImportCategory) &&
                    downloadClient.PostImportCategory != downloadClient.Category)
                {
                    try
                    {
                        var categoryChanged = await downloadClientService.ChangeCategoryAsync(
                            downloadClient, download.DownloadId, downloadClient.PostImportCategory);

                        if (categoryChanged)
                        {
                            _logger.LogInformation("[Completed Download Handler] Moved {Title} to post-import category: {Category}",
                                download.Title, downloadClient.PostImportCategory);
                        }
                        else
                        {
                            _logger.LogWarning("[Completed Download Handler] Failed to change category for {Title}",
                                download.Title);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Don't fail the import if category change fails (Sonarr behavior)
                        _logger.LogWarning(ex, "[Completed Download Handler] Failed to set post-import category for {Title}",
                            download.Title);
                    }
                }

                // Optionally remove from download client if seeding is complete
                // This is handled by the download client's "Remove Completed Downloads" setting
            }
            catch (Exception ex)
            {
                download.Status = DownloadStatus.Failed;
                _logger.LogError(ex, "[Completed Download Handler] Import failed for {Title}",
                    download.Title);
            }
        }
        else if (status.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("[Completed Download Handler] Download failed: {Title}", download.Title);
            download.Status = DownloadStatus.Failed;
        }
    }
}
