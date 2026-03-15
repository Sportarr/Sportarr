using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
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

            var settings = await db.MediaManagementSettings.FirstOrDefaultAsync(cancellationToken);
            if (settings?.RootFolders == null || settings.RootFolders.Count == 0)
            {
                _logger.LogInformation("[File Watcher] No root folders configured, watching disabled");
                return;
            }

            foreach (var rootFolder in settings.RootFolders)
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

        var settings = await db.MediaManagementSettings.FirstOrDefaultAsync(cancellationToken);
        var configuredPaths = settings?.RootFolders?.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>();

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
        DebouncedHandleNewFile(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (IsVideoFile(e.FullPath))
            DebouncedHandleNewFile(e.FullPath);

        if (IsVideoFile(e.OldFullPath))
            _ = HandleDeletedFileAsync(e.OldFullPath);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (!IsVideoFile(e.FullPath)) return;
        _ = HandleDeletedFileAsync(e.FullPath);
    }

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

    private async Task HandleNewFileAsync(string filePath)
    {
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

            var fileInfo = new FileInfo(filePath);
            var filename = Path.GetFileNameWithoutExtension(filePath);

            // Simple event matching
            int? suggestedEventId = null;
            int confidence = 0;

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
                    .FirstOrDefaultAsync();

                if (matchedEvent != null)
                {
                    suggestedEventId = matchedEvent.Id;
                    confidence = 50;
                }
            }

            // Detect quality
            string? quality = null;
            if (filename.Contains("2160p", StringComparison.OrdinalIgnoreCase) || filename.Contains("4K", StringComparison.OrdinalIgnoreCase))
                quality = "2160p";
            else if (filename.Contains("1080p", StringComparison.OrdinalIgnoreCase))
                quality = "1080p";
            else if (filename.Contains("720p", StringComparison.OrdinalIgnoreCase))
                quality = "720p";

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[File Watcher] Error handling new file: {Path}", filePath);
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
