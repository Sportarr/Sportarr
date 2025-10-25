namespace Fightarr.Api.Models;

/// <summary>
/// Health check result for system monitoring
/// </summary>
public class HealthCheckResult
{
    public HealthCheckType Type { get; set; }
    public HealthCheckLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Types of health checks performed by the system
/// </summary>
public enum HealthCheckType
{
    // Configuration checks
    RootFolderMissing,
    RootFolderInaccessible,
    DownloadClientUnavailable,
    IndexerUnavailable,

    // System resource checks
    DiskSpaceLow,
    DiskSpaceCritical,

    // Application state checks
    UpdateAvailable,
    DatabaseMigrationNeeded,

    // Integration checks
    MetadataApiUnavailable,
    NotificationTestFailed,

    // Security checks
    AuthenticationDisabled,
    ApiKeyMissing,

    // Data integrity checks
    OrphanedEvents,
    CorruptedDatabase
}

/// <summary>
/// Severity level of a health check issue
/// </summary>
public enum HealthCheckLevel
{
    /// <summary>
    /// Everything is working correctly
    /// </summary>
    Ok = 0,

    /// <summary>
    /// Minor issue that should be addressed
    /// </summary>
    Notice = 1,

    /// <summary>
    /// Warning that may affect functionality
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Critical issue requiring immediate attention
    /// </summary>
    Error = 3
}
