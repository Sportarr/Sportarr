using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sportarr.Api.Services;

/// <summary>
/// Describes an indexer that was skipped during a search, with a human-readable
/// reason and a category string the frontend can group on.
/// Categories: TemporarilyDisabled, RateLimited, QueryLimit, Disabled,
/// NoDownloadClient, TagMismatch, Other
/// </summary>
public record SkippedIndexer(int IndexerId, string Name, string Reason, string Category);

/// <summary>
/// Unified indexer search service that searches across all configured indexers.
/// Implements quality-based scoring and automatic release selection with rate limiting.
/// Uses IndexerStatusService for health tracking and exponential backoff.
///
/// Rate limiting strategy:
/// 1. Max 5 concurrent indexer queries per search (prevents overwhelming any single search).
/// 2. HTTP-layer pacing via IndexerQueryQuotaHandler (2-second default delay plus jitter).
/// 3. Exponential backoff for failed indexers (0s, 1m, 5m, 15m, 30m, 1h, 24h max).
/// 4. HTTP 429 responses use Retry-After header only (no additional backoff).
/// </summary>
public class IndexerSearchService : IIndexerSearchService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<IndexerSearchService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRateLimitService _rateLimitService;
    private readonly ReleaseEvaluator _releaseEvaluator;
    private readonly ReleaseProfileService _releaseProfileService;
    private readonly QualityDetectionService _qualityDetection;
    private readonly IndexerStatusService _indexerStatus;
    private readonly ConfigService _configService;
    private readonly SourceOutcomeCache? _sourceOutcomes;

    // Max concurrent indexer queries per search (prevents overwhelming many indexers at once)
    private const int MaxConcurrentIndexerQueries = 5;

    // Static tracking for the active search status indicator.
    private static readonly object _statusLock = new();
    private static ActiveSearchStatus? _currentSearch = null;

    /// <summary>
    /// Get current active search status (drives the bottom-left indicator).
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
        IRateLimitService rateLimitService,
        ILogger<IndexerSearchService> logger,
        ReleaseEvaluator releaseEvaluator,
        ReleaseProfileService releaseProfileService,
        QualityDetectionService qualityDetection,
        IndexerStatusService indexerStatus,
        ConfigService configService,
        SearchResultCache? searchResultCache = null)
    {
        _db = db;
        _loggerFactory = loggerFactory;
        _httpClientFactory = httpClientFactory;
        _rateLimitService = rateLimitService;
        _logger = logger;
        _releaseEvaluator = releaseEvaluator;
        _releaseProfileService = releaseProfileService;
        _qualityDetection = qualityDetection;
        _indexerStatus = indexerStatus;
        _configService = configService;
        _sourceOutcomes = searchResultCache?.SourceOutcomes;
    }

    public async Task<string> GetSearchSourceFingerprintAsync(bool interactiveSearch, IEnumerable<int>? leagueTags)
    {
        var tags = leagueTags?.ToList();
        var indexers = await _db.Indexers.AsNoTracking()
            .Where(i => i.Enabled && (interactiveSearch ? i.EnableInteractiveSearch : i.EnableAutomaticSearch))
            .OrderBy(i => i.Id)
            .ToListAsync();
        if (tags != null)
            indexers = indexers.Where(i => Helpers.TagHelper.TagsMatch(i.Tags, tags)).ToList();

        var clientTypes = await _db.DownloadClients.AsNoTracking()
            .Where(client => client.Enabled)
            .OrderBy(client => client.Type)
            .Select(client => client.Type)
            .Distinct()
            .ToListAsync();
        var identity = new
        {
            Mode = interactiveSearch ? "interactive" : "automatic",
            Indexers = indexers.Select(SourceIdentity),
            ClientTypes = clientTypes
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity)));
    }

    private static object SourceIdentity(Indexer i) => new
    {
        i.Id, i.Name, i.Type, i.Url, i.ApiPath, i.ApiKey, i.AdditionalParameters,
        Categories = i.Categories.OrderBy(value => value).ToArray(),
        Tags = i.Tags.OrderBy(value => value).ToArray(),
        i.MinimumSeeders, i.DownloadClientId, i.EarlyReleaseLimit,
        MultiLanguages = i.MultiLanguages?.OrderBy(value => value).ToArray(),
        i.RssUseEzrssFormat, i.RssUseEnclosureUrl, i.RssUseEnclosureLength,
        i.RssParseSizeInDescription, i.RssParseSeedersInDescription,
        i.RssAllowZeroSize, i.RssSizeElementName, i.LastModified
    };

    private static string SourceRequestKey(Indexer indexer, string query, int maximum, string? eventId,
        bool useCategoryFilter, bool interactiveSearch) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            Source = SourceIdentity(indexer), Query = query, Maximum = maximum,
            EventId = Helpers.SportarrIdToken.Normalize(eventId), useCategoryFilter, interactiveSearch
        })));

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
    public async Task<List<ReleaseSearchResult>> SearchAllIndexersAsync(string query, int maxResultsPerIndexer = 10000, int? qualityProfileId = null, string? requestedPart = null, string? sport = null, bool enableMultiPartEpisodes = true, string? eventTitle = null, List<int>? leagueTags = null, List<SkippedIndexer>? skippedIndexers = null, bool allowHighlights = false, string? sportarrId = null, bool useCategoryFilter = true, bool interactiveSearch = true, string? leagueName = null)
        => (await SearchAllIndexersDetailedAsync(query, maxResultsPerIndexer, qualityProfileId, requestedPart, sport, enableMultiPartEpisodes, eventTitle, leagueTags, skippedIndexers, allowHighlights, sportarrId, useCategoryFilter, interactiveSearch, leagueName: leagueName)).Releases;

    public async Task<SearchOperationOutcome> SearchAllIndexersDetailedAsync(string query, int maxResultsPerIndexer = 10000, int? qualityProfileId = null, string? requestedPart = null, string? sport = null, bool enableMultiPartEpisodes = true, string? eventTitle = null, List<int>? leagueTags = null, List<SkippedIndexer>? skippedIndexers = null, bool allowHighlights = false, string? sportarrId = null, bool useCategoryFilter = true, bool interactiveSearch = true, bool forceRefresh = false, bool cacheSuccessfulSources = false, string? leagueName = null)
    {
        _logger.LogInformation("[Indexer Search] Searching all indexers for: {Query}", query);

        var diagnostics = new System.Collections.Concurrent.ConcurrentBag<IndexerSearchDiagnostic>();
        var sourceCache = cacheSuccessfulSources ? _sourceOutcomes : null;
        var cachedExpiries = new System.Collections.Concurrent.ConcurrentBag<DateTimeOffset>();
        var fetchedOutcomes = new System.Collections.Concurrent.ConcurrentBag<(string Key, IndexerSearchOutcome Outcome)>();
        var cacheDuration = (await _configService.GetConfigAsync()).SearchCacheDuration;

        // Lock for thread-safe appends to skippedIndexers from parallel tasks
        var skipLock = new object();
        void RecordSkip(int id, string name, string reason, string category)
        {
            if (skippedIndexers == null) return;
            lock (skipLock)
            {
                skippedIndexers.Add(new SkippedIndexer(id, name, reason, category));
            }
        }

        var indexers = await _db.Indexers
            .Where(i => i.Enabled && (interactiveSearch ? i.EnableInteractiveSearch : i.EnableAutomaticSearch))
            .OrderBy(i => i.Priority)
            .ToListAsync();

        // Filter indexers by tag matching (untagged indexers apply to all leagues)
        if (leagueTags != null)
        {
            var beforeCount = indexers.Count;
            var filtered = new List<Indexer>();
            foreach (var idx in indexers)
            {
                if (Helpers.TagHelper.TagsMatch(idx.Tags, leagueTags))
                {
                    filtered.Add(idx);
                }
                else
                {
                    RecordSkip(idx.Id, idx.Name, $"Excluded by league tag filter [{string.Join(", ", leagueTags)}]", "TagMismatch");
                }
            }
            indexers = filtered;
            if (indexers.Count < beforeCount)
            {
                _logger.LogInformation("[Indexer Search] Tag filtering: {Before} → {After} indexers for league tags [{Tags}]",
                    beforeCount, indexers.Count, string.Join(", ", leagueTags));
            }
        }

        if (!indexers.Any())
        {
            _logger.LogWarning("[Indexer Search] No enabled indexers configured");
            return new(new(), Array.Empty<IndexerSearchDiagnostic>(), false);
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
            return new(new(), Array.Empty<IndexerSearchDiagnostic>(), false);
        }

        // Determine which protocols are supported based on available clients.
        // Use the canonical protocol map - a local copy of these lists went
        // stale and silently dropped the blackhole types, so blackhole-only
        // setups were told no client supports their indexer's protocol.
        var torrentClients = DownloadClientService.GetClientTypesForProtocol("torrent");
        var usenetClients = DownloadClientService.GetClientTypesForProtocol("usenet");

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
                // Plain-RSS feeds serve torrent links/magnets (their releases
                // are tagged Protocol=Torrent throughout), so they need a
                // torrent client like any other torrent indexer. Rss was
                // missing here and fell through to the default, which skipped
                // every plain-RSS indexer regardless of configured clients.
                IndexerType.Torznab or IndexerType.Torrent or IndexerType.BroadcasTheNet or IndexerType.Rss => hasTorrentClient,
                IndexerType.Newznab => hasUsenetClient,
                _ => false
            };

            if (!include)
            {
                _logger.LogInformation("[Indexer Search] Skipping {Indexer} ({Type}) - no matching download client available",
                    indexer.Name, indexer.Type);
                RecordSkip(indexer.Id, indexer.Name,
                    $"No enabled download client supports {indexer.Type} protocol",
                    "NoDownloadClient");
            }

            return include;
        }).ToList();

        if (!indexers.Any())
        {
            _logger.LogWarning("[Indexer Search] No indexers available for configured download clients ({OriginalCount} total indexers, but none match available clients)",
                countBeforeClientFilter);
            return new(new(), Array.Empty<IndexerSearchDiagnostic>(), false);
        }

        _logger.LogInformation("[Indexer Search] Using {Count} of {OriginalCount} indexers (filtered by download client availability)",
            indexers.Count, countBeforeClientFilter);

        var allResults = new List<ReleaseSearchResult>();

        // STATUS TRACKING: Initialize status for the bottom-left indicator.
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
            // THROTTLING: Limit concurrent indexer queries to prevent overwhelming indexers.
            // Instead of hitting all 39 indexers simultaneously, we process max 5 at a time.
            // Combined with HTTP-layer rate limiting, this prevents rate limit errors.
            using var indexerSemaphore = new SemaphoreSlim(MaxConcurrentIndexerQueries, MaxConcurrentIndexerQueries);

            using var requestBatch = new IndexerSearchRequestBatch();
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
                    var sourceKey = SourceRequestKey(indexer, query, maxResultsPerIndexer, sportarrId, useCategoryFilter, interactiveSearch);
                    if (forceRefresh) sourceCache?.Invalidate(sourceKey);
                    // Pre-check availability so we can surface the skip reason to callers.
                    // SearchIndexerAsync also runs this check internally (for safety); the
                    // duplicate call is cheap and keeps the public API unchanged.
                    if (skippedIndexers != null || sourceCache != null)
                    {
                        var (isAvailable, reason) = await _indexerStatus.IsIndexerAvailableAsync(indexer.Id);
                        if (!isAvailable)
                        {
                            var category = reason switch
                            {
                                var r when r != null && r.StartsWith("Temporarily disabled", StringComparison.OrdinalIgnoreCase) => "TemporarilyDisabled",
                                var r when r != null && r.StartsWith("Rate limited", StringComparison.OrdinalIgnoreCase) => "RateLimited",
                                var r when r != null && r.StartsWith("Query limit", StringComparison.OrdinalIgnoreCase) => "QueryLimit",
                                var r when r != null && r.StartsWith("Indexer is disabled", StringComparison.OrdinalIgnoreCase) => "Disabled",
                                _ => "Other"
                            };
                            RecordSkip(indexer.Id, indexer.Name, reason ?? "Unavailable", category);

                            lock (_statusLock)
                            {
                                if (_currentSearch != null)
                                {
                                    _currentSearch.CompletedIndexers++;
                                    _currentSearch.ActiveIndexers = Math.Max(0, Math.Min(MaxConcurrentIndexerQueries,
                                        indexers.Count - _currentSearch.CompletedIndexers));
                                }
                            }
                            diagnostics.Add(new(indexer.Id, indexer.Name, query, SearchTermination.Unavailable, 0, Array.Empty<SearchPageObservation>(), false, false));
                            return new List<ReleaseSearchResult>();
                        }
                    }

                    var outcome = forceRefresh ? null : sourceCache?.TryGet(sourceKey, cacheDuration);
                    if (outcome == null)
                    {
                        var retrievalCache = sourceCache == null ? null : new RawIndexerRetrievalCache(sourceCache,
                            cacheDuration, forceRefresh, SourceRequestKey(indexer, query, maxResultsPerIndexer, null, useCategoryFilter, interactiveSearch));
                        outcome = await SearchIndexerCoreDetailedAsync(indexer, query, maxResultsPerIndexer, sportarrId, useCategoryFilter, requestBatch, retrievalCache);
                        fetchedOutcomes.Add((sourceKey, outcome));
                    }
                    if (outcome.CacheExpiresAt.HasValue) cachedExpiries.Add(outcome.CacheExpiresAt.Value);
                    diagnostics.Add(new(indexer.Id, indexer.Name, query, outcome.Termination, outcome.RawCursor, outcome.Pages, outcome.KnownPageLimit, outcome.SatisfiesRequest));
                    var results = outcome.Releases;

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

        // Keep healthy evidence when another source prevents aggregate caching.
        // Store before evaluation and never extend a reused row's expiry.
        if (diagnostics.Any(d => !d.SatisfiesRequest && d.Termination is not (SearchTermination.UnknownTail or SearchTermination.PageCeiling)))
            foreach (var fetched in fetchedOutcomes)
                sourceCache?.Store(fetched.Key, fetched.Outcome, cacheDuration);

        await EvaluateReleasesAsync(allResults, qualityProfileId, requestedPart, sport,
            enableMultiPartEpisodes, eventTitle, leagueTags, allowHighlights, leagueName);

        // Sort by ranking priority (quality trumps all):
        // 1. Approved status (approved first)
        // 2. Quality score (profile position)
        // 3. Revision (repack > proper > none) unless propers are set to Do Not Prefer
        // 4. Custom format score
        // 5. Indexer flags (freeleech etc.) when Prefer Indexer Flags is on
        // 6. Seeders (for torrents)
        // 7. Size score (proximity to preferred size, or larger if no preferred)
        var rankingConfig = await _configService.GetConfigAsync();
        var preferIndexerFlags = rankingConfig.PreferIndexerFlags;
        var preferRevisions = rankingConfig.DownloadPropersAndRepacks != "doNotPrefer";
        allResults = allResults
            .OrderByDescending(r => r.Approved)
            .ThenByDescending(r => r.QualityScore)
            .ThenByDescending(r => preferRevisions ? Helpers.ReleaseRevision.Parse(r.Title) : 0)
            .ThenByDescending(r => r.CustomFormatScore)
            .ThenByDescending(r => preferIndexerFlags && !string.IsNullOrEmpty(r.IndexerFlags) ? 1 : 0)
            .ThenByDescending(r => r.Seeders ?? 0)
            .ThenByDescending(r => r.SizeScore)
            .ToList();

        _logger.LogInformation("[Indexer Search] Found {Count} total results across {IndexerCount} indexers ({Approved} approved)",
            allResults.Count, indexers.Count, allResults.Count(r => r.Approved));

        return new(allResults, diagnostics.OrderBy(d => d.IndexerId).ToArray(), diagnostics.All(d => d.SatisfiesRequest))
        {
            CacheExpiresAt = cachedExpiries.IsEmpty ? null : cachedExpiries.Min()
        };
    }

    /// <summary>
    /// Apply current search policy before event matching and blocklist validation.
    /// </summary>
    public async Task EvaluateReleasesAsync(
        List<ReleaseSearchResult> releases,
        int? qualityProfileId,
        string? requestedPart,
        string? sport,
        bool enableMultiPartEpisodes,
        string? eventTitle,
        List<int>? leagueTags = null,
        bool allowHighlights = false,
        string? leagueName = null)
    {
        // Load release profiles for keyword filtering.
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

        // Load quality definitions for size validation.
        qualityDefinitions = await _db.QualityDefinitions.ToListAsync();

        // Load config for indexer retention setting
        var config = await _configService.GetConfigAsync();
        var retentionDays = config.IndexerRetention;
        var retentionCutoff = retentionDays > 0 ? DateTime.UtcNow.AddDays(-retentionDays) : (DateTime?)null;

        // Evaluate each release
        foreach (var release in releases)
        {
            // Mixed cache hits can include rows that already passed this policy.
            release.Score = 0;
            release.QualityScore = 0;
            release.CustomFormatScore = 0;
            release.SizeScore = 0;
            release.Approved = true;
            release.Rejections = new List<string>();
            release.MatchedFormats = new List<MatchedFormat>();
            release.Part = requestedPart;

            // Check indexer retention - reject releases older than configured days
            if (retentionCutoff.HasValue && release.PublishDate < retentionCutoff.Value)
            {
                // Rejected rows retain the quality supplied before evaluation.
                release.Quality = release.SourceQuality;
                var ageInDays = (DateTime.UtcNow - release.PublishDate).Days;
                release.Approved = false;
                release.Rejections.Add($"Release is {ageInDays} days old (retention: {retentionDays} days)");
                _logger.LogDebug("[Indexer Search] {Title} rejected: {AgeInDays} days old exceeds retention of {Retention} days",
                    release.Title, ageInDays, retentionDays);
                continue; // Skip further evaluation for rejected releases
            }

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
                config.DefaultSportsRuntimeMinutes,
                isPack,
                allowHighlights,
                leagueName: leagueName);

            // Update release with evaluation results
            release.Score = evaluation.TotalScore;
            release.QualityScore = evaluation.QualityScore;
            release.CustomFormatScore = evaluation.CustomFormatScore;
            release.SizeScore = evaluation.SizeScore;
            release.Approved = evaluation.Approved;
            release.Rejections = evaluation.Rejections;
            release.MatchedFormats = evaluation.MatchedFormats;
            release.Quality = evaluation.Quality;

            // Apply release profile filtering (Required/Ignored keywords, Preferred score)
            if (releaseProfiles.Any())
            {
                var profileEval = _releaseProfileService.EvaluateRelease(release, releaseProfiles, leagueTags);

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

    }

    /// <summary>
    /// Search a single indexer with health tracking.
    /// Rate limiting is handled at the HTTP layer via IndexerQueryQuotaHandler.
    /// </summary>
    public async Task<List<ReleaseSearchResult>> SearchIndexerAsync(Indexer indexer, string query, int maxResults = 10000, string? sportarrId = null, bool useCategoryFilter = true)
        => (await SearchIndexerDetailedAsync(indexer, query, maxResults, sportarrId, useCategoryFilter)).Releases;

    public Task<IndexerSearchOutcome> SearchIndexerDetailedAsync(Indexer indexer, string query, int maxResults = 10000, string? sportarrId = null, bool useCategoryFilter = true)
        => SearchIndexerCoreDetailedAsync(indexer, query, maxResults, sportarrId, useCategoryFilter, null);

    private async Task<IndexerSearchOutcome> SearchIndexerCoreDetailedAsync(Indexer indexer, string query, int maxResults, string? sportarrId, bool useCategoryFilter, IndexerSearchRequestBatch? requestBatch, RawIndexerRetrievalCache? retrievalCache = null)
    {
        IndexerSearchOutcome outcome;
        try
        {
            var (available, reason) = await _indexerStatus.IsIndexerAvailableAsync(indexer.Id);
            if (!available)
            {
                _logger.LogInformation("[Indexer Search] Skipping {Indexer}: {Reason}", indexer.Name, reason);
                return new(new(), SearchTermination.Unavailable, 0, Array.Empty<SearchPageObservation>());
            }
            if (indexer.Type == IndexerType.Torznab)
                outcome = await SearchTorznabAsync(indexer, query, maxResults, sportarrId, useCategoryFilter, requestBatch, retrievalCache);
            else if (indexer.Type == IndexerType.Newznab)
                outcome = await SearchNewznabAsync(indexer, query, maxResults, sportarrId, useCategoryFilter, requestBatch, retrievalCache);
            else
            {
                var legacy = indexer.Type switch
                {
                    IndexerType.BroadcasTheNet => await SearchBroadcasTheNetAsync(indexer, query, maxResults),
                    IndexerType.Rss or IndexerType.Torrent => await SearchPlainRssAsync(indexer, query, maxResults),
                    _ => new List<ReleaseSearchResult>()
                };
                outcome = new(legacy, SearchTermination.Exhausted, 0, Array.Empty<SearchPageObservation>());
            }
        }
        catch (Exception failure)
        {
            outcome = IndexerSearchOutcome.Failed(failure);
        }

        if (outcome.Failure is IndexerQueryAdmissionException admission)
            LogQueryAdmissionFailure(indexer, admission);
        else if (outcome.Failure is IndexerRateLimitException rateLimit)
            await _indexerStatus.RecordRateLimitedAsync(indexer.Id, rateLimit.RetryAfter);
        else if (outcome.Termination == SearchTermination.LocalCancelled)
            _logger.LogInformation("[Indexer Search] Search cancelled for {Indexer}", indexer.Name);
        else if (outcome.Failure != null)
            await RecordQueryOutcomeFailureAsync(indexer, outcome.Failure.Message);
        else if (outcome.SatisfiesRequest && !outcome.FromCache)
        {
            if (UsesQueryAdmission(indexer)) await _indexerStatus.RecordSuccessHealthAsync(indexer.Id);
            else await _indexerStatus.RecordSuccessAsync(indexer.Id);
        }

        var results = outcome.Releases;
        // Set protocol and indexer ID based on indexer type
        var protocol = indexer.Type switch
        {
            IndexerType.Torznab => "Torrent",
            IndexerType.BroadcasTheNet => "Torrent",
            IndexerType.Torrent => "Torrent",
            IndexerType.Rss => "Torrent", // RSS feeds are typically torrents
            IndexerType.Newznab => "Usenet",
            _ => "Torrent" // Default to torrent for unknown types
        };
        foreach (var result in results)
        {
            result.SourceQuality = result.Quality;
            result.Protocol = protocol;
            result.IndexerId = indexer.Id; // For release profile filtering
        }

        // Filter by minimum seeders (for torrents). Releases with UNKNOWN
        // seed counts pass: some indexers omit the seeders attribute
        // entirely, and rejecting null as "below minimum" silently threw
        // away every result they returned (null >= min is false).
        if ((indexer.Type == IndexerType.Torznab || indexer.Type == IndexerType.BroadcasTheNet) && indexer.MinimumSeeders > 0)
        {
            var beforeSeederFilter = results.Count;
            results = results.Where(r => !r.Seeders.HasValue || r.Seeders.Value >= indexer.MinimumSeeders).ToList();
            if (results.Count < beforeSeederFilter)
            {
                _logger.LogInformation("[Indexer Search] {Indexer}: {Dropped} of {Total} results below minimum seeders ({Min})",
                    indexer.Name, beforeSeederFilter - results.Count, beforeSeederFilter, indexer.MinimumSeeders);
            }
        }


        _logger.LogInformation("[Indexer Search] {Indexer} returned {Count} results with {Termination}",
            indexer.Name, results.Count, outcome.Termination);
        return outcome with { Releases = results };
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
                IndexerType.Rss or IndexerType.Torrent => await TestRssAsync(indexer),
                IndexerType.BroadcasTheNet => await TestBroadcasTheNetAsync(indexer),
                _ => false
            };
        }
        catch (IndexerRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Indexer Search] Error testing {Indexer}", indexer.Name);
            return false;
        }
    }

    /// <summary>
    /// Fetch RSS feeds from all RSS-enabled indexers (RSS sync).
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
        var requestGroups = indexers.GroupBy(indexer => GetRssRequestGroupKey(indexer, maxReleasesPerIndexer));

        // THROTTLING: Limit concurrent RSS fetches.
        using var indexerSemaphore = new SemaphoreSlim(MaxConcurrentIndexerQueries, MaxConcurrentIndexerQueries);

        var fetchTasks = requestGroups.Select(async group =>
        {
            await indexerSemaphore.WaitAsync();
            try
            {
                using var requestBatch = new IndexerSearchRequestBatch(
                    maxSharedBodyBytes: 2 * IndexerSearchRequestBatch.MaxSharedBodyBytes,
                    maxRetainedBytes: 2 * IndexerSearchRequestBatch.MaxSharedBodyBytes,
                    maxEntries: 1);
                var rowTasks = group.Select(indexer =>
                    FetchRssFeedFromIndexerAsync(indexer, maxReleasesPerIndexer, requestBatch));
                var rowResults = await Task.WhenAll(rowTasks);
                return rowResults.SelectMany(result => result).ToList();
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

    private static string GetRssRequestGroupKey(Indexer indexer, int maxResults)
    {
        if (indexer.Type is not IndexerType.Torznab and not IndexerType.Newznab)
            return indexer.Type + ":" + indexer.Id;

        var categories = indexer.Categories?.Any() == true
            ? indexer.Categories
            : NewznabCategories.DefaultSportCategories.ToList();
        var fields = new[]
        {
            indexer.Type.ToString(),
            indexer.Url.TrimEnd('/'),
            indexer.ApiPath?.Trim('/') ?? "",
            indexer.ApiKey ?? "",
            string.Join(",", categories),
            maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture),
            indexer.AdditionalParameters?.Trim() ?? "",
            (indexer.RequestDelayMs > 0 ? indexer.RequestDelayMs : 2000)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return string.Join('\u001f', fields);
    }

    /// <summary>
    /// Fetch RSS feed from a single indexer with health tracking
    /// </summary>
    private async Task<List<ReleaseSearchResult>> FetchRssFeedFromIndexerAsync(
        Indexer indexer,
        int maxResults,
        IndexerSearchRequestBatch requestBatch)
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
                    IndexerType.Torznab => await FetchTorznabRssAsync(indexer, maxResults, requestBatch),
                    IndexerType.Newznab => await FetchNewznabRssAsync(indexer, maxResults, requestBatch),
                    IndexerType.Rss or IndexerType.Torrent => await FetchPlainRssAsync(indexer, maxResults),
                    IndexerType.BroadcasTheNet => await FetchBroadcasTheNetRecentAsync(indexer, maxResults),
                    _ => new List<ReleaseSearchResult>()
                };

                // Record success
                if (UsesQueryAdmission(indexer)) await _indexerStatus.RecordSuccessHealthAsync(indexer.Id);
                else await _indexerStatus.RecordSuccessAsync(indexer.Id);
            }
            catch (IndexerQueryAdmissionException ex)
            {
                LogQueryAdmissionFailure(indexer, ex);
                return new List<ReleaseSearchResult>();
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
                await RecordQueryOutcomeFailureAsync(indexer, ex.Message);
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
                result.SourceQuality = result.Quality;
                result.Protocol = protocol;
                // Stamp which indexer this came from. The search path did this
                // and the feed path did not, so a release profile scoped to a
                // particular indexer matched nothing during RSS sync and a
                // release it should have rejected went through.
                result.IndexerId = indexer.Id;
            }

            // Filter by minimum seeders (for torrents). Unknown seed counts
            // pass - see the search-path filter above for why.
            if ((indexer.Type == IndexerType.Torznab || indexer.Type == IndexerType.BroadcasTheNet) && indexer.MinimumSeeders > 0)
            {
                results = results.Where(r => !r.Seeders.HasValue || r.Seeders.Value >= indexer.MinimumSeeders).ToList();
            }

            _logger.LogDebug("[RSS Feed] {Indexer} returned {Count} releases", indexer.Name, results.Count);

            return results;
        }
        catch (IndexerQueryAdmissionException ex)
        {
            LogQueryAdmissionFailure(indexer, ex);
            return new List<ReleaseSearchResult>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RSS Feed] Error fetching from {Indexer}", indexer.Name);
            await RecordQueryOutcomeFailureAsync(indexer, ex.Message);
            return new List<ReleaseSearchResult>();
        }
    }

    private async Task<List<ReleaseSearchResult>> FetchTorznabRssAsync(
        Indexer indexer,
        int maxResults,
        IndexerSearchRequestBatch requestBatch)
    {
        // Log categories being used for RSS (important for filtering out non-TV content)
        var categories = indexer.Categories?.Any() == true
            ? string.Join(",", indexer.Categories)
            : string.Join(",", NewznabCategories.DefaultSportCategories);
        _logger.LogDebug("[RSS Feed] {Indexer}: Fetching with categories [{Categories}]", indexer.Name, categories);

        var httpClient = _httpClientFactory.CreateClient("IndexerClient");
        var torznabLogger = _loggerFactory.CreateLogger<TorznabClient>();
        var client = new TorznabClient(httpClient, torznabLogger, _qualityDetection, new IndexerQueryContext(indexer.Id));
        client.SearchRequestSender = (request, completion) =>
            requestBatch.SendAsync(httpClient, nameof(IndexerType.Torznab), indexer.RequestDelayMs, request, completion);

        return await client.FetchRssFeedAsync(indexer, maxResults);
    }

    private static bool UsesQueryAdmission(Indexer indexer) =>
        indexer.Type is IndexerType.Torznab or IndexerType.Newznab;

    private Task RecordQueryOutcomeFailureAsync(Indexer indexer, string reason) =>
        UsesQueryAdmission(indexer)
            ? _indexerStatus.RecordFailureHealthAsync(indexer.Id, reason)
            : _indexerStatus.RecordFailureAsync(indexer.Id, reason);

    private void LogQueryAdmissionFailure(Indexer indexer, IndexerQueryAdmissionException error)
    {
        if (error.Kind == QueryAdmissionFailure.Persistence)
            _logger.LogError(error, "[Indexer Search] Local query admission failed for {Indexer}", indexer.Name);
        else
            _logger.LogDebug("[Indexer Search] Query admission {Kind} for {Indexer}: {Reason}", error.Kind, indexer.Name, error.Message);
    }

    private BroadcasTheNetClient CreateBtnClient()
    {
        var httpClient = _httpClientFactory.CreateClient("BtnClient");
        var btnLogger = _loggerFactory.CreateLogger<BroadcasTheNetClient>();
        return new BroadcasTheNetClient(httpClient, _rateLimitService, btnLogger, _qualityDetection);
    }

    private async Task<List<ReleaseSearchResult>> FetchBroadcasTheNetRecentAsync(Indexer indexer, int maxResults)
    {
        var client = CreateBtnClient();
        return await client.FetchRecentAsync(indexer, maxResults);
    }

    private async Task<bool> TestBroadcasTheNetAsync(Indexer indexer)
    {
        var client = CreateBtnClient();
        return await client.TestConnectionAsync(indexer);
    }

    private async Task<List<ReleaseSearchResult>> FetchNewznabRssAsync(
        Indexer indexer,
        int maxResults,
        IndexerSearchRequestBatch requestBatch)
    {
        // Log categories being used for RSS (important for filtering out non-TV content)
        var categories = indexer.Categories?.Any() == true
            ? string.Join(",", indexer.Categories)
            : string.Join(",", NewznabCategories.DefaultSportCategories);
        _logger.LogDebug("[RSS Feed] {Indexer}: Fetching with categories [{Categories}]", indexer.Name, categories);

        var httpClient = _httpClientFactory.CreateClient("IndexerClient");
        var newznabLogger = _loggerFactory.CreateLogger<NewznabClient>();
        var client = new NewznabClient(httpClient, newznabLogger, _qualityDetection, new IndexerQueryContext(indexer.Id));
        client.SearchRequestSender = (request, completion) =>
            requestBatch.SendAsync(httpClient, nameof(IndexerType.Newznab), indexer.RequestDelayMs, request, completion);

        return await client.FetchRssFeedAsync(indexer, maxResults);
    }

    // Private helper methods

    private async Task<IndexerSearchOutcome> SearchTorznabAsync(Indexer indexer, string query, int maxResults, string? sportarrId = null, bool useCategoryFilter = true, IndexerSearchRequestBatch? requestBatch = null, RawIndexerRetrievalCache? retrievalCache = null)
    {
        var httpClient = _httpClientFactory.CreateClient("IndexerClient");
        var torznabLogger = _loggerFactory.CreateLogger<TorznabClient>();
        var client = new TorznabClient(httpClient, torznabLogger, _qualityDetection, new IndexerQueryContext(indexer.Id));
        client.RetrievalCache = retrievalCache;

        if (requestBatch != null)
            client.SearchRequestSender = (request, completion) => requestBatch.SendAsync(httpClient, nameof(IndexerType.Torznab), indexer.RequestDelayMs, request, completion);

        return await client.SearchDetailedAsync(indexer, query, maxResults, sportarrId, useCategoryFilter);
    }

    private async Task<IndexerSearchOutcome> SearchNewznabAsync(Indexer indexer, string query, int maxResults, string? sportarrId = null, bool useCategoryFilter = true, IndexerSearchRequestBatch? requestBatch = null, RawIndexerRetrievalCache? retrievalCache = null)
    {
        var httpClient = _httpClientFactory.CreateClient("IndexerClient");
        var newznabLogger = _loggerFactory.CreateLogger<NewznabClient>();
        var client = new NewznabClient(httpClient, newznabLogger, _qualityDetection, new IndexerQueryContext(indexer.Id));
        client.RetrievalCache = retrievalCache;

        if (requestBatch != null)
            client.SearchRequestSender = (request, completion) => requestBatch.SendAsync(httpClient, nameof(IndexerType.Newznab), indexer.RequestDelayMs, request, completion);

        return await client.SearchDetailedAsync(indexer, query, maxResults, sportarrId, useCategoryFilter);
    }

    private async Task<List<ReleaseSearchResult>> SearchBroadcasTheNetAsync(Indexer indexer, string query, int maxResults)
    {
        var client = CreateBtnClient();
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

    private async Task<List<ReleaseSearchResult>> FetchPlainRssAsync(Indexer indexer, int maxResults)
    {
        var client = BuildRssClient();
        return await client.FetchRssFeedAsync(indexer, maxResults);
    }

    /// <summary>
    /// "Search" a plain-RSS feed: fetch the current feed and keep items whose
    /// title contains most of the query's terms. Separator-insensitive
    /// substring matching per token ("Formula1.2026.British.GP" satisfies
    /// "formula", "1", "british", "2026"), requiring at least half the
    /// tokens so date fragments and stylistic differences don't zero out
    /// legitimate hits, while unrelated feed items still drop out instead
    /// of flooding the search results.
    /// </summary>
    private async Task<List<ReleaseSearchResult>> SearchPlainRssAsync(Indexer indexer, string query, int maxResults)
    {
        var client = BuildRssClient();
        var feed = await client.FetchRssFeedAsync(indexer, maxResults);

        var tokens = System.Text.RegularExpressions.Regex
            .Matches(query.ToLowerInvariant(), @"[a-z0-9]+")
            .Select(m => m.Value)
            .Where(t => t.Length >= 2 || t == "1")
            .Distinct()
            .ToList();

        if (tokens.Count == 0)
        {
            return feed;
        }

        var required = Math.Max(1, (tokens.Count + 1) / 2);
        var matched = feed.Where(r =>
        {
            var normalizedTitle = System.Text.RegularExpressions.Regex
                .Replace(r.Title.ToLowerInvariant(), @"[^a-z0-9]+", " ");
            var hits = tokens.Count(t => normalizedTitle.Contains(t));
            return hits >= required;
        }).ToList();

        _logger.LogDebug("[Indexer Search] RSS feed '{Indexer}': {Matched}/{Total} items matched query '{Query}'",
            indexer.Name, matched.Count, feed.Count, query);

        return matched;
    }

    private async Task<bool> TestRssAsync(Indexer indexer)
    {
        var client = BuildRssClient();
        return await client.TestConnectionAsync(indexer);
    }

    /// <summary>
    /// Probe the RSS feed for the given indexer, write the auto-detected
    /// parser config back onto the entity, and return a friendly summary.
    /// Caller (the Test endpoint) is responsible for db.SaveChangesAsync
    /// when Success is true. When Success is false the indexer entity is
    /// left untouched so the bad probe doesn't half-save bogus settings.
    /// </summary>
    public async Task<RssDetectionResult> DetectRssSettingsAsync(Indexer indexer)
    {
        if (indexer.Type != IndexerType.Rss)
        {
            return new RssDetectionResult(false, "Auto-detect only runs for plain-RSS indexers.");
        }
        var client = BuildRssClient();
        return await client.DetectAndSaveSettingsAsync(indexer);
    }

    private RssClient BuildRssClient()
    {
        var httpClient = _httpClientFactory.CreateClient("IndexerClient");
        var rssLogger = _loggerFactory.CreateLogger<RssClient>();
        return new RssClient(httpClient, rssLogger);
    }
}

/// <summary>
/// Represents the current active search status (drives the bottom-left indicator).
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
