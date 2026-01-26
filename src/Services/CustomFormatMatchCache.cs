using System.Collections.Concurrent;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// In-memory cache for Custom Format match results.
///
/// Caches which custom formats match a given release title, avoiding expensive
/// regex evaluation on every search. The actual SCORE lookup is still done at
/// runtime since scores depend on the quality profile being used.
///
/// Cache key: Normalized release title
/// Cache value: List of matched format IDs
///
/// This dramatically speeds up repeated searches because:
/// - Format matching involves many regex operations (expensive)
/// - Score lookup is just a dictionary lookup (cheap)
/// - Release titles are immutable from indexers
/// - Custom formats rarely change (only on sync/edit)
///
/// Invalidation triggers:
/// - Custom format sync from TRaSH Guides
/// - Custom format create/update/delete
/// - Manual cache clear
/// </summary>
public class CustomFormatMatchCache : IDisposable
{
    private readonly ILogger<CustomFormatMatchCache> _logger;
    private readonly ConcurrentDictionary<string, CachedFormatMatches> _cache = new();
    private readonly System.Threading.Timer _cleanupTimer;
    private bool _disposed = false;

    /// <summary>
    /// Version number that increments when custom formats change.
    /// Cached entries with older versions are considered stale.
    /// </summary>
    private long _formatVersion = 0;

    /// <summary>
    /// Maximum number of entries before triggering cleanup (reduced from 5000 for memory optimization).
    /// Prevents unbounded memory growth from many unique release titles.
    /// </summary>
    private const int MaxCacheEntries = 2000;

    /// <summary>
    /// Threshold at which proactive cleanup begins (80% of max).
    /// Prevents memory spikes by cleaning up before hitting the hard limit.
    /// </summary>
    private const int CleanupThreshold = 1600;

    /// <summary>
    /// Maximum age of cache entries in seconds before cleanup.
    /// Entries older than this are removed during periodic cleanup.
    /// </summary>
    private const int MaxEntryAgeSeconds = 1800; // 30 minutes (reduced from 1 hour)

    /// <summary>
    /// Cached format match results for a release
    /// </summary>
    public class CachedFormatMatches
    {
        /// <summary>
        /// IDs of custom formats that matched this release
        /// </summary>
        public List<int> MatchedFormatIds { get; set; } = new();

        /// <summary>
        /// Names of matched formats (for logging/debugging)
        /// </summary>
        public List<string> MatchedFormatNames { get; set; } = new();

        /// <summary>
        /// When this was cached
        /// </summary>
        public DateTime CachedAt { get; set; }

        /// <summary>
        /// Format version when cached (for invalidation)
        /// </summary>
        public long FormatVersion { get; set; }
    }

    public CustomFormatMatchCache(ILogger<CustomFormatMatchCache> logger)
    {
        _logger = logger;

        // Start periodic cleanup timer to prevent unbounded memory growth
        // Runs every 5 minutes regardless of whether Store() is called
        _cleanupTimer = new System.Threading.Timer(
            PeriodicCleanup,
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Periodic cleanup called by timer to ensure cache doesn't grow unbounded
    /// even if UI isn't actively polling or Store() isn't called.
    /// </summary>
    private void PeriodicCleanup(object? state)
    {
        if (_cache.Count > CleanupThreshold)
        {
            CleanupOldEntries();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cleanupTimer?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Normalize cache key - lowercase, trim
    /// </summary>
    private string NormalizeKey(string title)
    {
        return title.ToLowerInvariant().Trim();
    }

    /// <summary>
    /// Try to get cached format matches for a release title
    /// </summary>
    /// <param name="releaseTitle">The release title</param>
    /// <returns>Cached matches if valid, null if not found or stale</returns>
    public CachedFormatMatches? TryGetCached(string releaseTitle)
    {
        var key = NormalizeKey(releaseTitle);

        if (_cache.TryGetValue(key, out var cached))
        {
            // Check if cache entry is from current format version
            if (cached.FormatVersion == _formatVersion)
            {
                _logger.LogDebug("[CF Cache] HIT for '{Title}' - {Count} matched formats",
                    releaseTitle, cached.MatchedFormatIds.Count);
                return cached;
            }
            else
            {
                // Stale entry - remove it
                _logger.LogDebug("[CF Cache] STALE for '{Title}' (version {Cached} < {Current})",
                    releaseTitle, cached.FormatVersion, _formatVersion);
                _cache.TryRemove(key, out _);
            }
        }

        return null;
    }

    /// <summary>
    /// Store format match results for a release
    /// </summary>
    /// <param name="releaseTitle">The release title</param>
    /// <param name="matchedFormats">List of matched formats with their info</param>
    public void Store(string releaseTitle, List<(int FormatId, string FormatName)> matchedFormats)
    {
        var key = NormalizeKey(releaseTitle);

        var cached = new CachedFormatMatches
        {
            MatchedFormatIds = matchedFormats.Select(m => m.FormatId).ToList(),
            MatchedFormatNames = matchedFormats.Select(m => m.FormatName).ToList(),
            CachedAt = DateTime.UtcNow,
            FormatVersion = _formatVersion
        };

        _cache[key] = cached;

        _logger.LogDebug("[CF Cache] Stored {Count} matches for '{Title}'",
            matchedFormats.Count, releaseTitle);

        // Proactive cleanup at 80% capacity to prevent memory spikes
        if (_cache.Count > CleanupThreshold)
        {
            CleanupOldEntries();
        }
    }

    /// <summary>
    /// Remove old/stale entries to keep cache size bounded.
    /// Removes entries older than MaxEntryAgeSeconds first, then oldest entries if still over limit.
    /// </summary>
    private void CleanupOldEntries()
    {
        var now = DateTime.UtcNow;
        var removedCount = 0;

        // First pass: remove expired entries (older than MaxEntryAgeSeconds)
        var expiredKeys = _cache
            .Where(kvp => (now - kvp.Value.CachedAt).TotalSeconds > MaxEntryAgeSeconds)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            if (_cache.TryRemove(key, out _))
            {
                removedCount++;
            }
        }

        // Second pass: if still over limit, remove oldest entries
        if (_cache.Count > MaxCacheEntries)
        {
            var entriesToRemove = _cache.Count - MaxCacheEntries + 500; // Remove extra 500 to avoid frequent cleanups
            var oldestKeys = _cache
                .OrderBy(kvp => kvp.Value.CachedAt)
                .Take(entriesToRemove)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldestKeys)
            {
                if (_cache.TryRemove(key, out _))
                {
                    removedCount++;
                }
            }
        }

        if (removedCount > 0)
        {
            _logger.LogDebug("[CF Cache] Cleanup removed {Count} entries, {Remaining} remaining",
                removedCount, _cache.Count);
        }
    }

    /// <summary>
    /// Calculate scores for cached format matches using the given profile.
    /// This is the fast path - just dictionary lookups.
    /// </summary>
    /// <param name="cached">Cached format matches</param>
    /// <param name="profile">Quality profile with FormatItems scores</param>
    /// <param name="isPack">Whether this is a pack (skip certain penalty formats)</param>
    /// <returns>List of matched formats with scores and total score</returns>
    public (List<MatchedFormat> MatchedFormats, int TotalScore) CalculateScores(
        CachedFormatMatches cached,
        QualityProfile? profile,
        bool isPack = false)
    {
        var matchedFormats = new List<MatchedFormat>();
        var totalScore = 0;

        // Build FormatId → Score lookup from profile
        var formatScores = profile?.FormatItems?.ToDictionary(fi => fi.FormatId, fi => fi.Score)
            ?? new Dictionary<int, int>();

        // Pack penalty patterns to skip
        var packSkipNames = new[] { "no-rlsgroup", "no-releasegroup", "no-group", "unknown-group" };

        for (int i = 0; i < cached.MatchedFormatIds.Count; i++)
        {
            var formatId = cached.MatchedFormatIds[i];
            var formatName = cached.MatchedFormatNames[i];

            // Look up score from profile (default 0 if not configured)
            formatScores.TryGetValue(formatId, out var formatScore);

            // For packs, skip penalty formats that don't apply
            if (isPack && formatScore < 0)
            {
                var nameLower = formatName.ToLowerInvariant().Replace(" ", "").Replace("-", "");
                if (packSkipNames.Any(p => nameLower.Contains(p.Replace("-", ""))))
                {
                    continue;
                }
            }

            matchedFormats.Add(new MatchedFormat
            {
                Name = formatName,
                Score = formatScore
            });

            totalScore += formatScore;
        }

        return (matchedFormats, totalScore);
    }

    /// <summary>
    /// Invalidate all cached format matches.
    /// Call this when custom formats are synced, created, updated, or deleted.
    /// </summary>
    public void InvalidateAll()
    {
        var previousVersion = _formatVersion;
        _formatVersion = DateTime.UtcNow.Ticks; // Use timestamp as unique version

        _logger.LogInformation("[CF Cache] Invalidated all entries (version {Old} → {New})",
            previousVersion, _formatVersion);
    }

    /// <summary>
    /// Clear the cache entirely
    /// </summary>
    public void Clear()
    {
        var count = _cache.Count;
        _cache.Clear();
        _logger.LogInformation("[CF Cache] Cleared {Count} entries", count);
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public (int EntryCount, long FormatVersion) GetStats()
    {
        return (_cache.Count, _formatVersion);
    }
}
