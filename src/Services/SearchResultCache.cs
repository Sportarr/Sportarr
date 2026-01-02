using System.Collections.Concurrent;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// In-memory cache for raw indexer search results.
/// Stores unprocessed releases from indexers keyed by the search query.
/// When a cache hit occurs, the raw results are re-matched against the new event
/// to generate fresh match scores, rejections, and custom format evaluations.
///
/// This dramatically reduces indexer API calls for:
/// - Multi-part events (UFC 300 Prelims, UFC 300 Main Card share cache)
/// - Same-year events (NFL.2025 query works for all 2025 NFL games)
/// - Rapid successive searches by users
/// </summary>
public class SearchResultCache
{
    private readonly ILogger<SearchResultCache> _logger;
    private readonly ConcurrentDictionary<string, CachedSearchResults> _cache = new();

    /// <summary>
    /// Represents cached raw results from indexers
    /// </summary>
    public class CachedSearchResults
    {
        /// <summary>
        /// Raw, unprocessed releases from indexers (before matching/scoring)
        /// </summary>
        public List<RawRelease> RawReleases { get; set; } = new();

        /// <summary>
        /// When these results were cached
        /// </summary>
        public DateTime CachedAt { get; set; }

        /// <summary>
        /// The search query used to fetch these results
        /// </summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// Which indexers returned results
        /// </summary>
        public List<string> IndexersQueried { get; set; } = new();
    }

    /// <summary>
    /// Raw release data from indexer - before any event-specific processing
    /// </summary>
    public class RawRelease
    {
        public string Title { get; set; } = string.Empty;
        public string? Guid { get; set; }
        public string? DownloadUrl { get; set; }
        public string Indexer { get; set; } = string.Empty;
        public string? IndexerFlags { get; set; }
        public long Size { get; set; }
        public DateTime PublishDate { get; set; }
        public int? Seeders { get; set; }
        public int? Leechers { get; set; }
        public string? Quality { get; set; }
        public string? Codec { get; set; }
        public string? Source { get; set; }
        public string? Language { get; set; }
        public string? TorrentInfoHash { get; set; }
        public string? Protocol { get; set; }
        public bool IsPack { get; set; }

        /// <summary>
        /// Convert a ReleaseSearchResult to a RawRelease for caching
        /// Strips out event-specific matching data
        /// </summary>
        public static RawRelease FromSearchResult(ReleaseSearchResult result)
        {
            return new RawRelease
            {
                Title = result.Title,
                Guid = result.Guid,
                DownloadUrl = result.DownloadUrl,
                Indexer = result.Indexer,
                IndexerFlags = result.IndexerFlags,
                Size = result.Size,
                PublishDate = result.PublishDate,
                Seeders = result.Seeders,
                Leechers = result.Leechers,
                Quality = result.Quality,
                Codec = result.Codec,
                Source = result.Source,
                Language = result.Language,
                TorrentInfoHash = result.TorrentInfoHash,
                Protocol = result.Protocol,
                IsPack = result.IsPack
            };
        }

        /// <summary>
        /// Convert back to a fresh ReleaseSearchResult for event-specific processing
        /// All matching/scoring fields are reset to be recalculated
        /// </summary>
        public ReleaseSearchResult ToSearchResult()
        {
            return new ReleaseSearchResult
            {
                Title = Title,
                Guid = Guid ?? string.Empty,
                DownloadUrl = DownloadUrl ?? string.Empty,
                Indexer = Indexer,
                IndexerFlags = IndexerFlags,
                Size = Size,
                PublishDate = PublishDate,
                Seeders = Seeders,
                Leechers = Leechers,
                Quality = Quality,
                Codec = Codec,
                Source = Source,
                Language = Language,
                TorrentInfoHash = TorrentInfoHash,
                Protocol = Protocol,
                IsPack = IsPack,
                // Reset all event-specific fields
                Approved = true,
                Rejections = new List<string>(),
                MatchScore = 0,
                Score = 0,
                QualityScore = 0,
                CustomFormatScore = 0,
                MatchedFormats = new List<MatchedFormat>(),
                IsBlocklisted = false,
                BlocklistReason = null
            };
        }
    }

    public SearchResultCache(ILogger<SearchResultCache> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Normalize cache key - lowercase, remove special characters
    /// </summary>
    private string NormalizeKey(string query)
    {
        return query.ToLowerInvariant().Trim();
    }

    /// <summary>
    /// Try to get cached results for a query
    /// </summary>
    /// <param name="query">The search query (e.g., "UFC.300", "NFL.2025")</param>
    /// <param name="cacheDurationSeconds">How long cached results are valid</param>
    /// <returns>Cached results if valid, null if not found or expired</returns>
    public CachedSearchResults? TryGetCached(string query, int cacheDurationSeconds)
    {
        var key = NormalizeKey(query);

        if (_cache.TryGetValue(key, out var cached))
        {
            var age = DateTime.UtcNow - cached.CachedAt;
            if (age.TotalSeconds <= cacheDurationSeconds)
            {
                _logger.LogInformation("[ReleaseCache] Cache HIT for '{Query}' - {Count} raw releases (age: {Age:F1}s)",
                    query, cached.RawReleases.Count, age.TotalSeconds);
                return cached;
            }
            else
            {
                _logger.LogDebug("[ReleaseCache] Cache EXPIRED for '{Query}' (age: {Age:F1}s > {Max}s)",
                    query, age.TotalSeconds, cacheDurationSeconds);
                _cache.TryRemove(key, out _);
            }
        }

        _logger.LogDebug("[ReleaseCache] Cache MISS for '{Query}'", query);
        return null;
    }

    /// <summary>
    /// Store raw results in cache
    /// </summary>
    /// <param name="query">The search query used</param>
    /// <param name="results">Raw results from indexers</param>
    /// <param name="indexersQueried">Which indexers were queried</param>
    public void Store(string query, IEnumerable<ReleaseSearchResult> results, IEnumerable<string>? indexersQueried = null)
    {
        var key = NormalizeKey(query);
        var rawReleases = results.Select(RawRelease.FromSearchResult).ToList();

        var cached = new CachedSearchResults
        {
            RawReleases = rawReleases,
            CachedAt = DateTime.UtcNow,
            Query = query,
            IndexersQueried = indexersQueried?.ToList() ?? new List<string>()
        };

        _cache[key] = cached;

        _logger.LogInformation("[ReleaseCache] Cached {Count} raw releases for '{Query}'",
            rawReleases.Count, query);

        // Clean up old entries (simple periodic cleanup)
        CleanupExpired(300); // Remove anything older than 5 minutes
    }

    /// <summary>
    /// Convert cached raw releases back to fresh ReleaseSearchResults
    /// All event-specific fields are reset for re-matching
    /// </summary>
    public List<ReleaseSearchResult> ToSearchResults(CachedSearchResults cached)
    {
        return cached.RawReleases.Select(r => r.ToSearchResult()).ToList();
    }

    /// <summary>
    /// Clear cache for a specific query (e.g., when user clicks "Refresh")
    /// </summary>
    public void Invalidate(string query)
    {
        var key = NormalizeKey(query);
        if (_cache.TryRemove(key, out _))
        {
            _logger.LogInformation("[ReleaseCache] Invalidated cache for '{Query}'", query);
        }
    }

    /// <summary>
    /// Clear all cached results
    /// </summary>
    public void Clear()
    {
        var count = _cache.Count;
        _cache.Clear();
        _logger.LogInformation("[ReleaseCache] Cleared all {Count} cached queries", count);
    }

    /// <summary>
    /// Remove expired entries from cache
    /// </summary>
    private void CleanupExpired(int maxAgeSeconds)
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _cache
            .Where(kvp => (now - kvp.Value.CachedAt).TotalSeconds > maxAgeSeconds)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogDebug("[ReleaseCache] Cleaned up {Count} expired cache entries", expiredKeys.Count);
        }
    }

    /// <summary>
    /// Get cache statistics for debugging/monitoring
    /// </summary>
    public (int EntryCount, int TotalReleases) GetStats()
    {
        var totalReleases = _cache.Values.Sum(c => c.RawReleases.Count);
        return (_cache.Count, totalReleases);
    }
}
