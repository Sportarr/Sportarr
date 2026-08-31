using System.Collections.Concurrent;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// In-memory cache for raw indexer search results.
/// Stores raw release data from indexers to avoid repeated API calls.
/// Quality/CF scoring is recalculated on cache hit since it depends on the quality profile.
///
/// This dramatically reduces indexer API calls for:
/// - Multi-part events (UFC 300 Prelims, UFC 300 Main Card share cache)
/// - Same-year events (NFL.2025 query works for all 2025 NFL games)
/// - Rapid successive searches by users
///
/// Cached (raw indexer data):
/// - Title, Guid, DownloadUrl, Indexer, Size, PublishDate, Seeders, etc.
/// - Codec, Source, Language (parsed from title)
///
/// Recalculated per search (requires quality profile):
/// - Quality, QualityScore, CustomFormatScore, MatchedFormats
/// - Approved, Rejections (depends on profile, part matching, date validation)
/// - MatchScore (how well release matches specific event)
/// - IsBlocklisted (needs fresh DB check)
/// </summary>
public class SearchResultCache : IDisposable
{
    private readonly ILogger<SearchResultCache> _logger;
    private readonly ConcurrentDictionary<string, CachedSearchResults> _cache = new();

    /// <summary>
    /// Gates so only the first caller for a query goes to the indexers. A
    /// fighting event searches once per part, and the parts build the same
    /// query because the part is not in it, so all of them missed the cache
    /// together and each ran the whole search. The later parts wait here and
    /// then find the answer already stored.
    ///
    /// The gates are a fixed set of stripes rather than one per key. A
    /// per-key set needs pruning, and a pruner can retire a gate between a
    /// caller's lookup and its wait, which splits one flight in two, the
    /// exact failure this exists to stop. Two different queries can land on
    /// the same stripe and take turns; that costs a wait, never correctness,
    /// and with this many stripes it is rare.
    /// </summary>
    private readonly SemaphoreSlim[] _fillGates =
        Enumerable.Range(0, FillGateStripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private const int FillGateStripes = 256;

    private readonly System.Threading.Timer _cleanupTimer;

    /// <summary>
    /// Hard ceiling on cached queries. A backlog sweep asks for thousands of
    /// distinct queries, and every entry holds a full release list, so the
    /// time limit alone let this grow to hundreds of megabytes.
    /// </summary>
    private const int MaxCacheEntries = 500;

    private const int CleanupThreshold = 400;

    /// <summary>
    /// How long an empty answer is trusted. Long enough to absorb the parts
    /// of one event searching together, short enough that an indexer blip
    /// cannot hide a release for long.
    /// </summary>
    public const int EmptyResultLifetimeSeconds = 60;

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
        /// The lifetime this entry was stored with.
        ///
        /// Cleanup used to run against whatever duration the current caller
        /// happened to pass, so one caller with a short window wiped entries
        /// another had stored for much longer, and the same searches went back
        /// out to the indexers over and over. Each entry is judged on its own
        /// lifetime now.
        /// </summary>
        public int LifetimeSeconds { get; set; } = 300;

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
    /// Cached release data - stores raw indexer data only.
    /// Quality/CF scoring is recalculated on cache hit since it depends on quality profile.
    /// </summary>
    public class RawRelease
    {
        // Core release info from indexer
        public string Title { get; set; } = string.Empty;
        public string? Guid { get; set; }
        public string? DownloadUrl { get; set; }
        public string? InfoUrl { get; set; }
        public string Indexer { get; set; } = string.Empty;
        public string? IndexerFlags { get; set; }
        public long Size { get; set; }
        public DateTime PublishDate { get; set; }
        public int? Seeders { get; set; }
        public int? Leechers { get; set; }
        public string? TorrentInfoHash { get; set; }
        public string? Protocol { get; set; }
        public bool IsPack { get; set; }

        // Title-parsed fields (preserved from initial indexer response)
        public string? Codec { get; set; }
        public string? Source { get; set; }
        public string? Language { get; set; }

        /// <summary>
        /// Convert a ReleaseSearchResult to a RawRelease for caching.
        /// Only stores raw indexer data - scoring is recalculated on cache hit.
        /// </summary>
        public static RawRelease FromSearchResult(ReleaseSearchResult result)
        {
            return new RawRelease
            {
                Title = result.Title,
                Guid = result.Guid,
                DownloadUrl = result.DownloadUrl,
                InfoUrl = result.InfoUrl,
                Indexer = result.Indexer,
                IndexerFlags = result.IndexerFlags,
                Size = result.Size,
                PublishDate = result.PublishDate,
                Seeders = result.Seeders,
                Leechers = result.Leechers,
                TorrentInfoHash = result.TorrentInfoHash,
                Protocol = result.Protocol,
                IsPack = result.IsPack,
                Codec = result.Codec,
                Source = result.Source,
                Language = result.Language
            };
        }

        /// <summary>
        /// Convert back to a ReleaseSearchResult for evaluation.
        /// All scoring fields are zeroed - must be recalculated by ReleaseEvaluator.
        /// </summary>
        public ReleaseSearchResult ToSearchResult()
        {
            return new ReleaseSearchResult
            {
                Title = Title,
                Guid = Guid ?? string.Empty,
                DownloadUrl = DownloadUrl ?? string.Empty,
                InfoUrl = InfoUrl,
                Indexer = Indexer,
                IndexerFlags = IndexerFlags,
                Size = Size,
                PublishDate = PublishDate,
                Seeders = Seeders,
                Leechers = Leechers,
                TorrentInfoHash = TorrentInfoHash,
                Protocol = Protocol ?? "Unknown",
                IsPack = IsPack,
                Codec = Codec,
                Source = Source,
                Language = Language,
                // All scoring/evaluation fields reset - will be calculated by ReleaseEvaluator
                Quality = null,
                Score = 0,
                QualityScore = 0,
                CustomFormatScore = 0,
                SizeScore = 0,
                MatchedFormats = new List<MatchedFormat>(),
                Approved = true,
                Rejections = new List<string>(),
                MatchScore = 0,
                IsBlocklisted = false,
                BlocklistReason = null
            };
        }
    }

    public SearchResultCache(ILogger<SearchResultCache> logger)
    {
        _logger = logger;

        // Cleanup used to run only inside Store, so a burst of searches that
        // then stopped left everything it had cached in memory until the next
        // search arrived, which could be hours.
        _cleanupTimer = new System.Threading.Timer(
            _ => PeriodicCleanup(),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }

    private void PeriodicCleanup()
    {
        try
        {
            CleanupExpired(0);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[ReleaseCache] Periodic cleanup failed");
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }

    /// <summary>
    /// Normalize cache key - lowercase, remove special characters
    /// </summary>
    private string NormalizeKey(string query)
    {
        return query.ToLowerInvariant().Trim();
    }

    /// <summary>
    /// Build the key for one query as searched under one set of indexer tags.
    ///
    /// Tags decide which indexers a search actually reaches. Two leagues can
    /// produce the same query while pointing at different indexers, and on the
    /// query alone the second league takes the first one's answer and never
    /// asks its own indexers, hiding releases that are really there.
    /// </summary>
    public static string ScopeKey(string query, IEnumerable<int>? indexerTags)
    {
        var tags = indexerTags?.Distinct().OrderBy(t => t).ToList() ?? new List<int>();
        return tags.Count == 0 ? query : $"tags:{string.Join(",", tags)}|{query}";
    }

    /// <summary>
    /// Take the fill slot for one query. The caller checks the cache again
    /// after this returns, because the caller it waited for has usually
    /// stored the answer by then.
    /// </summary>
    public async Task<IDisposable> EnterFillAsync(string query, CancellationToken cancellationToken = default)
    {
        var key = NormalizeKey(query);
        var gate = _fillGates[(int)((uint)key.GetHashCode() % FillGateStripes)];
        await gate.WaitAsync(cancellationToken);
        return new FillSlot(gate);
    }

    private sealed class FillSlot : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private bool _released;

        public FillSlot(SemaphoreSlim gate) => _gate = gate;

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            _gate.Release();
        }
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
            // An entry is only served inside its own lifetime as well as the
            // caller's window. An empty answer is stored with a short one,
            // and judging it by the caller's window alone would keep serving
            // it long after it stopped being trustworthy.
            if (age.TotalSeconds <= Math.Min(cacheDurationSeconds, cached.LifetimeSeconds))
            {
                _logger.LogInformation("[ReleaseCache] Cache HIT for '{Query}' - {Count} raw releases (age: {Age:F1}s)",
                    query, cached.RawReleases.Count, age.TotalSeconds);
                return cached;
            }
            else
            {
                _logger.LogDebug("[ReleaseCache] Cache EXPIRED for '{Query}' (age: {Age:F1}s > {Max}s)",
                    query, age.TotalSeconds, cacheDurationSeconds);
                // Only drop it if it is past its own lifetime too. A reader
                // with a shorter tolerance treats it as a miss without taking
                // it away from readers who would still accept it.
                if (age.TotalSeconds > cached.LifetimeSeconds)
                {
                    _cache.TryRemove(key, out _);
                }
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
    /// <param name="cacheDurationSeconds">Configured cache TTL; used as the cleanup ceiling so
    /// entries aren't evicted before their configured lifetime (previously hardcoded to 300s,
    /// which silently truncated any user-configured duration above 5 minutes).</param>
    /// <param name="indexersQueried">Which indexers were queried</param>
    public void Store(string query, IEnumerable<ReleaseSearchResult> results, int cacheDurationSeconds = 300, IEnumerable<string>? indexersQueried = null)
    {
        var key = NormalizeKey(query);
        var rawReleases = results.Select(RawRelease.FromSearchResult).ToList();

        // An empty result is cached briefly rather than not at all. Refusing
        // it outright protected against a transient indexer outage shadowing
        // real results for the whole TTL, but it also meant the parts of an
        // unreleased event queued behind one empty search each ran the whole
        // search again the moment the gate opened. A short lifetime keeps
        // both: the burst coalesces, and an outage shadows nothing for more
        // than a minute.
        var lifetime = rawReleases.Count == 0
            ? Math.Min(EmptyResultLifetimeSeconds, cacheDurationSeconds)
            : cacheDurationSeconds;

        var cached = new CachedSearchResults
        {
            RawReleases = rawReleases,
            CachedAt = DateTime.UtcNow,
            LifetimeSeconds = lifetime,
            Query = query,
            IndexersQueried = indexersQueried?.ToList() ?? new List<string>()
        };

        _cache[key] = cached;

        _logger.LogInformation("[ReleaseCache] Cached {Count} raw releases for '{Query}'",
            rawReleases.Count, query);

        // Clean up old entries (simple periodic cleanup) using the configured duration
        CleanupExpired(cacheDurationSeconds);
        EnforceCeiling();
    }

    /// <summary>
    /// Convert cached raw releases back to fresh ReleaseSearchResults.
    /// All scoring fields are zeroed - must be recalculated by ReleaseEvaluator.
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
        // Each entry is judged against the lifetime it was stored with, not
        // against whatever the caller who triggered this cleanup asked for.
        var expiredKeys = _cache
            .Where(kvp => (now - kvp.Value.CachedAt).TotalSeconds > Math.Max(maxAgeSeconds, kvp.Value.LifetimeSeconds))
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
    /// Drop the oldest entries once the cache is over its ceiling. Age alone
    /// cannot bound this, because a sweep can cache thousands of distinct
    /// queries well inside one lifetime.
    /// </summary>
    private void EnforceCeiling()
    {
        // Trimming starts at the ceiling and cuts back to the threshold, so
        // a sweep gets a hundred entries of headroom between trims. Trimming
        // at the threshold made every insert past it sort the whole cache
        // and evict exactly one entry.
        if (_cache.Count <= MaxCacheEntries) return;

        var overBy = _cache.Count - CleanupThreshold;
        var oldest = _cache
            .OrderBy(kvp => kvp.Value.CachedAt)
            .Take(overBy)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldest)
        {
            _cache.TryRemove(key, out _);
        }

        _logger.LogDebug("[ReleaseCache] Trimmed {Count} entries to stay under {Max}", oldest.Count, MaxCacheEntries);
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
