using System.Collections.Concurrent;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for managing the task queue and execution.
/// </summary>
public class TaskService : ITaskService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TaskService> _logger;
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _cancellationTokens = new();
    private readonly SemaphoreSlim _taskLock = new(1, 1);

    public TaskService(IServiceScopeFactory scopeFactory, ILogger<TaskService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Queue a new task for execution
    /// </summary>
    public async Task<AppTask> QueueTaskAsync(string name, string commandName, int priority = 0, string? body = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        var task = new AppTask
        {
            Name = name,
            CommandName = commandName,
            Status = Models.TaskStatus.Queued,
            Queued = DateTime.UtcNow,
            Priority = priority,
            Body = body,
            CancellationId = Guid.NewGuid().ToString()
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        _logger.LogInformation("[TASK] Queued task: {Name} (ID: {TaskId})", name, task.Id);

        // Start processing queue
        _ = ProcessQueueAsync();

        return task;
    }

    /// <summary>
    /// Process the task queue
    /// </summary>
    private async Task ProcessQueueAsync()
    {
        // Prevent multiple queue processors running at once. The lock is held
        // for the duration of the drain loop, so we must NOT re-enter from
        // ExecuteTaskAsync's finally — instead the loop body picks up the next
        // queued task itself. Re-entering would deadlock the queue: the
        // recursive call would see the lock still held (we haven't returned
        // to release it yet), bail, and nobody else would re-trigger us.
        if (!await _taskLock.WaitAsync(0))
        {
            return;
        }

        // Whether the loop stopped because there was nothing left, rather
        // than because something went wrong. Only a clean drain re-checks
        // afterwards, so a persistent failure cannot spin.
        var drained = false;
        var consecutiveFailures = 0;

        try
        {
            while (true)
            {
                int nextTaskId;
                try
                {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

                    // Defensive: a previous task that crashed or was killed
                    // mid-flight could leave a Running row. We don't reset it
                    // here — that's a separate recovery concern — but we do
                    // refuse to start a second concurrent task.
                    var runningTask = await db.Tasks
                        .Where(t => t.Status == Models.TaskStatus.Running)
                        .FirstOrDefaultAsync();

                    if (runningTask != null)
                    {
                        // A Running row with no registered cancellation token
                        // belongs to no in-flight execution in this process
                        // (tokens are registered before the row is marked
                        // Running and removed only after its terminal status
                        // persists). It is an orphan — e.g. the terminal
                        // write lost a 'database is locked' fight — and left
                        // alone it wedges the queue until a restart.
                        if (!_cancellationTokens.ContainsKey(runningTask.Id))
                        {
                            _logger.LogWarning(
                                "[TASK] Recovering orphaned running task: {Name} (ID: {TaskId}) — no in-process execution owns it",
                                runningTask.Name, runningTask.Id);
                            runningTask.Status = Models.TaskStatus.Failed;
                            runningTask.Ended = DateTime.UtcNow;
                            runningTask.Duration = runningTask.Ended - runningTask.Started;
                            runningTask.Message = "Recovered: task had no running execution (interrupted terminal status write)";
                            await db.SaveChangesAsync();
                            continue;
                        }

                        _logger.LogDebug("[TASK] Task already running: {Name}", runningTask.Name);
                        break;
                    }

                    var nextTask = await db.Tasks
                        .Where(t => t.Status == Models.TaskStatus.Queued)
                        .OrderByDescending(t => t.Priority)
                        .ThenBy(t => t.Queued)
                        .FirstOrDefaultAsync();

                    if (nextTask == null)
                    {
                        _logger.LogDebug("[TASK] No queued tasks to process");
                        drained = true;
                        break;
                    }

                    nextTaskId = nextTask.Id;
                }

                await ExecuteTaskAsync(nextTaskId);
                consecutiveFailures = 0;
                }
                catch (Exception ex)
                {
                    // A transient database error used to escape the drain
                    // loop entirely. Everything still queued then sat there
                    // until some unrelated task happened to be queued or the
                    // application restarted.
                    consecutiveFailures++;
                    _logger.LogError(ex,
                        "[TASK] Queue processing failed (consecutive failure {Count}/{Max})",
                        consecutiveFailures, MaxConsecutiveQueueFailures);
                    if (consecutiveFailures >= MaxConsecutiveQueueFailures)
                    {
                        _logger.LogError("[TASK] Giving up on this drain. Queued work will be picked up on the next trigger.");
                        break;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2 * consecutiveFailures));
                }
            }
        }
        finally
        {
            _taskLock.Release();
        }

        // Anything queued between the empty read above and the release saw a
        // processor holding the lock, gave up, and left its row waiting for an
        // unrelated trigger. Re-check now that the lock is free.
        if (drained)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
                if (await db.Tasks.AnyAsync(t => t.Status == Models.TaskStatus.Queued))
                {
                    _ = ProcessQueueAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TASK] Could not re-check the queue after draining");
            }
        }
    }

    /// <summary>
    /// How many failures in a row end a drain. Without a ceiling a database
    /// that is down would spin here forever.
    /// </summary>
    private const int MaxConsecutiveQueueFailures = 3;

    /// <summary>
    /// Execute a specific task
    /// </summary>
    private async Task ExecuteTaskAsync(int taskId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        var task = await db.Tasks.FindAsync(taskId);
        if (task == null)
        {
            _logger.LogWarning("[TASK] Task not found: {TaskId}", taskId);
            return;
        }

        // Create cancellation token
        var cts = new CancellationTokenSource();
        _cancellationTokens[taskId] = cts;

        // Update task status to running
        task.Status = Models.TaskStatus.Running;
        task.Started = DateTime.UtcNow;
        task.Progress = 0;
        await db.SaveChangesAsync();

        _logger.LogInformation("[TASK] Starting task: {Name} (ID: {TaskId})", task.Name, task.Id);

        try
        {
            // Execute the task based on command name
            await ExecuteCommandAsync(task, cts.Token);

            // Mark as completed
            task.Status = Models.TaskStatus.Completed;
            task.Ended = DateTime.UtcNow;
            task.Duration = task.Ended - task.Started;
            task.Progress = 100;
            task.Message = "Task completed successfully";

            _logger.LogInformation("[TASK] Completed task: {Name} (ID: {TaskId}) in {Duration}",
                task.Name, task.Id, task.Duration);
        }
        catch (OperationCanceledException)
        {
            task.Status = Models.TaskStatus.Cancelled;
            task.Ended = DateTime.UtcNow;
            task.Duration = task.Ended - task.Started;
            task.Message = "Task was cancelled";

            _logger.LogInformation("[TASK] Cancelled task: {Name} (ID: {TaskId})", task.Name, task.Id);
        }
        catch (Exception ex)
        {
            task.Status = Models.TaskStatus.Failed;
            task.Ended = DateTime.UtcNow;
            task.Duration = task.Ended - task.Started;
            task.Message = ex.Message;
            task.Exception = ex.ToString();

            _logger.LogError(ex, "[TASK] Failed task: {Name} (ID: {TaskId})", task.Name, task.Id);
        }
        finally
        {
            await PersistTerminalStatusAsync(db, task);
            _cancellationTokens.TryRemove(taskId, out _);

            // Don't recursively kick the queue here — the outer ProcessQueueAsync
            // loop is still holding _taskLock and will pick up the next queued
            // task on its next iteration. A re-entrant call would only see the
            // held lock and bail, leaving the queue stuck (the bug this fixes).
        }
    }

    /// <summary>
    /// Persist a task's terminal status with retries. This write must not be
    /// allowed to fail quietly: if the row stays Running, ProcessQueueAsync
    /// refuses to start anything else and the whole queue wedges until a
    /// restart. A busy database (e.g. a full-history sync just committed a
    /// multi-thousand-row transaction) can throw transient 'database is
    /// locked' here — retry on the same context first (EF keeps the pending
    /// changes across a failed SaveChanges), then fall back to a direct
    /// UPDATE on a fresh context that depends on nothing tracked.
    /// </summary>
    private async Task PersistTerminalStatusAsync(SportarrDbContext db, AppTask task)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await db.SaveChangesAsync();
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[TASK] Terminal status write failed for task {TaskId} (attempt {Attempt}/5)",
                    task.Id, attempt);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var freshDb = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            await freshDb.Tasks
                .Where(t => t.Id == task.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, task.Status)
                    .SetProperty(t => t.Ended, task.Ended)
                    .SetProperty(t => t.Duration, task.Duration)
                    .SetProperty(t => t.Progress, task.Progress)
                    .SetProperty(t => t.Message, task.Message)
                    // The structured payload a library import or scan builds
                    // lives here. Leaving it out of the fallback recorded the
                    // task as completed with nothing for the page polling it
                    // to show.
                    .SetProperty(t => t.Result, task.Result)
                    .SetProperty(t => t.Exception, task.Exception));
            _logger.LogInformation(
                "[TASK] Terminal status for task {TaskId} persisted via fallback update", task.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[TASK] Could not persist terminal status for task {TaskId}; the queue's orphan recovery will reclaim it",
                task.Id);
        }
    }

    /// <summary>
    /// Execute command based on command name
    /// </summary>
    private async Task ExecuteCommandAsync(AppTask task, CancellationToken cancellationToken)
    {
        // This is where you would implement actual task logic
        // For now, we'll just simulate some work
        switch (task.CommandName)
        {
            case "TestTask":
                await SimulateWorkAsync(task, cancellationToken);
                break;

            case "IndexerSync":
                await IndexerSyncAsync(task, cancellationToken);
                break;

            case "RssSync":
                await RssSyncAsync(task, cancellationToken);
                break;

            case "RefreshDownloads":
                await RefreshDownloadsAsync(task, cancellationToken);
                break;

            case "EventSearch":
                await EventSearchAsync(task, cancellationToken);
                break;

            case "EpgSync":
                await EpgSyncAsync(task, cancellationToken);
                break;

            case "RefreshLeague":
                await RefreshLeagueAsync(task, cancellationToken);
                break;

            case "LibraryImport":
                await LibraryImportAsync(task, cancellationToken);
                break;

            case "LibraryScan":
                await LibraryScanAsync(task, cancellationToken);
                break;

            default:
                _logger.LogWarning("[TASK] Unknown command: {CommandName}", task.CommandName);
                await SimulateWorkAsync(task, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Simulate work with progress updates (for testing)
    /// </summary>
    private async Task SimulateWorkAsync(AppTask task, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        for (int i = 0; i <= 100; i += 10)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Update progress
            var dbTask = await db.Tasks.FindAsync(task.Id);
            if (dbTask != null)
            {
                dbTask.Progress = i;
                dbTask.Message = $"Processing... {i}%";
                await db.SaveChangesAsync();
            }

            await Task.Delay(500, cancellationToken);
        }
    }

    /// <summary>
    /// Cancel a running task
    /// </summary>
    public async Task<bool> CancelTaskAsync(int taskId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        var task = await db.Tasks.FindAsync(taskId);
        if (task == null)
        {
            _logger.LogWarning("[TASK] Cannot cancel - task not found: {TaskId}", taskId);
            return false;
        }

        if (task.Status != Models.TaskStatus.Running && task.Status != Models.TaskStatus.Queued)
        {
            _logger.LogWarning("[TASK] Cannot cancel - task status is {Status}: {TaskId}", task.Status, taskId);
            return false;
        }

        if (task.Status == Models.TaskStatus.Queued)
        {
            // Just mark as cancelled
            task.Status = Models.TaskStatus.Cancelled;
            task.Ended = DateTime.UtcNow;
            task.Duration = task.Ended - task.Started;
            task.Message = "Task was cancelled before execution";
            await db.SaveChangesAsync();

            _logger.LogInformation("[TASK] Cancelled queued task: {Name} (ID: {TaskId})", task.Name, task.Id);

            // Process next task
            _ = ProcessQueueAsync();
            return true;
        }

        // Cancel running task
        if (_cancellationTokens.TryGetValue(taskId, out var cts))
        {
            task.Status = Models.TaskStatus.Aborting;
            await db.SaveChangesAsync();

            _logger.LogInformation("[TASK] Cancelling running task: {Name} (ID: {TaskId})", task.Name, task.Id);
            cts.Cancel();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get all tasks
    /// </summary>
    public async Task<List<AppTask>> GetAllTasksAsync(int? limit = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        var query = db.Tasks
            .OrderByDescending(t => t.Queued)
            .AsQueryable();

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToListAsync();
    }

    /// <summary>
    /// Get task by ID
    /// </summary>
    public async Task<AppTask?> GetTaskAsync(int taskId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        return await db.Tasks.FindAsync(taskId);
    }

    /// <summary>
    /// Clean up old completed tasks
    /// </summary>
    public async Task RecoverAndResumeAsync()
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

            // Any task still marked Running at startup belongs to a previous
            // process that died before it could mark the row Completed/Failed.
            // Leaving it Running blocks the queue (ProcessQueueAsync refuses to
            // start a second concurrent task), so flip it back to Queued. We
            // prefer requeue over fail so legitimate work gets retried after
            // a restart.
            var orphans = await db.Tasks
                .Where(t => t.Status == Models.TaskStatus.Running)
                .ToListAsync();

            foreach (var orphan in orphans)
            {
                orphan.Status = Models.TaskStatus.Queued;
                orphan.Started = null;
                orphan.Progress = 0;
                _logger.LogWarning("[TASK] Requeuing orphan Running task left over from prior process: {Name} (ID: {TaskId})",
                    orphan.Name, orphan.Id);
            }

            if (orphans.Count > 0)
            {
                await db.SaveChangesAsync();
            }
        }

        _ = ProcessQueueAsync();
    }

    public async Task CleanupOldTasksAsync(int keepCount = 100)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        var completedTasks = await db.Tasks
            .Where(t => t.Status == Models.TaskStatus.Completed ||
                       t.Status == Models.TaskStatus.Failed ||
                       t.Status == Models.TaskStatus.Cancelled)
            .OrderByDescending(t => t.Ended)
            .Skip(keepCount)
            .ToListAsync();

        if (completedTasks.Any())
        {
            db.Tasks.RemoveRange(completedTasks);
            await db.SaveChangesAsync();

            _logger.LogInformation("[TASK] Cleaned up {Count} old tasks", completedTasks.Count);
        }
    }

    /// <summary>
    /// Sync events from configured indexers (check for new releases)
    /// </summary>
    private async Task IndexerSyncAsync(AppTask task, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        try
        {
            var dbTask = await db.Tasks.FindAsync(task.Id);
            if (dbTask != null)
            {
                dbTask.Progress = 10;
                dbTask.Message = "Loading indexers...";
                await db.SaveChangesAsync();
            }

            // Get all enabled indexers
            var indexers = await db.Indexers
                .Where(i => i.Enabled && i.EnableAutomaticSearch)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("[INDEXER SYNC] Found {Count} enabled indexers for sync", indexers.Count);

            if (indexers.Count == 0)
            {
                if (dbTask != null)
                {
                    dbTask.Progress = 100;
                    dbTask.Message = "No enabled indexers found";
                    await db.SaveChangesAsync();
                }
                return;
            }

            // Get monitored events that don't have files
            var monitoredEvents = await db.Events
                .Where(e => e.Monitored && !e.HasFile)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("[INDEXER SYNC] Found {Count} monitored events without files", monitoredEvents.Count);

            if (dbTask != null)
            {
                dbTask.Progress = 30;
                dbTask.Message = $"Checking {indexers.Count} indexers for {monitoredEvents.Count} events...";
                await db.SaveChangesAsync();
            }

            int totalFound = 0;
            int progressStep = indexers.Count > 0 ? 60 / indexers.Count : 60;
            int currentProgress = 30;

            // Check each indexer for releases
            foreach (var indexer in indexers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation("[INDEXER SYNC] Checking indexer: {Name}", indexer.Name);

                if (dbTask != null)
                {
                    currentProgress = Math.Min(90, currentProgress + progressStep);
                    dbTask.Progress = currentProgress;
                    dbTask.Message = $"Checking {indexer.Name}...";
                    await db.SaveChangesAsync();
                }

                // Note: Actual indexer search logic would go here
                // This would typically call IndexerSearchService to search for each event
                // For now, we log that the check was performed
                await Task.Delay(500, cancellationToken); // Simulate API call
            }

            if (dbTask != null)
            {
                dbTask.Progress = 100;
                dbTask.Message = $"Sync complete - checked {indexers.Count} indexers";
                await db.SaveChangesAsync();
            }

            _logger.LogInformation("[INDEXER SYNC] Completed - checked {Count} indexers, found {Found} new releases",
                indexers.Count, totalFound);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INDEXER SYNC] Error during indexer sync");
            throw;
        }
    }

    /// <summary>
    /// Check RSS feeds for new releases
    /// </summary>
    private async Task RssSyncAsync(AppTask task, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        try
        {
            var dbTask = await db.Tasks.FindAsync(task.Id);
            if (dbTask != null)
            {
                dbTask.Progress = 10;
                dbTask.Message = "Loading indexers with RSS enabled...";
                await db.SaveChangesAsync();
            }

            // Get all enabled indexers with RSS enabled
            var indexers = await db.Indexers
                .Where(i => i.Enabled && i.EnableRss)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("[RSS SYNC] Found {Count} indexers with RSS enabled", indexers.Count);

            if (indexers.Count == 0)
            {
                if (dbTask != null)
                {
                    dbTask.Progress = 100;
                    dbTask.Message = "No RSS-enabled indexers found";
                    await db.SaveChangesAsync();
                }
                return;
            }

            if (dbTask != null)
            {
                dbTask.Progress = 30;
                dbTask.Message = $"Checking RSS feeds from {indexers.Count} indexers...";
                await db.SaveChangesAsync();
            }

            int totalNewReleases = 0;
            int progressStep = indexers.Count > 0 ? 60 / indexers.Count : 60;
            int currentProgress = 30;

            // Check RSS feed for each indexer
            foreach (var indexer in indexers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation("[RSS SYNC] Checking RSS for: {Name}", indexer.Name);

                if (dbTask != null)
                {
                    currentProgress = Math.Min(90, currentProgress + progressStep);
                    dbTask.Progress = currentProgress;
                    dbTask.Message = $"Checking RSS: {indexer.Name}...";
                    await db.SaveChangesAsync();
                }

                // Note: Actual RSS feed parsing logic would go here
                // This would typically fetch the RSS feed URL and parse new releases
                // For now, we log that the check was performed
                await Task.Delay(300, cancellationToken); // Simulate RSS fetch
            }

            if (dbTask != null)
            {
                dbTask.Progress = 100;
                dbTask.Message = $"RSS sync complete - checked {indexers.Count} feeds, found {totalNewReleases} new releases";
                await db.SaveChangesAsync();
            }

            _logger.LogInformation("[RSS SYNC] Completed - checked {Count} feeds, found {Found} new releases",
                indexers.Count, totalNewReleases);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RSS SYNC] Error during RSS sync");
            throw;
        }
    }

    /// <summary>
    /// Refresh download queue status from download clients
    /// </summary>
    private async Task RefreshDownloadsAsync(AppTask task, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        try
        {
            var dbTask = await db.Tasks.FindAsync(task.Id);
            if (dbTask != null)
            {
                dbTask.Progress = 10;
                dbTask.Message = "Loading download clients...";
                await db.SaveChangesAsync();
            }

            // Get all enabled download clients
            var downloadClients = await db.DownloadClients
                .Where(dc => dc.Enabled)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("[DOWNLOAD REFRESH] Found {Count} enabled download clients", downloadClients.Count);

            if (downloadClients.Count == 0)
            {
                if (dbTask != null)
                {
                    dbTask.Progress = 100;
                    dbTask.Message = "No download clients configured";
                    await db.SaveChangesAsync();
                }
                return;
            }

            if (dbTask != null)
            {
                dbTask.Progress = 30;
                dbTask.Message = $"Refreshing status from {downloadClients.Count} download clients...";
                await db.SaveChangesAsync();
            }

            int totalActive = 0;
            int totalCompleted = 0;
            int progressStep = downloadClients.Count > 0 ? 60 / downloadClients.Count : 60;
            int currentProgress = 30;

            // Check each download client
            foreach (var client in downloadClients)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation("[DOWNLOAD REFRESH] Checking client: {Name}", client.Name);

                if (dbTask != null)
                {
                    currentProgress = Math.Min(90, currentProgress + progressStep);
                    dbTask.Progress = currentProgress;
                    dbTask.Message = $"Checking {client.Name}...";
                    await db.SaveChangesAsync();
                }

                // Note: Actual download client API calls would go here
                // This would fetch current downloads and update their status in the database
                // Status updates: downloading -> completed, update progress percentages, etc.
                await Task.Delay(200, cancellationToken); // Simulate API call
            }

            if (dbTask != null)
            {
                dbTask.Progress = 100;
                dbTask.Message = $"Refresh complete - {totalActive} active, {totalCompleted} completed";
                await db.SaveChangesAsync();
            }

            _logger.LogInformation("[DOWNLOAD REFRESH] Completed - checked {Count} clients, {Active} active downloads, {Completed} completed",
                downloadClients.Count, totalActive, totalCompleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DOWNLOAD REFRESH] Error during download refresh");
            throw;
        }
    }

    /// <summary>
    /// Search for an event across indexers
    /// </summary>
    private async Task EventSearchAsync(AppTask task, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var automaticSearchService = scope.ServiceProvider.GetRequiredService<AutomaticSearchService>();

        try
        {
            // Parse event ID and optional part from body
            // Format: "{eventId}" or "{eventId}|{part}" (e.g., "123" or "123|Early Prelims")
            if (string.IsNullOrEmpty(task.Body))
            {
                throw new ArgumentException("Event ID required in task body");
            }

            int eventId;
            string? part = null;

            // Check if body contains part information (multi-part episode search)
            var bodyParts = task.Body.Split('|');
            if (bodyParts.Length > 1)
            {
                if (!int.TryParse(bodyParts[0], out eventId))
                {
                    throw new ArgumentException($"Invalid event ID: {bodyParts[0]}");
                }
                part = bodyParts[1];
                _logger.LogInformation("[EVENT SEARCH] Parsed multi-part search - Event ID: {EventId}, Part: {Part}", eventId, part);
            }
            else
            {
                if (!int.TryParse(task.Body, out eventId))
                {
                    throw new ArgumentException($"Invalid event ID: {task.Body}");
                }
            }

            var dbTask = await db.Tasks.FindAsync(task.Id);
            if (dbTask != null)
            {
                dbTask.Progress = 10;
                dbTask.Message = "Loading event details...";
                await db.SaveChangesAsync();
            }

            // Get event
            var evt = await db.Events.FindAsync(eventId);
            if (evt == null)
            {
                throw new Exception($"Event not found: {eventId}");
            }

            var searchTarget = part != null ? $"{evt.Title} ({part})" : evt.Title;
            _logger.LogInformation("[EVENT SEARCH] Starting search for: {Title}", searchTarget);

            if (dbTask != null)
            {
                dbTask.Progress = 20;
                dbTask.Message = $"Searching indexers for: {searchTarget}...";
                await db.SaveChangesAsync();
            }

            // Get all enabled indexers
            var indexers = await db.Indexers
                .Where(i => i.Enabled && i.EnableAutomaticSearch)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("[EVENT SEARCH] Found {Count} enabled indexers", indexers.Count);

            if (dbTask != null)
            {
                dbTask.Progress = 30;
                dbTask.Message = $"Searching {indexers.Count} indexers...";
                await db.SaveChangesAsync();
            }

            // Perform the search (with optional part information for multi-part episodes)
            // NOTE: Task-based searches initiated by user clicking "Auto Search" are manual searches
            // They should work regardless of monitored status - only background scheduled searches check monitored flag
            var result = await automaticSearchService.SearchAndDownloadEventAsync(eventId, null, part, isManualSearch: true);

            if (dbTask != null)
            {
                dbTask.Progress = 90;
                if (result.Success)
                {
                    dbTask.Message = $"Found {result.ReleasesFound} releases - Downloaded: {result.SelectedRelease}";
                }
                else
                {
                    dbTask.Message = $"Search completed - {result.Message}";
                }
                await db.SaveChangesAsync();
            }

            _logger.LogInformation("[EVENT SEARCH] Completed - Found {Count} releases, Success: {Success}",
                result.ReleasesFound, result.Success);

            if (!result.Success && result.ReleasesFound == 0)
            {
                throw new Exception($"No releases found for: {evt.Title}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EVENT SEARCH] Error during event search");
            throw;
        }
    }

    /// <summary>
    /// Sync EPG data from all active sources
    /// </summary>
    private async Task EpgSyncAsync(AppTask task, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var epgService = scope.ServiceProvider.GetRequiredService<EpgService>();

        try
        {
            var dbTask = await db.Tasks.FindAsync(task.Id);
            if (dbTask != null)
            {
                dbTask.Progress = 10;
                dbTask.Message = "Loading EPG sources...";
                await db.SaveChangesAsync();
            }

            // Get all active EPG sources
            var sources = await db.EpgSources
                .Where(s => s.IsActive)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("[EPG SYNC] Found {Count} active EPG sources", sources.Count);

            if (sources.Count == 0)
            {
                if (dbTask != null)
                {
                    dbTask.Progress = 100;
                    dbTask.Message = "No active EPG sources found";
                    await db.SaveChangesAsync();
                }
                return;
            }

            if (dbTask != null)
            {
                dbTask.Progress = 20;
                dbTask.Message = $"Syncing {sources.Count} EPG sources...";
                await db.SaveChangesAsync();
            }

            int totalPrograms = 0;
            int totalChannels = 0;
            int successCount = 0;
            int failCount = 0;
            int progressStep = sources.Count > 0 ? 70 / sources.Count : 70;
            int currentProgress = 20;

            // Sync each EPG source
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation("[EPG SYNC] Syncing source: {Name}", source.Name);

                if (dbTask != null)
                {
                    currentProgress = Math.Min(90, currentProgress + progressStep);
                    dbTask.Progress = currentProgress;
                    dbTask.Message = $"Syncing {source.Name}...";
                    await db.SaveChangesAsync();
                }

                var result = await epgService.SyncSourceAsync(source.Id, cancellationToken);

                if (result.Success)
                {
                    successCount++;
                    totalPrograms += result.ProgramCount;
                    totalChannels += result.ChannelCount;
                    _logger.LogInformation("[EPG SYNC] Source {Name} synced - {ProgramCount} programs, {ChannelCount} channels",
                        source.Name, result.ProgramCount, result.ChannelCount);
                }
                else
                {
                    failCount++;
                    _logger.LogWarning("[EPG SYNC] Source {Name} failed: {Error}", source.Name, result.Error);
                }
            }

            // Cleanup old programs
            var deletedPrograms = await epgService.CleanupOldProgramsAsync(1);

            if (dbTask != null)
            {
                dbTask.Progress = 100;
                dbTask.Message = $"EPG sync complete - {successCount}/{sources.Count} sources, {totalPrograms} programs";
                await db.SaveChangesAsync();
            }

            _logger.LogInformation("[EPG SYNC] Completed - {Success}/{Total} sources, {Programs} programs, {Channels} channels, {Deleted} old programs deleted",
                successCount, sources.Count, totalPrograms, totalChannels, deletedPrograms);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EPG SYNC] Error during EPG sync");
            throw;
        }
    }

    /// <summary>
    /// Run a league refresh in the background. Wraps
    /// LeagueEventSyncService.SyncLeagueEventsAsync so the renamer's
    /// existing footer status bar can render live progress next to the
    /// other in-flight tasks. The task body is expected to be a JSON
    /// object of shape {"leagueId": int, "scope": "current"|"full"}.
    /// </summary>
    /// <summary>
    /// Library import as a background task. The import endpoint used to run
    /// file transfers and ffprobe inline in the HTTP request; multi-gigabyte
    /// copies routinely outlived reverse-proxy timeouts and users got 504s
    /// while the import kept running invisibly. The endpoint now queues this
    /// task and the UI polls /api/task/{id}; the full per-file ImportResult
    /// lands in the task's Result column when done.
    /// </summary>
    private async Task LibraryImportAsync(AppTask task, CancellationToken cancellationToken)
    {
        List<FileImportRequest> requests;
        try
        {
            requests = System.Text.Json.JsonSerializer.Deserialize<List<FileImportRequest>>(
                task.Body ?? "[]",
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TASK] LibraryImport task {TaskId} has invalid body", task.Id);
            throw;
        }

        if (requests.Count == 0)
        {
            throw new InvalidOperationException("Library import task has no file requests");
        }

        // Short-lived scope per progress write, same rationale as the league
        // refresh task above.
        Func<int, string, Task> onProgress = async (pct, msg) =>
        {
            try
            {
                using var s = _scopeFactory.CreateScope();
                var d = s.ServiceProvider.GetRequiredService<SportarrDbContext>();
                var dbTask = await d.Tasks.FindAsync(task.Id);
                if (dbTask != null)
                {
                    dbTask.Progress = pct;
                    dbTask.Message = msg;
                    await d.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TASK] Failed to write progress for import task {TaskId}", task.Id);
            }
        };

        await onProgress(1, $"Importing 0/{requests.Count} files");

        using var scope = _scopeFactory.CreateScope();
        var importService = scope.ServiceProvider.GetRequiredService<LibraryImportService>();

        var result = await importService.ImportFilesAsync(requests, async (done, total) =>
        {
            var pct = Math.Clamp((int)(done * 100.0 / Math.Max(1, total)), 1, 99);
            await onProgress(pct, $"Importing {done}/{total} files");
        });

        // Set the structured result on the runner's tracked entity - the
        // terminal-status save that follows this method persists it. Camel
        // case so the polling frontend parses it exactly like the old inline
        // response body.
        task.Result = System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
    }

    private async Task LibraryScanAsync(AppTask task, CancellationToken cancellationToken)
    {
        string folderPath;
        bool includeSubfolders;
        try
        {
            var body = System.Text.Json.JsonDocument.Parse(task.Body ?? "{}").RootElement;
            folderPath = body.GetProperty("folderPath").GetString() ?? "";
            includeSubfolders = !body.TryGetProperty("includeSubfolders", out var inc) || inc.GetBoolean();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TASK] LibraryScan task {TaskId} has invalid body", task.Id);
            throw;
        }

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new InvalidOperationException("Library scan task has no folder path");
        }

        // Short-lived scope per progress write, same rationale as the
        // library import task above.
        Func<int, int, Task> onProgress = async (done, total) =>
        {
            try
            {
                using var s = _scopeFactory.CreateScope();
                var d = s.ServiceProvider.GetRequiredService<SportarrDbContext>();
                var dbTask = await d.Tasks.FindAsync(task.Id);
                if (dbTask != null)
                {
                    dbTask.Progress = Math.Clamp((int)(done * 100.0 / Math.Max(1, total)), 1, 99);
                    dbTask.Message = $"Scanning {done}/{total} files";
                    await d.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TASK] Failed to write progress for scan task {TaskId}", task.Id);
            }
        };

        await onProgress(0, 1);

        using var scope = _scopeFactory.CreateScope();
        var importService = scope.ServiceProvider.GetRequiredService<LibraryImportService>();

        var result = await importService.ScanFolderAsync(folderPath, includeSubfolders, onProgress);

        // Same rationale as LibraryImportAsync above: the result column
        // carries the same LibraryScanResult shape the old inline response
        // body used, so the polling frontend parses it identically.
        task.Result = System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
    }

    private async Task RefreshLeagueAsync(AppTask task, CancellationToken cancellationToken)
    {
        int leagueId;
        string scope;
        try
        {
            var body = System.Text.Json.JsonDocument.Parse(task.Body ?? "{}").RootElement;
            leagueId = body.GetProperty("leagueId").GetInt32();
            scope = body.TryGetProperty("scope", out var s) ? (s.GetString() ?? "current") : "current";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TASK] RefreshLeague task {TaskId} has invalid body: {Body}", task.Id, task.Body);
            throw;
        }

        using var scope_ = _scopeFactory.CreateScope();
        var syncService = scope_.ServiceProvider.GetRequiredService<LeagueEventSyncService>();

        // Progress callback opens a SHORT-lived scope per update so each
        // SaveChanges runs against its own DbContext — keeps the long
        // sync's DbContext clean of write contention from the progress
        // updates and avoids the SyncService's tracked entities getting
        // flushed mid-loop just because a status row was nudged.
        Func<int, string, Task> onProgress = async (pct, msg) =>
        {
            try
            {
                using var s = _scopeFactory.CreateScope();
                var d = s.ServiceProvider.GetRequiredService<SportarrDbContext>();
                var dbTask = await d.Tasks.FindAsync(task.Id);
                if (dbTask != null)
                {
                    dbTask.Progress = pct;
                    dbTask.Message = msg;
                    await d.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                // Progress reporting must never abort the sync.
                _logger.LogWarning(ex, "[TASK] Failed to write progress for refresh task {TaskId}", task.Id);
            }
        };

        await onProgress(1, $"Queued refresh for league {leagueId}, scope={scope}");

        var fullHistoricalSync = string.Equals(scope, "full", StringComparison.OrdinalIgnoreCase);

        // "current" scope is now an immediate hub changes poll: the feed
        // names exactly what changed (per league, per season, historical
        // included), so asking the hub "what changed right now" replaces
        // the blind current/future season walk. The poll is global - it
        // applies pending changes for every monitored league, not just the
        // one whose button was clicked, which is strictly more useful and
        // costs less. "full" remains the blind walk of every season for
        // recovery (restored DB, install offline past feed retention).
        if (!fullHistoricalSync)
        {
            var poller = scope_.ServiceProvider.GetRequiredService<HubChangesPollerService>();
            await onProgress(10, "Checking the hub for changes...");
            var summary = await poller.PollNowAsync(cancellationToken);
            _logger.LogInformation("[TASK] Hub change check complete for task {TaskId}: {Summary}", task.Id, summary);

            // The poll is cursor-based and global, so it can truthfully
            // report "current" while THIS league is locally missing whole
            // seasons: monitored after its changes flowed, events created
            // before the feed existed, or local rows lost. Verify the
            // league's current/future season coverage against the hub and
            // sync only what is missing.
            await onProgress(60, "Verifying season coverage...");
            var db = scope_.ServiceProvider.GetRequiredService<SportarrDbContext>();
            var league = await db.Leagues.FindAsync(new object[] { leagueId }, cancellationToken);
            if (!string.IsNullOrEmpty(league?.ExternalId))
            {
                var apiClient = scope_.ServiceProvider.GetRequiredService<SportarrApiClient>();
                var hubSeasons = await apiClient.GetAllSeasonsAsync(league!.ExternalId!);
                if (hubSeasons != null && hubSeasons.Count > 0)
                {
                    var localSeasons = await db.Events
                        .Where(e => e.LeagueId == leagueId && e.Season != null)
                        .Select(e => e.Season!)
                        .Distinct()
                        .ToListAsync(cancellationToken);
                    var missing = LeagueEventSyncService.FindMissingCurrentSeasons(
                        hubSeasons.Select(s => s.StrSeason ?? string.Empty), localSeasons);

                    // A season counts as present as soon as one event from it
                    // survives locally, so a season missing forty of its
                    // forty-one events looked complete and a "current" refresh
                    // did nothing about it. The current and future seasons are
                    // resynced regardless, which is what pressing refresh is
                    // asking for. Older seasons still need the full scope.
                    var currentSeasons = hubSeasons
                        .Select(s => s.StrSeason ?? string.Empty)
                        .Where(s => !string.IsNullOrEmpty(s) && LeagueEventSyncService.IsCurrentOrFutureSeason(s))
                        .ToList();
                    foreach (var season in currentSeasons)
                    {
                        if (!missing.Contains(season, StringComparer.OrdinalIgnoreCase))
                        {
                            missing.Add(season);
                        }
                    }

                    if (missing.Count > 0)
                    {
                        _logger.LogInformation(
                            "[TASK] Resyncing {LeagueName} season(s) {Seasons} to close any local gap the change cursor cannot see",
                            league.Name, string.Join(", ", missing));
                        await onProgress(70, $"Syncing season(s): {string.Join(", ", missing)}");
                        var gapResult = await syncService.SyncLeagueEventsAsync(
                            leagueId,
                            seasons: missing,
                            fullHistoricalSync: false,
                            forceRefresh: true,
                            onProgress: onProgress,
                            cancellationToken: cancellationToken);
                        if (!gapResult.Success)
                        {
                            throw new Exception(gapResult.Message ?? "Missing-season sync failed");
                        }
                        await onProgress(100,
                            $"{summary}; recovered {gapResult.NewCount} event(s) from season(s) {string.Join(", ", missing)}");
                        return;
                    }
                }
            }
            await onProgress(100, summary);
            return;
        }

        var result = await syncService.SyncLeagueEventsAsync(
            leagueId,
            seasons: null,
            fullHistoricalSync: fullHistoricalSync,
            forceRefresh: false,
            onProgress: onProgress,
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new Exception(result.Message ?? "League refresh failed");
        }

        await onProgress(100,
            $"Refresh complete: {result.NewCount} new, {result.UpdatedCount} updated, " +
            $"{result.RemovedCount} removed, {result.FailedCount} failed");
    }
}
