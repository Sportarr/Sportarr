using System.Collections.Concurrent;

namespace Sportarr.Api.Services;

/// <summary>
/// HTTP-level rate limiting service that matches Sonarr/Radarr's implementation.
/// Uses a two-level key system (host + subkey) to enforce per-indexer rate limits.
/// This is enforced at the HTTP client layer, not the application layer, to create
/// natural request distribution instead of predictable patterns.
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// Wait until the rate limit allows a request, then pulse to record the request time.
    /// Uses two-level keying: baseKey (host) and subKey (indexer ID) for proper isolation.
    /// </summary>
    /// <param name="baseKey">The base key (typically the host)</param>
    /// <param name="subKey">The sub key (typically the indexer ID)</param>
    /// <param name="rateLimit">Minimum time between requests</param>
    Task WaitAndPulseAsync(string baseKey, string? subKey, TimeSpan rateLimit);

    /// <summary>
    /// Get the time until the next request is allowed for a given key combination.
    /// </summary>
    TimeSpan GetTimeUntilAllowed(string baseKey, string? subKey);
}

/// <summary>
/// Sonarr-style rate limit service implementation.
/// Key features:
/// - Two-level keying (host + indexer ID) prevents one indexer from blocking others on the same host
/// - Random jitter (0-500ms) prevents predictable bot-like patterns
/// - Thread-safe concurrent dictionary for tracking
/// - Enforced at HTTP layer, not application layer
/// </summary>
public class RateLimitService : IRateLimitService
{
    private readonly ConcurrentDictionary<string, DateTime> _lastRequestTimes = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Random _random = new();
    private readonly ILogger<RateLimitService> _logger;

    // Random jitter range to prevent predictable patterns (0-500ms)
    private const int MaxJitterMs = 500;

    public RateLimitService(ILogger<RateLimitService> logger)
    {
        _logger = logger;
    }

    public async Task WaitAndPulseAsync(string baseKey, string? subKey, TimeSpan rateLimit)
    {
        var key = BuildKey(baseKey, subKey);

        await _lock.WaitAsync();
        try
        {
            if (_lastRequestTimes.TryGetValue(key, out var lastRequest))
            {
                var elapsed = DateTime.UtcNow - lastRequest;
                if (elapsed < rateLimit)
                {
                    var waitTime = rateLimit - elapsed;

                    // Add random jitter to prevent predictable patterns
                    var jitter = TimeSpan.FromMilliseconds(_random.Next(0, MaxJitterMs));
                    waitTime += jitter;

                    _logger.LogDebug("[RateLimit] Waiting {WaitMs}ms for {Key} (includes {JitterMs}ms jitter)",
                        (int)waitTime.TotalMilliseconds, key, (int)jitter.TotalMilliseconds);

                    // Release lock while waiting so other keys can proceed
                    _lock.Release();
                    await Task.Delay(waitTime);
                    await _lock.WaitAsync();
                }
            }

            // Record the request time
            _lastRequestTimes[key] = DateTime.UtcNow;
        }
        finally
        {
            _lock.Release();
        }
    }

    public TimeSpan GetTimeUntilAllowed(string baseKey, string? subKey)
    {
        var key = BuildKey(baseKey, subKey);

        if (_lastRequestTimes.TryGetValue(key, out var lastRequest))
        {
            var elapsed = DateTime.UtcNow - lastRequest;
            if (elapsed < TimeSpan.FromSeconds(2)) // Default rate limit
            {
                return TimeSpan.FromSeconds(2) - elapsed;
            }
        }

        return TimeSpan.Zero;
    }

    private static string BuildKey(string baseKey, string? subKey)
    {
        // Two-level key: "host:indexerId" or just "host" if no subkey
        return string.IsNullOrEmpty(subKey) ? baseKey : $"{baseKey}:{subKey}";
    }
}
