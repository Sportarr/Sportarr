using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Serilog;
using Sportarr.Api.Services;
using Sportarr.Api.Models;

namespace Sportarr.Api.Endpoints;

public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
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
        });

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
