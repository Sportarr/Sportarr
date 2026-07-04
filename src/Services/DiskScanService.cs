using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service that verifies file existence and updates event status,
/// and discovers new files dropped into root folders outside the import flow.
/// </summary>
public class DiskScanService : BackgroundService, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DiskScanService> _logger;
    private const int ScanIntervalMinutes = 60; // Scan every hour

    // Semaphore used as an async-friendly trigger. Initialized to 0 so the
    // first WaitAsync inside ExecuteAsync blocks until either the interval
    // timeout elapses or TriggerScanNow() releases the semaphore. Was a
    // ManualResetEventSlim, but the only way to wait on that with a timeout
    // is the synchronous Wait(TimeSpan), which permanently consumes one of
    // the small handful of ThreadPool workers when wrapped in Task.Run.
    // A managed-dump capture caught it blocked there. SemaphoreSlim.WaitAsync
    // is purely cooperative — no worker thread parked.
    private readonly SemaphoreSlim _scanTrigger = new(0, 1);
    private bool _disposed = false;

    public DiskScanService(
        IServiceProvider serviceProvider,
        ILogger<DiskScanService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Trigger an immediate disk scan (instance method for DI). Idempotent —
    /// already-pending triggers are swallowed so the caller never has to care
    /// whether a previous trigger has been observed yet.
    /// </summary>
    public void TriggerScanNow()
    {
        try { _scanTrigger.Release(); }
        catch (SemaphoreFullException) { /* already pending - nothing to do */ }
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

            // Wait for next scan or manual trigger. WaitAsync returns true when
            // TriggerScanNow released the semaphore (manual trigger), false when
            // the timeout elapses (regular cadence). Either is fine — we just
            // loop back to scan. OperationCanceledException = host shutdown.
            try
            {
                await _scanTrigger.WaitAsync(TimeSpan.FromMinutes(ScanIntervalMinutes), stoppingToken);
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
    ///
    /// Missing-file handling is grace-period based: when a path's File.Exists()
    /// flips from true to false the row is marked Exists=false and stamped
    /// MissingSince=now. The hard-delete only happens after the file has been
    /// continuously missing for Config.EventFileMissingDeleteAfterDays. Files
    /// living under root folders that aren't currently reachable (mount not
    /// attached, NAS down, restored backup whose paths haven't been remapped
    /// yet) are skipped entirely so a transient outage can't trigger the
    /// missing-transition for a thousand files at once.
    /// </summary>
    private async Task ScanAllFilesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();

        _logger.LogInformation("[Disk Scan] Starting disk scan...");

        // Identify unreachable root folders up front. Files under any of them
        // are excluded from the existence check — we don't know whether they're
        // really missing or the mount is just down, so we leave their state
        // alone instead of marking them missing. This is the equivalent of
        // Sonarr's "skip cleanup when the series folder is missing" guard,
        // applied at the root-folder level.
        var unreachableRoots = (await db.RootFolders.ToListAsync(cancellationToken))
            .Where(rf => !string.IsNullOrEmpty(rf.Path) && !Directory.Exists(rf.Path))
            .Select(rf => rf.Path)
            .ToList();
        var unreachableRootsLookup = unreachableRoots
            .Select(p => p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToList();
        if (unreachableRoots.Count > 0)
        {
            _logger.LogWarning(
                "[Disk Scan] Skipping miss-detection for files under {Count} unreachable root folder(s): {Roots}",
                unreachableRoots.Count, string.Join(", ", unreachableRoots));
        }

        bool IsUnderUnreachableRoot(string? path)
        {
            if (string.IsNullOrEmpty(path) || unreachableRootsLookup.Count == 0) return false;
            return unreachableRootsLookup.Any(prefix =>
                path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        var totalMissing = 0;
        var totalFound = 0;
        var totalVerified = 0;
        var totalSkippedUnreachable = 0;

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
            if (IsUnderUnreachableRoot(evt.FilePath))
            {
                totalSkippedUnreachable++;
                continue;
            }
            // A path inside the recycle bin (or any excluded folder) is treated as missing,
            // not "present". This self-heals records the watcher previously re-pointed into
            // the recycle bin, which would otherwise pass File.Exists and never be flagged.
            if (LibraryPathFilter.IsExcluded(evt.FilePath, config.RecycleBin) || !File.Exists(evt.FilePath))
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

        // Batch update missing events. Only flips HasFile=false; FilePath /
        // FileSize / Quality stay so the next scan can re-verify when the
        // path becomes reachable again. Cleanup of these fields happens
        // separately, after the grace period elapses, gated on EventFiles'
        // MissingSince column.
        if (missingEventIds.Count > 0)
        {
            await db.Events
                .Where(e => missingEventIds.Contains(e.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.HasFile, false),
                    cancellationToken);
        }

        // Then scan EventFiles table using AsNoTracking
        var eventFilesToCheck = await db.EventFiles
            .AsNoTracking()
            .Select(ef => new { ef.Id, ef.FilePath, ef.Exists, ef.MissingSince, EventTitle = ef.Event != null ? ef.Event.Title : null })
            .ToListAsync(cancellationToken);

        _logger.LogInformation("[Disk Scan] Checking {Count} event file records...", eventFilesToCheck.Count);

        var filesToMarkMissing = new List<int>();
        var filesToMarkFound = new List<int>();
        var now = DateTime.UtcNow;

        foreach (var file in eventFilesToCheck)
        {
            if (IsUnderUnreachableRoot(file.FilePath))
            {
                totalSkippedUnreachable++;
                continue;
            }

            // Treat recycle-bin / excluded paths as not present (see the Events loop above).
            var exists = !LibraryPathFilter.IsExcluded(file.FilePath, config.RecycleBin) && File.Exists(file.FilePath);
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

        // Batch update files that are now missing.
        // Stamp MissingSince=now for the grace-period cleanup downstream.
        if (filesToMarkMissing.Count > 0)
        {
            await db.EventFiles
                .Where(ef => filesToMarkMissing.Contains(ef.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(ef => ef.Exists, false)
                    .SetProperty(ef => ef.MissingSince, (DateTime?)now)
                    .SetProperty(ef => ef.LastVerified, now),
                    cancellationToken);
        }

        // Batch update files that are now found.
        // Clear MissingSince so the grace-period clock resets if the file
        // ever goes missing again.
        if (filesToMarkFound.Count > 0)
        {
            await db.EventFiles
                .Where(ef => filesToMarkFound.Contains(ef.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(ef => ef.Exists, true)
                    .SetProperty(ef => ef.MissingSince, (DateTime?)null)
                    .SetProperty(ef => ef.LastVerified, now),
                    cancellationToken);
        }

        // Update LastVerified for all existing files (that weren't changed
        // and weren't skipped due to unreachable root)
        var processedIds = new HashSet<int>(filesToMarkMissing.Concat(filesToMarkFound));
        var skippedIds = new HashSet<int>(eventFilesToCheck
            .Where(f => IsUnderUnreachableRoot(f.FilePath))
            .Select(f => f.Id));
        var unchangedFileIds = eventFilesToCheck
            .Where(f => !processedIds.Contains(f.Id) && !skippedIds.Contains(f.Id))
            .Select(f => f.Id)
            .ToList();

        if (unchangedFileIds.Count > 0)
        {
            await db.EventFiles
                .Where(ef => unchangedFileIds.Contains(ef.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(ef => ef.LastVerified, now), cancellationToken);
        }

        // Grace-period hard-delete. Rows that have been continuously missing
        // for longer than Config.EventFileMissingDeleteAfterDays get pruned.
        // 0 = never auto-delete. The grace period covers backup-restore and
        // transient unreachability while still letting genuine user
        // deletions clean themselves up over time.
        var graceDays = config.EventFileMissingDeleteAfterDays;
        if (graceDays > 0)
        {
            var cutoff = now.AddDays(-graceDays);
            var staleRemoved = await db.EventFiles
                .Where(ef => !ef.Exists && ef.MissingSince != null && ef.MissingSince <= cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (staleRemoved > 0)
            {
                _logger.LogInformation(
                    "[Disk Scan] Pruned {Count} EventFile rows missing for more than {Days} days",
                    staleRemoved, graceDays);
            }

            // Apply the same cutoff to the Event-level Quality/FilePath/FileSize
            // wipe. An Event with HasFile=false and ALL of its EventFiles either
            // gone or still missing past the cutoff is genuinely deleted; we
            // can clear the legacy direct fields without losing recoverable
            // metadata. Done as a single SQL statement so we don't load the
            // whole Events table into memory.
            var eventsWithLingeringPath = await db.Events
                .Where(e => !e.HasFile && e.FilePath != null && !e.Files.Any(f => f.Exists || (f.MissingSince != null && f.MissingSince > cutoff)))
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);
            if (eventsWithLingeringPath.Count > 0)
            {
                await db.Events
                    .Where(e => eventsWithLingeringPath.Contains(e.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.FilePath, (string?)null)
                        .SetProperty(e => e.FileSize, (long?)null)
                        .SetProperty(e => e.Quality, (string?)null),
                        cancellationToken);
                _logger.LogInformation(
                    "[Disk Scan] Cleared legacy file fields on {Count} events whose files are past the grace period",
                    eventsWithLingeringPath.Count);
            }
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

        // Remove stale disk-discovered PendingImport rows whose source file is gone
        await CleanupStalePendingImportsAsync(db, config, cancellationToken);
    }

    /// <summary>
    /// Remove disk-discovered PendingImport rows (DownloadClientId == null - found by
    /// this service, FileWatcherService, or a manual league scan, not reported by a
    /// tracked download client) whose source file no longer exists. Without this,
    /// a download client configured to remove completed/failed downloads after
    /// processing (e.g. SABnzbd) deletes the file out from under an unmatched
    /// "No match found" row, which then has nothing left to import and piles up in
    /// the Activity page forever (issue #129) - clicking Refresh re-fetches the same
    /// stale rows since nothing re-checks the file's existence.
    ///
    /// Client-tracked rows (DownloadClientId set) are already reconciled against the
    /// client's live queue/history by EnhancedDownloadMonitorService and are excluded
    /// here. Uses the same grace-period + parent-directory-reachability guard as the
    /// EventFile missing-file handling above: a download directory that's temporarily
    /// unmounted must not be mistaken for "the file was deleted".
    /// </summary>
    private async Task CleanupStalePendingImportsAsync(SportarrDbContext db, Config config, CancellationToken cancellationToken)
    {
        if (config.EventFileMissingDeleteAfterDays <= 0)
            return; // 0 = never auto-delete, same convention as the EventFile grace period

        var graceCutoff = DateTime.UtcNow.AddDays(-config.EventFileMissingDeleteAfterDays);

        var candidates = await db.PendingImports
            .Where(p => p.DownloadClientId == null
                && p.Status == PendingImportStatus.Pending
                && p.Detected < graceCutoff)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return;

        var removed = 0;
        foreach (var pending in candidates)
        {
            if (File.Exists(pending.FilePath))
                continue;

            var parentDir = Path.GetDirectoryName(pending.FilePath);
            if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
                continue; // Containing directory is gone too - the mount may just be down, not the file itself

            _logger.LogInformation(
                "[Disk Scan] Removing stale PendingImport {Id} ({Title}) - source file has been missing for over {Days} days: {Path}",
                pending.Id, pending.Title, config.EventFileMissingDeleteAfterDays, pending.FilePath);
            db.PendingImports.Remove(pending);
            removed++;
        }

        if (removed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[Disk Scan] Removed {Count} stale pending import(s)", removed);
        }
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

        // Batch update events marked as missing. Only flips HasFile=false;
        // FilePath / FileSize / Quality stay (see earlier comment in the
        // direct-path-check branch above for the rationale — temporarily
        // unreachable paths must not destroy user data).
        if (eventsToMarkMissing.Count > 0)
        {
            await db.Events
                .Where(e => eventsToMarkMissing.Contains(e.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.HasFile, false),
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
        // Root folders live in the RootFolders table (the UI's source of truth).
        var rootFolders = await db.RootFolders.ToListAsync(cancellationToken);
        if (rootFolders.Count == 0)
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

        // Exclude paths the user is currently being asked about (open PendingImport
        // rows) so we don't re-add a duplicate row each scan while it's awaiting
        // resolution.
        var pendingPaths = new HashSet<string>(
            await db.PendingImports
                .Select(pi => pi.FilePath)
                .ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        // Exclude paths the user has blocklisted. When a user rejects a
        // disk-discovered import via /api/pending-imports/{id}/remove-from-client
        // or /reject, the row is hard-deleted and a Blocklist entry is written
        // with FilePath set. Without honoring it here the next scan would just
        // re-discover the same file and recreate the PendingImport, producing
        // an infinite loop where Remove never sticks.
        var blocklistedPaths = new HashSet<string>(
            await db.Blocklist
                .Where(b => b.FilePath != null)
                .Select(b => b.FilePath!)
                .ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        var videoExtensions = new HashSet<string>(SupportedExtensions.Video, StringComparer.OrdinalIgnoreCase);
        var discoveredCount = 0;

        foreach (var rootFolder in rootFolders)
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
                // Skip recycle bin, dot folders, and system folders so recycled/system copies
                // are never re-discovered as new files (the source of the "47 files vs 21" inflation).
                files = LibraryPathFilter.FilterExcluded(files);
                // Skip release sample clips so a stray preview is never suggested
                // as a pending import for a real event.
                files = SampleFileFilter.FilterSamples(files, rootFolder.Path);

                foreach (var filePath in files)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    // Skip if already tracked, already pending, or blocklisted
                    if (trackedPaths.Contains(filePath) || pendingPaths.Contains(filePath) || blocklistedPaths.Contains(filePath))
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
