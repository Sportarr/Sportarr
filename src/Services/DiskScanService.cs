using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service to verify file existence and update event status
/// Similar to Sonarr's disk scan functionality
/// </summary>
public class DiskScanService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DiskScanService> _logger;
    private const int ScanIntervalMinutes = 60; // Scan every hour

    // Event to allow manual trigger of scan
    private readonly ManualResetEventSlim _scanTrigger = new(false);
    private static DiskScanService? _instance;

    public DiskScanService(
        IServiceProvider serviceProvider,
        ILogger<DiskScanService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _instance = this;
    }

    /// <summary>
    /// Trigger an immediate disk scan
    /// </summary>
    public static void TriggerScan()
    {
        _instance?._scanTrigger.Set();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Disk Scan Service started");

        // Wait 2 minutes before first scan to let the app fully start
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAllFilesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disk scan");
            }

            // Wait for next scan or manual trigger
            try
            {
                await Task.Run(() => _scanTrigger.Wait(TimeSpan.FromMinutes(ScanIntervalMinutes), stoppingToken), stoppingToken);
                _scanTrigger.Reset();
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Scan all event files and verify they exist on disk
    /// </summary>
    private async Task ScanAllFilesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        _logger.LogInformation("[Disk Scan] Starting disk scan...");

        var totalMissing = 0;
        var totalFound = 0;
        var totalVerified = 0;

        // First, scan Events table directly (for events that have FilePath set but no EventFiles records)
        var eventsWithFiles = await db.Events
            .Where(e => e.HasFile && !string.IsNullOrEmpty(e.FilePath))
            .ToListAsync(cancellationToken);

        _logger.LogInformation("[Disk Scan] Checking {Count} events with direct file paths...", eventsWithFiles.Count);

        foreach (var evt in eventsWithFiles)
        {
            if (!File.Exists(evt.FilePath))
            {
                _logger.LogWarning("[Disk Scan] Missing file for event '{Title}': {FilePath}", evt.Title, evt.FilePath);
                evt.HasFile = false;
                evt.FilePath = null;
                evt.FileSize = null;
                evt.Quality = null;
                totalMissing++;
            }
            else
            {
                totalVerified++;
            }
        }

        // Then scan EventFiles table
        var eventFiles = await db.EventFiles
            .Include(ef => ef.Event)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("[Disk Scan] Checking {Count} event file records...", eventFiles.Count);

        foreach (var file in eventFiles)
        {
            var exists = File.Exists(file.FilePath);
            var previousExists = file.Exists;

            if (exists != previousExists)
            {
                file.Exists = exists;
                file.LastVerified = DateTime.UtcNow;

                if (exists)
                {
                    _logger.LogInformation("[Disk Scan] File found again: {Path} (Event: {EventTitle})",
                        file.FilePath, file.Event?.Title);
                    totalFound++;
                }
                else
                {
                    _logger.LogWarning("[Disk Scan] File missing: {Path} (Event: {EventTitle})",
                        file.FilePath, file.Event?.Title);
                    totalMissing++;
                }
            }
            else
            {
                // Just update verification timestamp
                file.LastVerified = DateTime.UtcNow;
                if (exists) totalVerified++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Update event HasFile status based on file existence
        await UpdateEventFileStatusAsync(db, cancellationToken);

        _logger.LogInformation("[Disk Scan] Complete. Verified: {Verified}, Missing: {Missing}, Found: {Found}",
            totalVerified, totalMissing, totalFound);
    }

    /// <summary>
    /// Update Event.HasFile based on whether any files exist
    /// </summary>
    private async Task UpdateEventFileStatusAsync(SportarrDbContext db, CancellationToken cancellationToken)
    {
        // Get all events that have files
        var eventsWithFiles = await db.Events
            .Include(e => e.Files)
            .Where(e => e.Files.Any())
            .ToListAsync(cancellationToken);

        var updatedCount = 0;

        foreach (var evt in eventsWithFiles)
        {
            // Event has file if ANY of its files exist on disk
            var hasAnyFiles = evt.Files.Any(f => f.Exists);
            var previousHasFile = evt.HasFile;

            if (hasAnyFiles != previousHasFile)
            {
                evt.HasFile = hasAnyFiles;

                if (!hasAnyFiles)
                {
                    // All files are missing - clear file path
                    evt.FilePath = null;
                    evt.FileSize = null;
                    evt.Quality = null;
                    _logger.LogWarning("Event {EventTitle} marked as missing - all files deleted", evt.Title);
                }
                else
                {
                    // Update to point to an existing file
                    var existingFile = evt.Files.FirstOrDefault(f => f.Exists);
                    if (existingFile != null)
                    {
                        evt.FilePath = existingFile.FilePath;
                        evt.FileSize = existingFile.Size;
                        evt.Quality = existingFile.Quality;
                        _logger.LogInformation("Event {EventTitle} file restored: {Path}", evt.Title, existingFile.FilePath);
                    }
                }

                updatedCount++;
            }
        }

        if (updatedCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Updated HasFile status for {Count} events", updatedCount);
        }
    }
}
