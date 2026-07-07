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
    private sealed record ScheduledTaskInfo(string Id, string Name, string Description, string Interval, bool Triggerable);

    // Curated view of the recurring background services for the System >
    // Tasks page. Intervals mirror each service's actual loop (config-
    // driven ones say so). Triggerable = a manual "run now" exists, either
    // as a TaskService command or a direct service method.
    private static readonly ScheduledTaskInfo[] ScheduledTasks =
    {
        new("rss-sync", "RSS Sync", "Fetch indexer RSS feeds and grab matching releases", "Configurable (default 15 min)", true),
        new("refresh-downloads", "Refresh Downloads", "Poll download clients and update queue state", "30 sec", true),
        new("disk-scan", "Disk Scan", "Verify library files exist and discover untracked files", "60 min", true),
        new("hub-changes", "Hub Changes Poll", "Pull event and league updates from sportarr.net", "15 min", true),
        new("epg-sync", "EPG Sync", "Refresh TV guide data from all EPG sources", "On schedule / manual", true),
        new("indexer-sync", "Indexer Sync", "Test indexers and refresh their capabilities", "Manual", true),
        new("backlog-search", "Backlog Search", "Search for missing and cutoff-unmet events", "Configurable", false),
        new("import-list-sync", "Import List Sync", "Sync enabled import lists", "6 hours", false),
        new("tv-schedule-sync", "TV Schedule Sync", "Refresh upcoming broadcast schedule data", "12 hours", false),
        new("trash-sync", "TRaSH Guides Sync", "Sync custom formats and quality definitions", "60 min", false),
        new("dvr-auto-scheduler", "DVR Auto Scheduler", "Schedule recordings for monitored events", "15 min", false),
        new("catchup-downloads", "Catchup Downloads", "Download finished events from provider archives", "5 min", false),
        new("health-check", "Health Check", "Evaluate system health and notify on changes", "15 min", false),
        new("housekeeping", "Housekeeping", "Scheduled backups, recycle bin cleanup, retention, log pruning", "24 hours", false),
        new("event-retention", "Event Retention", "Unmonitor and clean up events past the retention window", "24 hours (opt-in)", false),
    };

    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        // API: Scheduled background services (recurring), as opposed to the
        // one-off command queue below.
        app.MapGet("/api/task/scheduled", () => Results.Ok(ScheduledTasks));

        // API: Manually trigger a scheduled service that supports it.
        app.MapPost("/api/task/scheduled/{id}/trigger", async (
            string id,
            TaskService taskService,
            DiskScanService diskScanService,
            HubChangesPollerService hubPoller) =>
        {
            switch (id)
            {
                case "rss-sync":
                    await taskService.QueueTaskAsync("RSS Sync", "RssSync");
                    return Results.Ok(new { queued = true });
                case "refresh-downloads":
                    await taskService.QueueTaskAsync("Refresh Downloads", "RefreshDownloads");
                    return Results.Ok(new { queued = true });
                case "epg-sync":
                    await taskService.QueueTaskAsync("EPG Sync", "EpgSync");
                    return Results.Ok(new { queued = true });
                case "indexer-sync":
                    await taskService.QueueTaskAsync("Indexer Sync", "IndexerSync");
                    return Results.Ok(new { queued = true });
                case "disk-scan":
                    diskScanService.TriggerScanNow();
                    return Results.Ok(new { queued = true });
                case "hub-changes":
                    _ = hubPoller.PollNowAsync(CancellationToken.None);
                    return Results.Ok(new { queued = true });
                default:
                    return Results.BadRequest(new { error = $"'{id}' does not support manual triggering" });
            }
        });

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
}
