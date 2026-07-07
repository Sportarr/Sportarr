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
            var config = await scope.ServiceProvider.GetRequiredService<ConfigService>().GetConfigAsync();
            _recycleBinPath = config.RecycleBin;

            var rootFolders = await db.RootFolders.ToListAsync(cancellationToken);
            var watchFolders = ResolveWatchFolders(config, rootFolders.Select(r => r.Path));
            if (rootFolders.Count == 0 && watchFolders.Count == 0)
            {
                _logger.LogInformation("[File Watcher] No root folders or watch folders configured, watching disabled");
                return;
            }

            foreach (var rootFolder in rootFolders)
            {
                if (!Directory.Exists(rootFolder.Path))
                {
                    _logger.LogWarning("[File Watcher] Root folder not accessible: {Path}", rootFolder.Path);
                    continue;
                }

                CreateWatcher(rootFolder.Path, "root folder");
            }

            foreach (var watchFolder in watchFolders)
            {
                CreateWatcher(watchFolder, "watch folder");
            }

            _logger.LogInformation("[File Watcher] Monitoring {Count} folder(s) ({Roots} root, {Watch} watch)",
                _watchers.Count, rootFolders.Count(r => Directory.Exists(r.Path)), watchFolders.Count);

            // Recordings/drops that finished while Sportarr was down produced
            // no watcher events, and no other scan covers folders outside the
            // library roots - sweep watch folders once at startup.
            SweepWatchFolders(watchFolders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[File Watcher] Error during setup");
        }
    }

    /// <summary>
    /// Normalize the configured watch folders: drop blanks and missing
    /// directories, and skip any folder already covered by a root-folder
    /// watcher (identical path or nested inside one).
    /// </summary>
    private List<string> ResolveWatchFolders(Config config, IEnumerable<string> rootPaths)
    {
        var roots = rootPaths.ToList();
        var resolved = new List<string>();
        foreach (var raw in config.WatchFolders ?? new List<string>())
        {
            var path = raw?.Trim();
            if (string.IsNullOrEmpty(path))
                continue;
            if (!Directory.Exists(path))
            {
                _logger.LogWarning("[File Watcher] Watch folder not accessible: {Path}", path);
                continue;
            }
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (roots.Any(r => IsSameOrUnder(normalized, r)))
            {
                _logger.LogDebug("[File Watcher] Watch folder {Path} is inside a root folder; already watched", path);
                continue;
            }
            if (!resolved.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                resolved.Add(normalized);
            }
        }
        return resolved;
    }

    private static bool IsSameOrUnder(string path, string parent)
    {
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        if (string.Equals(path, normalizedParent, StringComparison.OrdinalIgnoreCase))
            return true;
        return path.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalizedParent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private void CreateWatcher(string path, string kind)
    {
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
            _logger.LogInformation("[File Watcher] Watching {Kind}: {Path}", kind, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[File Watcher] Failed to create watcher for: {Path}", path);
        }
    }

    /// <summary>
    /// Kick off analysis for every untracked video file already sitting in
    /// the watch folders. HandleNewFileAsync is idempotent (tracked/pending
    /// checks plus an in-flight guard) and waits for file stability itself,
    /// so each file is fire-and-forget - a recording still in progress just
    /// parks in the stability wait like it would after a watcher event.
    /// FileSystemWatcher is also unreliable on network mounts, so the
    /// periodic refresh re-sweeps as a polling fallback.
    /// </summary>
    private void SweepWatchFolders(IReadOnlyCollection<string> watchFolders)
    {
        foreach (var folder in watchFolders)
        {
            try
            {
                var candidates = LibraryPathFilter
                    .FilterExcluded(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories), _recycleBinPath)
                    .Where(IsVideoFile)
                    .ToList();
                if (candidates.Count == 0)
                    continue;

                _logger.LogDebug("[File Watcher] Sweeping watch folder {Path}: {Count} video file(s)", folder, candidates.Count);
                foreach (var file in candidates)
                {
                    _ = HandleNewFileAsync(file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[File Watcher] Failed to sweep watch folder {Path}", folder);
            }
        }
    }

    private async Task RefreshWatchersAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var config = await scope.ServiceProvider.GetRequiredService<ConfigService>().GetConfigAsync();
        _recycleBinPath = config.RecycleBin;

        var rootPaths = (await db.RootFolders.ToListAsync(cancellationToken)).Select(r => r.Path).ToList();
        var watchFolders = ResolveWatchFolders(config, rootPaths);

        var configuredPaths = rootPaths
            .Concat(watchFolders)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var watchedPaths = _watchers.Select(w => w.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Add watchers for newly configured root/watch folders
        foreach (var path in configuredPaths.Except(watchedPaths))
        {
            if (!Directory.Exists(path)) continue;
            CreateWatcher(path, watchFolders.Contains(path, StringComparer.OrdinalIgnoreCase) ? "watch folder" : "root folder");
        }

        // Remove watchers for folders no longer configured
        var watchersToRemove = _watchers.Where(w => !configuredPaths.Contains(w.Path)).ToList();
        foreach (var watcher in watchersToRemove)
        {
            _logger.LogInformation("[File Watcher] Removing watcher for removed folder: {Path}", watcher.Path);
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _watchers.Remove(watcher);
        }

        // Poll fallback: watcher events are best-effort on network mounts,
        // and a watch folder has no other scan covering it.
        SweepWatchFolders(watchFolders);
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

            // User-ignored files: rejecting a pending import writes a
            // Blocklist row carrying the path. Without this check the
            // watcher re-detects the file on the next filesystem event (or
            // auto-imports it), so the ignore never sticks for files that
            // stay on disk. DiskScanService applies the same rule.
            var isIgnored = await db.Blocklist.AnyAsync(b => b.FilePath == filePath);
            if (isIgnored)
            {
                _logger.LogDebug("[File Watcher] Skipping ignored (blocklisted) file: {Path}", filePath);
                return;
            }

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

            // Manually split part files (ptN in the name) only auto-import
            // for fighting sports, where parts are first-class (Prelims,
            // Main Card) and a transcoded part file must reattach on its
            // own. For every other sport a ptN file is a manual split
            // Sportarr doesn't model (e.g. an F1 pre-show plus race cut),
            // so whether and how it attaches is a human decision - it
            // surfaces as a pending suggestion to import or ignore instead.
            var isPartFile = System.Text.RegularExpressions.Regex.IsMatch(
                fileInfo.Name, @"(?<![a-zA-Z])pt\d+\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var blockPartAutoImport = false;
            if (isPartFile && suggestedEventId.HasValue)
            {
                var targetEvent = await db.Events
                    .FirstOrDefaultAsync(e => e.Id == suggestedEventId.Value);
                blockPartAutoImport = targetEvent == null ||
                    !EventPartDetector.IsFightingSport(targetEvent.Sport ?? "");
            }

            // Auto-import when the match clears the same confidence floor
            // the library rescan auto-imports at, the target event isn't
            // already satisfied, and the file proved stable. The
            // highest-confidence case is exactly the transcode flow: the
            // replacement lands in the folder of a just-deleted tracked
            // file for the same event.
            if (stable &&
                !blockPartAutoImport &&
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

    /// <summary>
    /// Honor the Unmonitor Deleted Events setting: an externally deleted
    /// file (cron cleanup, media server delete-after-watch) unmonitors its
    /// event so it isn't re-downloaded. Sportarr-initiated replacements are
    /// naturally exempt because their new file is tracked before the old
    /// path's deletion is observed (the event still has files, so this path
    /// never runs), and retention deletions unmonitor on their own anyway.
    /// </summary>
    private async Task MaybeUnmonitorDeletedAsync(SportarrDbContext db, Event evt)
    {
        if (!evt.Monitored) return;

        var mediaSettings = await db.MediaManagementSettings.AsNoTracking().FirstOrDefaultAsync();
        if (mediaSettings?.UnmonitorDeletedEvents != true) return;

        evt.Monitored = false;
        _logger.LogInformation("[File Watcher] Unmonitored '{Title}' - its file was deleted from disk (Unmonitor Deleted Events is enabled)",
            evt.Title);
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
                        await MaybeUnmonitorDeletedAsync(db, evt);
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
                await MaybeUnmonitorDeletedAsync(db, directEvent);
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
