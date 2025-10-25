using System.Collections.Concurrent;
using Fightarr.Api.Data;
using Fightarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Fightarr.Api.Services;

/// <summary>
/// Service for managing the task queue and execution
/// Similar to Sonarr/Radarr command queue system
/// </summary>
public class TaskService
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
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();

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
        // Prevent multiple queue processors running at once
        if (!await _taskLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();

            // Check if there's already a running task
            var runningTask = await db.Tasks
                .Where(t => t.Status == Models.TaskStatus.Running)
                .FirstOrDefaultAsync();

            if (runningTask != null)
            {
                _logger.LogDebug("[TASK] Task already running: {Name}", runningTask.Name);
                return;
            }

            // Get next queued task by priority
            var nextTask = await db.Tasks
                .Where(t => t.Status == Models.TaskStatus.Queued)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.Queued)
                .FirstOrDefaultAsync();

            if (nextTask == null)
            {
                _logger.LogDebug("[TASK] No queued tasks to process");
                return;
            }

            // Execute the task
            await ExecuteTaskAsync(nextTask.Id);
        }
        finally
        {
            _taskLock.Release();
        }
    }

    /// <summary>
    /// Execute a specific task
    /// </summary>
    private async Task ExecuteTaskAsync(int taskId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();

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
            await db.SaveChangesAsync();
            _cancellationTokens.TryRemove(taskId, out _);

            // Process next task in queue
            _ = ProcessQueueAsync();
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
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();

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
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();

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
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();

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
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();

        return await db.Tasks.FindAsync(taskId);
    }

    /// <summary>
    /// Clean up old completed tasks
    /// </summary>
    public async Task CleanupOldTasksAsync(int keepCount = 100)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();

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
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();

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
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();

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
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();

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
}
