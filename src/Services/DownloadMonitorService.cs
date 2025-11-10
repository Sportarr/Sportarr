using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service that monitors download clients for completed downloads
/// and triggers import process
/// </summary>
public class DownloadMonitorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DownloadMonitorService> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

    public DownloadMonitorService(
        IServiceProvider serviceProvider,
        ILogger<DownloadMonitorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Download Monitor] Service started");

        // Wait a bit before starting to allow app to fully initialize
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorDownloadsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Download Monitor] Error monitoring downloads");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }

        _logger.LogInformation("[Download Monitor] Service stopped");
    }

    private async Task MonitorDownloadsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var downloadClientService = scope.ServiceProvider.GetRequiredService<DownloadClientService>();
        var fileImportService = scope.ServiceProvider.GetRequiredService<FileImportService>();

        // Get all active downloads (not completed, not imported, not failed)
        var activeDownloads = await db.DownloadQueue
            .Include(d => d.DownloadClient)
            .Include(d => d.Event)
            .Where(d => d.Status != DownloadStatus.Completed &&
                       d.Status != DownloadStatus.Imported &&
                       d.Status != DownloadStatus.Failed)
            .ToListAsync(cancellationToken);

        if (activeDownloads.Count == 0)
            return;

        _logger.LogDebug("[Download Monitor] Checking {Count} active downloads", activeDownloads.Count);

        foreach (var download in activeDownloads)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await ProcessDownloadAsync(download, downloadClientService, fileImportService, db);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Download Monitor] Error processing download: {Title}", download.Title);

                // Mark as failed
                download.Status = DownloadStatus.Failed;
                download.ErrorMessage = ex.Message;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessDownloadAsync(
        DownloadQueueItem download,
        DownloadClientService downloadClientService,
        FileImportService fileImportService,
        SportarrDbContext db)
    {
        if (download.DownloadClient == null)
        {
            _logger.LogWarning("[Download Monitor] Download {Title} has no download client", download.Title);
            return;
        }

        // Query download client for status
        var status = await downloadClientService.GetDownloadStatusAsync(
            download.DownloadClient,
            download.DownloadId);

        if (status == null)
        {
            _logger.LogWarning("[Download Monitor] Could not get status for download {DownloadId}", download.DownloadId);
            return;
        }

        // Update download info
        download.Progress = status.Progress;
        download.Downloaded = status.Downloaded;
        download.Size = status.Size;
        download.TimeRemaining = status.TimeRemaining;

        // Update status based on client status
        var previousStatus = download.Status;

        download.Status = status.Status switch
        {
            "downloading" => DownloadStatus.Downloading,
            "paused" => DownloadStatus.Paused,
            "completed" => DownloadStatus.Completed,
            "failed" or "error" => DownloadStatus.Failed,
            "queued" or "waiting" => DownloadStatus.Queued,
            _ => download.Status
        };

        if (status.ErrorMessage != null)
        {
            download.ErrorMessage = status.ErrorMessage;
        }

        // Log status changes
        if (previousStatus != download.Status)
        {
            _logger.LogInformation("[Download Monitor] Download '{Title}' status changed: {OldStatus} -> {NewStatus}",
                download.Title, previousStatus, download.Status);
        }

        // If completed, trigger import
        if (download.Status == DownloadStatus.Completed && previousStatus != DownloadStatus.Completed)
        {
            download.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation("[Download Monitor] Download completed, starting import: {Title}", download.Title);

            try
            {
                await fileImportService.ImportDownloadAsync(download);

                _logger.LogInformation("[Download Monitor] Import successful: {Title}", download.Title);

                // Optionally remove from download client
                var settings = await db.MediaManagementSettings.FirstOrDefaultAsync();
                if (settings?.RemoveCompletedDownloads == true)
                {
                    await downloadClientService.RemoveDownloadAsync(
                        download.DownloadClient,
                        download.DownloadId,
                        deleteFiles: false); // Files already imported
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Download Monitor] Import failed: {Title}", download.Title);
                download.Status = DownloadStatus.Failed;
                download.ErrorMessage = $"Import failed: {ex.Message}";
            }
        }
    }
}

/// <summary>
/// Download status returned from download client
/// </summary>
public class DownloadClientStatus
{
    public required string Status { get; set; }
    public double Progress { get; set; }
    public long Downloaded { get; set; }
    public long Size { get; set; }
    public TimeSpan? TimeRemaining { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SavePath { get; set; }
}
