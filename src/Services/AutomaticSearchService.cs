using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Automatic search and download service for monitored events
/// Implements Sonarr/Radarr-style automation: search → select → download
/// Includes concurrent event search limiting (max 3) to prevent indexer rate limiting
/// </summary>
public class AutomaticSearchService
{
    private readonly SportarrDbContext _db;
    private readonly IndexerSearchService _indexerSearchService;
    private readonly DownloadClientService _downloadClientService;
    private readonly EventQueryService _eventQueryService;
    private readonly DelayProfileService _delayProfileService;
    private readonly ReleaseMatchingService _releaseMatchingService;
    private readonly ConfigService _configService;
    private readonly ReleaseCacheService _releaseCacheService;
    private readonly ReleaseMatchScorer _releaseMatchScorer;
    private readonly SearchResultCache _searchResultCache;
    private readonly ReleaseEvaluator _releaseEvaluator;
    private readonly ReleaseProfileService _releaseProfileService;
    private readonly ILogger<AutomaticSearchService> _logger;

    // Concurrent event search limiting (Sonarr-style)
    // Max 3 concurrent event searches to prevent overwhelming indexers
    private static readonly SemaphoreSlim _eventSearchSemaphore = new(3, 3);

    // Delay between starting new event searches when processing many events
    private const int EventSearchDelayMs = 3000;

    public AutomaticSearchService(
        SportarrDbContext db,
        IndexerSearchService indexerSearchService,
        DownloadClientService downloadClientService,
        EventQueryService eventQueryService,
        DelayProfileService delayProfileService,
        ReleaseMatchingService releaseMatchingService,
        ConfigService configService,
        ReleaseCacheService releaseCacheService,
        ReleaseMatchScorer releaseMatchScorer,
        SearchResultCache searchResultCache,
        ReleaseEvaluator releaseEvaluator,
        ReleaseProfileService releaseProfileService,
        ILogger<AutomaticSearchService> logger)
    {
        _db = db;
        _indexerSearchService = indexerSearchService;
        _downloadClientService = downloadClientService;
        _eventQueryService = eventQueryService;
        _delayProfileService = delayProfileService;
        _releaseMatchingService = releaseMatchingService;
        _configService = configService;
        _releaseCacheService = releaseCacheService;
        _releaseMatchScorer = releaseMatchScorer;
        _searchResultCache = searchResultCache;
        _releaseEvaluator = releaseEvaluator;
        _releaseProfileService = releaseProfileService;
        _logger = logger;
    }

    /// <summary>
    /// Automatically search and download for a specific event (universal for all sports)
    /// </summary>
    /// <param name="eventId">The event ID to search for</param>
    /// <param name="qualityProfileId">Optional quality profile ID</param>
    /// <param name="part">Optional multi-part episode segment (e.g., "Early Prelims", "Prelims", "Main Card")</param>
    /// <param name="isManualSearch">If true, bypasses monitored check and retry backoff (user-initiated search)</param>
    public async Task<AutomaticSearchResult> SearchAndDownloadEventAsync(int eventId, int? qualityProfileId = null, string? part = null, bool isManualSearch = false)
    {
        var result = new AutomaticSearchResult { EventId = eventId };
        var searchType = isManualSearch ? "Manual Search" : "Automatic Search";

        try
        {
            // Load config for multi-part episode setting and queue threshold
            var config = await _configService.GetConfigAsync();

            // QUEUE THRESHOLD CHECK (Huntarr-style)
            // For automatic searches, check if download queue exceeds threshold
            // This prevents overwhelming download clients and indexers
            if (!isManualSearch)
            {
                if (config.MaxDownloadQueueSize > 0)
                {
                    var activeDownloads = await _db.DownloadQueue
                        .CountAsync(d => d.Status == DownloadStatus.Queued ||
                                        d.Status == DownloadStatus.Downloading);

                    if (activeDownloads >= config.MaxDownloadQueueSize)
                    {
                        result.Success = false;
                        result.Message = $"Queue threshold reached ({activeDownloads}/{config.MaxDownloadQueueSize}). Pausing automatic searches.";
                        _logger.LogInformation("[{SearchType}] Queue threshold reached ({Active}/{Max}). Skipping search for event {EventId}",
                            searchType, activeDownloads, config.MaxDownloadQueueSize, eventId);
                        return result;
                    }
                }
            }

            // Get event
            var evt = await _db.Events.FindAsync(eventId);
            if (evt == null)
            {
                result.Success = false;
                result.Message = "Event not found";
                return result;
            }

            // Load league to check its monitored status
            await _db.Entry(evt).Reference(e => e.League).LoadAsync();

            // MONITORED CHECK: Only applies to automatic background searches
            // Manual searches (user clicking search button) should always work
            // Individual event monitoring takes precedence over league monitoring
            if (!isManualSearch)
            {
                // Check if event is unmonitored
                // NOTE: We check event first because users can manually monitor individual events
                // even when the league itself is unmonitored (no teams selected)
                if (!evt.Monitored)
                {
                    result.Success = false;
                    result.Message = "Event is unmonitored (skipped by automatic search)";
                    _logger.LogInformation("[{SearchType}] Skipping unmonitored event: {Title}", searchType, evt.Title);
                    return result;
                }

                // If event IS monitored, we proceed regardless of league status
                // This allows users to manually monitor specific events even when no teams are selected
                if (evt.League != null && !evt.League.Monitored)
                {
                    _logger.LogInformation("[{SearchType}] Event is individually monitored (league unmonitored): {Title}",
                        searchType, evt.Title);
                }
            }

            if (isManualSearch && !evt.Monitored)
            {
                _logger.LogInformation("[{SearchType}] Processing unmonitored event (manual search): {Title}", searchType, evt.Title);
            }

            // Check for recent failed downloads - prevent immediate re-attempts
            // This implements Sonarr/Radarr-style retry backoff: don't hammer failed downloads
            // NOTE: Manual searches bypass this check - user explicitly wants to retry
            DownloadQueueItem? recentFailedDownload = null;
            if (!isManualSearch)
            {
                recentFailedDownload = await _db.DownloadQueue
                    .Where(d => d.EventId == eventId && d.Status == DownloadStatus.Failed)
                    .OrderByDescending(d => d.LastUpdate)
                    .FirstOrDefaultAsync();

                if (recentFailedDownload != null)
                {
                    // Calculate backoff time based on retry count (exponential backoff)
                    // 1st retry: 30min, 2nd: 1hr, 3rd: 2hr, 4th: 4hr, 5th+: 8hr
                    var retryDelays = new[] { 30, 60, 120, 240, 480 }; // minutes
                    var currentRetryCount = recentFailedDownload.RetryCount ?? 0;
                    var delayMinutes = currentRetryCount < retryDelays.Length ? retryDelays[currentRetryCount] : retryDelays[^1];
                    var nextRetryTime = (recentFailedDownload.LastUpdate ?? DateTime.UtcNow).AddMinutes(delayMinutes);

                    if (DateTime.UtcNow < nextRetryTime)
                    {
                        var waitTime = nextRetryTime - DateTime.UtcNow;
                        result.Success = false;
                        result.Message = $"Recent failed download - retry #{currentRetryCount + 1} available in {Math.Ceiling(waitTime.TotalMinutes)} minutes";
                        _logger.LogInformation("[{SearchType}] Skipping {Title} - recent failed download (retry #{Retry} in {Minutes} minutes)",
                            searchType, evt.Title, currentRetryCount + 1, Math.Ceiling(waitTime.TotalMinutes));
                        return result;
                    }

                    _logger.LogInformation("[{SearchType}] Retry #{Retry} for {Title} after {Delay} minute backoff",
                        searchType, currentRetryCount + 1, evt.Title, delayMinutes);
                }
            }
            else
            {
                // For manual searches, still get the failed download for retry count tracking
                recentFailedDownload = await _db.DownloadQueue
                    .Where(d => d.EventId == eventId && d.Status == DownloadStatus.Failed)
                    .OrderByDescending(d => d.LastUpdate)
                    .FirstOrDefaultAsync();
            }

            var searchTarget = part != null ? $"{evt.Title} ({part})" : evt.Title;
            _logger.LogInformation("[Automatic Search] Starting search for event: {Title} ({Sport})",
                searchTarget, evt.Sport);

            // Load related entities for query building (universal - league/teams used for all sports)
            await _db.Entry(evt).Reference(e => e.HomeTeam).LoadAsync();
            await _db.Entry(evt).Reference(e => e.AwayTeam).LoadAsync();
            await _db.Entry(evt).Reference(e => e.League).LoadAsync();

            // Build queries WITH the part included for accurate results
            // Indexers return different results: "UFC 299" vs "UFC 299 Prelims"
            var queries = _eventQueryService.BuildEventQueries(evt, part);

            _logger.LogInformation("[Automatic Search] Built {Count} prioritized queries for {Sport}{PartInfo}",
                queries.Count, evt.Sport, part != null ? $" (Part: {part})" : "");

            // Check cache for primary query first (avoids redundant API calls)
            // Multiple events often share the same primary query (e.g., "Formula1.2025" for all F1 races)
            var allReleases = new List<ReleaseSearchResult>();
            var primaryQuery = queries.FirstOrDefault();
            bool usedCache = false;

            if (!string.IsNullOrEmpty(primaryQuery))
            {
                var cachedResults = _searchResultCache.TryGetCached(primaryQuery, config.SearchCacheDuration);
                if (cachedResults != null)
                {
                    allReleases = _searchResultCache.ToSearchResults(cachedResults);
                    usedCache = true;
                    _logger.LogInformation("[Automatic Search] Using cached results for query '{Query}' ({Count} releases, cache valid for {Duration}s)",
                        primaryQuery, allReleases.Count, config.SearchCacheDuration);

                    // Re-evaluate cached releases against quality profile
                    // Cached releases have Approved=true and empty Rejections by default
                    // We must run ReleaseEvaluator to apply CF minimum score and other profile requirements
                    await ReEvaluateCachedReleasesAsync(allReleases, qualityProfileId, part, evt.Sport, config.EnableMultiPartEpisodes, evt.Title);
                }
            }

            // If no cache hit, perform live search
            if (!usedCache)
            {
                // Intelligent fallback search - try primary query first, exit early if no results
                int queriesAttempted = 0;
                int consecutiveEmptyResults = 0;
                const int MinimumResults = 3;
                const int MaxConsecutiveEmpty = 2;

                foreach (var query in queries)
                {
                    queriesAttempted++;
                    _logger.LogInformation("[Automatic Search] Trying query {Attempt}/{Total}: '{Query}'",
                        queriesAttempted, queries.Count, query);

                    // Pass part to indexer for proper filtering
                    var releases = await _indexerSearchService.SearchAllIndexersAsync(query, maxResultsPerIndexer: 100, qualityProfileId, part, evt.Sport, config.EnableMultiPartEpisodes, evt.Title);

                    if (releases.Count == 0)
                    {
                        consecutiveEmptyResults++;
                        _logger.LogInformation("[Automatic Search] No results for query '{Query}' ({Empty}/{MaxEmpty} consecutive empty)",
                            query, consecutiveEmptyResults, MaxConsecutiveEmpty);

                        if (consecutiveEmptyResults >= MaxConsecutiveEmpty)
                        {
                            _logger.LogInformation("[Automatic Search] Stopping search - {Empty} consecutive empty results (event likely not released yet)",
                                consecutiveEmptyResults);
                            break;
                        }
                    }
                    else
                    {
                        consecutiveEmptyResults = 0;
                        allReleases.AddRange(releases);

                        if (allReleases.Count >= MinimumResults)
                        {
                            _logger.LogInformation("[Automatic Search] Found {Count} results - skipping remaining {Remaining} fallback queries",
                                allReleases.Count, queries.Count - queriesAttempted);
                            break;
                        }

                        _logger.LogInformation("[Automatic Search] Found {Count} results so far (need {Min} minimum)",
                            allReleases.Count, MinimumResults);
                    }
                }

                // Store results in cache for subsequent searches using the same query
                // This prevents redundant indexer queries when searching multiple events (e.g., F1 races)
                if (allReleases.Any() && !string.IsNullOrEmpty(primaryQuery))
                {
                    _searchResultCache.Store(primaryQuery, allReleases);
                    _logger.LogDebug("[Automatic Search] Cached {Count} results for query '{Query}'",
                        allReleases.Count, primaryQuery);
                }
            }

            if (!allReleases.Any())
            {
                result.Success = false;
                result.Message = "No releases found";
                _logger.LogWarning("[Automatic Search] No releases found for: {Title}", evt.Title);
                return result;
            }

            result.ReleasesFound = allReleases.Count;
            _logger.LogInformation("[Automatic Search] Found {Count} total releases", allReleases.Count);

            // MATCH SCORING: Calculate how well each release matches the event
            // This is critical for filtering out wrong releases (different games, TV shows, etc.)
            // Cached releases already have MatchScore set, but live indexer results need calculation
            var scoredCount = 0;
            foreach (var release in allReleases)
            {
                // Only calculate if not already scored (cached releases have scores)
                if (release.MatchScore == 0)
                {
                    release.MatchScore = _releaseMatchScorer.CalculateMatchScore(release.Title, evt);
                    scoredCount++;
                }

                // Mark releases that don't meet minimum match score as rejected
                // This ensures they're filtered out and the rejection reason is visible
                if (release.MatchScore < ReleaseMatchScorer.MinimumMatchScore)
                {
                    release.Approved = false;
                    if (!release.Rejections.Contains($"Release doesn't match event (score: {release.MatchScore})"))
                    {
                        release.Rejections.Add($"Release doesn't match event (score: {release.MatchScore})");
                    }
                }
            }

            if (scoredCount > 0)
            {
                _logger.LogInformation("[{SearchType}] Calculated match scores for {Count} live indexer results",
                    searchType, scoredCount);

                // Log match score distribution for debugging
                var matchingCount = allReleases.Count(r => r.MatchScore >= ReleaseMatchScorer.MinimumMatchScore);
                var nonMatchingCount = allReleases.Count - matchingCount;
                if (nonMatchingCount > 0)
                {
                    _logger.LogDebug("[{SearchType}] Match score distribution: {Matching} matching (>={MinScore}), {NonMatching} non-matching",
                        searchType, matchingCount, ReleaseMatchScorer.MinimumMatchScore, nonMatchingCount);
                }
            }

            // BLOCKLIST CHECK: Reject releases that are in the blocklist
            // This prevents auto-grabbing releases that were previously removed/failed
            // Supports both torrent (by hash) and Usenet (by title+indexer)
            var blocklistItems = await _db.Blocklist
                .Select(b => new { b.TorrentInfoHash, b.Title, b.Indexer, b.Protocol })
                .ToListAsync();

            // Build hash set for torrent blocklist (fast lookup)
            var blocklistHashSet = new HashSet<string>(
                blocklistItems.Where(b => !string.IsNullOrEmpty(b.TorrentInfoHash)).Select(b => b.TorrentInfoHash!),
                StringComparer.OrdinalIgnoreCase);

            // Build set for Usenet blocklist (title+indexer combinations)
            var usenetBlocklist = blocklistItems
                .Where(b => b.Protocol == "Usenet" || string.IsNullOrEmpty(b.TorrentInfoHash))
                .Select(b => $"{b.Title}|{b.Indexer}".ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var blocklistedCount = 0;
            foreach (var release in allReleases)
            {
                bool isBlocked = false;

                // Check torrent hash blocklist
                if (!string.IsNullOrEmpty(release.TorrentInfoHash) && blocklistHashSet.Contains(release.TorrentInfoHash))
                {
                    isBlocked = true;
                }
                // Check Usenet blocklist (by title+indexer)
                else if (release.Protocol == "Usenet" || string.IsNullOrEmpty(release.TorrentInfoHash))
                {
                    var usenetKey = $"{release.Title}|{release.Indexer}".ToLowerInvariant();
                    if (usenetBlocklist.Contains(usenetKey))
                    {
                        isBlocked = true;
                    }
                }

                if (isBlocked)
                {
                    release.IsBlocklisted = true;
                    release.Approved = false;
                    release.Rejections.Add("Release is blocklisted");
                    blocklistedCount++;
                }
            }

            if (blocklistedCount > 0)
            {
                _logger.LogInformation("[{SearchType}] {Count} releases rejected (blocklisted)", searchType, blocklistedCount);
            }

            // SONARR-STYLE RELEASE FILTERING: Use releases that passed ReleaseEvaluator validation
            // ReleaseEvaluator already handles:
            // - Part validation (Main Card vs Prelims)
            // - Size validation (min/max per quality)
            // - Quality cutoff checking
            // - Custom format minimum score
            // Releases with Approved=true and no rejections are valid candidates
            var approvedReleases = allReleases
                .Where(r => r.Approved && !r.Rejections.Any())
                .ToList();

            _logger.LogInformation("[Automatic Search] {ApprovedCount}/{TotalCount} releases approved by quality/part validation",
                approvedReleases.Count, allReleases.Count);

            // MATCH SCORE FILTERING: For automatic searches, require minimum match score
            // This prevents auto-grabbing releases that only loosely match the event
            // Manual searches show all results but automatic grabbing needs high confidence
            const int AutoGrabMinMatchScore = 50;
            if (!isManualSearch)
            {
                var highConfidenceReleases = approvedReleases
                    .Where(r => r.MatchScore >= AutoGrabMinMatchScore)
                    .ToList();

                if (highConfidenceReleases.Count < approvedReleases.Count)
                {
                    _logger.LogInformation("[Automatic Search] Match score filter: {HighCount}/{TotalCount} releases have score >= {MinScore}",
                        highConfidenceReleases.Count, approvedReleases.Count, AutoGrabMinMatchScore);

                    // Log low-scoring releases for debugging
                    var lowScoreReleases = approvedReleases
                        .Where(r => r.MatchScore < AutoGrabMinMatchScore)
                        .OrderByDescending(r => r.MatchScore)
                        .Take(3);
                    foreach (var low in lowScoreReleases)
                    {
                        _logger.LogDebug("[Automatic Search] Low match score release: '{Title}' (Score: {Score})",
                            low.Title, low.MatchScore);
                    }
                }

                if (!highConfidenceReleases.Any() && approvedReleases.Any())
                {
                    // Have approved releases but none meet match score threshold
                    result.Success = false;
                    result.Message = $"Found {approvedReleases.Count} releases but none have sufficient match confidence (need score >= {AutoGrabMinMatchScore})";
                    _logger.LogWarning("[Automatic Search] All {Count} approved releases have low match scores for: {Title}. " +
                        "Top score: {TopScore}. Consider using manual search.",
                        approvedReleases.Count, evt.Title,
                        approvedReleases.Max(r => r.MatchScore));
                    return result;
                }

                approvedReleases = highConfidenceReleases;
            }

            if (!approvedReleases.Any())
            {
                // Log rejection reasons for debugging
                var rejectionSummary = allReleases
                    .Where(r => r.Rejections.Any())
                    .GroupBy(r => r.Rejections.FirstOrDefault() ?? "Unknown")
                    .Select(g => $"{g.Key}: {g.Count()}")
                    .Take(5);

                result.Success = false;
                result.Message = $"No approved releases found. {allReleases.Count} releases were rejected.";
                _logger.LogWarning("[Automatic Search] All {Count} releases rejected for: {Title}. Top reasons: {Reasons}",
                    allReleases.Count, evt.Title, string.Join(", ", rejectionSummary));
                return result;
            }

            // DATE/EVENT VALIDATION: Apply ReleaseMatchingService validation to filter out wrong dates
            // This catches releases like NBA.2024.03.12... when searching for a June 2025 event
            // The validation uses SportsFileNameParser to extract dates and hard-rejects mismatches >30 days
            var validatedReleases = new List<ReleaseSearchResult>();
            var dateRejectionCount = 0;

            foreach (var release in approvedReleases)
            {
                var matchResult = _releaseMatchingService.ValidateRelease(release, evt, part, config.EnableMultiPartEpisodes);

                if (matchResult.IsHardRejection)
                {
                    // Hard rejection (date mismatch, year mismatch, etc.)
                    dateRejectionCount++;
                    _logger.LogDebug("[Automatic Search] Release rejected by validation: {Title} - {Reason}",
                        release.Title, string.Join(", ", matchResult.Rejections));

                    // Add rejection reason to the release so it shows in UI
                    release.Approved = false;
                    release.Rejections.AddRange(matchResult.Rejections);
                }
                else
                {
                    validatedReleases.Add(release);
                }
            }

            if (dateRejectionCount > 0)
            {
                _logger.LogInformation("[Automatic Search] {RejectedCount} releases rejected by date/event validation",
                    dateRejectionCount);
            }

            if (!validatedReleases.Any())
            {
                // Log rejection reasons for debugging
                var rejectionSummary = approvedReleases
                    .Where(r => r.Rejections.Any())
                    .SelectMany(r => r.Rejections)
                    .GroupBy(r => r)
                    .Select(g => $"{g.Key}: {g.Count()}")
                    .Take(5);

                result.Success = false;
                result.Message = $"No valid releases found. {approvedReleases.Count} releases were rejected by date/event validation.";
                _logger.LogWarning("[Automatic Search] All releases rejected by date/event validation for: {Title}. Reasons: {Reasons}",
                    evt.Title, string.Join(", ", rejectionSummary));
                return result;
            }

            _logger.LogInformation("[Automatic Search] {ValidCount}/{ApprovedCount} releases passed date/event validation",
                validatedReleases.Count, approvedReleases.Count);

            // Use validated releases for further processing
            var matchedReleases = validatedReleases;

            // MULTI-PART CONSISTENCY CHECK: For automatic searches, ensure new releases match existing parts
            // This prevents downloading mismatched quality/codec/source for multi-part episodes
            // Plex requires all parts to have matching quality and codec for proper playback
            if (!isManualSearch && !string.IsNullOrEmpty(part))
            {
                var existingPartFiles = await _db.EventFiles
                    .Where(f => f.EventId == eventId && f.Exists && f.PartName != null && f.PartName != part)
                    .ToListAsync();

                if (existingPartFiles.Any())
                {
                    var referenceFile = existingPartFiles.First();

                    // Extract resolution from EventFile.Quality (format: "1080p WEB-DL H.264")
                    var referenceResolution = ExtractResolution(referenceFile.Quality);
                    // Build quality string for group matching (e.g., "WEBDL-1080p" from source "WEB-DL" and resolution "1080p")
                    var referenceQualityForGroup = BuildQualityString(referenceFile.Source, referenceResolution);
                    var referenceQualityGroup = FindQualityGroup(referenceQualityForGroup);
                    var referenceCodec = referenceFile.Codec;

                    _logger.LogInformation("[Automatic Search] Multi-part consistency check: Found existing parts with Resolution={Resolution}, Source={Source}, QualityGroup={Group}, Codec={Codec}",
                        referenceResolution, referenceFile.Source, referenceQualityGroup ?? "none", referenceCodec ?? "none");

                    var consistentReleases = matchedReleases.Where(r =>
                    {
                        // Extract resolution from release Quality (format: "WEBDL-1080p")
                        var releaseResolution = ExtractResolution(r.Quality);

                        // Check resolution match - REQUIRED for Plex compatibility
                        bool resolutionMatch = string.IsNullOrEmpty(referenceResolution) ||
                            string.Equals(releaseResolution, referenceResolution, StringComparison.OrdinalIgnoreCase);

                        // Check quality group match (WEBDL and WEBRip are in same "WEB" group)
                        // This allows downloading WEBRip when existing file is WEBDL, as they're equivalent quality
                        var releaseQualityGroup = FindQualityGroup(r.Quality);
                        bool qualityGroupMatch = string.IsNullOrEmpty(referenceQualityGroup) ||
                            string.IsNullOrEmpty(releaseQualityGroup) ||
                            string.Equals(releaseQualityGroup, referenceQualityGroup, StringComparison.OrdinalIgnoreCase);

                        // Codec match - REQUIRED for Plex compatibility when reference has a codec
                        // Mismatched codecs (e.g., H.264 vs H.265) cause playback issues in Plex
                        bool codecMatch = string.IsNullOrEmpty(referenceCodec) ||
                            string.IsNullOrEmpty(r.Codec) ||
                            string.Equals(r.Codec, referenceCodec, StringComparison.OrdinalIgnoreCase);

                        if (!resolutionMatch)
                        {
                            _logger.LogDebug("[Automatic Search] Rejecting release {Title}: Resolution mismatch (need {Ref}, got {Release})",
                                r.Title, referenceResolution, releaseResolution);
                        }
                        else if (!qualityGroupMatch)
                        {
                            _logger.LogDebug("[Automatic Search] Rejecting release {Title}: Quality group mismatch (need {Ref}, got {Release})",
                                r.Title, referenceQualityGroup, releaseQualityGroup);
                        }
                        else if (!codecMatch)
                        {
                            _logger.LogDebug("[Automatic Search] Rejecting release {Title}: Codec mismatch (need {Ref}, got {Release})",
                                r.Title, referenceCodec, r.Codec);
                        }

                        return resolutionMatch && qualityGroupMatch && codecMatch;
                    }).ToList();

                    if (consistentReleases.Any())
                    {
                        _logger.LogInformation("[Automatic Search] Found {Count} releases matching existing part specs (filtered from {Total})",
                            consistentReleases.Count, matchedReleases.Count);
                        matchedReleases = consistentReleases;
                    }
                    else
                    {
                        _logger.LogWarning("[Automatic Search] No releases match existing part specs (Resolution={Resolution}, QualityGroup={QualityGroup}, Codec={Codec}). Skipping to avoid Plex playback issues.",
                            referenceResolution, referenceQualityGroup, referenceCodec);
                        // Return failure - don't download mismatched quality/codec as it breaks Plex
                        result.Success = false;
                        result.Message = $"No releases found matching existing parts (need {referenceResolution} {referenceQualityGroup} {referenceCodec})";
                        return result;
                    }
                }
            }

            // Get quality profile - use provided ID, then event's profile, then league's, then default
            // Items and FormatItems are stored as JSON columns, so they're automatically loaded
            QualityProfile? qualityProfile = null;

            // First: Use explicitly provided profile ID
            if (qualityProfileId.HasValue)
            {
                qualityProfile = await _db.QualityProfiles
                    .FirstOrDefaultAsync(p => p.Id == qualityProfileId.Value);
            }

            // Second: Use event's assigned quality profile
            if (qualityProfile == null && evt.QualityProfileId.HasValue)
            {
                qualityProfile = await _db.QualityProfiles
                    .FirstOrDefaultAsync(p => p.Id == evt.QualityProfileId.Value);
            }

            // Third: Use league's quality profile
            if (qualityProfile == null && evt.League?.QualityProfileId != null)
            {
                qualityProfile = await _db.QualityProfiles
                    .FirstOrDefaultAsync(p => p.Id == evt.League.QualityProfileId.Value);
            }

            // Final fallback: Default profile (first by ID)
            if (qualityProfile == null)
            {
                qualityProfile = await _db.QualityProfiles
                    .OrderBy(q => q.Id)
                    .FirstOrDefaultAsync();
            }

            _logger.LogInformation("[{SearchType}] Using quality profile '{ProfileName}' (ID: {ProfileId}) for event '{EventTitle}'",
                searchType, qualityProfile?.Name ?? "None", qualityProfile?.Id, evt.Title);

            if (qualityProfile == null)
            {
                result.Success = false;
                result.Message = "No quality profile configured";
                return result;
            }

            // Get delay profile for this event
            var delayProfile = await _delayProfileService.GetDelayProfileForEventAsync(eventId);
            if (delayProfile == null)
            {
                _logger.LogWarning("[Automatic Search] No delay profile found, using defaults");
                delayProfile = new DelayProfile();
            }

            // Select best release using delay profile and protocol priority (from validated releases only)
            var bestRelease = _delayProfileService.SelectBestReleaseWithDelayProfile(
                matchedReleases, delayProfile, qualityProfile);

            if (bestRelease == null)
            {
                result.Success = false;
                result.Message = "No releases available (may be delayed or filtered)";
                _logger.LogWarning("[Automatic Search] No releases available for: {Title}", evt.Title);
                return result;
            }

            result.SelectedRelease = bestRelease.Title;
            result.SelectedIndexer = bestRelease.Indexer;
            result.Quality = bestRelease.Quality;
            _logger.LogInformation("[Automatic Search] Selected: {Release} from {Indexer} (Score: {Score})",
                bestRelease.Title, bestRelease.Indexer, bestRelease.Score);

            // UPGRADE CHECK: Part-aware file comparison
            // When searching for a specific part, check if:
            // 1. A full event file exists (PartName is null) - full event covers all parts, skip download
            // 2. A file for THIS specific part exists - do quality comparison
            // 3. No file for this part exists - allow download (other parts may exist)
            if (evt.HasFile)
            {
                // Get existing files for this event
                var existingFiles = await _db.EventFiles
                    .Where(f => f.EventId == eventId && f.Exists)
                    .ToListAsync();

                EventFile? relevantFile = null;

                if (!string.IsNullOrEmpty(part))
                {
                    // Searching for a specific part - check for full event file OR matching part file
                    var fullEventFile = existingFiles.FirstOrDefault(f => f.PartName == null);
                    var partSpecificFile = existingFiles.FirstOrDefault(f =>
                        string.Equals(f.PartName, part, StringComparison.OrdinalIgnoreCase));

                    if (fullEventFile != null)
                    {
                        // Full event file exists - it covers all parts including the requested one
                        _logger.LogInformation("[Automatic Search] Full event file exists (covers all parts): {Quality}",
                            fullEventFile.Quality);
                        result.Success = false;
                        result.Message = $"Full event file already exists ({fullEventFile.Quality}). No need to download individual parts.";
                        return result;
                    }

                    if (partSpecificFile != null)
                    {
                        // File for this specific part exists - use for quality comparison
                        relevantFile = partSpecificFile;
                        _logger.LogInformation("[Automatic Search] Found existing file for part '{Part}': {Quality}",
                            part, partSpecificFile.Quality);
                    }
                    else
                    {
                        // No file for this part yet - allow download
                        _logger.LogInformation("[Automatic Search] No existing file for part '{Part}' - proceeding with download", part);
                    }
                }
                else
                {
                    // Not searching for a specific part - use event-level quality (legacy behavior)
                    // Or use the first/best existing file
                    relevantFile = existingFiles.FirstOrDefault();
                    if (relevantFile != null)
                    {
                        _logger.LogInformation("[Automatic Search] Event already has file: {Quality} (Part: {Part})",
                            relevantFile.Quality, relevantFile.PartName ?? "Full Event");
                    }
                }

                // Perform upgrade eligibility check if we have a relevant existing file
                // This implements Sonarr-style upgrade logic:
                // 1. Check if UpgradesAllowed is enabled on the quality profile
                // 2. Check if existing file meets or exceeds CutoffQuality
                // 3. Check if existing file meets or exceeds CutoffFormatScore
                // 4. Compare quality/format scores to determine if new release is actually better
                if (relevantFile != null && !string.IsNullOrEmpty(relevantFile.Quality))
                {
                    // SONARR CHECK 1: UpgradesAllowed
                    // If upgrades are disabled on the quality profile, don't upgrade existing files
                    if (!qualityProfile.UpgradesAllowed)
                    {
                        result.Success = false;
                        result.Message = $"Upgrades disabled on quality profile '{qualityProfile.Name}'. Existing file: {relevantFile.Quality}";
                        _logger.LogInformation("[Automatic Search] Skipping - upgrades disabled on profile '{Profile}': {Title}",
                            qualityProfile.Name, evt.Title);
                        return result;
                    }

                    // Use stored scores if available, otherwise calculate from quality string
                    var existingQualityScore = relevantFile.QualityScore > 0
                        ? relevantFile.QualityScore
                        : CalculateQualityScore(relevantFile.Quality);
                    var existingFormatScore = relevantFile.CustomFormatScore;
                    var newReleaseQualityScore = bestRelease.QualityScore;
                    var newReleaseFormatScore = bestRelease.CustomFormatScore;

                    _logger.LogInformation("[Automatic Search] Upgrade check - Existing: Quality={ExistingQuality} (score={ExistingQScore}), Format={ExistingFScore} | New: Quality={NewQuality} (score={NewQScore}), Format={NewFScore}",
                        relevantFile.Quality, existingQualityScore, existingFormatScore,
                        bestRelease.Quality, newReleaseQualityScore, newReleaseFormatScore);

                    // SONARR CHECK 2: CutoffQuality
                    // If existing file quality meets or exceeds cutoff, don't upgrade based on quality alone
                    bool qualityCutoffMet = false;
                    if (qualityProfile.CutoffQuality.HasValue)
                    {
                        // CutoffQuality is an index into the quality items list
                        // Higher index = higher quality in Sonarr's model
                        // We use QualityScore which is calculated similarly
                        var cutoffScore = GetQualityScoreFromIndex(qualityProfile, qualityProfile.CutoffQuality.Value);
                        qualityCutoffMet = existingQualityScore >= cutoffScore;

                        if (qualityCutoffMet)
                        {
                            _logger.LogInformation("[Automatic Search] Quality cutoff met (existing={Existing} >= cutoff={Cutoff})",
                                existingQualityScore, cutoffScore);
                        }
                    }

                    // SONARR CHECK 3: CutoffFormatScore
                    // If existing file custom format score meets or exceeds cutoff, don't upgrade based on format score
                    bool formatCutoffMet = false;
                    if (qualityProfile.CutoffFormatScore.HasValue)
                    {
                        formatCutoffMet = existingFormatScore >= qualityProfile.CutoffFormatScore.Value;

                        if (formatCutoffMet)
                        {
                            _logger.LogInformation("[Automatic Search] Format score cutoff met (existing={Existing} >= cutoff={Cutoff})",
                                existingFormatScore, qualityProfile.CutoffFormatScore.Value);
                        }
                    }

                    // Both cutoffs met = no upgrade needed
                    if (qualityCutoffMet && (formatCutoffMet || !qualityProfile.CutoffFormatScore.HasValue))
                    {
                        result.Success = false;
                        result.Message = $"Cutoff met - existing file ({relevantFile.Quality}) meets quality profile requirements. No upgrade needed.";
                        _logger.LogInformation("[Automatic Search] Skipping - cutoff met for: {Title}", evt.Title);
                        return result;
                    }

                    // SONARR CHECK 4: Is the new release actually better?
                    // Compare quality scores first, then format scores as tiebreaker
                    bool isQualityUpgrade = newReleaseQualityScore > existingQualityScore;
                    bool isFormatUpgrade = newReleaseFormatScore > existingFormatScore &&
                                          (newReleaseFormatScore - existingFormatScore) >= qualityProfile.FormatScoreIncrement;

                    // If quality cutoff not met, allow quality upgrades
                    // If quality cutoff met but format cutoff not met, only allow format upgrades
                    bool shouldUpgrade;
                    string upgradeReason;

                    if (!qualityCutoffMet && isQualityUpgrade)
                    {
                        shouldUpgrade = true;
                        upgradeReason = $"quality upgrade ({existingQualityScore} -> {newReleaseQualityScore})";
                    }
                    else if (qualityCutoffMet && !formatCutoffMet && isFormatUpgrade)
                    {
                        shouldUpgrade = true;
                        upgradeReason = $"format score upgrade ({existingFormatScore} -> {newReleaseFormatScore})";
                    }
                    else if (!qualityCutoffMet && !isQualityUpgrade && isFormatUpgrade)
                    {
                        // Same quality but better format score
                        shouldUpgrade = true;
                        upgradeReason = $"format score upgrade at same quality ({existingFormatScore} -> {newReleaseFormatScore})";
                    }
                    else
                    {
                        shouldUpgrade = false;
                        upgradeReason = "not better than existing";
                    }

                    if (!shouldUpgrade)
                    {
                        result.Success = false;
                        result.Message = $"Existing file ({relevantFile.Quality}) is already good enough. New release is {upgradeReason}.";
                        _logger.LogInformation("[Automatic Search] Skipping - {Reason}: {Title}", upgradeReason, evt.Title);
                        return result;
                    }

                    _logger.LogInformation("[Automatic Search] Proceeding with {Reason} for: {Title}", upgradeReason, evt.Title);
                }
            }

            // Get download client for this protocol
            var downloadClient = await GetPreferredDownloadClientAsync(bestRelease.Protocol);

            if (downloadClient == null)
            {
                result.Success = false;
                result.Message = $"No {bestRelease.Protocol} download client configured";
                _logger.LogError("[Automatic Search] No {Protocol} download client found for: {Title}",
                    bestRelease.Protocol, evt.Title);
                return result;
            }

            _logger.LogInformation("[Automatic Search] Using {ClientType} download client: {ClientName} for {Protocol} release",
                downloadClient.Type, downloadClient.Name, bestRelease.Protocol);

            // NOTE: We do NOT specify download path - download client uses its own configured directory
            // The category is used to track Sportarr downloads
            // Root folders are used later during the import process (not here)
            // This matches Sonarr/Radarr behavior

            // Send to download client (category only, no path)
            var downloadId = await _downloadClientService.AddDownloadAsync(
                downloadClient,
                bestRelease.DownloadUrl,
                downloadClient.Category,
                bestRelease.Title  // Pass release title for better matching
            );

            if (downloadId == null)
            {
                result.Success = false;
                result.Message = "Failed to add to download client";
                _logger.LogError("[Automatic Search] Failed to add to download client: {Client}", downloadClient.Name);
                return result;
            }

            result.DownloadId = downloadId;
            _logger.LogInformation("[Automatic Search] Added to download client: {Client} (ID: {DownloadId})",
                downloadClient.Name, downloadId);

            // UNIVERSAL: Add to download queue tracking (event-level, no fight card subdivisions)
            // If this is a retry, increment the retry count from the previous failed download
            var retryCount = recentFailedDownload != null ? (recentFailedDownload.RetryCount ?? 0) + 1 : 0;

            var queueItem = new DownloadQueueItem
            {
                EventId = eventId,
                Title = bestRelease.Title,
                DownloadId = downloadId,
                DownloadClientId = downloadClient.Id,
                Status = DownloadStatus.Queued,
                Quality = bestRelease.Quality,
                Codec = bestRelease.Codec,
                Source = bestRelease.Source,
                Size = bestRelease.Size,
                Downloaded = 0,
                Progress = 0,
                Indexer = bestRelease.Indexer,
                Protocol = bestRelease.Protocol,
                TorrentInfoHash = bestRelease.TorrentInfoHash,
                RetryCount = retryCount,
                LastUpdate = DateTime.UtcNow,
                QualityScore = bestRelease.QualityScore,
                CustomFormatScore = bestRelease.CustomFormatScore,
                Part = part // Store the part (e.g., "Prelims", "Main Card") for multi-part imports
            };

            _db.DownloadQueue.Add(queueItem);

            // Save grab history for potential re-grabbing (Sportarr-exclusive feature)
            // This allows users to re-download the exact same release if they lose their media files
            var indexerRecord = await _db.Indexers
                .FirstOrDefaultAsync(i => i.Name == bestRelease.Indexer);

            // Mark any previous grabs for the same event+part as superseded
            // This prevents users from re-grabbing an old file that was replaced
            var previousGrabs = await _db.GrabHistory
                .Where(g => g.EventId == eventId && g.PartName == part && !g.Superseded)
                .ToListAsync();
            foreach (var oldGrab in previousGrabs)
            {
                oldGrab.Superseded = true;
                _logger.LogDebug("[Automatic Search] Marked previous grab as superseded: {Title}", oldGrab.Title);
            }

            var grabHistory = new GrabHistory
            {
                EventId = eventId,
                Title = bestRelease.Title,
                Indexer = bestRelease.Indexer,
                IndexerId = indexerRecord?.Id,
                DownloadUrl = bestRelease.DownloadUrl,
                Guid = bestRelease.Guid,
                Protocol = bestRelease.Protocol,
                TorrentInfoHash = bestRelease.TorrentInfoHash,
                Size = bestRelease.Size,
                Quality = bestRelease.Quality,
                Codec = bestRelease.Codec,
                Source = bestRelease.Source,
                QualityScore = bestRelease.QualityScore,
                CustomFormatScore = bestRelease.CustomFormatScore,
                PartName = part,
                GrabbedAt = DateTime.UtcNow,
                DownloadClientId = downloadClient.Id
            };
            _db.GrabHistory.Add(grabHistory);

            await _db.SaveChangesAsync();

            // Immediately check download status (Sonarr/Radarr behavior)
            // This ensures the download appears in the Activity page with real-time status
            _logger.LogInformation("[Automatic Search] Performing immediate status check...");
            try
            {
                // Give SABnzbd a moment to register the download in its queue
                // SABnzbd may need 1-2 seconds after AddNzbAsync returns before the download appears in queue API
                await Task.Delay(2000); // 2 second delay
                _logger.LogDebug("[Automatic Search] Checking status after 2s delay...");

                var status = await _downloadClientService.GetDownloadStatusAsync(downloadClient, downloadId);
                if (status != null)
                {
                    queueItem.Status = status.Status switch
                    {
                        "downloading" => DownloadStatus.Downloading,
                        "paused" => DownloadStatus.Paused,
                        "completed" => DownloadStatus.Completed,
                        "queued" or "waiting" => DownloadStatus.Queued,
                        _ => DownloadStatus.Queued
                    };
                    queueItem.Progress = status.Progress;
                    queueItem.Downloaded = status.Downloaded;
                    queueItem.Size = status.Size > 0 ? status.Size : bestRelease.Size;
                    queueItem.LastUpdate = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                    _logger.LogInformation("[Automatic Search] Initial status: {Status}, Progress: {Progress:F1}%",
                        queueItem.Status, queueItem.Progress);
                }
                else
                {
                    _logger.LogDebug("[Automatic Search] Status not available yet (download still initializing)");
                }
            }
            catch (Exception ex)
            {
                // Don't fail the automatic search if status check fails
                _logger.LogWarning(ex, "[Automatic Search] Failed to get initial status (download will be tracked by monitor)");
            }

            result.Success = true;
            result.Message = "Download started successfully";
            result.QueueItemId = queueItem.Id;

            _logger.LogInformation("[Automatic Search] SUCCESS: Event {Title} queued for download", evt.Title);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Automatic Search] Error searching for event {EventId}", eventId);
            result.Success = false;
            result.Message = $"Error: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// Search for all monitored events (checks for upgrades if files exist)
    /// For multi-part events (fighting sports), searches each missing monitored part separately
    /// Uses concurrent limiting (max 3 parallel searches) to prevent indexer rate limiting
    /// </summary>
    public async Task<List<AutomaticSearchResult>> SearchAllMonitoredEventsAsync()
    {
        _logger.LogInformation("[Automatic Search] Searching all monitored events (max 3 concurrent)");

        // QUEUE THRESHOLD CHECK (Huntarr-style) - check at batch level before starting
        var config = await _configService.GetConfigAsync();
        if (config.MaxDownloadQueueSize > 0)
        {
            var activeDownloads = await _db.DownloadQueue
                .CountAsync(d => d.Status == DownloadStatus.Queued ||
                                d.Status == DownloadStatus.Downloading);

            if (activeDownloads >= config.MaxDownloadQueueSize)
            {
                _logger.LogInformation("[Automatic Search] Queue threshold reached ({Active}/{Max}). Skipping batch search cycle. Will retry in {Sleep} seconds.",
                    activeDownloads, config.MaxDownloadQueueSize, config.SearchSleepDuration);
                return new List<AutomaticSearchResult>
                {
                    new() { Success = false, Message = $"Queue threshold reached ({activeDownloads}/{config.MaxDownloadQueueSize})" }
                };
            }

            _logger.LogDebug("[Automatic Search] Queue check passed: {Active}/{Max} downloads active",
                activeDownloads, config.MaxDownloadQueueSize);
        }

        // Get all monitored events from monitored leagues only
        // Both the event AND the league must be monitored for automatic background search
        // Include Files to check which parts are already downloaded
        var events = await _db.Events
            .Include(e => e.League)
            .Include(e => e.Files)
            .Where(e => e.Monitored && e.League != null && e.League.Monitored)
            .ToListAsync();

        _logger.LogInformation("[Automatic Search] Found {Count} monitored events (from monitored leagues) to search", events.Count);

        // Build list of search targets (event + optional part)
        // For multi-part fighting events, expand into individual part searches
        var searchTargets = new List<(int EventId, string? Part, string Description)>();

        foreach (var evt in events)
        {
            // Check if this is a fighting sport with multi-part episodes enabled
            if (config.EnableMultiPartEpisodes && EventPartDetector.IsFightingSport(evt.Sport ?? ""))
            {
                // Get parts for this event type (respects Fight Night vs PPV differences)
                var segmentDefinitions = EventPartDetector.GetSegmentDefinitions(evt.Sport ?? "Fighting", evt.Title);
                var parts = segmentDefinitions.Where(s => s.PartNumber > 0).ToList();

                // Reorder parts for search priority: Main Card first, then Prelims, then Early Prelims, then Post Show last
                // This ensures the most important content is downloaded first
                parts = parts
                    .OrderBy(p => p.Name switch
                    {
                        "Main Card" => 1,    // Most important - download first
                        "Prelims" => 2,      // Second priority
                        "Early Prelims" => 3, // Third priority
                        "Post Show" => 4,     // Least important - download last
                        _ => 5               // Any other parts after
                    })
                    .ToList();

                // Parse monitored parts
                // null = all parts monitored by default
                // "" = no parts monitored
                // "Part1,Part2" = specific parts monitored
                var monitoredPartNames = evt.MonitoredParts == null
                    ? null // null means all monitored
                    : evt.MonitoredParts.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var part in parts)
                {
                    // Check if this part is monitored
                    bool isMonitored = monitoredPartNames == null || // null = all monitored
                                      monitoredPartNames.Contains(part.Name);

                    if (!isMonitored)
                    {
                        _logger.LogDebug("[Automatic Search] Skipping unmonitored part: {Event} - {Part}",
                            evt.Title, part.Name);
                        continue;
                    }

                    // Check if this part already has a file
                    var hasFile = evt.Files.Any(f => f.PartNumber == part.PartNumber && f.Exists);
                    if (hasFile)
                    {
                        // TODO: Could check for upgrades here in the future
                        _logger.LogDebug("[Automatic Search] Skipping {Event} - {Part} (already downloaded)",
                            evt.Title, part.Name);
                        continue;
                    }

                    // Check if already in download queue
                    // Note: This is a quick check - full queue check happens in SearchAndDownloadEventAsync
                    searchTargets.Add((evt.Id, part.Name, $"{evt.Title} ({part.Name})"));
                }
            }
            else
            {
                // Non-fighting sport or multi-part disabled - search for full event
                if (!evt.HasFile)
                {
                    searchTargets.Add((evt.Id, null, evt.Title ?? $"Event {evt.Id}"));
                }
            }
        }

        _logger.LogInformation("[Automatic Search] Expanded to {Count} search targets (events + parts)", searchTargets.Count);

        if (!searchTargets.Any())
        {
            _logger.LogInformation("[Automatic Search] No missing events/parts to search");
            return new List<AutomaticSearchResult>();
        }

        // Use concurrent limiting with staggered starts
        var tasks = searchTargets.Select(async (target, index) =>
        {
            // Stagger start times to spread load
            if (index > 0)
            {
                await Task.Delay(index * 1000); // 1 second stagger between starts
            }

            // Wait for available slot in semaphore (max 3 concurrent)
            await _eventSearchSemaphore.WaitAsync();
            try
            {
                // Additional delay before search
                await Task.Delay(EventSearchDelayMs);
                _logger.LogDebug("[Automatic Search] Searching: {Description}", target.Description);
                return await SearchAndDownloadEventAsync(target.EventId, null, target.Part, isManualSearch: false);
            }
            finally
            {
                _eventSearchSemaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        var successful = results.Count(r => r.Success);
        _logger.LogInformation("[Automatic Search] Completed: {Success}/{Total} successful",
            successful, results.Length);

        return results.ToList();
    }

    // Private helper methods

    /// <summary>
    /// Re-evaluate cached releases against the current quality profile and release profiles
    /// Cached releases have Approved=true and empty Rejections - we need to apply
    /// ReleaseEvaluator logic to enforce CF minimum scores, size limits, and release profile filtering.
    /// </summary>
    private async Task ReEvaluateCachedReleasesAsync(
        List<ReleaseSearchResult> releases,
        int? qualityProfileId,
        string? requestedPart,
        string? sport,
        bool enableMultiPartEpisodes,
        string? eventTitle)
    {
        if (!releases.Any()) return;

        // Load release profiles for keyword filtering
        var releaseProfiles = await _releaseProfileService.LoadReleaseProfilesAsync();

        // Load profile and custom formats
        QualityProfile? profile = null;
        List<CustomFormat>? customFormats = null;
        List<QualityDefinition>? qualityDefinitions = null;

        if (qualityProfileId.HasValue)
        {
            profile = await _db.QualityProfiles
                .FirstOrDefaultAsync(p => p.Id == qualityProfileId.Value);
            customFormats = await _db.CustomFormats.ToListAsync();
        }

        qualityDefinitions = await _db.QualityDefinitions.ToListAsync();

        if (profile == null)
        {
            _logger.LogDebug("[Automatic Search] No quality profile for cached release evaluation");
            return;
        }

        _logger.LogDebug("[Automatic Search] Re-evaluating {Count} cached releases against profile '{Profile}'",
            releases.Count, profile.Name);

        int rejectedCount = 0;
        foreach (var release in releases)
        {
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
                null,
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

            if (release.Rejections.Any())
            {
                rejectedCount++;
            }
        }

        if (rejectedCount > 0)
        {
            _logger.LogInformation("[Automatic Search] Re-evaluation rejected {Count}/{Total} cached releases (CF score, size, release profiles, etc.)",
                rejectedCount, releases.Count);
        }
    }

    private async Task<DownloadClient?> GetPreferredDownloadClientAsync(string protocol)
    {
        // Get client types that support this protocol
        var supportedTypes = DownloadClientService.GetClientTypesForProtocol(protocol);

        if (supportedTypes.Count == 0)
        {
            _logger.LogWarning("[Automatic Search] Unknown protocol: {Protocol}", protocol);
            return null;
        }

        // Get highest priority enabled download client that supports this protocol
        return await _db.DownloadClients
            .Where(dc => dc.Enabled && supportedTypes.Contains(dc.Type))
            .OrderBy(dc => dc.Priority)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Calculate quality score from quality string (matches ReleaseEvaluator logic)
    /// </summary>
    private int CalculateQualityScore(string quality)
    {
        if (string.IsNullOrEmpty(quality)) return 0;

        int score = 0;

        // Resolution scores
        if (quality.Contains("2160p", StringComparison.OrdinalIgnoreCase)) score += 1000;
        else if (quality.Contains("1080p", StringComparison.OrdinalIgnoreCase)) score += 800;
        else if (quality.Contains("720p", StringComparison.OrdinalIgnoreCase)) score += 600;
        else if (quality.Contains("480p", StringComparison.OrdinalIgnoreCase)) score += 400;
        else if (quality.Contains("360p", StringComparison.OrdinalIgnoreCase)) score += 200;

        // Source scores
        if (quality.Contains("BluRay", StringComparison.OrdinalIgnoreCase)) score += 100;
        else if (quality.Contains("WEB-DL", StringComparison.OrdinalIgnoreCase)) score += 90;
        else if (quality.Contains("WEBRip", StringComparison.OrdinalIgnoreCase)) score += 85;
        else if (quality.Contains("HDTV", StringComparison.OrdinalIgnoreCase)) score += 70;
        else if (quality.Contains("DVDRip", StringComparison.OrdinalIgnoreCase)) score += 60;
        else if (quality.Contains("SDTV", StringComparison.OrdinalIgnoreCase)) score += 40;

        return score;
    }

    /// <summary>
    /// Get quality score from a quality profile index (Sonarr-style cutoff)
    /// The CutoffQuality in Sonarr is an index into the quality items list
    /// </summary>
    private int GetQualityScoreFromIndex(QualityProfile profile, int qualityIndex)
    {
        // Find the quality item at this index
        var qualityItem = profile.Items.FirstOrDefault(i => i.Quality == qualityIndex);

        if (qualityItem != null)
        {
            // Calculate score based on quality name
            return CalculateQualityScore(qualityItem.Name ?? "");
        }

        // Fallback: use index-based scoring (higher index = higher quality)
        // Standard quality tiers: Unknown(0), SDTV(1), WEBDL-480p(2), ... Bluray-2160p(highest)
        return qualityIndex * 100;
    }

    /// <summary>
    /// Extract resolution from quality string (handles multiple formats)
    /// Supports: "1080p WEB-DL" (EventFile format), "WEBDL-1080p" (Release format), etc.
    /// </summary>
    private static string? ExtractResolution(string? quality)
    {
        if (string.IsNullOrEmpty(quality)) return null;

        // Match common resolution patterns: 2160p, 1080p, 720p, 480p, 360p
        var match = System.Text.RegularExpressions.Regex.Match(
            quality,
            @"\b(2160p|1080p|720p|480p|360p|4K)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (match.Success)
        {
            var resolution = match.Value.ToLower();
            // Normalize 4K to 2160p
            return resolution == "4k" ? "2160p" : resolution;
        }

        return null;
    }

    /// <summary>
    /// Build quality string in standard format for group matching
    /// Converts source formats like "WEB-DL" to "WEBDL-1080p"
    /// </summary>
    private static string? BuildQualityString(string? source, string? resolution)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(resolution))
            return null;

        // Normalize source names (WEB-DL -> WEBDL, Web-DL -> WEBDL)
        var normalizedSource = source
            .Replace("-", "")
            .Replace(" ", "");

        return $"{normalizedSource}-{resolution}";
    }

    /// <summary>
    /// Quality group mappings - matches ReleaseEvaluator.QualityGroupMappings
    /// WEB groups contain both WEBDL and WEBRip as equivalent qualities
    /// </summary>
    private static readonly Dictionary<string, string[]> QualityGroupMappings = new()
    {
        // WEB groups - WEBDL and WEBRip are equivalent at each resolution
        { "WEB 2160p", new[] { "WEBDL-2160p", "WEBRip-2160p", "WEB-DL-2160p", "WEBDL2160p" } },
        { "WEB 1080p", new[] { "WEBDL-1080p", "WEBRip-1080p", "WEB-DL-1080p", "WEBDL1080p" } },
        { "WEB 720p", new[] { "WEBDL-720p", "WEBRip-720p", "WEB-DL-720p", "WEBDL720p" } },
        { "WEB 480p", new[] { "WEBDL-480p", "WEBRip-480p", "WEB-DL-480p", "WEBDL480p" } },

        // HDTV groups
        { "HDTV 2160p", new[] { "HDTV-2160p", "HDTV2160p" } },
        { "HDTV 1080p", new[] { "HDTV-1080p", "HDTV1080p" } },
        { "HDTV 720p", new[] { "HDTV-720p", "HDTV720p" } },

        // Bluray groups
        { "Bluray 2160p", new[] { "Bluray-2160p", "BluRay-2160p", "Bluray2160p" } },
        { "Bluray 1080p", new[] { "Bluray-1080p", "BluRay-1080p", "Bluray1080p" } },
        { "Bluray 720p", new[] { "Bluray-720p", "BluRay-720p", "Bluray720p" } },
    };

    /// <summary>
    /// Find which quality group a quality string belongs to
    /// e.g., "WEBDL-1080p" and "WEBRip-1080p" both return "WEB 1080p"
    /// </summary>
    private static string? FindQualityGroup(string? quality)
    {
        if (string.IsNullOrEmpty(quality)) return null;

        foreach (var (groupName, members) in QualityGroupMappings)
        {
            if (members.Any(m => quality.Contains(m.Replace("-", ""), StringComparison.OrdinalIgnoreCase) ||
                                 m.Equals(quality, StringComparison.OrdinalIgnoreCase)))
            {
                return groupName;
            }
        }

        // Try to match by resolution and source type
        var resolution = ExtractResolution(quality);
        if (resolution != null)
        {
            if (quality.Contains("WEB", StringComparison.OrdinalIgnoreCase))
                return $"WEB {resolution}";
            if (quality.Contains("HDTV", StringComparison.OrdinalIgnoreCase))
                return $"HDTV {resolution}";
            if (quality.Contains("Blu", StringComparison.OrdinalIgnoreCase))
                return $"Bluray {resolution}";
        }

        return null;
    }
}

/// <summary>
/// Result of automatic search operation
/// </summary>
public class AutomaticSearchResult
{
    public int EventId { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int ReleasesFound { get; set; }
    public string? SelectedRelease { get; set; }
    public string? SelectedIndexer { get; set; }
    public string? Quality { get; set; }
    public string? DownloadId { get; set; }
    public int? QueueItemId { get; set; }
}
