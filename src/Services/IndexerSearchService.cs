using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Unified indexer search service that searches across all configured indexers
/// Implements quality-based scoring and automatic release selection with rate limiting
/// Uses IndexerStatusService for Sonarr-style health tracking and exponential backoff
///
/// Rate limiting strategy (Sonarr-style):
/// 1. Max 5 concurrent indexer queries per search (prevents overwhelming any single search)
/// 2. HTTP-layer rate limiting via RateLimitHandler (2-second delay per indexer + jitter)
/// 3. Exponential backoff for failed indexers (0s → 1m → 5m → 15m → 30m → 1h → 24h max)
/// 4. HTTP 429 responses use Retry-After header only (no additional backoff)
/// </summary>
public class IndexerSearchService : IIndexerSearchService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<IndexerSearchService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ReleaseEvaluator _releaseEvaluator;
    private readonly ReleaseProfileService _releaseProfileService;
    private readonly QualityDetectionService _qualityDetection;
    private readonly IndexerStatusService _indexerStatus;

    // Max concurrent indexer queries per search (prevents overwhelming many indexers at once)
    private const int MaxConcurrentIndexerQueries = 5;

    // Static tracking for Sonarr-style search status indicator
    private static readonly object _statusLock = new();
    private static ActiveSearchStatus? _currentSearch = null;

    /// <summary>
    /// Get current active search status (for Sonarr-style bottom-left indicator)
    /// </summary>
    public static ActiveSearchStatus? GetCurrentSearchStatus()
    {
        lock (_statusLock)
        {
            return _currentSearch;
        }
    }

    private static void SetSearchStatus(ActiveSearchStatus? status)
    {
        lock (_statusLock)
        {
            _currentSearch = status;
        }
    }

    public IndexerSearchService(
        SportarrDbContext db,
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<IndexerSearchService> logger,
        ReleaseEvaluator releaseEvaluator,
        ReleaseProfileService releaseProfileService,
        QualityDetectionService qualityDetection,
        IndexerStatusService indexerStatus)
    {
        _db = db;
        _loggerFactory = loggerFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _releaseEvaluator = releaseEvaluator;
        _releaseProfileService = releaseProfileService;
        _qualityDetection = qualityDetection;
        _indexerStatus = indexerStatus;
    }

    /// <summary>
    /// Search all enabled indexers for releases matching query with rate limiting
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="maxResultsPerIndexer">Maximum results per indexer</param>
    /// <param name="qualityProfileId">Quality profile for filtering</param>
    /// <param name="requestedPart">For multi-part episodes, the specific part being searched (e.g., "Prelims", "Main Card")</param>
    /// <param name="sport">Sport type for part validation (e.g., "Fighting")</param>
    /// <param name="enableMultiPartEpisodes">Whether multi-part episodes are enabled. When false, rejects releases with detected parts.</param>
    /// <param name="eventTitle">Optional event title for event-type-specific part handling (e.g., Fight Night vs PPV)</param>
    public async Task<List<ReleaseSearchResult>> SearchAllIndexersAsync(string query, int maxResultsPerIndexer = 10000, int? qualityProfileId = null, string? requestedPart = null, string? sport = null, bool enableMultiPartEpisodes = true, string? eventTitle = null)
    {
        _logger.LogInformation("[Indexer Search] Searching all indexers for: {Query}", query);

        var indexers = await _db.Indexers
            .Where(i => i.Enabled)
            .OrderBy(i => i.Priority)
            .ToListAsync();

        if (!indexers.Any())
        {
            _logger.LogWarning("[Indexer Search] No enabled indexers configured");
            return new List<ReleaseSearchResult>();
        }

        // DEDUPLICATION: Remove duplicate indexers by URL to prevent searching the same endpoint multiple times
        // This can happen if Prowlarr sync adds the same indexer multiple times or manual duplicates exist
        var originalCount = indexers.Count;
        indexers = indexers
            .GroupBy(i => i.Url?.ToLowerInvariant() ?? i.Name) // Group by URL (or name if no URL)
            .Select(g => g.First()) // Take the first (highest priority) from each group
            .ToList();

        if (indexers.Count < originalCount)
        {
            _logger.LogWarning("[Indexer Search] Removed {DuplicateCount} duplicate indexers (same URL). {RemainingCount} unique indexers will be searched.",
                originalCount - indexers.Count, indexers.Count);
        }

        // Check available download client types
        var downloadClients = await _db.DownloadClients
            .Where(dc => dc.Enabled)
            .Select(dc => dc.Type)
            .Distinct()
            .ToListAsync();

        if (!downloadClients.Any())
        {
            _logger.LogWarning("[Indexer Search] No enabled download clients configured - cannot search any indexers. " +
                "Please add and enable a download client (qBittorrent, SABnzbd, etc.) in Settings > Download Clients.");
            return new List<ReleaseSearchResult>();
        }

        // Determine which protocols are supported based on available clients
        var torrentClients = new[] { DownloadClientType.QBittorrent, DownloadClientType.Transmission,
                                     DownloadClientType.Deluge, DownloadClientType.RTorrent,
                                     DownloadClientType.UTorrent, DownloadClientType.Decypharr };
        var usenetClients = new[] { DownloadClientType.Sabnzbd, DownloadClientType.NzbGet,
                                    DownloadClientType.DecypharrUsenet, DownloadClientType.NZBdav };

        var hasTorrentClient = downloadClients.Any(dc => torrentClients.Contains(dc));
        var hasUsenetClient = downloadClients.Any(dc => usenetClients.Contains(dc));

        _logger.LogInformation("[Indexer Search] Available download clients: Torrent={HasTorrent}, Usenet={HasUsenet}",
            hasTorrentClient, hasUsenetClient);

        // Filter indexers based on available download client types
        var countBeforeClientFilter = indexers.Count;
        indexers = indexers.Where(indexer =>
        {
            var include = indexer.Type switch
            {
                IndexerType.Torznab or IndexerType.Torrent => hasTorrentClient,
                IndexerType.Newznab => hasUsenetClient,
                _ => false
            };

            if (!include)
            {
                _logger.LogInformation("[Indexer Search] Skipping {Indexer} ({Type}) - no matching download client available",
                    indexer.Name, indexer.Type);
            }

            return include;
        }).ToList();

        if (!indexers.Any())
        {
            _logger.LogWarning("[Indexer Search] No indexers available for configured download clients ({OriginalCount} total indexers, but none match available clients)",
                countBeforeClientFilter);
            return new List<ReleaseSearchResult>();
        }

        _logger.LogInformation("[Indexer Search] Using {Count} of {OriginalCount} indexers (filtered by download client availability)",
            indexers.Count, countBeforeClientFilter);

        var allResults = new List<ReleaseSearchResult>();

        // SONARR-STYLE STATUS TRACKING: Initialize status for bottom-left indicator
        var searchStatus = new ActiveSearchStatus
        {
            SearchQuery = query,
            EventTitle = eventTitle,
            Part = requestedPart,
            TotalIndexers = indexers.Count,
            ActiveIndexers = 0,
            CompletedIndexers = 0,
            ReleasesFound = 0,
            StartedAt = DateTime.UtcNow,
            IsComplete = false
        };
        SetSearchStatus(searchStatus);

        try
        {
            // SONARR-STYLE THROTTLING: Limit concurrent indexer queries to prevent overwhelming indexers
            // Instead of hitting all 39 indexers simultaneously, we process max 5 at a time
            // Combined with HTTP-layer rate limiting, this prevents rate limit errors
            using var indexerSemaphore = new SemaphoreSlim(MaxConcurrentIndexerQueries, MaxConcurrentIndexerQueries);

            var searchTasks = indexers.Select(async indexer =>
            {
                await indexerSemaphore.WaitAsync();

                // Update active count (ensure non-negative in case of race conditions)
                lock (_statusLock)
                {
                    if (_currentSearch != null)
                        _currentSearch.ActiveIndexers = Math.Max(0, Math.Min(MaxConcurrentIndexerQueries, indexers.Count - _currentSearch.CompletedIndexers));
                }

                try
                {
                    var results = await SearchIndexerAsync(indexer, query, maxResultsPerIndexer);

                    // Update status with results (ensure non-negative in case of race conditions)
                    lock (_statusLock)
                    {
                        if (_currentSearch != null)
                        {
                            _currentSearch.CompletedIndexers++;
                            _currentSearch.ReleasesFound += results.Count;
                            _currentSearch.ActiveIndexers = Math.Max(0, Math.Min(MaxConcurrentIndexerQueries,
                                indexers.Count - _currentSearch.CompletedIndexers));
                        }
                    }

                    return results;
                }
                finally
                {
                    indexerSemaphore.Release();
                }
            });

            var results = await Task.WhenAll(searchTasks);

            // Combine all results
            foreach (var indexerResults in results)
            {
                allResults.AddRange(indexerResults);
            }
        }
        finally
        {
            // Mark search as complete
            lock (_statusLock)
            {
                if (_currentSearch != null)
                {
                    _currentSearch.IsComplete = true;
                    _currentSearch.ReleasesFound = allResults.Count;
                }
            }

            // Clear status after a short delay to allow UI to show completion
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                SetSearchStatus(null);
            });
        }

        // Load release profiles for keyword filtering (Sonarr-style)
        var releaseProfiles = await _releaseProfileService.LoadReleaseProfilesAsync();

        // Evaluate releases against quality profile
        QualityProfile? profile = null;
        List<CustomFormat>? customFormats = null;
        List<QualityDefinition>? qualityDefinitions = null;

        if (qualityProfileId.HasValue)
        {
            // Items and FormatItems are stored as JSON columns, so they're automatically loaded
            profile = await _db.QualityProfiles
                .FirstOrDefaultAsync(p => p.Id == qualityProfileId.Value);
            // Specifications is stored as a JSON column, so it's automatically loaded
            customFormats = await _db.CustomFormats.ToListAsync();
        }

        // Load quality definitions for Sonarr-style size validation
        qualityDefinitions = await _db.QualityDefinitions.ToListAsync();

        // Evaluate each release
        foreach (var release in allResults)
        {
            // Detect if this is a pack result (marked by pack search endpoint or contains pack keywords)
            var isPack = release.IsPack;

            var evaluation = _releaseEvaluator.EvaluateRelease(
                release,
                profile,
                customFormats,
                qualityDefinitions,
                requestedPart,
                sport,
                enableMultiPartEpisodes,
                eventTitle,
                null,  // runtimeMinutes
                isPack);

            // Update release with evaluation results
            release.Score = evaluation.TotalScore;
            release.QualityScore = evaluation.QualityScore;
            release.CustomFormatScore = evaluation.CustomFormatScore;
            release.SizeScore = evaluation.SizeScore;
            release.Approved = evaluation.Approved;
            release.Rejections = evaluation.Rejections;
            release.MatchedFormats = evaluation.MatchedFormats;
            release.Quality = evaluation.Quality;
            release.Part = requestedPart; // Store the requested part for multi-part imports

            // Apply release profile filtering (Required/Ignored keywords, Preferred score)
            if (releaseProfiles.Any())
            {
                var profileEval = _releaseProfileService.EvaluateRelease(release, releaseProfiles);

                // Add rejections from release profiles
                if (profileEval.IsRejected)
                {
                    release.Approved = false;
                    release.Rejections.AddRange(profileEval.Rejections);
                }

                // Add preferred score to custom format score (affects ranking)
                if (profileEval.PreferredScore != 0)
                {
                    release.CustomFormatScore += profileEval.PreferredScore;
                    release.Score += profileEval.PreferredScore;
                }
            }
        }

        // Sort by ranking priority (Sonarr-style - quality trumps all):
        // 1. Approved status (approved first)
        // 2. Quality score (profile position)
        // 3. Custom format score
        // 4. Seeders (for torrents)
        // 5. Size score (proximity to preferred size, or larger if no preferred)
        allResults = allResults
            .OrderByDescending(r => r.Approved)
            .ThenByDescending(r => r.QualityScore)
            .ThenByDescending(r => r.CustomFormatScore)
            .ThenByDescending(r => r.Seeders ?? 0)
            .ThenByDescending(r => r.SizeScore)
            .ToList();

        _logger.LogInformation("[Indexer Search] Found {Count} total results across {IndexerCount} indexers ({Approved} approved)",
            allResults.Count, indexers.Count, allResults.Count(r => r.Approved));

        return allResults;
    }

    /// <summary>
    /// Search a single indexer with health tracking.
    /// Rate limiting is handled at the HTTP layer via RateLimitHandler.
    /// </summary>
    public async Task<List<ReleaseSearchResult>> SearchIndexerAsync(Indexer indexer, string query, int maxResults = 10000)
    {
        try
        {
            // Check if indexer is available (not disabled due to failures or rate limits)
            var (isAvailable, reason) = await _indexerStatus.IsIndexerAvailableAsync(indexer.Id);
            if (!isAvailable)
            {
                _logger.LogInformation("[Indexer Search] Skipping {Indexer}: {Reason}", indexer.Name, reason);
                return new List<ReleaseSearchResult>();
            }

            _logger.LogInformation("[Indexer Search] Searching {Indexer} ({Type})", indexer.Name, indexer.Type);

            List<ReleaseSearchResult> results;
            try
            {
                results = indexer.Type switch
                {
                    IndexerType.Torznab => await SearchTorznabAsync(indexer, query, maxResults),
                    IndexerType.Newznab => await SearchNewznabAsync(indexer, query, maxResults),
                    _ => new List<ReleaseSearchResult>()
                };

                // Record success
                await _indexerStatus.RecordSuccessAsync(indexer.Id);
            }
            catch (IndexerRateLimitException ex)
            {
                // Handle HTTP 429 - record rate limit status
                await _indexerStatus.RecordRateLimitedAsync(indexer.Id, ex.RetryAfter);
                return new List<ReleaseSearchResult>();
            }
            catch (IndexerRequestException ex)
            {
                // Handle other HTTP errors - record failure with backoff
                await _indexerStatus.RecordFailureAsync(indexer.Id, ex.Message);
                return new List<ReleaseSearchResult>();
            }

            // Set protocol and indexer ID based on indexer type
            var protocol = indexer.Type switch
            {
                IndexerType.Torznab => "Torrent",
                IndexerType.Torrent => "Torrent",
                IndexerType.Rss => "Torrent", // RSS feeds are typically torrents
                IndexerType.Newznab => "Usenet",
                _ => "Torrent" // Default to torrent for unknown types
            };
            foreach (var result in results)
            {
                result.Protocol = protocol;
                result.IndexerId = indexer.Id; // For release profile filtering
            }

            // Filter by minimum seeders (for torrents)
            if (indexer.Type == IndexerType.Torznab && indexer.MinimumSeeders > 0)
            {
                results = results.Where(r => r.Seeders >= indexer.MinimumSeeders).ToList();
            }

            _logger.LogInformation("[Indexer Search] {Indexer} returned {Count} results", indexer.Name, results.Count);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Indexer Search] Error searching {Indexer}", indexer.Name);
            // Record general failure
            await _indexerStatus.RecordFailureAsync(indexer.Id, ex.Message);
            return new List<ReleaseSearchResult>();
        }
    }

    /// <summary>
    /// Select best release from search results based on quality profile
    /// </summary>
    public ReleaseSearchResult? SelectBestRelease(
        List<ReleaseSearchResult> results,
        QualityProfile qualityProfile)
    {
        if (!results.Any())
        {
            return null;
        }

        _logger.LogInformation("[Indexer Search] Selecting best release from {Count} results", results.Count);

        // Filter by allowed qualities
        var allowedQualities = qualityProfile.Items
            .Where(q => q.Allowed)
            .Select(q => q.Name.ToLower())
            .ToList();

        var filteredResults = results.Where(r =>
        {
            if (string.IsNullOrEmpty(r.Quality))
            {
                return true; // Include unknown quality
            }
            return allowedQualities.Contains(r.Quality.ToLower());
        }).ToList();

        if (!filteredResults.Any())
        {
            _logger.LogWarning("[Indexer Search] No results match quality profile");
            return null;
        }

        // Get highest priority allowed quality
        var preferredQuality = qualityProfile.Items
            .Where(q => q.Allowed)
            .OrderByDescending(q => q.Quality)
            .FirstOrDefault();

        // Find releases matching preferred quality
        var preferredReleases = filteredResults
            .Where(r => r.Quality == preferredQuality?.Name)
            .ToList();

        if (preferredReleases.Any())
        {
            // Return highest scored release of preferred quality
            var best = preferredReleases.OrderByDescending(r => r.Score).First();
            _logger.LogInformation("[Indexer Search] Selected: {Title} from {Indexer} (Score: {Score})",
                best.Title, best.Indexer, best.Score);
            return best;
        }

        // Fallback to highest scored release of any allowed quality
        var fallback = filteredResults.OrderByDescending(r => r.Score).First();
        _logger.LogInformation("[Indexer Search] Selected (fallback): {Title} from {Indexer} (Score: {Score})",
            fallback.Title, fallback.Indexer, fallback.Score);
        return fallback;
    }

    /// <summary>
    /// Test connection to an indexer
    /// </summary>
    public async Task<bool> TestIndexerAsync(Indexer indexer)
    {
        try
        {
            return indexer.Type switch
            {
                IndexerType.Torznab => await TestTorznabAsync(indexer),
                IndexerType.Newznab => await TestNewznabAsync(indexer),
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Indexer Search] Error testing {Indexer}", indexer.Name);
            return false;
        }
    }

    /// <summary>
    /// Fetch RSS feeds from all RSS-enabled indexers (Sonarr-style RSS sync)
    /// This fetches recent releases WITHOUT a search query - used for passive discovery
    /// Much more efficient than searching per-event: O(indexers) vs O(events * indexers)
    /// </summary>
    public async Task<List<ReleaseSearchResult>> FetchAllRssFeedsAsync(int maxReleasesPerIndexer = 500)
    {
        _logger.LogInformation("[Indexer Search] Fetching RSS feeds from all indexers");

        var indexers = await _db.Indexers
            .Where(i => i.Enabled && i.EnableRss)
            .OrderBy(i => i.Priority)
            .ToListAsync();

        if (!indexers.Any())
        {
            _logger.LogDebug("[Indexer Search] No RSS-enabled indexers configured");
            return new List<ReleaseSearchResult>();
        }

        var allResults = new List<ReleaseSearchResult>();

        // SONARR-STYLE THROTTLING: Limit concurrent RSS fetches
        using var indexerSemaphore = new SemaphoreSlim(MaxConcurrentIndexerQueries, MaxConcurrentIndexerQueries);

        var fetchTasks = indexers.Select(async indexer =>
        {
            await indexerSemaphore.WaitAsync();
            try
            {
                return await FetchRssFeedFromIndexerAsync(indexer, maxReleasesPerIndexer);
            }
            finally
            {
                indexerSemaphore.Release();
            }
        });

        var results = await Task.WhenAll(fetchTasks);

        // Combine all results
        foreach (var indexerResults in results)
        {
            allResults.AddRange(indexerResults);
        }

        _logger.LogInformation("[Indexer Search] Fetched {Count} total releases from {IndexerCount} RSS feeds",
            allResults.Count, indexers.Count);

        return allResults;
    }

    /// <summary>
    /// Fetch RSS feed from a single indexer with health tracking
    /// </summary>
    private async Task<List<ReleaseSearchResult>> FetchRssFeedFromIndexerAsync(Indexer indexer, int maxResults)
    {
        try
        {
            // Check if indexer is available (not disabled due to failures or rate limits)
            var (isAvailable, reason) = await _indexerStatus.IsIndexerAvailableAsync(indexer.Id);
            if (!isAvailable)
            {
                _logger.LogDebug("[RSS Feed] Skipping {Indexer}: {Reason}", indexer.Name, reason);
                return new List<ReleaseSearchResult>();
            }

            List<ReleaseSearchResult> results;
            try
            {
                results = indexer.Type switch
                {
                    IndexerType.Torznab => await FetchTorznabRssAsync(indexer, maxResults),
                    IndexerType.Newznab => await FetchNewznabRssAsync(indexer, maxResults),
                    _ => new List<ReleaseSearchResult>()
                };

                // Record success
                await _indexerStatus.RecordSuccessAsync(indexer.Id);
            }
            catch (IndexerRateLimitException ex)
            {
                // Handle HTTP 429 - record rate limit status
                await _indexerStatus.RecordRateLimitedAsync(indexer.Id, ex.RetryAfter);
                return new List<ReleaseSearchResult>();
            }
            catch (IndexerRequestException ex)
            {
                // Handle other HTTP errors - record failure with backoff
                await _indexerStatus.RecordFailureAsync(indexer.Id, ex.Message);
                return new List<ReleaseSearchResult>();
            }

            // Set protocol based on indexer type
            var protocol = indexer.Type switch
            {
                IndexerType.Torznab => "Torrent",
                IndexerType.Torrent => "Torrent",
                IndexerType.Rss => "Torrent",
                IndexerType.Newznab => "Usenet",
                _ => "Torrent"
            };
            foreach (var result in results)
            {
                result.Protocol = protocol;
            }

            // Filter by minimum seeders (for torrents)
            if (indexer.Type == IndexerType.Torznab && indexer.MinimumSeeders > 0)
            {
                results = results.Where(r => r.Seeders >= indexer.MinimumSeeders).ToList();
            }

            _logger.LogDebug("[RSS Feed] {Indexer} returned {Count} releases", indexer.Name, results.Count);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RSS Feed] Error fetching from {Indexer}", indexer.Name);
            await _indexerStatus.RecordFailureAsync(indexer.Id, ex.Message);
            return new List<ReleaseSearchResult>();
        }
    }

    private async Task<List<ReleaseSearchResult>> FetchTorznabRssAsync(Indexer indexer, int maxResults)
    {
        // Log categories being used for RSS (important for filtering out non-TV content)
        var categories = indexer.Categories?.Any() == true
            ? string.Join(",", indexer.Categories)
            : string.Join(",", NewznabCategories.DefaultSportCategories);
        _logger.LogDebug("[RSS Feed] {Indexer}: Fetching with categories [{Categories}]", indexer.Name, categories);

        var httpClient = _httpClientFactory.CreateClient("IndexerClient");
        var torznabLogger = _loggerFactory.CreateLogger<TorznabClient>();
        var client = new TorznabClient(httpClient, torznabLogger, _qualityDetection);

        return await client.FetchRssFeedAsync(indexer, maxResults);
    }

    private async Task<List<ReleaseSearchResult>> FetchNewznabRssAsync(Indexer indexer, int maxResults)
    {
        // Log categories being used for RSS (important for filtering out non-TV content)
        var categories = indexer.Categories?.Any() == true
            ? string.Join(",", indexer.Categories)
            : string.Join(",", NewznabCategories.DefaultSportCategories);
        _logger.LogDebug("[RSS Feed] {Indexer}: Fetching with categories [{Categories}]", indexer.Name, categories);

        var httpClient = _httpClientFactory.CreateClient("IndexerClient");
        var newznabLogger = _loggerFactory.CreateLogger<NewznabClient>();
        var client = new NewznabClient(httpClient, newznabLogger, _qualityDetection);

        return await client.FetchRssFeedAsync(indexer, maxResults);
    }

    // Private helper methods

    private async Task<List<ReleaseSearchResult>> SearchTorznabAsync(Indexer indexer, string query, int maxResults)
    {
        var httpClient = _httpClientFactory.CreateClient("IndexerClient");
        var torznabLogger = _loggerFactory.CreateLogger<TorznabClient>();
        var client = new TorznabClient(httpClient, torznabLogger, _qualityDetection);

        return await client.SearchAsync(indexer, query, maxResults);
    }

    private async Task<List<ReleaseSearchResult>> SearchNewznabAsync(Indexer indexer, string query, int maxResults)
    {
        var httpClient = _httpClientFactory.CreateClient("IndexerClient");
        var newznabLogger = _loggerFactory.CreateLogger<NewznabClient>();
        var client = new NewznabClient(httpClient, newznabLogger, _qualityDetection);

        return await client.SearchAsync(indexer, query, maxResults);
    }

    private async Task<bool> TestTorznabAsync(Indexer indexer)
    {
        var httpClient = _httpClientFactory.CreateClient("IndexerClient");
        var torznabLogger = _loggerFactory.CreateLogger<TorznabClient>();
        var client = new TorznabClient(httpClient, torznabLogger, _qualityDetection);

        return await client.TestConnectionAsync(indexer);
    }

    private async Task<bool> TestNewznabAsync(Indexer indexer)
    {
        var httpClient = _httpClientFactory.CreateClient("IndexerClient");
        var newznabLogger = _loggerFactory.CreateLogger<NewznabClient>();
        var client = new NewznabClient(httpClient, newznabLogger);

        return await client.TestConnectionAsync(indexer);
    }
}

/// <summary>
/// Represents the current active search status (Sonarr-style bottom-left indicator)
/// </summary>
public class ActiveSearchStatus
{
    public string SearchQuery { get; set; } = "";
    public string? EventTitle { get; set; }
    public string? Part { get; set; }
    public int TotalIndexers { get; set; }
    public int ActiveIndexers { get; set; }
    public int CompletedIndexers { get; set; }
    public int ReleasesFound { get; set; }
    public DateTime StartedAt { get; set; }
    public bool IsComplete { get; set; }
}
