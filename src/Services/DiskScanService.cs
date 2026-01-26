using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service to verify file existence and update event status
/// Similar to Sonarr's disk scan functionality
/// </summary>
public class DiskScanService : BackgroundService, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DiskScanService> _logger;
    private const int ScanIntervalMinutes = 60; // Scan every hour

    // Event to allow manual trigger of scan
    private readonly ManualResetEventSlim _scanTrigger = new(false);
    private bool _disposed = false;

    public DiskScanService(
        IServiceProvider serviceProvider,
        ILogger<DiskScanService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Trigger an immediate disk scan (instance method for DI)
    /// </summary>
    public void TriggerScanNow()
    {
        _scanTrigger.Set();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Disk Scan Service stopping...");
        await base.StopAsync(cancellationToken);
        DisposeResources();
    }

    public async ValueTask DisposeAsync()
    {
        DisposeResources();
        GC.SuppressFinalize(this);
        await Task.CompletedTask;
    }

    private void DisposeResources()
    {
        if (!_disposed)
        {
            _scanTrigger?.Dispose();
            _disposed = true;
        }
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
    /// Scan all event files and verify they exist on disk.
    /// Optimized to use AsNoTracking and batch updates for memory efficiency.
    /// </summary>
    private async Task ScanAllFilesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        _logger.LogInformation("[Disk Scan] Starting disk scan...");

        var totalMissing = 0;
        var totalFound = 0;
        var totalVerified = 0;

        // First, scan Events table directly using AsNoTracking and batch updates
        // Only select the fields we need to check file existence
        var eventsToCheck = await db.Events
            .AsNoTracking()
            .Where(e => e.HasFile && !string.IsNullOrEmpty(e.FilePath))
            .Select(e => new { e.Id, e.Title, e.FilePath })
            .ToListAsync(cancellationToken);

        _logger.LogInformation("[Disk Scan] Checking {Count} events with direct file paths...", eventsToCheck.Count);

        // Find missing files
        var missingEventIds = new List<int>();
        foreach (var evt in eventsToCheck)
        {
            if (!File.Exists(evt.FilePath))
            {
                _logger.LogWarning("[Disk Scan] Missing file for event '{Title}': {FilePath}", evt.Title, evt.FilePath);
                missingEventIds.Add(evt.Id);
                totalMissing++;
            }
            else
            {
                totalVerified++;
            }
        }

        // Batch update missing events using ExecuteUpdateAsync (no tracking needed)
        if (missingEventIds.Count > 0)
        {
            await db.Events
                .Where(e => missingEventIds.Contains(e.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.HasFile, false)
                    .SetProperty(e => e.FilePath, (string?)null)
                    .SetProperty(e => e.FileSize, (long?)null)
                    .SetProperty(e => e.Quality, (string?)null),
                    cancellationToken);
        }

        // Then scan EventFiles table using AsNoTracking
        var eventFilesToCheck = await db.EventFiles
            .AsNoTracking()
            .Select(ef => new { ef.Id, ef.FilePath, ef.Exists, EventTitle = ef.Event != null ? ef.Event.Title : null })
            .ToListAsync(cancellationToken);

        _logger.LogInformation("[Disk Scan] Checking {Count} event file records...", eventFilesToCheck.Count);

        var filesToMarkMissing = new List<int>();
        var filesToMarkFound = new List<int>();
        var now = DateTime.UtcNow;

        foreach (var file in eventFilesToCheck)
        {
            var exists = File.Exists(file.FilePath);
            var previousExists = file.Exists;

            if (exists != previousExists)
            {
                if (exists)
                {
                    _logger.LogInformation("[Disk Scan] File found again: {Path} (Event: {EventTitle})",
                        file.FilePath, file.EventTitle);
                    filesToMarkFound.Add(file.Id);
                    totalFound++;
                }
                else
                {
                    _logger.LogWarning("[Disk Scan] File missing: {Path} (Event: {EventTitle})",
                        file.FilePath, file.EventTitle);
                    filesToMarkMissing.Add(file.Id);
                    totalMissing++;
                }
            }
            else
            {
                if (exists) totalVerified++;
            }
        }

        // Batch update files that are now missing
        if (filesToMarkMissing.Count > 0)
        {
            await db.EventFiles
                .Where(ef => filesToMarkMissing.Contains(ef.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(ef => ef.Exists, false)
                    .SetProperty(ef => ef.LastVerified, now),
                    cancellationToken);
        }

        // Batch update files that are now found
        if (filesToMarkFound.Count > 0)
        {
            await db.EventFiles
                .Where(ef => filesToMarkFound.Contains(ef.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(ef => ef.Exists, true)
                    .SetProperty(ef => ef.LastVerified, now),
                    cancellationToken);
        }

        // Update LastVerified for all existing files (that weren't changed)
        var unchangedFileIds = eventFilesToCheck
            .Where(f => !filesToMarkMissing.Contains(f.Id) && !filesToMarkFound.Contains(f.Id))
            .Select(f => f.Id)
            .ToList();

        if (unchangedFileIds.Count > 0)
        {
            await db.EventFiles
                .Where(ef => unchangedFileIds.Contains(ef.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(ef => ef.LastVerified, now), cancellationToken);
        }

        // Update event HasFile status based on file existence
        await UpdateEventFileStatusAsync(db, cancellationToken);

        _logger.LogInformation("[Disk Scan] Complete. Verified: {Verified}, Missing: {Missing}, Found: {Found}",
            totalVerified, totalMissing, totalFound);
    }

    /// <summary>
    /// Update Event.HasFile based on whether any files exist.
    /// Optimized to use AsNoTracking queries and batch updates.
    /// </summary>
    private async Task UpdateEventFileStatusAsync(SportarrDbContext db, CancellationToken cancellationToken)
    {
        // Use AsNoTracking and group by EventId to determine file status
        var eventFileStatus = await db.EventFiles
            .AsNoTracking()
            .GroupBy(ef => ef.EventId)
            .Select(g => new
            {
                EventId = g.Key,
                HasAnyExisting = g.Any(f => f.Exists),
                FirstExistingFile = g.Where(f => f.Exists).Select(f => new { f.FilePath, f.Size, f.Quality }).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        // Get current event status (only needed fields)
        var eventIds = eventFileStatus.Select(e => e.EventId).ToList();
        var events = await db.Events
            .AsNoTracking()
            .Where(e => eventIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Title, e.HasFile })
            .ToListAsync(cancellationToken);

        var eventsToMarkMissing = new List<int>();
        var eventsToRestore = new List<(int Id, string FilePath, long Size, string Quality)>();
        var updatedCount = 0;

        foreach (var evt in events)
        {
            var fileStatus = eventFileStatus.FirstOrDefault(f => f.EventId == evt.Id);
            if (fileStatus == null) continue;

            var hasAnyFiles = fileStatus.HasAnyExisting;
            var previousHasFile = evt.HasFile;

            if (hasAnyFiles != previousHasFile)
            {
                if (!hasAnyFiles)
                {
                    // All files are missing - clear file path
                    eventsToMarkMissing.Add(evt.Id);
                    _logger.LogWarning("Event {EventTitle} marked as missing - all files deleted", evt.Title);
                }
                else if (fileStatus.FirstExistingFile != null)
                {
                    // Update to point to an existing file
                    eventsToRestore.Add((evt.Id, fileStatus.FirstExistingFile.FilePath,
                        fileStatus.FirstExistingFile.Size, fileStatus.FirstExistingFile.Quality ?? ""));
                    _logger.LogInformation("Event {EventTitle} file restored: {Path}", evt.Title, fileStatus.FirstExistingFile.FilePath);
                }

                updatedCount++;
            }
        }

        // Batch update events marked as missing
        if (eventsToMarkMissing.Count > 0)
        {
            await db.Events
                .Where(e => eventsToMarkMissing.Contains(e.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.HasFile, false)
                    .SetProperty(e => e.FilePath, (string?)null)
                    .SetProperty(e => e.FileSize, (long?)null)
                    .SetProperty(e => e.Quality, (string?)null),
                    cancellationToken);
        }

        // For restored events, we need individual updates since each has different file info
        foreach (var restore in eventsToRestore)
        {
            await db.Events
                .Where(e => e.Id == restore.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.HasFile, true)
                    .SetProperty(e => e.FilePath, restore.FilePath)
                    .SetProperty(e => e.FileSize, restore.Size)
                    .SetProperty(e => e.Quality, restore.Quality),
                    cancellationToken);
        }

        if (updatedCount > 0)
        {
            _logger.LogInformation("Updated HasFile status for {Count} events", updatedCount);
        }
    }
}
