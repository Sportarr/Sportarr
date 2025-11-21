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

    public DiskScanService(
        IServiceProvider serviceProvider,
        ILogger<DiskScanService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Disk Scan Service started");

        // Wait 5 minutes before first scan to let the app fully start
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

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

            // Wait for next scan
            await Task.Delay(TimeSpan.FromMinutes(ScanIntervalMinutes), stoppingToken);
        }
    }

    /// <summary>
    /// Scan all event files and verify they exist on disk
    /// </summary>
    private async Task ScanAllFilesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        _logger.LogInformation("Starting disk scan...");

        var eventFiles = await db.EventFiles
            .Include(ef => ef.Event)
            .ToListAsync(cancellationToken);

        var changedCount = 0;
        var missingCount = 0;
        var foundCount = 0;

        foreach (var file in eventFiles)
        {
            var exists = File.Exists(file.FilePath);
            var previousExists = file.Exists;

            if (exists != previousExists)
            {
                file.Exists = exists;
                file.LastVerified = DateTime.UtcNow;
                changedCount++;

                if (exists)
                {
                    _logger.LogInformation("File found again: {Path} (Event: {EventTitle})",
                        file.FilePath, file.Event?.Title);
                    foundCount++;
                }
                else
                {
                    _logger.LogWarning("File missing: {Path} (Event: {EventTitle})",
                        file.FilePath, file.Event?.Title);
                    missingCount++;
                }
            }
            else
            {
                // Just update verification timestamp
                file.LastVerified = DateTime.UtcNow;
            }
        }

        if (changedCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Disk scan complete: {Changed} files changed status ({Found} found, {Missing} missing)",
                changedCount, foundCount, missingCount);

            // Update event HasFile status based on file existence
            await UpdateEventFileStatusAsync(db, cancellationToken);
        }
        else
        {
            // Still save to update LastVerified timestamps
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Disk scan complete: No file status changes detected. {Total} files verified.",
                eventFiles.Count);
        }
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
