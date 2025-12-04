using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for managing indexer health status and rate limiting (Sonarr-style)
/// Implements exponential backoff for failed indexers and respects API rate limits
/// </summary>
public class IndexerStatusService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<IndexerStatusService> _logger;

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
        SportarrDbContext db,
        ILogger<IndexerStatusService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Get or create status for an indexer
    /// </summary>
    public async Task<IndexerStatus> GetOrCreateStatusAsync(int indexerId)
    {
        var status = await _db.IndexerStatuses
            .FirstOrDefaultAsync(s => s.IndexerId == indexerId);

        if (status == null)
        {
            status = new IndexerStatus
            {
                IndexerId = indexerId,
                HourResetTime = DateTime.UtcNow.AddHours(1)
            };
            _db.IndexerStatuses.Add(status);
            await _db.SaveChangesAsync();
        }

        return status;
    }

    /// <summary>
    /// Check if an indexer is available for use (not disabled, not rate limited)
    /// </summary>
    public async Task<(bool IsAvailable, string? Reason)> IsIndexerAvailableAsync(int indexerId)
    {
        var status = await GetOrCreateStatusAsync(indexerId);
        var indexer = await _db.Indexers.FindAsync(indexerId);

        if (indexer == null || !indexer.Enabled)
        {
            return (false, "Indexer is disabled");
        }

        // Check if temporarily disabled due to failures
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
        await ResetHourlyCountersIfNeededAsync(status);

        // Check query limit
        if (indexer.QueryLimit.HasValue && status.QueriesThisHour >= indexer.QueryLimit.Value)
        {
            var resetIn = status.HourResetTime.HasValue ? status.HourResetTime.Value - DateTime.UtcNow : TimeSpan.Zero;
            return (false, $"Query limit ({indexer.QueryLimit}) reached. Resets in {resetIn.TotalMinutes:F0} minutes");
        }

        return (true, null);
    }

    /// <summary>
    /// Record a successful query to an indexer
    /// </summary>
    public async Task RecordSuccessAsync(int indexerId)
    {
        var status = await GetOrCreateStatusAsync(indexerId);

        // Reset failure counters on success
        status.ConsecutiveFailures = 0;
        status.LastFailure = null;
        status.LastFailureReason = null;
        status.DisabledUntil = null;
        status.LastSuccess = DateTime.UtcNow;

        // Increment query counter
        await ResetHourlyCountersIfNeededAsync(status);
        status.QueriesThisHour++;

        await _db.SaveChangesAsync();

        _logger.LogDebug("[Indexer Status] Recorded success for indexer {IndexerId}", indexerId);
    }

    /// <summary>
    /// Record a successful grab from an indexer
    /// </summary>
    public async Task RecordGrabAsync(int indexerId)
    {
        var status = await GetOrCreateStatusAsync(indexerId);

        // Increment grab counter
        await ResetHourlyCountersIfNeededAsync(status);
        status.GrabsThisHour++;

        await _db.SaveChangesAsync();

        _logger.LogDebug("[Indexer Status] Recorded grab for indexer {IndexerId}", indexerId);
    }

    /// <summary>
    /// Check if a grab is allowed (within grab limits)
    /// </summary>
    public async Task<(bool IsAllowed, string? Reason)> CanGrabAsync(int indexerId)
    {
        var status = await GetOrCreateStatusAsync(indexerId);
        var indexer = await _db.Indexers.FindAsync(indexerId);

        if (indexer == null || !indexer.Enabled)
        {
            return (false, "Indexer is disabled");
        }

        // Reset hourly counters if needed
        await ResetHourlyCountersIfNeededAsync(status);

        // Check grab limit
        if (indexer.GrabLimit.HasValue && status.GrabsThisHour >= indexer.GrabLimit.Value)
        {
            var resetIn = status.HourResetTime.HasValue ? status.HourResetTime.Value - DateTime.UtcNow : TimeSpan.Zero;
            return (false, $"Grab limit ({indexer.GrabLimit}) reached. Resets in {resetIn.TotalMinutes:F0} minutes");
        }

        return (true, null);
    }

    /// <summary>
    /// Record a failure for an indexer (implements exponential backoff)
    /// </summary>
    public async Task RecordFailureAsync(int indexerId, string reason)
    {
        var status = await GetOrCreateStatusAsync(indexerId);

        status.ConsecutiveFailures++;
        status.LastFailure = DateTime.UtcNow;
        status.LastFailureReason = reason;

        // Calculate backoff duration using exponential backoff
        var backoffIndex = Math.Min(status.ConsecutiveFailures - 1, BackoffDurations.Length - 1);
        var backoffDuration = BackoffDurations[backoffIndex];
        status.DisabledUntil = DateTime.UtcNow.Add(backoffDuration);

        await _db.SaveChangesAsync();

        _logger.LogWarning("[Indexer Status] Indexer {IndexerId} failure #{FailureCount}: {Reason}. Disabled until {DisabledUntil} ({Duration} backoff)",
            indexerId, status.ConsecutiveFailures, reason, status.DisabledUntil, backoffDuration);
    }

    /// <summary>
    /// Record HTTP 429 rate limit response
    /// </summary>
    public async Task RecordRateLimitedAsync(int indexerId, TimeSpan? retryAfter = null)
    {
        var status = await GetOrCreateStatusAsync(indexerId);

        // Use Retry-After if provided, otherwise default to 5 minutes
        var waitTime = retryAfter ?? TimeSpan.FromMinutes(5);

        // Cap at 1 hour max wait
        if (waitTime > TimeSpan.FromHours(1))
        {
            waitTime = TimeSpan.FromHours(1);
        }

        status.RateLimitedUntil = DateTime.UtcNow.Add(waitTime);
        status.LastFailure = DateTime.UtcNow;
        status.LastFailureReason = "HTTP 429 Too Many Requests";

        await _db.SaveChangesAsync();

        _logger.LogWarning("[Indexer Status] Indexer {IndexerId} rate limited (HTTP 429). Retry after {WaitTime}",
            indexerId, waitTime);
    }

    /// <summary>
    /// Get all indexers with their availability status
    /// </summary>
    public async Task<List<(Indexer Indexer, bool IsAvailable, string? Reason)>> GetAllIndexerStatusesAsync()
    {
        var indexers = await _db.Indexers
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
        var indexer = await _db.Indexers.FindAsync(indexerId);
        return indexer?.RequestDelayMs ?? 0;
    }

    /// <summary>
    /// Clear failure history for an indexer (manual reset)
    /// </summary>
    public async Task ClearFailureHistoryAsync(int indexerId)
    {
        var status = await GetOrCreateStatusAsync(indexerId);

        status.ConsecutiveFailures = 0;
        status.LastFailure = null;
        status.LastFailureReason = null;
        status.DisabledUntil = null;
        status.RateLimitedUntil = null;

        await _db.SaveChangesAsync();

        _logger.LogInformation("[Indexer Status] Cleared failure history for indexer {IndexerId}", indexerId);
    }

    /// <summary>
    /// Get time remaining until indexer is available
    /// </summary>
    public async Task<TimeSpan?> GetTimeUntilAvailableAsync(int indexerId)
    {
        var status = await GetOrCreateStatusAsync(indexerId);

        var disabledRemaining = status.DisabledUntil.HasValue
            ? status.DisabledUntil.Value - DateTime.UtcNow
            : TimeSpan.Zero;

        var rateLimitRemaining = status.RateLimitedUntil.HasValue
            ? status.RateLimitedUntil.Value - DateTime.UtcNow
            : TimeSpan.Zero;

        var maxRemaining = TimeSpan.FromTicks(Math.Max(disabledRemaining.Ticks, rateLimitRemaining.Ticks));

        return maxRemaining > TimeSpan.Zero ? maxRemaining : null;
    }

    /// <summary>
    /// Reset hourly counters if the hour has passed
    /// </summary>
    private async Task ResetHourlyCountersIfNeededAsync(IndexerStatus status)
    {
        if (!status.HourResetTime.HasValue || DateTime.UtcNow >= status.HourResetTime.Value)
        {
            status.QueriesThisHour = 0;
            status.GrabsThisHour = 0;
            status.HourResetTime = DateTime.UtcNow.AddHours(1);

            await _db.SaveChangesAsync();
        }
    }
}
