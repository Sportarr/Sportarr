using System.Collections.Concurrent;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Caches search results to prevent duplicate API calls to indexers.
///
/// OPTIMIZATION: When searching for multi-part events (UFC Main Card, Prelims, etc.),
/// we often search the same event multiple times within seconds. This cache:
/// 1. Stores search results for a configurable time (default 60 seconds)
/// 2. Returns cached results for the same event within the cache window
/// 3. Prevents rate limiting from excessive indexer queries
///
/// This follows Sonarr's approach: search ONCE, get ALL releases, filter locally.
/// </summary>
public class SearchCacheService
{
    private readonly ILogger<SearchCacheService> _logger;
    private readonly ConfigService _configService;

    // Cache key format: "eventId" or "eventId:part" for part-specific searches
    // Value: (results, timestamp)
    private readonly ConcurrentDictionary<string, (List<ReleaseSearchResult> Results, DateTime CachedAt)> _cache = new();

    // Maximum cache entries to prevent memory bloat
    private const int MaxCacheEntries = 100;

    // Last cleanup time
    private DateTime _lastCleanup = DateTime.UtcNow;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);

    public SearchCacheService(ILogger<SearchCacheService> logger, ConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    /// <summary>
    /// Get cache duration from config (default 60 seconds)
    /// </summary>
    private async Task<TimeSpan> GetCacheDurationAsync()
    {
        var config = await _configService.GetConfigAsync();
        var seconds = config.SearchCacheDuration > 0 ? config.SearchCacheDuration : 60;
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Get cache duration synchronously (for cleanup operations)
    /// Uses Task.Result which is safe here since ConfigService caches the config
    /// </summary>
    private TimeSpan GetCacheDuration()
    {
        var config = _configService.GetConfigAsync().GetAwaiter().GetResult();
        var seconds = config.SearchCacheDuration > 0 ? config.SearchCacheDuration : 60;
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Try to get cached search results for an event.
    /// Returns null if not cached or cache expired.
    /// </summary>
    /// <param name="eventId">The event ID</param>
    /// <param name="part">Optional part name (null for whole event search)</param>
    public async Task<List<ReleaseSearchResult>?> TryGetCachedAsync(int eventId, string? part = null)
    {
        CleanupIfNeeded();

        var key = GetCacheKey(eventId, part);
        var cacheDuration = await GetCacheDurationAsync();

        if (_cache.TryGetValue(key, out var cached))
        {
            if (DateTime.UtcNow - cached.CachedAt < cacheDuration)
            {
                _logger.LogInformation("[Search Cache] HIT: Event {EventId}{Part} ({Count} results, cached {Seconds}s ago, TTL: {TTL}s)",
                    eventId, part != null ? $" ({part})" : "", cached.Results.Count,
                    (int)(DateTime.UtcNow - cached.CachedAt).TotalSeconds, (int)cacheDuration.TotalSeconds);
                return cached.Results;
            }
            else
            {
                // Expired - remove it
                _cache.TryRemove(key, out _);
                _logger.LogDebug("[Search Cache] EXPIRED: Event {EventId}{Part}", eventId, part != null ? $" ({part})" : "");
            }
        }

        _logger.LogDebug("[Search Cache] MISS: Event {EventId}{Part}", eventId, part != null ? $" ({part})" : "");
        return null;
    }

    /// <summary>
    /// Try to get cached results for the base event (without part).
    /// This is useful when we searched for "UFC 299" and want results for "UFC 299 Main Card".
    /// The base search results contain ALL parts.
    /// </summary>
    public async Task<List<ReleaseSearchResult>?> TryGetBaseCachedAsync(int eventId)
    {
        return await TryGetCachedAsync(eventId, null);
    }

    /// <summary>
    /// Cache search results for an event.
    /// </summary>
    public void Cache(int eventId, string? part, List<ReleaseSearchResult> results)
    {
        CleanupIfNeeded();

        // Prevent cache from growing too large
        if (_cache.Count >= MaxCacheEntries)
        {
            // Remove oldest entries
            var oldestKeys = _cache
                .OrderBy(kvp => kvp.Value.CachedAt)
                .Take(MaxCacheEntries / 4)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldestKeys)
            {
                _cache.TryRemove(key, out _);
            }

            _logger.LogDebug("[Search Cache] Evicted {Count} old entries (cache full)", oldestKeys.Count);
        }

        var cacheKey = GetCacheKey(eventId, part);
        _cache[cacheKey] = (results.ToList(), DateTime.UtcNow); // ToList() to create a copy

        _logger.LogInformation("[Search Cache] STORED: Event {EventId}{Part} ({Count} results)",
            eventId, part != null ? $" ({part})" : "", results.Count);
    }

    /// <summary>
    /// Invalidate cache for an event (all parts).
    /// Call this when a download is grabbed or search settings change.
    /// </summary>
    public void Invalidate(int eventId)
    {
        var keysToRemove = _cache.Keys.Where(k => k.StartsWith($"{eventId}:") || k == eventId.ToString()).ToList();
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }

        if (keysToRemove.Any())
        {
            _logger.LogDebug("[Search Cache] Invalidated {Count} entries for event {EventId}", keysToRemove.Count, eventId);
        }
    }

    /// <summary>
    /// Clear entire cache.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _logger.LogInformation("[Search Cache] Cache cleared");
    }

    /// <summary>
    /// Get cache statistics for debugging.
    /// </summary>
    public (int Count, int ExpiredCount, int CacheDurationSeconds) GetStats()
    {
        var now = DateTime.UtcNow;
        var cacheDuration = GetCacheDuration();
        var expired = _cache.Count(kvp => now - kvp.Value.CachedAt >= cacheDuration);
        return (_cache.Count, expired, (int)cacheDuration.TotalSeconds);
    }

    private string GetCacheKey(int eventId, string? part)
    {
        return part != null ? $"{eventId}:{part.ToLowerInvariant()}" : eventId.ToString();
    }

    private void CleanupIfNeeded()
    {
        if (DateTime.UtcNow - _lastCleanup < _cleanupInterval)
        {
            return;
        }

        _lastCleanup = DateTime.UtcNow;

        var now = DateTime.UtcNow;
        var cacheDuration = GetCacheDuration();
        var expiredKeys = _cache
            .Where(kvp => now - kvp.Value.CachedAt >= cacheDuration)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }

        if (expiredKeys.Any())
        {
            _logger.LogDebug("[Search Cache] Cleaned up {Count} expired entries (TTL: {TTL}s)", expiredKeys.Count, (int)cacheDuration.TotalSeconds);
        }
    }
}
