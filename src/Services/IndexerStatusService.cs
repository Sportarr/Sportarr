using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for managing indexer health status and rate limiting (Enhanced Sonarr-style)
///
/// Key improvements over basic implementation:
/// 1. Separate query vs grab backoffs (Sonarr issue #3132) - grab failures don't prevent searching
/// 2. Connection errors don't escalate (Sonarr pattern) - DNS/network issues are user problems
/// 3. HTTP 429 respects only Retry-After without adding exponential backoff
/// 4. Startup grace period (Lidarr pattern) - don't over-penalize during initialization
///
/// Uses IDbContextFactory to support concurrent indexer searches without DbContext threading issues
/// </summary>
public class IndexerStatusService
{
    private readonly IDbContextFactory<SportarrDbContext> _dbFactory;
    private readonly ILogger<IndexerStatusService> _logger;

    // Track when the service started to implement startup grace period
    private static readonly DateTime _startupTime = DateTime.UtcNow;

    // Startup grace period: limit backoff to 5 minutes max during first 15 minutes
    // This prevents over-penalizing indexers during initialization (matches Lidarr pattern)
    private static readonly TimeSpan StartupGracePeriod = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxBackoffDuringStartup = TimeSpan.FromMinutes(5);

    // Exponential backoff configuration (Sonarr-style)
    private static readonly TimeSpan[] BackoffDurations = new[]
    {
        TimeSpan.FromMinutes(5),    // First failure: 5 minutes
        TimeSpan.FromMinutes(10),   // Second: 10 minutes
        TimeSpan.FromMinutes(20),   // Third: 20 minutes
        TimeSpan.FromMinutes(40),   // Fourth: 40 minutes
        TimeSpan.FromHours(1),      // Fifth: 1 hour
        TimeSpan.FromHours(2),      // Sixth: 2 hours
        TimeSpan.FromHours(4),      // Seventh: 4 hours
        TimeSpan.FromHours(8),      // Eighth: 8 hours
        TimeSpan.FromHours(16),     // Ninth: 16 hours
        TimeSpan.FromHours(24),     // Tenth+: 24 hours (max)
    };

    public IndexerStatusService(
        IDbContextFactory<SportarrDbContext> dbFactory,
        ILogger<IndexerStatusService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Check if we're still in the startup grace period
    /// </summary>
    private bool IsInStartupGracePeriod => DateTime.UtcNow - _startupTime < StartupGracePeriod;

    /// <summary>
    /// Get or create status for an indexer
    /// </summary>
    public async Task<IndexerStatus> GetOrCreateStatusAsync(int indexerId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var status = await db.IndexerStatuses
            .FirstOrDefaultAsync(s => s.IndexerId == indexerId);

        if (status == null)
        {
            status = new IndexerStatus
            {
                IndexerId = indexerId,
                HourResetTime = DateTime.UtcNow.AddHours(1)
            };
            db.IndexerStatuses.Add(status);
            await db.SaveChangesAsync();
        }

        return status;
    }

    /// <summary>
    /// Check if an indexer is available for querying (searching/RSS)
    /// Uses QueryDisabledUntil for query-specific backoff
    /// </summary>
    public async Task<(bool IsAvailable, string? Reason)> IsIndexerAvailableAsync(int indexerId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var status = await db.IndexerStatuses
            .FirstOrDefaultAsync(s => s.IndexerId == indexerId);

        if (status == null)
        {
            status = new IndexerStatus
            {
                IndexerId = indexerId,
                HourResetTime = DateTime.UtcNow.AddHours(1)
            };
            db.IndexerStatuses.Add(status);
            await db.SaveChangesAsync();
        }

        var indexer = await db.Indexers.FindAsync(indexerId);

        if (indexer == null || !indexer.Enabled)
        {
            return (false, "Indexer is disabled");
        }

        // Check query-specific backoff first (new separate tracking)
        if (status.QueryDisabledUntil.HasValue && status.QueryDisabledUntil.Value > DateTime.UtcNow)
        {
            var remaining = status.QueryDisabledUntil.Value - DateTime.UtcNow;
            return (false, $"Temporarily disabled for {remaining.TotalMinutes:F0} minutes after {status.QueryFailures} query failures");
        }

        // Legacy: Check if temporarily disabled due to failures (for backward compatibility)
        if (status.DisabledUntil.HasValue && status.DisabledUntil.Value > DateTime.UtcNow)
        {
            var remaining = status.DisabledUntil.Value - DateTime.UtcNow;
            return (false, $"Temporarily disabled for {remaining.TotalMinutes:F0} minutes after {status.ConsecutiveFailures} consecutive failures");
        }

        // Check if rate limited by HTTP 429
        if (status.RateLimitedUntil.HasValue && status.RateLimitedUntil.Value > DateTime.UtcNow)
        {
            var remaining = status.RateLimitedUntil.Value - DateTime.UtcNow;
            return (false, $"Rate limited by indexer for {remaining.TotalSeconds:F0} seconds");
        }

        // Reset hourly counters if needed
        if (!status.HourResetTime.HasValue || DateTime.UtcNow >= status.HourResetTime.Value)
        {
            status.QueriesThisHour = 0;
            status.GrabsThisHour = 0;
            status.HourResetTime = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
        }

        // Check query limit
        if (indexer.QueryLimit.HasValue && status.QueriesThisHour >= indexer.QueryLimit.Value)
        {
            var resetIn = status.HourResetTime.HasValue ? status.HourResetTime.Value - DateTime.UtcNow : TimeSpan.Zero;
            return (false, $"Query limit ({indexer.QueryLimit}) reached. Resets in {resetIn.TotalMinutes:F0} minutes");
        }

        return (true, null);
    }

    /// <summary>
    /// Check if an indexer is available for grabbing (downloading)
    /// Separate from query availability - per Sonarr #3132, grab failures shouldn't prevent searching
    /// </summary>
    public async Task<(bool IsAllowed, string? Reason)> CanGrabAsync(int indexerId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var status = await db.IndexerStatuses
            .FirstOrDefaultAsync(s => s.IndexerId == indexerId);

        if (status == null)
        {
            status = new IndexerStatus
            {
                IndexerId = indexerId,
                HourResetTime = DateTime.UtcNow.AddHours(1)
            };
            db.IndexerStatuses.Add(status);
            await db.SaveChangesAsync();
        }

        var indexer = await db.Indexers.FindAsync(indexerId);

        if (indexer == null || !indexer.Enabled)
        {
            return (false, "Indexer is disabled");
        }

        // Check grab-specific backoff (new separate tracking)
        if (status.GrabDisabledUntil.HasValue && status.GrabDisabledUntil.Value > DateTime.UtcNow)
        {
            var remaining = status.GrabDisabledUntil.Value - DateTime.UtcNow;
            return (false, $"Grab temporarily disabled for {remaining.TotalMinutes:F0} minutes after {status.GrabFailures} grab failures");
        }

        // Reset hourly counters if needed
        if (!status.HourResetTime.HasValue || DateTime.UtcNow >= status.HourResetTime.Value)
        {
            status.QueriesThisHour = 0;
            status.GrabsThisHour = 0;
            status.HourResetTime = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
        }

        // Check grab limit
        if (indexer.GrabLimit.HasValue && status.GrabsThisHour >= indexer.GrabLimit.Value)
        {
            var resetIn = status.HourResetTime.HasValue ? status.HourResetTime.Value - DateTime.UtcNow : TimeSpan.Zero;
            return (false, $"Grab limit ({indexer.GrabLimit}) reached. Resets in {resetIn.TotalMinutes:F0} minutes");
        }

        return (true, null);
    }

    /// <summary>
    /// Record a successful query to an indexer
    /// </summary>
    public async Task RecordSuccessAsync(int indexerId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var status = await db.IndexerStatuses
            .FirstOrDefaultAsync(s => s.IndexerId == indexerId);

        if (status == null)
        {
            status = new IndexerStatus
            {
                IndexerId = indexerId,
                HourResetTime = DateTime.UtcNow.AddHours(1)
            };
            db.IndexerStatuses.Add(status);
        }

        // Reset query failure counters on success
        status.QueryFailures = 0;
        status.QueryDisabledUntil = null;
        status.LastQueryFailure = null;
        status.LastQueryFailureReason = null;

        // Legacy: Reset old failure counters for backward compatibility
        status.ConsecutiveFailures = 0;
        status.LastFailure = null;
        status.LastFailureReason = null;
        status.DisabledUntil = null;

        // Clear rate limiting on success
        status.RateLimitedUntil = null;

        // Reset connection error tracking on success
        status.ConnectionErrors = 0;
        status.LastConnectionError = null;

        status.LastSuccess = DateTime.UtcNow;

        // Reset hourly counters if needed
        if (!status.HourResetTime.HasValue || DateTime.UtcNow >= status.HourResetTime.Value)
        {
            status.QueriesThisHour = 0;
            status.GrabsThisHour = 0;
            status.HourResetTime = DateTime.UtcNow.AddHours(1);
        }
        status.QueriesThisHour++;

        await db.SaveChangesAsync();

        _logger.LogDebug("[Indexer Status] Recorded success for indexer {IndexerId}", indexerId);
    }

    /// <summary>
    /// Record a successful grab from an indexer
    /// </summary>
    public async Task RecordGrabAsync(int indexerId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var status = await db.IndexerStatuses
            .FirstOrDefaultAsync(s => s.IndexerId == indexerId);

        if (status == null)
        {
            status = new IndexerStatus
            {
                IndexerId = indexerId,
                HourResetTime = DateTime.UtcNow.AddHours(1)
            };
            db.IndexerStatuses.Add(status);
        }

        // Reset grab failure counters on successful grab
        status.GrabFailures = 0;
        status.GrabDisabledUntil = null;
        status.LastGrabFailure = null;
        status.LastGrabFailureReason = null;

        // Reset hourly counters if needed
        if (!status.HourResetTime.HasValue || DateTime.UtcNow >= status.HourResetTime.Value)
        {
            status.QueriesThisHour = 0;
            status.GrabsThisHour = 0;
            status.HourResetTime = DateTime.UtcNow.AddHours(1);
        }
        status.GrabsThisHour++;

        await db.SaveChangesAsync();

        _logger.LogDebug("[Indexer Status] Recorded grab for indexer {IndexerId}", indexerId);
    }

    /// <summary>
    /// Record a query failure for an indexer (implements exponential backoff)
    /// Only for actual indexer errors, NOT for connection/DNS issues
    /// </summary>
    public async Task RecordQueryFailureAsync(int indexerId, string reason)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var status = await db.IndexerStatuses
            .FirstOrDefaultAsync(s => s.IndexerId == indexerId);

        if (status == null)
        {
            status = new IndexerStatus
            {
                IndexerId = indexerId,
                HourResetTime = DateTime.UtcNow.AddHours(1)
            };
            db.IndexerStatuses.Add(status);
        }

        status.QueryFailures++;
        status.LastQueryFailure = DateTime.UtcNow;
        status.LastQueryFailureReason = reason;

        // Calculate backoff duration using exponential backoff
        var backoffIndex = Math.Min(status.QueryFailures - 1, BackoffDurations.Length - 1);
        var backoffDuration = BackoffDurations[backoffIndex];

        // During startup grace period, limit backoff to prevent over-penalizing indexers
        if (IsInStartupGracePeriod && backoffDuration > MaxBackoffDuringStartup)
        {
            _logger.LogInformation("[Indexer Status] Startup grace period active - limiting query backoff from {Original} to {Limited}",
                backoffDuration, MaxBackoffDuringStartup);
            backoffDuration = MaxBackoffDuringStartup;
        }

        status.QueryDisabledUntil = DateTime.UtcNow.Add(backoffDuration);

        await db.SaveChangesAsync();

        _logger.LogWarning("[Indexer Status] Indexer {IndexerId} query failure #{FailureCount}: {Reason}. Query disabled until {DisabledUntil} ({Duration} backoff)",
            indexerId, status.QueryFailures, reason, status.QueryDisabledUntil, backoffDuration);
    }

    /// <summary>
    /// Record a grab failure for an indexer (separate from query failures)
    /// Per Sonarr #3132: grab failures shouldn't prevent searching
    /// </summary>
    public async Task RecordGrabFailureAsync(int indexerId, string reason)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var status = await db.IndexerStatuses
            .FirstOrDefaultAsync(s => s.IndexerId == indexerId);

        if (status == null)
        {
            status = new IndexerStatus
            {
                IndexerId = indexerId,
                HourResetTime = DateTime.UtcNow.AddHours(1)
            };
            db.IndexerStatuses.Add(status);
        }

        status.GrabFailures++;
        status.LastGrabFailure = DateTime.UtcNow;
        status.LastGrabFailureReason = reason;

        // Calculate backoff duration using exponential backoff
        var backoffIndex = Math.Min(status.GrabFailures - 1, BackoffDurations.Length - 1);
        var backoffDuration = BackoffDurations[backoffIndex];

        // During startup grace period, limit backoff
        if (IsInStartupGracePeriod && backoffDuration > MaxBackoffDuringStartup)
        {
            _logger.LogInformation("[Indexer Status] Startup grace period active - limiting grab backoff from {Original} to {Limited}",
                backoffDuration, MaxBackoffDuringStartup);
            backoffDuration = MaxBackoffDuringStartup;
        }

        status.GrabDisabledUntil = DateTime.UtcNow.Add(backoffDuration);

        await db.SaveChangesAsync();

        _logger.LogWarning("[Indexer Status] Indexer {IndexerId} grab failure #{FailureCount}: {Reason}. Grab disabled until {DisabledUntil} ({Duration} backoff)",
            indexerId, status.GrabFailures, reason, status.GrabDisabledUntil, backoffDuration);
    }

    /// <summary>
    /// Record a connection error (DNS, timeout, network issues)
    /// Per Sonarr pattern: connection errors don't escalate backoff - they're likely user network issues
    /// </summary>
    public async Task RecordConnectionErrorAsync(int indexerId, string reason)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var status = await db.IndexerStatuses
            .FirstOrDefaultAsync(s => s.IndexerId == indexerId);

        if (status == null)
        {
            status = new IndexerStatus
            {
                IndexerId = indexerId,
                HourResetTime = DateTime.UtcNow.AddHours(1)
            };
            db.IndexerStatuses.Add(status);
        }

        status.ConnectionErrors++;
        status.LastConnectionError = DateTime.UtcNow;

        await db.SaveChangesAsync();

        // Only log as info, don't disable the indexer - this is likely a user network issue
        _logger.LogInformation("[Indexer Status] Indexer {IndexerId} connection error #{Count}: {Reason}. Not escalating backoff (likely user network issue)",
            indexerId, status.ConnectionErrors, reason);
    }

    /// <summary>
    /// Record a failure for an indexer (legacy method for backward compatibility)
    /// Routes to appropriate new method based on error type
    /// </summary>
    public async Task RecordFailureAsync(int indexerId, string reason)
    {
        // Detect connection errors and route appropriately
        var reasonLower = reason.ToLowerInvariant();
        if (reasonLower.Contains("dns") ||
            reasonLower.Contains("nameresolution") ||
            reasonLower.Contains("timeout") ||
            reasonLower.Contains("connection refused") ||
            reasonLower.Contains("network") ||
            reasonLower.Contains("socket"))
        {
            await RecordConnectionErrorAsync(indexerId, reason);
            return;
        }

        // Default to query failure for other errors
        await RecordQueryFailureAsync(indexerId, reason);
    }

    /// <summary>
    /// Record HTTP 429 rate limit response
    /// Uses ONLY Retry-After, doesn't add exponential backoff on top (Sonarr improvement)
    /// </summary>
    public async Task RecordRateLimitedAsync(int indexerId, TimeSpan? retryAfter = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var status = await db.IndexerStatuses
            .FirstOrDefaultAsync(s => s.IndexerId == indexerId);

        if (status == null)
        {
            status = new IndexerStatus
            {
                IndexerId = indexerId,
                HourResetTime = DateTime.UtcNow.AddHours(1)
            };
            db.IndexerStatuses.Add(status);
        }

        // Use Retry-After if provided, otherwise default to 5 minutes
        // Per Sonarr improvement: use ONLY Retry-After, no additional exponential backoff
        var waitTime = retryAfter ?? TimeSpan.FromMinutes(5);

        // Cap at 1 hour max wait
        if (waitTime > TimeSpan.FromHours(1))
        {
            waitTime = TimeSpan.FromHours(1);
        }

        status.RateLimitedUntil = DateTime.UtcNow.Add(waitTime);

        await db.SaveChangesAsync();

        _logger.LogWarning("[Indexer Status] Indexer {IndexerId} rate limited (HTTP 429). Retry after {WaitTime} (using Retry-After only, no extra backoff)",
            indexerId, waitTime);
    }

    /// <summary>
    /// Get all indexers with their availability status
    /// </summary>
    public async Task<List<(Indexer Indexer, bool IsAvailable, string? Reason)>> GetAllIndexerStatusesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var indexers = await db.Indexers
            .Include(i => i.Status)
            .ToListAsync();

        var results = new List<(Indexer, bool, string?)>();

        foreach (var indexer in indexers)
        {
            var (isAvailable, reason) = await IsIndexerAvailableAsync(indexer.Id);
            results.Add((indexer, isAvailable, reason));
        }

        return results;
    }

    /// <summary>
    /// Get delay before querying an indexer (respects RequestDelayMs)
    /// </summary>
    public async Task<int> GetRequestDelayAsync(int indexerId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var indexer = await db.Indexers.FindAsync(indexerId);
        return indexer?.RequestDelayMs ?? 0;
    }

    /// <summary>
    /// Clear failure history for an indexer (manual reset)
    /// </summary>
    public async Task ClearFailureHistoryAsync(int indexerId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var status = await db.IndexerStatuses
            .FirstOrDefaultAsync(s => s.IndexerId == indexerId);

        if (status == null)
        {
            return; // Nothing to clear
        }

        // Clear query failures
        status.QueryFailures = 0;
        status.QueryDisabledUntil = null;
        status.LastQueryFailure = null;
        status.LastQueryFailureReason = null;

        // Clear grab failures
        status.GrabFailures = 0;
        status.GrabDisabledUntil = null;
        status.LastGrabFailure = null;
        status.LastGrabFailureReason = null;

        // Clear legacy failures
        status.ConsecutiveFailures = 0;
        status.LastFailure = null;
        status.LastFailureReason = null;
        status.DisabledUntil = null;

        // Clear rate limiting
        status.RateLimitedUntil = null;

        // Clear connection errors
        status.ConnectionErrors = 0;
        status.LastConnectionError = null;

        await db.SaveChangesAsync();

        _logger.LogInformation("[Indexer Status] Cleared all failure history for indexer {IndexerId}", indexerId);
    }

    /// <summary>
    /// Clear rate limits for all indexers (manual reset)
    /// </summary>
    public async Task<int> ClearAllRateLimitsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var rateLimitedStatuses = await db.IndexerStatuses
            .Where(s => s.RateLimitedUntil != null || s.DisabledUntil != null ||
                        s.QueryDisabledUntil != null || s.GrabDisabledUntil != null)
            .ToListAsync();

        foreach (var status in rateLimitedStatuses)
        {
            // Clear all backoffs
            status.QueryFailures = 0;
            status.QueryDisabledUntil = null;
            status.GrabFailures = 0;
            status.GrabDisabledUntil = null;
            status.ConsecutiveFailures = 0;
            status.DisabledUntil = null;
            status.RateLimitedUntil = null;
            status.ConnectionErrors = 0;
        }

        await db.SaveChangesAsync();

        _logger.LogInformation("[Indexer Status] Cleared rate limits and backoffs for {Count} indexers", rateLimitedStatuses.Count);
        return rateLimitedStatuses.Count;
    }

    /// <summary>
    /// Get time remaining until indexer is available for queries
    /// </summary>
    public async Task<TimeSpan?> GetTimeUntilAvailableAsync(int indexerId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var status = await db.IndexerStatuses
            .FirstOrDefaultAsync(s => s.IndexerId == indexerId);

        if (status == null)
        {
            return null; // No status = available
        }

        var queryDisabledRemaining = status.QueryDisabledUntil.HasValue
            ? status.QueryDisabledUntil.Value - DateTime.UtcNow
            : TimeSpan.Zero;

        var disabledRemaining = status.DisabledUntil.HasValue
            ? status.DisabledUntil.Value - DateTime.UtcNow
            : TimeSpan.Zero;

        var rateLimitRemaining = status.RateLimitedUntil.HasValue
            ? status.RateLimitedUntil.Value - DateTime.UtcNow
            : TimeSpan.Zero;

        var maxRemaining = new[] { queryDisabledRemaining, disabledRemaining, rateLimitRemaining }.Max();

        return maxRemaining > TimeSpan.Zero ? maxRemaining : null;
    }

    /// <summary>
    /// Get time remaining until indexer is available for grabs
    /// </summary>
    public async Task<TimeSpan?> GetTimeUntilGrabAvailableAsync(int indexerId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var status = await db.IndexerStatuses
            .FirstOrDefaultAsync(s => s.IndexerId == indexerId);

        if (status == null)
        {
            return null; // No status = available
        }

        var grabDisabledRemaining = status.GrabDisabledUntil.HasValue
            ? status.GrabDisabledUntil.Value - DateTime.UtcNow
            : TimeSpan.Zero;

        return grabDisabledRemaining > TimeSpan.Zero ? grabDisabledRemaining : null;
    }
}
