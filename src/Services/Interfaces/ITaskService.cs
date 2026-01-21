using Sportarr.Api.Models;

namespace Sportarr.Api.Services.Interfaces;

/// <summary>
/// Interface for task queue management.
/// Similar to Sonarr/Radarr command queue system.
/// </summary>
public interface ITaskService
{
    /// <summary>
    /// Queue a new task for execution
    /// </summary>
    /// <param name="name">Display name for the task</param>
    /// <param name="commandName">Command identifier</param>
    /// <param name="priority">Execution priority (higher = sooner)</param>
    /// <param name="body">Optional task body/parameters</param>
    Task<AppTask> QueueTaskAsync(string name, string commandName, int priority = 0, string? body = null);

    /// <summary>
    /// Cancel a running or queued task
    /// </summary>
    Task<bool> CancelTaskAsync(int taskId);

    /// <summary>
    /// Get all tasks
    /// </summary>
    /// <param name="limit">Optional limit on returned tasks</param>
    Task<List<AppTask>> GetAllTasksAsync(int? limit = null);

    /// <summary>
    /// Get a specific task by ID
    /// </summary>
    Task<AppTask?> GetTaskAsync(int taskId);

    /// <summary>
    /// Clean up old completed tasks
    /// </summary>
    /// <param name="keepCount">Number of recent tasks to keep</param>
    Task CleanupOldTasksAsync(int keepCount = 100);
}
