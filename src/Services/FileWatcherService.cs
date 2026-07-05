using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service that monitors root folders for file changes using FileSystemWatcher.
/// Detects new, renamed, and deleted video files in real-time.
/// New/renamed files create PendingImport records for user review.
/// Deleted files update event tracking status.
/// </summary>
public class FileWatcherService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FileWatcherService> _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, System.Threading.Timer> _debounceTimers = new();
    private readonly HashSet<string> _videoExtensions;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);

    // Configured recycle bin path, cached so per-event filtering doesn't hit the DB.
    // Refreshed alongside the watcher list. The dot-folder rule in LibraryPathFilter
    // already catches the default ".Recycle.Bin"; this also covers a non-dotted custom path.
    private volatile string? _recycleBinPath;

    public FileWatcherService(
        IServiceProvider serviceProvider,
        ILogger<FileWatcherService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _videoExtensions = new HashSet<string>(SupportedExtensions.Video, StringComparer.OrdinalIgnoreCase);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[File Watcher] Service started");

        // Wait for app to initialize
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        await SetupWatchersAsync(stoppingToken);

        // Keep running and periodically refresh watchers (in case root folders change)
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);

            // Refresh watchers in case root folders were added/removed
            try
            {
                await RefreshWatchersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[File Watcher] Error refreshing watchers");
            }
        }
    }

    private async Task SetupWatchersAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            _recycleBinPath = (await scope.ServiceProvider.GetRequiredService<ConfigService>().GetConfigAsync()).RecycleBin;

            var rootFolders = await db.RootFolders.ToListAsync(cancellationToken);
            if (rootFolders.Count == 0)
            {
                _logger.LogInformation("[File Watcher] No root folders configured, watching disabled");
                return;
            }

            foreach (var rootFolder in rootFolders)
            {
                if (!Directory.Exists(rootFolder.Path))
                {
                    _logger.LogWarning("[File Watcher] Root folder not accessible: {Path}", rootFolder.Path);
                    continue;
                }

                try
                {
                    var watcher = new FileSystemWatcher(rootFolder.Path)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                        EnableRaisingEvents = true
                    };

                    watcher.Created += OnFileCreated;
                    watcher.Renamed += OnFileRenamed;
                    watcher.Deleted += OnFileDeleted;
                    watcher.Error += OnWatcherError;

                    _watchers.Add(watcher);
                    _logger.LogInformation("[File Watcher] Watching root folder: {Path}", rootFolder.Path);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[File Watcher] Failed to create watcher for: {Path}", rootFolder.Path);
                }
            }

            _logger.LogInformation("[File Watcher] Monitoring {Count} root folders", _watchers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[File Watcher] Error during setup");
        }
    }

    private async Task RefreshWatchersAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        _recycleBinPath = (await scope.ServiceProvider.GetRequiredService<ConfigService>().GetConfigAsync()).RecycleBin;

        var configuredPaths = (await db.RootFolders.ToListAsync(cancellationToken))
            .Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var watchedPaths = _watchers.Select(w => w.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Add watchers for new root folders
        foreach (var path in configuredPaths.Except(watchedPaths))
        {
            if (!Directory.Exists(path)) continue;

            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };

                watcher.Created += OnFileCreated;
                watcher.Renamed += OnFileRenamed;
                watcher.Deleted += OnFileDeleted;
                watcher.Error += OnWatcherError;

                _watchers.Add(watcher);
                _logger.LogInformation("[File Watcher] Added watcher for new root folder: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[File Watcher] Failed to add watcher for: {Path}", path);
            }
        }

        // Remove watchers for deleted root folders
        var watchersToRemove = _watchers.Where(w => !configuredPaths.Contains(w.Path)).ToList();
        foreach (var watcher in watchersToRemove)
        {
            _logger.LogInformation("[File Watcher] Removing watcher for removed root folder: {Path}", watcher.Path);
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _watchers.Remove(watcher);
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!IsVideoFile(e.FullPath)) return;
        // Ignore files appearing inside the recycle bin / dot / system folders. A file the
        // app moved to the recycle bin must not be re-imported as a "new" library file.
        if (IsExcluded(e.FullPath)) return;
        // Ignore events produced by Sportarr's own moves (rename, renumber, import).
        if (SelfMoveTracker.ShouldIgnore(e.FullPath)) return;
        DebouncedHandleNewFile(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // Skip renames that are Sportarr's own work (renumber/rename/import). Reacting to
        // them races our synchronous DB update and can duplicate or re-point records.
        if (SelfMoveTracker.ShouldIgnore(e.FullPath) || SelfMoveTracker.ShouldIgnore(e.OldFullPath))
            return;

        // A move INTO the recycle bin (or any excluded folder) surfaces as a rename. Treat it
        // as a deletion of the old (library) path, NOT as a rename that would re-point the
        // tracked record into the recycle bin. This is the fix for event files ending up
        // pointing at /data/.Recycle.Bin/...
        if (IsExcluded(e.FullPath))
        {
            if (IsVideoFile(e.OldFullPath) && !IsExcluded(e.OldFullPath))
                _ = HandleDeletedFileAsync(e.OldFullPath);
            return;
        }

        // A move OUT of an excluded folder into the library is just a new file at the new path.
        if (IsExcluded(e.OldFullPath))
        {
            if (IsVideoFile(e.FullPath))
                DebouncedHandleNewFile(e.FullPath);
            return;
        }

        // Handle video-to-video renames by updating existing records in place
        if (IsVideoFile(e.OldFullPath) && IsVideoFile(e.FullPath))
        {
            _ = HandleRenamedFileAsync(e.OldFullPath, e.FullPath);
            return;
        }

        // Non-video renamed to video = new file
        if (IsVideoFile(e.FullPath))
            DebouncedHandleNewFile(e.FullPath);

        // Video renamed to non-video = deleted
        if (IsVideoFile(e.OldFullPath))
            _ = HandleDeletedFileAsync(e.OldFullPath);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (!IsVideoFile(e.FullPath)) return;
        if (IsExcluded(e.FullPath)) return;
        if (SelfMoveTracker.ShouldIgnore(e.FullPath)) return;
        _ = HandleDeletedFileAsync(e.FullPath);
    }

    private bool IsExcluded(string path) => LibraryPathFilter.IsExcluded(path, _recycleBinPath);

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var watcher = sender as FileSystemWatcher;
        _logger.LogWarning(e.GetException(), "[File Watcher] Watcher error for {Path}", watcher?.Path ?? "unknown");

        // Try to restart the watcher
        if (watcher != null)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                if (Directory.Exists(watcher.Path))
                {
                    watcher.EnableRaisingEvents = true;
                    _logger.LogInformation("[File Watcher] Restarted watcher for: {Path}", watcher.Path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[File Watcher] Failed to restart watcher for: {Path}", watcher.Path);
            }
        }
    }

    private bool IsVideoFile(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && _videoExtensions.Contains(ext.ToLowerInvariant());
    }

    /// <summary>
    /// Debounce file creation events to handle files being copied/written over time.
    /// </summary>
    private void DebouncedHandleNewFile(string filePath)
    {
        // Cancel any existing timer for this path
        if (_debounceTimers.TryRemove(filePath, out var existingTimer))
        {
            existingTimer.Dispose();
        }

        // Set a new timer
        var timer = new System.Threading.Timer(state =>
        {
            _debounceTimers.TryRemove(filePath, out _);
            _ = HandleNewFileAsync(filePath);
        }, null, DebounceDelay, Timeout.InfiniteTimeSpan);

        _debounceTimers[filePath] = timer;
    }

    // Paths currently mid-analysis. FileSystemWatcher fires multiple events
    // for one file (create + several changes while it's written); without
    // this, each event races through the stability wait and the file gets
    // analyzed / pended more than once.
    private readonly ConcurrentDictionary<string, byte> _inFlightNewFiles = new(StringComparer.Ordinal);

    private async Task HandleNewFileAsync(string filePath)
    {
        if (!_inFlightNewFiles.TryAdd(filePath, 0))
        {
            return;
        }
        try
        {
            if (!File.Exists(filePath)) return;

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

            // Check if file is already tracked
            var isTracked = await db.Events.AnyAsync(e => e.FilePath == filePath) ||
                           await db.EventFiles.AnyAsync(ef => ef.FilePath == filePath);
            if (isTracked) return;

            // Check if already pending
            var isPending = await db.PendingImports
                .AnyAsync(pi => pi.FilePath == filePath && pi.Status == PendingImportStatus.Pending);
            if (isPending) return;

            // Wait for the file to stop growing before analyzing it.
            // Transcode tools (the Tdarr replace-in-place flow this path
            // exists for) write the new file over minutes; matching or
            // importing a half-written file would move it out from under
            // the writer. We're on a fire-and-forget task, so waiting here
            // blocks nothing. A file that never stabilizes within the cap
            // still gets a pending record below, just never an auto-import.
            var stable = await WaitForStableFileAsync(filePath, TimeSpan.FromMinutes(10));
            if (!File.Exists(filePath)) return;

            var fileInfo = new FileInfo(filePath);

            // Run the same match engine the library rescan uses, scoped to
            // the file's own folder. The old inline LIKE-pattern matcher
            // pinned every match at confidence 50, which guaranteed a
            // manual-review PendingImport no matter how obvious the match;
            // the real engine produces a genuine 0-100 score, so the
            // watcher can auto-import at the same floor the periodic
            // rescan already trusts.
            var libraryImport = scope.ServiceProvider.GetRequiredService<LibraryImportService>();
            var parentFolder = Path.GetDirectoryName(filePath);
            ImportableFile? analysis = null;
            if (!string.IsNullOrEmpty(parentFolder))
            {
                var scan = await libraryImport.ScanFolderAsync(parentFolder, includeSubfolders: false);
                analysis = scan.MatchedFiles.FirstOrDefault(f => f.FilePath == filePath)
                        ?? scan.AlreadyInLibrary.FirstOrDefault(f => f.FilePath == filePath)
                        ?? scan.UnmatchedFiles.FirstOrDefault(f => f.FilePath == filePath);

                if (scan.AlreadyInLibrary.Any(f => f.FilePath == filePath))
                {
                    _logger.LogDebug("[File Watcher] File already linked in library, nothing to do: {Path}", filePath);
                    return;
                }
            }

            var suggestedEventId = analysis?.MatchedEventId;
            var confidence = analysis?.MatchConfidence ?? 0;
            var quality = analysis?.Quality;

            // Auto-import when the match clears the same confidence floor
            // the library rescan auto-imports at, the target event isn't
            // already satisfied, and the file proved stable. The
            // highest-confidence case is exactly the transcode flow: the
            // replacement lands in the folder of a just-deleted tracked
            // file for the same event.
            if (stable &&
                suggestedEventId.HasValue &&
                confidence >= LibraryImportService.AutoImportConfidenceFloor &&
                analysis?.ExistingEventId == null)
            {
                try
                {
                    var importResult = await libraryImport.ImportFilesAsync(new List<FileImportRequest>
                    {
                        new()
                        {
                            FilePath = filePath,
                            EventId = suggestedEventId,
                            Quality = quality,
                        }
                    });
                    if (importResult.Imported.Count + importResult.Created.Count > 0)
                    {
                        _logger.LogInformation(
                            "[File Watcher] Auto-imported {Path} (confidence {Confidence}%, event {EventId})",
                            filePath, confidence, suggestedEventId);
                        return;
                    }
                    _logger.LogWarning(
                        "[File Watcher] Auto-import of {Path} did not import (skipped: {Skipped}, failed: {Failed}); leaving it for manual review",
                        filePath, importResult.Skipped.Count, importResult.Failed.Count + importResult.Errors.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[File Watcher] Auto-import of {Path} failed; leaving it for manual review", filePath);
                }
            }

            var pendingImport = new PendingImport
            {
                DownloadClientId = null,
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
            await db.SaveChangesAsync();

            _logger.LogInformation("[File Watcher] New file detected: {Path} (Confidence: {Confidence}%)",
                filePath, confidence);

            try
            {
                var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();
                await notificationService.SendNotificationAsync(
                    NotificationTrigger.OnManualInteractionRequired,
                    $"Manual import required: {fileInfo.Name}",
                    $"A new file appeared in the library folders and could not be matched automatically (confidence {confidence}%). Review it under Activity.",
                    new Dictionary<string, object>
                    {
                        { "filePath", filePath },
                        { "confidence", confidence },
                    });
            }
            catch (Exception notifyEx)
            {
                _logger.LogWarning(notifyEx, "[File Watcher] Failed to send pending-import notification");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[File Watcher] Error handling new file: {Path}", filePath);
        }
        finally
        {
            _inFlightNewFiles.TryRemove(filePath, out _);
        }
    }

    /// <summary>
    /// True once the file's size has stayed unchanged across two
    /// consecutive probes; false if the cap elapses first (or the file
    /// vanishes). Probes every 10 seconds.
    /// </summary>
    private static async Task<bool> WaitForStableFileAsync(string filePath, TimeSpan maxWait)
    {
        var deadline = DateTime.UtcNow + maxWait;
        long lastSize = -1;
        var stableProbes = 0;
        while (DateTime.UtcNow < deadline)
        {
            if (!File.Exists(filePath)) return false;
            long size;
            try
            {
                size = new FileInfo(filePath).Length;
            }
            catch (IOException)
            {
                size = -1; // still locked by the writer
            }

            if (size >= 0 && size == lastSize)
            {
                stableProbes++;
                if (stableProbes >= 2) return true;
            }
            else
            {
                stableProbes = 0;
            }
            lastSize = size;
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
        return false;
    }

    private async Task HandleRenamedFileAsync(string oldPath, string newPath)
    {
        try
        {
            if (!File.Exists(newPath)) return;

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

            var updated = false;

            // Update EventFile record if the old path is tracked there
            var eventFile = await db.EventFiles.FirstOrDefaultAsync(ef => ef.FilePath == oldPath);
            if (eventFile != null)
            {
                eventFile.FilePath = newPath;
                eventFile.LastVerified = DateTime.UtcNow;

                // Also update the parent Event's FilePath if it points to the old path
                var evt = await db.Events.FindAsync(eventFile.EventId);
                if (evt != null && evt.FilePath == oldPath)
                {
                    evt.FilePath = newPath;
                }

                updated = true;
                _logger.LogInformation("[File Watcher] File renamed (EventFile updated): {OldPath} -> {NewPath}", oldPath, newPath);
            }

            // Update Event direct file path if tracked there
            var directEvent = await db.Events.FirstOrDefaultAsync(e => e.FilePath == oldPath);
            if (directEvent != null)
            {
                directEvent.FilePath = newPath;
                updated = true;
                _logger.LogInformation("[File Watcher] File renamed (Event updated): {OldPath} -> {NewPath}", oldPath, newPath);
            }

            if (updated)
            {
                await db.SaveChangesAsync();
            }
            else
            {
                // Old path wasn't tracked, treat new path as a new file
                _logger.LogDebug("[File Watcher] Renamed file not tracked, treating as new: {Path}", newPath);
                await HandleNewFileAsync(newPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[File Watcher] Error handling renamed file: {OldPath} -> {NewPath}", oldPath, newPath);
        }
    }

    private async Task HandleDeletedFileAsync(string filePath)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

            // Check EventFiles table
            var eventFile = await db.EventFiles.FirstOrDefaultAsync(ef => ef.FilePath == filePath);
            if (eventFile != null)
            {
                eventFile.Exists = false;
                eventFile.LastVerified = DateTime.UtcNow;

                // Check if the event has any other existing files
                var hasOtherFiles = await db.EventFiles
                    .AnyAsync(ef => ef.EventId == eventFile.EventId && ef.Id != eventFile.Id && ef.Exists);

                if (!hasOtherFiles)
                {
                    var evt = await db.Events.FindAsync(eventFile.EventId);
                    if (evt != null)
                    {
                        evt.HasFile = false;
                        evt.FilePath = null;
                        evt.FileSize = null;
                        evt.Quality = null;
                    }
                }

                await db.SaveChangesAsync();
                _logger.LogWarning("[File Watcher] File deleted: {Path}", filePath);
            }

            // Check Events table direct file path
            var directEvent = await db.Events.FirstOrDefaultAsync(e => e.FilePath == filePath);
            if (directEvent != null)
            {
                directEvent.HasFile = false;
                directEvent.FilePath = null;
                directEvent.FileSize = null;
                directEvent.Quality = null;
                await db.SaveChangesAsync();
                _logger.LogWarning("[File Watcher] File deleted (direct event): {Path}", filePath);
            }
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            // Race condition: the API endpoint already deleted/updated this record
            // before the FileWatcher could process it. This is expected and harmless.
            _logger.LogDebug("[File Watcher] File already handled by another process: {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[File Watcher] Error handling deleted file: {Path}", filePath);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[File Watcher] Service stopping...");

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();

        foreach (var timer in _debounceTimers.Values)
        {
            timer.Dispose();
        }
        _debounceTimers.Clear();

        await base.StopAsync(cancellationToken);
    }
}
