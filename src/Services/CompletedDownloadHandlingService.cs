using Fightarr.Api.Data;
using Fightarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Fightarr.Api.Services;

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
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();
        var downloadClientService = scope.ServiceProvider.GetRequiredService<DownloadClientService>();
        var importService = scope.ServiceProvider.GetRequiredService<ImportService>();

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
                await ProcessDownloadAsync(db, downloadClientService, importService, download);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Completed Download Handler] Error processing download {DownloadId}", download.DownloadId);
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task ProcessDownloadAsync(
        FightarrDbContext db,
        DownloadClientService downloadClientService,
        ImportService importService,
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
        // Status is a string from DownloadClientStatus, convert to enum
        download.Status = status.Status.ToLower() switch
        {
            "downloading" => DownloadStatus.Downloading,
            "queued" => DownloadStatus.Queued,
            "completed" => DownloadStatus.Completed,
            "failed" => DownloadStatus.Failed,
            _ => DownloadStatus.Downloading
        };

        _logger.LogDebug("[Completed Download Handler] Download {Title}: {Progress}% ({Status})",
            download.Title, status.Progress, status.Status);

        // If completed, process for import
        if (status.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(status.SavePath))
        {
            _logger.LogInformation("[Completed Download Handler] Download completed: {Title} at {Path}",
                download.Title, status.SavePath);

            // Import the completed download
            var importResult = await importService.ImportCompletedDownloadAsync(
                download.EventId,
                status.SavePath,
                downloadClient.Host);

            if (importResult.Success)
            {
                download.Status = DownloadStatus.Imported;
                _logger.LogInformation("[Completed Download Handler] Successfully imported: {Title}", download.Title);

                // Optionally remove from download client if seeding is complete
                // This is handled by the download client's "Remove Completed Downloads" setting
            }
            else
            {
                download.Status = DownloadStatus.Failed;
                _logger.LogError("[Completed Download Handler] Import failed for {Title}: {Message}",
                    download.Title, importResult.Message);
            }
        }
        else if (status.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("[Completed Download Handler] Download failed: {Title}", download.Title);
            download.Status = DownloadStatus.Failed;
        }
    }
}
