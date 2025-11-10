using System.ComponentModel.DataAnnotations;

namespace Sportarr.Api.Models;

/// <summary>
/// Represents a task in the Sportarr task queue system
/// Tracks background operations, scheduled jobs, and manual operations
/// Similar to Sonarr/Radarr command queue implementation
/// </summary>
public class AppTask
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Display name of the task (e.g., "Sync Indexers", "RSS Sync", "Refresh Downloads")
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Internal command name for identifying task type (e.g., "IndexerSync", "RssSync")
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string CommandName { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the task
    /// </summary>
    [Required]
    public TaskStatus Status { get; set; } = TaskStatus.Queued;

    /// <summary>
    /// When the task was added to the queue
    /// </summary>
    public DateTime Queued { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the task started executing (null if not started yet)
    /// </summary>
    public DateTime? Started { get; set; }

    /// <summary>
    /// When the task completed/failed/was cancelled (null if still running)
    /// </summary>
    public DateTime? Ended { get; set; }

    /// <summary>
    /// Duration of task execution (null if not started or still running)
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Status message, error details, or completion message
    /// </summary>
    [MaxLength(2000)]
    public string? Message { get; set; }

    /// <summary>
    /// Progress percentage (0-100, null if not applicable)
    /// </summary>
    public int? Progress { get; set; }

    /// <summary>
    /// Task priority (higher = more important)
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Serialized command body/parameters (JSON)
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Cancellation token source ID for managing cancellation
    /// </summary>
    public string? CancellationId { get; set; }

    /// <summary>
    /// Whether this task can be manually triggered
    /// </summary>
    public bool IsManual { get; set; } = true;

    /// <summary>
    /// Exception details if task failed
    /// </summary>
    [MaxLength(5000)]
    public string? Exception { get; set; }
}

/// <summary>
/// Task execution status
/// </summary>
public enum TaskStatus
{
    /// <summary>
    /// Task is waiting in the queue
    /// </summary>
    Queued = 0,

    /// <summary>
    /// Task is currently executing
    /// </summary>
    Running = 1,

    /// <summary>
    /// Task completed successfully
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Task failed with an error
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Task was cancelled by user or system
    /// </summary>
    Cancelled = 4,

    /// <summary>
    /// Task is waiting to be aborted
    /// </summary>
    Aborting = 5
}
