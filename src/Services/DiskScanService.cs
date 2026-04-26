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
                    _logger.LogDebug("[Disk Scan] File found again: {Path} (Event: {EventTitle})",
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

        // Clean up stale EventFile records:
        // 1. Remove records marked Exists=false (leftover from old upgrade logic that marked instead of removing)
        // 2. Remove duplicate records for the same event (keep only the newest Exists=true record per event+part)
        var staleRemoved = await db.EventFiles
            .Where(ef => !ef.Exists)
            .ExecuteDeleteAsync(cancellationToken);

        if (staleRemoved > 0)
        {
            _logger.LogInformation("[Disk Scan] Cleaned up {Count} stale EventFile records (Exists=false)", staleRemoved);
        }

        // Find events with duplicate Exists=true file records (same event, same part or both null)
        // Keep the newest record (highest Id) and remove the rest
        var duplicateFiles = await db.EventFiles
            .Where(ef => ef.Exists)
            .GroupBy(ef => new { ef.EventId, ef.PartNumber })
            .Where(g => g.Count() > 1)
            .Select(g => new { g.Key.EventId, g.Key.PartNumber, KeepId = g.Max(f => f.Id) })
            .ToListAsync(cancellationToken);

        if (duplicateFiles.Count > 0)
        {
            var keepIds = duplicateFiles.Select(d => d.KeepId).ToHashSet();
            var eventPartPairs = duplicateFiles.Select(d => new { d.EventId, d.PartNumber }).ToList();

            // Remove all but the newest record for each duplicate group
            foreach (var dup in duplicateFiles)
            {
                var dupsRemoved = await db.EventFiles
                    .Where(ef => ef.EventId == dup.EventId && ef.PartNumber == dup.PartNumber && ef.Exists && ef.Id != dup.KeepId)
                    .ExecuteDeleteAsync(cancellationToken);

                if (dupsRemoved > 0)
                {
                    _logger.LogInformation("[Disk Scan] Removed {Count} duplicate EventFile records for EventId={EventId} PartNumber={Part}",
                        dupsRemoved, dup.EventId, dup.PartNumber?.ToString() ?? "null");
                }
            }
        }

        // Update event HasFile status based on file existence
        await UpdateEventFileStatusAsync(db, cancellationToken);

        _logger.LogInformation("[Disk Scan] Complete. Verified: {Verified}, Missing: {Missing}, Found: {Found}",
            totalVerified, totalMissing, totalFound);

        // Discover new untracked files in root folders
        await DiscoverNewFilesAsync(db, cancellationToken);
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
                    _logger.LogDebug("Event {EventTitle} file restored: {Path}", evt.Title, fileStatus.FirstExistingFile.FilePath);
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

    /// <summary>
    /// Discover new untracked video files in root folders and create PendingImport records.
    /// Files are shown in the Activity page for user review before being linked to events.
    /// </summary>
    private async Task DiscoverNewFilesAsync(SportarrDbContext db, CancellationToken cancellationToken)
    {
        // Get root folders from media management settings
        var settings = await db.MediaManagementSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings?.RootFolders == null || settings.RootFolders.Count == 0)
        {
            _logger.LogWarning("[Disk Scan] No root folders configured — skipping file discovery. Configure root folders in Settings > Media Management.");
            return;
        }

        // Build set of all tracked file paths
        var trackedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var eventPaths = await db.Events
            .AsNoTracking()
            .Where(e => !string.IsNullOrEmpty(e.FilePath))
            .Select(e => e.FilePath!)
            .ToListAsync(cancellationToken);
        foreach (var p in eventPaths) trackedPaths.Add(p);

        var eventFilePaths = await db.EventFiles
            .AsNoTracking()
            .Select(ef => ef.FilePath)
            .ToListAsync(cancellationToken);
        foreach (var p in eventFilePaths) trackedPaths.Add(p);

        // Also exclude paths already in pending imports
        var pendingPaths = new HashSet<string>(
            await db.PendingImports
                .Where(pi => pi.Status == PendingImportStatus.Pending)
                .Select(pi => pi.FilePath)
                .ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        var videoExtensions = new HashSet<string>(SupportedExtensions.Video, StringComparer.OrdinalIgnoreCase);
        var discoveredCount = 0;

        foreach (var rootFolder in settings.RootFolders)
        {
            if (cancellationToken.IsCancellationRequested) break;

            if (!Directory.Exists(rootFolder.Path))
            {
                _logger.LogDebug("[Disk Scan] Root folder not accessible: {Path}", rootFolder.Path);
                continue;
            }

            try
            {
                var files = Directory.EnumerateFiles(rootFolder.Path, "*.*", SearchOption.AllDirectories)
                    .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

                foreach (var filePath in files)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    // Skip if already tracked or already pending
                    if (trackedPaths.Contains(filePath) || pendingPaths.Contains(filePath))
                        continue;

                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        var filename = Path.GetFileNameWithoutExtension(filePath);

                        // Try simple event matching by searching DB for title similarity
                        int? suggestedEventId = null;
                        int confidence = 0;

                        // Clean filename for matching
                        var cleanTitle = System.Text.RegularExpressions.Regex.Replace(filename,
                            @"[\.\-_](1080p|720p|2160p|4K|WEB-DL|WEBRip|BluRay|HDTV|x264|x265|HEVC|AAC|DDP?\d?\.\d).*$",
                            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        cleanTitle = cleanTitle.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Trim();

                        if (!string.IsNullOrEmpty(cleanTitle) && cleanTitle.Length > 3)
                        {
                            var pattern = $"%{cleanTitle}%";
                            var matchedEvent = await db.Events
                                .AsNoTracking()
                                .Where(e => !e.HasFile)
                                .Where(e => EF.Functions.Like(e.Title, pattern) ||
                                           e.Title != null && cleanTitle.Contains(e.Title))
                                .FirstOrDefaultAsync(cancellationToken);

                            if (matchedEvent != null)
                            {
                                suggestedEventId = matchedEvent.Id;
                                confidence = 50;
                            }
                        }

                        // Detect quality from filename
                        string? quality = null;
                        if (filename.Contains("2160p", StringComparison.OrdinalIgnoreCase) || filename.Contains("4K", StringComparison.OrdinalIgnoreCase))
                            quality = "2160p";
                        else if (filename.Contains("1080p", StringComparison.OrdinalIgnoreCase))
                            quality = "1080p";
                        else if (filename.Contains("720p", StringComparison.OrdinalIgnoreCase))
                            quality = "720p";
                        else if (filename.Contains("480p", StringComparison.OrdinalIgnoreCase))
                            quality = "480p";

                        var pendingImport = new PendingImport
                        {
                            DownloadClientId = null, // Sentinel: disk-discovered (no download client)
                            DownloadId = $"disk-{Guid.NewGuid():N}",
                            Title = fileInfo.Name,
                            FilePath = filePath,
                            Size = fileInfo.Length,
                            Quality = quality,
                            SuggestedEventId = suggestedEventId,
                            SuggestionConfidence = confidence,
                            Detected = DateTime.UtcNow,
                            Status = PendingImportStatus.Pending
                        };

                        db.PendingImports.Add(pendingImport);
                        pendingPaths.Add(filePath); // Prevent duplicates within this scan
                        discoveredCount++;

                        _logger.LogDebug("[Disk Scan] Discovered untracked file: {Path} (Confidence: {Confidence}%)",
                            filePath, confidence);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[Disk Scan] Error processing file: {Path}", filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Disk Scan] Error scanning root folder: {Path}", rootFolder.Path);
            }
        }

        if (discoveredCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[Disk Scan] Discovered {Count} new untracked files (available as pending imports in Activity)",
                discoveredCount);
        }
    }
}
