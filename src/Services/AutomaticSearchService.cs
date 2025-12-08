using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Automatic search and download service for monitored events
/// Implements Sonarr/Radarr-style automation: search → select → download
/// Includes concurrent event search limiting (max 3) to prevent indexer rate limiting
///
/// OPTIMIZATION: Uses SearchCacheService to prevent duplicate API calls.
/// When searching for multi-part events (UFC Main Card, Prelims, etc.):
/// - First search gets ALL results for the event (no part filter in query)
/// - Results are cached for 60 seconds
/// - Subsequent part searches use cached results, filtering locally
/// This reduces API calls from 3x (one per part) to 1x per event.
/// </summary>
public class AutomaticSearchService
{
    private readonly SportarrDbContext _db;
    private readonly IndexerSearchService _indexerSearchService;
    private readonly DownloadClientService _downloadClientService;
    private readonly EventQueryService _eventQueryService;
    private readonly DelayProfileService _delayProfileService;
    private readonly ReleaseMatchingService _releaseMatchingService;
    private readonly SearchCacheService _searchCache;
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
        SearchCacheService searchCache,
        ILogger<AutomaticSearchService> logger)
    {
        _db = db;
        _indexerSearchService = indexerSearchService;
        _downloadClientService = downloadClientService;
        _eventQueryService = eventQueryService;
        _delayProfileService = delayProfileService;
        _releaseMatchingService = releaseMatchingService;
        _searchCache = searchCache;
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

            // OPTIMIZATION: Check cache first (Sonarr-style)
            // For multi-part events, we search ONCE without part filter and cache results.
            // Part filtering happens locally, saving N-1 API calls per event.
            var allReleases = new List<ReleaseSearchResult>();
            bool usedCache = false;

            // Try to get cached results for this specific part first
            var cachedResults = await _searchCache.TryGetCachedAsync(eventId, part);
            if (cachedResults != null)
            {
                allReleases = cachedResults;
                usedCache = true;
                _logger.LogInformation("[Automatic Search] Using cached results: {Count} releases for {Title}{Part}",
                    allReleases.Count, evt.Title, part != null ? $" ({part})" : "");
            }

            // If no part-specific cache, try the base event cache
            // This is the key optimization: search "UFC 299" once, reuse for all parts
            if (!usedCache && part != null)
            {
                var baseCachedResults = await _searchCache.TryGetBaseCachedAsync(eventId);
                if (baseCachedResults != null)
                {
                    allReleases = baseCachedResults;
                    usedCache = true;
                    _logger.LogInformation("[Automatic Search] Using BASE cached results: {Count} releases for {Title} (filtering for {Part})",
                        allReleases.Count, evt.Title, part);
                }
            }

            // If no cache hit, perform the actual search
            if (!usedCache)
            {
                // OPTIMIZATION: For part-specific searches, search WITHOUT the part first
                // This gets ALL releases (Main Card, Prelims, etc.) in one search
                // We cache and filter locally rather than making separate API calls per part
                var searchWithoutPart = part != null;
                var searchPart = searchWithoutPart ? null : part; // Search broadly when part is requested

                // Build queries (without part for broad search)
                var queries = _eventQueryService.BuildEventQueries(evt, searchPart);

                _logger.LogInformation("[Automatic Search] Built {Count} prioritized queries for {Sport}{BroadSearch}",
                    queries.Count, evt.Sport, searchWithoutPart ? " (broad search for caching)" : "");

                // OPTIMIZATION: Intelligent fallback search (Sonarr/Radarr-style)
                // Try primary query first, exit early if no results (likely future event or no releases exist)
                // This reduces API hits significantly - especially for future events with no releases
                int queriesAttempted = 0;
                int consecutiveEmptyResults = 0;
                const int MinimumResults = 3; // Lower threshold - even 1 good result is enough
                const int MaxConsecutiveEmpty = 2; // Stop after 2 empty queries (event likely not released yet)

                foreach (var query in queries)
                {
                    queriesAttempted++;
                    _logger.LogInformation("[Automatic Search] Trying query {Attempt}/{Total}: '{Query}'",
                        queriesAttempted, queries.Count, query);

                    // Note: Pass part=null to indexer so we get ALL releases, filtering happens locally
                    var releases = await _indexerSearchService.SearchAllIndexersAsync(query, maxResultsPerIndexer: 100, qualityProfileId, null, evt.Sport);

                    if (releases.Count == 0)
                    {
                        consecutiveEmptyResults++;
                        _logger.LogInformation("[Automatic Search] No results for query '{Query}' ({Empty}/{MaxEmpty} consecutive empty)",
                            query, consecutiveEmptyResults, MaxConsecutiveEmpty);

                        // EARLY EXIT: If first 2 queries return nothing, event likely not available yet
                        // This prevents hammering indexers for future events
                        if (consecutiveEmptyResults >= MaxConsecutiveEmpty)
                        {
                            _logger.LogInformation("[Automatic Search] Stopping search - {Empty} consecutive empty results (event likely not released yet)",
                                consecutiveEmptyResults);
                            break;
                        }
                    }
                    else
                    {
                        // Found results - reset empty counter and add to collection
                        consecutiveEmptyResults = 0;
                        allReleases.AddRange(releases);

                        // SUCCESS: Found enough results - stop trying fallback queries
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

                // Cache results for future part searches
                // Cache as base event (null part) so all part searches can reuse
                if (allReleases.Any())
                {
                    _searchCache.Cache(eventId, null, allReleases);
                    _logger.LogInformation("[Automatic Search] Cached {Count} results for event {EventId} (available for all parts)",
                        allReleases.Count, eventId);
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
            _logger.LogInformation("[Automatic Search] Found {Count} total releases{CacheNote}",
                allReleases.Count, usedCache ? " (from cache)" : "");

            // SONARR-STYLE RELEASE VALIDATION: Filter out releases that don't actually match this event
            // This prevents downloading wrong content when search queries match multiple events
            _logger.LogInformation("[Automatic Search] Validating {Count} releases against event '{Title}'",
                allReleases.Count, evt.Title);

            var validReleases = _releaseMatchingService.FilterValidReleases(allReleases, evt, part);

            if (!validReleases.Any())
            {
                result.Success = false;
                result.Message = $"No matching releases found. {allReleases.Count} releases were filtered out (didn't match event).";
                _logger.LogWarning("[Automatic Search] All {Count} releases filtered out for: {Title} (none matched event criteria)",
                    allReleases.Count, evt.Title);
                return result;
            }

            // Extract just the releases (without match info) for further processing
            var matchedReleases = validReleases.Select(v => v.Release).ToList();
            _logger.LogInformation("[Automatic Search] {ValidCount}/{TotalCount} releases passed validation (min confidence: {MinConfidence}%)",
                matchedReleases.Count, allReleases.Count, ReleaseMatchingService.MinimumMatchConfidence);

            // MULTI-PART CONSISTENCY CHECK: For automatic searches, ensure new releases match existing parts
            // This prevents downloading mismatched quality/codec/source for multi-part episodes
            if (!isManualSearch && !string.IsNullOrEmpty(part))
            {
                var existingPartFiles = await _db.EventFiles
                    .Where(f => f.EventId == eventId && f.Exists && f.PartName != null && f.PartName != part)
                    .ToListAsync();

                if (existingPartFiles.Any())
                {
                    var referenceFile = existingPartFiles.First();
                    _logger.LogInformation("[Automatic Search] Multi-part consistency check: Found existing parts with Quality={Quality}, Codec={Codec}, Source={Source}",
                        referenceFile.Quality, referenceFile.Codec, referenceFile.Source);

                    var consistentReleases = matchedReleases.Where(r =>
                    {
                        // Only enforce consistency for fields that exist on the reference file
                        bool qualityMatch = string.IsNullOrEmpty(referenceFile.Quality) || r.Quality == referenceFile.Quality;
                        bool codecMatch = string.IsNullOrEmpty(referenceFile.Codec) || r.Codec == referenceFile.Codec;
                        bool sourceMatch = string.IsNullOrEmpty(referenceFile.Source) || r.Source == referenceFile.Source;

                        return qualityMatch && codecMatch && sourceMatch;
                    }).ToList();

                    if (consistentReleases.Any())
                    {
                        _logger.LogInformation("[Automatic Search] Found {Count} releases matching existing part specs (filtered from {Total})",
                            consistentReleases.Count, matchedReleases.Count);
                        matchedReleases = consistentReleases;
                    }
                    else
                    {
                        _logger.LogWarning("[Automatic Search] No releases match existing part specs. Proceeding with best available to avoid blocking download.");
                        // Don't filter - let user get something rather than nothing
                        // The warning will show in the UI after download completes
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

            // UPGRADE CHECK: If event already has a file, compare quality scores
            if (evt.HasFile && !string.IsNullOrEmpty(evt.Quality))
            {
                _logger.LogInformation("[Automatic Search] Event already has file: {Quality}", evt.Quality);

                // Calculate score of existing file
                var existingQualityScore = CalculateQualityScore(evt.Quality);
                var newReleaseScore = bestRelease.Score;

                _logger.LogInformation("[Automatic Search] Existing quality score: {ExistingScore}, New release score: {NewScore}",
                    existingQualityScore, newReleaseScore);

                // If existing file meets or exceeds cutoff, or new release isn't better, skip
                if (newReleaseScore <= existingQualityScore)
                {
                    result.Success = false;
                    result.Message = $"Existing file quality ({evt.Quality}) is already good enough. Skipping upgrade.";
                    _logger.LogInformation("[Automatic Search] Skipping - existing quality is sufficient: {Title}", evt.Title);
                    return result;
                }

                _logger.LogInformation("[Automatic Search] New release is better quality - proceeding with upgrade");
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
                CustomFormatScore = bestRelease.CustomFormatScore
            };

            _db.DownloadQueue.Add(queueItem);
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
    /// Uses concurrent limiting (max 3 parallel searches) to prevent indexer rate limiting
    /// </summary>
    public async Task<List<AutomaticSearchResult>> SearchAllMonitoredEventsAsync()
    {
        _logger.LogInformation("[Automatic Search] Searching all monitored events (max 3 concurrent)");

        // Get all monitored events from monitored leagues only
        // Both the event AND the league must be monitored for automatic background search
        var events = await _db.Events
            .Include(e => e.League)
            .Where(e => e.Monitored && e.League != null && e.League.Monitored)
            .ToListAsync();

        _logger.LogInformation("[Automatic Search] Found {Count} monitored events (from monitored leagues) to search", events.Count);

        // Use concurrent limiting with staggered starts
        var tasks = events.Select(async (evt, index) =>
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
                return await SearchAndDownloadEventAsync(evt.Id);
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
