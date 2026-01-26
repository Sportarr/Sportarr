using System.Collections.Concurrent;
using System.Text.Json;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for managing a queue of search requests with parallel execution.
/// Allows users to queue many searches (e.g., entire league) without blocking the UI.
/// Searches execute in parallel (up to MaxConcurrentSearches) while excess requests wait in queue.
///
/// Rate Limiting Strategy (Sonarr-style):
/// - Max 3 concurrent event searches
/// - 5-second delay between starting new event searches (prevents indexer rate limiting)
/// - Each search hits indexers sequentially with per-indexer rate limiting at HTTP layer
/// - HTTP 429 responses trigger indexer-specific backoff (uses Retry-After header)
/// </summary>
public class SearchQueueService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SearchQueueService> _logger;

    // Max concurrent searches (prevents overwhelming indexers)
    private const int MaxConcurrentSearches = 3;

    // Delay between starting new event searches (Sonarr-style throttling)
    // This prevents rapid sequential searches from triggering indexer rate limits
    private const int InterSearchDelayMs = 5000; // 5 seconds between search starts

    // How long to keep completed searches before cleanup (reduced from 5 minutes to 2 minutes for memory optimization)
    private const int CompletedSearchRetentionMinutes = 2;

    // Semaphore to limit concurrent searches
    private static readonly SemaphoreSlim _searchSemaphore = new(MaxConcurrentSearches, MaxConcurrentSearches);

    // Queue of pending search requests
    private static readonly ConcurrentQueue<SearchQueueItem> _pendingQueue = new();

    // Active searches (for status reporting)
    private static readonly ConcurrentDictionary<string, SearchQueueItem> _activeSearches = new();

    // Completed searches (kept for status, cleaned up automatically by timer)
    private static readonly ConcurrentDictionary<string, SearchQueueItem> _completedSearches = new();

    // Lock for queue processing
    private static readonly SemaphoreSlim _processingLock = new(1, 1);

    // Track last search start time for inter-search throttling
    private static DateTime _lastSearchStartTime = DateTime.MinValue;
    private static readonly object _lastSearchTimeLock = new();

    // Timer for automatic cleanup of completed searches (prevents unbounded memory growth)
    private static readonly System.Threading.Timer _cleanupTimer;

#pragma warning disable CS0414 // Field is assigned but never used - kept for future debugging/status tracking
    private static bool _isProcessing = false;
#pragma warning restore CS0414

    // Static constructor to initialize cleanup timer
    static SearchQueueService()
    {
        // Run cleanup every 60 seconds to remove old completed searches
        _cleanupTimer = new System.Threading.Timer(
            CleanupCompletedSearches,
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Automatic cleanup of completed searches older than retention period.
    /// This prevents unbounded memory growth when UI isn't actively polling.
    /// </summary>
    private static void CleanupCompletedSearches(object? state)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-CompletedSearchRetentionMinutes);
        var removedCount = 0;

        foreach (var key in _completedSearches.Keys.ToList())
        {
            if (_completedSearches.TryGetValue(key, out var item) && item.CompletedAt < cutoff)
            {
                if (_completedSearches.TryRemove(key, out _))
                {
                    removedCount++;
                }
            }
        }

        // Only log if we actually removed something (avoid log spam)
        if (removedCount > 0)
        {
            // Note: Can't use ILogger here since this is a static method
            // The cleanup is silent but effective
        }
    }

    public SearchQueueService(IServiceScopeFactory scopeFactory, ILogger<SearchQueueService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Queue a search for an event. Returns immediately with a queue ID.
    /// Also creates an entry in the Tasks table for visibility in System > Tasks.
    /// </summary>
    public async Task<SearchQueueItem> QueueSearchAsync(int eventId, string? part = null, bool isManualSearch = true)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        // Get event details for display and validation
        var evt = await db.Events.FindAsync(eventId);
        var eventTitle = evt?.Title ?? $"Event #{eventId}";

        // Validate part against event type (e.g., Fight Night events don't have "Early Prelims")
        if (!string.IsNullOrEmpty(part) && evt != null && EventPartDetector.IsFightingSport(evt.Sport ?? ""))
        {
            // Skip "Full Event" - that means search without part
            if (EventPartDetector.IsFullEvent(part))
            {
                part = null;
            }
            else
            {
                // Validate the part is valid for this event type
                var validSegments = EventPartDetector.GetSegmentDefinitions(evt.Sport ?? "Fighting", evt.Title);
                var validPartNames = validSegments.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (!validPartNames.Contains(part))
                {
                    _logger.LogWarning("[SEARCH QUEUE] Rejecting invalid part '{Part}' for event '{Title}' - not valid for this event type (valid parts: {ValidParts})",
                        part, evt.Title, string.Join(", ", validPartNames));

                    // Return a failed queue item instead of throwing
                    return new SearchQueueItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        EventId = eventId,
                        EventTitle = eventTitle,
                        Part = part,
                        IsManualSearch = isManualSearch,
                        Status = SearchQueueStatus.Failed,
                        QueuedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow,
                        Message = $"Invalid part '{part}' for this event type. Valid parts: {string.Join(", ", validPartNames.Where(p => p != EventPartDetector.FullEventSegmentName))}"
                    };
                }
            }
        }

        var queueItem = new SearchQueueItem
        {
            Id = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventTitle = eventTitle,
            Part = part,
            IsManualSearch = isManualSearch,
            Status = SearchQueueStatus.Queued,
            QueuedAt = DateTime.UtcNow,
            Message = "Waiting in queue..."
        };

        // Create a task entry for visibility in System > Tasks
        var taskName = part != null ? $"Search: {eventTitle} ({part})" : $"Search: {eventTitle}";
        var task = new AppTask
        {
            Name = taskName,
            CommandName = "EventSearch",
            Status = Models.TaskStatus.Queued,
            Queued = DateTime.UtcNow,
            Priority = 10,
            Body = part != null ? $"{eventId}|{part}" : eventId.ToString(),
            CancellationId = queueItem.Id,
            Message = "Waiting in search queue..."
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        queueItem.TaskId = task.Id;
        _pendingQueue.Enqueue(queueItem);

        _logger.LogInformation("[SEARCH QUEUE] Queued search: {Title}{Part} (ID: {QueueId}, TaskId: {TaskId})",
            eventTitle, part != null ? $" ({part})" : "", queueItem.Id, task.Id);

        // Start processing queue (non-blocking)
        _ = ProcessQueueAsync();

        return queueItem;
    }

    /// <summary>
    /// Queue multiple searches for a league (all monitored events).
    /// </summary>
    public async Task<List<SearchQueueItem>> QueueLeagueSearchAsync(int leagueId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        // Get all monitored events for this league that need files
        var events = await db.Events
            .Where(e => e.LeagueId == leagueId && e.Monitored && !e.HasFile)
            .ToListAsync();

        var league = await db.Leagues.FindAsync(leagueId);
        _logger.LogInformation("[SEARCH QUEUE] Queueing {Count} events for league: {League}",
            events.Count, league?.Name ?? $"League #{leagueId}");

        var queuedItems = new List<SearchQueueItem>();

        foreach (var evt in events)
        {
            // For Fighting sports with multi-part episodes, queue each monitored part
            // IMPORTANT: Validate parts against what's valid for this specific event type
            // e.g., Fight Night events don't have "Early Prelims"
            if (evt.Sport == "Fighting" && !string.IsNullOrEmpty(evt.MonitoredParts))
            {
                // Get valid segments for this event type (Fight Night vs PPV)
                var validSegments = EventPartDetector.GetSegmentDefinitions(evt.Sport, evt.Title);
                var validPartNames = validSegments.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Order parts by priority: Main Card first, then Prelims, then Early Prelims, then Post Show
                var parts = evt.MonitoredParts.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .OrderBy(p => p switch
                    {
                        "Main Card" => 1,    // Most important - download first
                        "Prelims" => 2,      // Second priority
                        "Early Prelims" => 3, // Third priority
                        "Post Show" => 4,     // Least important - download last
                        _ => 5               // Any other parts after
                    });
                foreach (var part in parts)
                {
                    // Skip "Full Event" - that means search without part
                    if (EventPartDetector.IsFullEvent(part))
                    {
                        var item = await QueueSearchAsync(evt.Id, null, isManualSearch: true);
                        queuedItems.Add(item);
                    }
                    // Only queue if the part is valid for this event type
                    else if (validPartNames.Contains(part))
                    {
                        var item = await QueueSearchAsync(evt.Id, part, isManualSearch: true);
                        queuedItems.Add(item);
                    }
                    else
                    {
                        _logger.LogDebug("[SEARCH QUEUE] Skipping invalid part '{Part}' for event '{Title}' - not valid for this event type",
                            part, evt.Title);
                    }
                }
            }
            else
            {
                var item = await QueueSearchAsync(evt.Id, null, isManualSearch: true);
                queuedItems.Add(item);
            }
        }

        return queuedItems;
    }

    /// <summary>
    /// Get current queue status (pending, active, and recent completed).
    /// Note: Cleanup is now handled automatically by a background timer (CleanupCompletedSearches)
    /// to prevent unbounded memory growth even when UI isn't polling.
    /// </summary>
    public SearchQueueStatusResponse GetQueueStatus()
    {
        return new SearchQueueStatusResponse
        {
            PendingCount = _pendingQueue.Count,
            ActiveCount = _activeSearches.Count,
            MaxConcurrent = MaxConcurrentSearches,
            PendingSearches = _pendingQueue.ToList(),
            ActiveSearches = _activeSearches.Values.ToList(),
            RecentlyCompleted = _completedSearches.Values
                .OrderByDescending(s => s.CompletedAt)
                .Take(20)
                .ToList()
        };
    }

    /// <summary>
    /// Get status of a specific queued search.
    /// </summary>
    public SearchQueueItem? GetSearchStatus(string queueId)
    {
        // Check active
        if (_activeSearches.TryGetValue(queueId, out var active))
            return active;

        // Check completed
        if (_completedSearches.TryGetValue(queueId, out var completed))
            return completed;

        // Check pending queue
        return _pendingQueue.FirstOrDefault(q => q.Id == queueId);
    }

    /// <summary>
    /// Cancel a pending search (cannot cancel active searches).
    /// </summary>
    public bool CancelSearch(string queueId)
    {
        // Can only cancel pending searches, not active ones
        // For now, we'll mark it as cancelled when it gets dequeued
        var pending = _pendingQueue.FirstOrDefault(q => q.Id == queueId);
        if (pending != null)
        {
            pending.Status = SearchQueueStatus.Cancelled;
            pending.Message = "Cancelled by user";
            _logger.LogInformation("[SEARCH QUEUE] Marked search for cancellation: {QueueId}", queueId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clear all pending searches.
    /// </summary>
    public int ClearPendingSearches()
    {
        int count = 0;
        while (_pendingQueue.TryDequeue(out var item))
        {
            item.Status = SearchQueueStatus.Cancelled;
            item.Message = "Queue cleared";
            item.CompletedAt = DateTime.UtcNow;
            _completedSearches[item.Id] = item;
            count++;
        }
        _logger.LogInformation("[SEARCH QUEUE] Cleared {Count} pending searches", count);
        return count;
    }

    /// <summary>
    /// Process the search queue - runs searches in parallel up to MaxConcurrentSearches.
    /// Implements Sonarr-style throttling with inter-search delays to prevent rate limiting.
    /// </summary>
    private async Task ProcessQueueAsync()
    {
        // Prevent multiple queue processors
        if (!await _processingLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            _isProcessing = true;

            while (_pendingQueue.TryDequeue(out var queueItem))
            {
                // Skip cancelled items
                if (queueItem.Status == SearchQueueStatus.Cancelled)
                {
                    queueItem.CompletedAt = DateTime.UtcNow;
                    _completedSearches[queueItem.Id] = queueItem;
                    continue;
                }

                // Wait for available search slot
                await _searchSemaphore.WaitAsync();

                // SONARR-STYLE THROTTLING: Enforce minimum delay between search starts
                // This prevents rapid-fire searches from overwhelming indexers
                TimeSpan waitTime;
                lock (_lastSearchTimeLock)
                {
                    var timeSinceLastSearch = DateTime.UtcNow - _lastSearchStartTime;
                    var requiredDelay = TimeSpan.FromMilliseconds(InterSearchDelayMs);

                    if (timeSinceLastSearch < requiredDelay)
                    {
                        waitTime = requiredDelay - timeSinceLastSearch;
                    }
                    else
                    {
                        waitTime = TimeSpan.Zero;
                    }
                }

                if (waitTime > TimeSpan.Zero)
                {
                    _logger.LogDebug("[SEARCH QUEUE] Throttling: waiting {WaitMs}ms before next search (inter-search delay)",
                        (int)waitTime.TotalMilliseconds);
                    await Task.Delay(waitTime);
                }

                // Update last search time
                lock (_lastSearchTimeLock)
                {
                    _lastSearchStartTime = DateTime.UtcNow;
                }

                // Move to active
                queueItem.Status = SearchQueueStatus.Searching;
                queueItem.StartedAt = DateTime.UtcNow;
                queueItem.Message = "Searching indexers...";
                _activeSearches[queueItem.Id] = queueItem;

                _logger.LogInformation("[SEARCH QUEUE] Starting search: {Title}{Part} (Queue: {Pending} pending, {Active} active)",
                    queueItem.EventTitle, queueItem.Part != null ? $" ({queueItem.Part})" : "",
                    _pendingQueue.Count, _activeSearches.Count);

                // Execute search in background (don't await - allows parallel execution)
                _ = ExecuteSearchAsync(queueItem);
            }
        }
        finally
        {
            _isProcessing = false;
            _processingLock.Release();
        }
    }

    /// <summary>
    /// Execute a single search.
    /// </summary>
    private async Task ExecuteSearchAsync(SearchQueueItem queueItem)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            var automaticSearchService = scope.ServiceProvider.GetRequiredService<AutomaticSearchService>();

            // Update task to Running status
            if (queueItem.TaskId.HasValue)
            {
                var task = await db.Tasks.FindAsync(queueItem.TaskId.Value);
                if (task != null)
                {
                    task.Status = Models.TaskStatus.Running;
                    task.Started = DateTime.UtcNow;
                    task.Progress = 10;
                    task.Message = "Searching indexers...";
                    await db.SaveChangesAsync();
                }
            }

            // Perform the search
            var result = await automaticSearchService.SearchAndDownloadEventAsync(
                queueItem.EventId,
                qualityProfileId: null,
                part: queueItem.Part,
                isManualSearch: queueItem.IsManualSearch
            );

            // Update queue item with results
            queueItem.ReleasesFound = result.ReleasesFound;
            queueItem.Success = result.Success;

            if (result.Success)
            {
                queueItem.Status = SearchQueueStatus.Completed;
                queueItem.Message = $"Downloaded: {result.SelectedRelease}";
                queueItem.SelectedRelease = result.SelectedRelease;
                queueItem.Quality = result.Quality;
            }
            else if (result.ReleasesFound > 0)
            {
                queueItem.Status = SearchQueueStatus.Completed;
                queueItem.Message = $"Found {result.ReleasesFound} releases - {result.Message}";
            }
            else
            {
                queueItem.Status = SearchQueueStatus.NoResults;
                queueItem.Message = result.Message ?? "No releases found";
            }

            // Update task to Completed status
            if (queueItem.TaskId.HasValue)
            {
                var task = await db.Tasks.FindAsync(queueItem.TaskId.Value);
                if (task != null)
                {
                    task.Status = queueItem.Success ? Models.TaskStatus.Completed : Models.TaskStatus.Failed;
                    task.Ended = DateTime.UtcNow;
                    task.Duration = task.Ended - task.Started;
                    task.Progress = 100;
                    task.Message = queueItem.Message;
                    await db.SaveChangesAsync();
                }
            }

            _logger.LogInformation("[SEARCH QUEUE] Completed search: {Title}{Part} - {Message}",
                queueItem.EventTitle, queueItem.Part != null ? $" ({queueItem.Part})" : "", queueItem.Message);
        }
        catch (Exception ex)
        {
            queueItem.Status = SearchQueueStatus.Failed;
            queueItem.Message = ex.Message;
            queueItem.Success = false;

            // Update task to Failed status
            if (queueItem.TaskId.HasValue)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
                var task = await db.Tasks.FindAsync(queueItem.TaskId.Value);
                if (task != null)
                {
                    task.Status = Models.TaskStatus.Failed;
                    task.Ended = DateTime.UtcNow;
                    task.Duration = task.Ended - task.Started;
                    task.Message = ex.Message;
                    task.Exception = ex.ToString();
                    await db.SaveChangesAsync();
                }
            }

            _logger.LogError(ex, "[SEARCH QUEUE] Search failed: {Title}{Part}",
                queueItem.EventTitle, queueItem.Part != null ? $" ({queueItem.Part})" : "");
        }
        finally
        {
            queueItem.CompletedAt = DateTime.UtcNow;

            // Move from active to completed
            _activeSearches.TryRemove(queueItem.Id, out _);
            _completedSearches[queueItem.Id] = queueItem;

            // Release semaphore for next search
            _searchSemaphore.Release();

            // Continue processing queue
            _ = ProcessQueueAsync();
        }
    }
}

/// <summary>
/// Represents a search request in the queue.
/// </summary>
public class SearchQueueItem
{
    public string Id { get; set; } = "";
    public int EventId { get; set; }
    public string EventTitle { get; set; } = "";
    public string? Part { get; set; }
    public bool IsManualSearch { get; set; } = true;
    public SearchQueueStatus Status { get; set; } = SearchQueueStatus.Queued;
    public string Message { get; set; } = "";
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ReleasesFound { get; set; }
    public bool Success { get; set; }
    public string? SelectedRelease { get; set; }
    public string? Quality { get; set; }
    public int? TaskId { get; set; } // Links to AppTask for visibility in System > Tasks
}

/// <summary>
/// Search queue item status.
/// </summary>
public enum SearchQueueStatus
{
    Queued,
    Searching,
    Completed,
    NoResults,
    Failed,
    Cancelled
}

/// <summary>
/// Overall queue status response.
/// </summary>
public class SearchQueueStatusResponse
{
    public int PendingCount { get; set; }
    public int ActiveCount { get; set; }
    public int MaxConcurrent { get; set; }
    public List<SearchQueueItem> PendingSearches { get; set; } = new();
    public List<SearchQueueItem> ActiveSearches { get; set; } = new();
    public List<SearchQueueItem> RecentlyCompleted { get; set; } = new();
}
