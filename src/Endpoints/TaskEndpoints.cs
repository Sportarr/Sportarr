using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Serilog;
using Sportarr.Api.Services;
using Sportarr.Api.Models;
using Sportarr.Api.Validators;

namespace Sportarr.Api.Endpoints;

public static class TaskEndpoints
{
    internal sealed record ScheduledTaskInfo(
        string Id, string Name, string Description, string Interval, bool Triggerable, string? ConfirmMessage = null);

    // Curated view of the recurring background services for the System >
    // Tasks page. Intervals mirror each service's actual loop (config-
    // driven ones say so). Triggerable = a manual "run now" exists, either
    // as a TaskService command or a direct service method. A task with a
    // ConfirmMessage deletes things, so the page asks before it runs it.
    internal static readonly ScheduledTaskInfo[] ScheduledTasks =
    {
        new("rss-sync", "RSS Sync", "Fetch indexer RSS feeds and grab matching releases", "Configurable (default 15 min)", true),
        new("refresh-downloads", "Refresh Downloads", "Poll download clients and update queue state", "30 sec", true),
        new("disk-scan", "Disk Scan", "Verify library files exist and discover untracked files", "60 min", true),
        new("hub-changes", "Hub Changes Poll", "Pull event and league updates from sportarr.net", "15 min", true),
        new("epg-sync", "EPG Sync", "Refresh TV guide data from all EPG sources", "On schedule / manual", true),
        new("indexer-sync", "Indexer Sync", "Test indexers and refresh their capabilities", "Manual", true),
        new("backlog-search", "Backlog Search", "Search for missing and cutoff-unmet events", "Configurable", true),
        new("import-list-sync", "Import List Sync", "Sync enabled import lists", "6 hours", true),
        new("tv-schedule-sync", "TV Schedule Sync", "Refresh upcoming broadcast schedule data", "12 hours", true),
        new("trash-sync", "TRaSH Guides Sync", "Sync custom formats and quality definitions", "60 min", true),
        new("dvr-auto-scheduler", "DVR Auto Scheduler", "Schedule recordings for monitored events", "15 min", true),
        new("catchup-downloads", "Catchup Downloads", "Download finished events from provider archives", "5 min", true),
        new("health-check", "Health Check", "Evaluate system health and notify on changes", "15 min", true),
        new("housekeeping", "Housekeeping", "Scheduled backups, recycle bin cleanup, retention, log pruning", "24 hours", true,
            "Housekeeping empties old items from the recycle bin and removes old DVR recordings and old log entries. Run it now?"),
        new("event-retention", "Event Retention", "Unmonitor and clean up events past the retention window", "24 hours (opt-in)", true,
            "Event Retention deletes the files of events past their league's retention window and unmonitors those events. Run it now?"),
    };

    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        // API: Scheduled background services (recurring), as opposed to the
        // one-off command queue below.
        app.MapGet("/api/task/scheduled", () => Results.Ok(ScheduledTasks));

        // API: Manually trigger a scheduled service that supports it.
        app.MapPost("/api/task/scheduled/{id}/trigger", (string id, IServiceProvider services) =>
            TriggerScheduledTaskAsync(id, services));

        // API: Get all tasks (with optional limit)
        app.MapGet("/api/task", async (TaskService taskService, int? pageSize) =>
        {
            try
            {
                var tasks = await taskService.GetAllTasksAsync(pageSize);
                return Results.Ok(tasks);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TASK API] Error getting tasks");
                return Results.Problem("Error getting tasks");
            }
        });

        // API: Get specific task
        app.MapGet("/api/task/{id:int}", async (int id, TaskService taskService) =>
        {
            try
            {
                var task = await taskService.GetTaskAsync(id);
                return task is null ? Results.NotFound() : Results.Ok(task);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TASK API] Error getting task {TaskId}", id);
                return Results.Problem("Error getting task");
            }
        });

        // API: Queue a new task (for testing)
        app.MapPost("/api/task", async (TaskService taskService, TaskRequest request) =>
        {
            try
            {
                var task = await taskService.QueueTaskAsync(
                    request.Name,
                    request.CommandName,
                    request.Priority ?? 0,
                    request.Body
                );
                return Results.Created($"/api/task/{task.Id}", task);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TASK API] Error queueing task");
                return Results.Problem("Error queueing task");
            }
        }).WithRequestValidation<TaskRequest>();

        // API: Cancel a task
        app.MapDelete("/api/task/{id:int}", async (int id, TaskService taskService) =>
        {
            try
            {
                var success = await taskService.CancelTaskAsync(id);
                if (!success)
                {
                    return Results.NotFound(new { message = "Task not found or cannot be cancelled" });
                }
                return Results.Ok(new { message = "Task cancelled successfully" });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TASK API] Error cancelling task {TaskId}", id);
                return Results.Problem("Error cancelling task");
            }
        });

        // API: Clean up old tasks
        app.MapPost("/api/task/cleanup", async (TaskService taskService, int? keepCount) =>
        {
            try
            {
                await taskService.CleanupOldTasksAsync(keepCount ?? 100);
                return Results.Ok(new { message = "Old tasks cleaned up successfully" });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TASK API] Error cleaning up tasks");
                return Results.Problem("Error cleaning up tasks");
            }
        });

        return app;
    }

    internal static async Task<IResult> TriggerScheduledTaskAsync(string id, IServiceProvider services)
    {
        switch (id)
        {
            case "rss-sync":
                await services.GetRequiredService<TaskService>().QueueTaskAsync("RSS Sync", "RssSync");
                return Results.Ok(new { queued = true });
            case "refresh-downloads":
                await services.GetRequiredService<TaskService>().QueueTaskAsync("Refresh Downloads", "RefreshDownloads");
                return Results.Ok(new { queued = true });
            case "epg-sync":
                await services.GetRequiredService<TaskService>().QueueTaskAsync("EPG Sync", "EpgSync");
                return Results.Ok(new { queued = true });
            case "indexer-sync":
                await services.GetRequiredService<TaskService>().QueueTaskAsync("Indexer Sync", "IndexerSync");
                return Results.Ok(new { queued = true });
            case "disk-scan":
                services.GetRequiredService<DiskScanService>().TriggerScanNow();
                return Results.Ok(new { queued = true });
            case "hub-changes":
                _ = services.GetRequiredService<HubChangesPollerService>().PollNowAsync(CancellationToken.None);
                return Results.Ok(new { queued = true });
            case "backlog-search":
                return await StartPassAsync(id, "BacklogSearch", services.GetRequiredService<BacklogSearchService>(), services);
            case "import-list-sync":
                return await StartPassAsync(id, "ImportListSync", services.GetRequiredService<ImportListSyncService>(), services);
            case "tv-schedule-sync":
                return await StartPassAsync(id, "TvScheduleSync", services.GetRequiredService<TvScheduleSyncService>(), services);
            case "trash-sync":
                return await StartPassAsync(id, "TrashSync", services.GetRequiredService<TrashSyncBackgroundService>(), services);
            case "dvr-auto-scheduler":
                return await StartPassAsync(id, "DvrAutoSchedule", services.GetRequiredService<DvrAutoSchedulerService>(), services);
            case "catchup-downloads":
                return await StartPassAsync(id, "CatchupDownloads", services.GetRequiredService<CatchupDownloadService>(), services);
            case "health-check":
                return await StartPassAsync(id, "HealthCheck", services.GetRequiredService<HealthCheckMonitorService>(), services);
            case "housekeeping":
                return await StartPassAsync(id, "Housekeeping", services.GetRequiredService<HousekeepingService>(), services);
            case "event-retention":
                return await StartPassAsync(id, "EventRetention", services.GetRequiredService<EventRetentionService>(), services);
            default:
                return Results.BadRequest(new { error = $"'{id}' does not support manual triggering" });
        }
    }

    // A manual run gets a row in the task list, so it shows as running and
    // stays in the task history. It runs outside the task queue.
    private static async Task<IResult> StartPassAsync(string id, string commandName, IManualPassService service, IServiceProvider services)
    {
        var name = ScheduledTasks.First(t => t.Id == id).Name;
        var task = await services.GetRequiredService<TaskService>().StartManualPassAsync(name, commandName, service);
        return task == null
            ? Results.Conflict(new { error = $"{name} is already running" })
            : Results.Ok(new { queued = true, taskId = task.Id });
    }
}
