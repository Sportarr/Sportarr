using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Automatic search and download service for monitored events
/// Implements Sonarr/Radarr-style automation: search → select → download
/// </summary>
public class AutomaticSearchService
{
    private readonly SportarrDbContext _db;
    private readonly IndexerSearchService _indexerSearchService;
    private readonly DownloadClientService _downloadClientService;
    private readonly EventQueryService _eventQueryService;
    private readonly DelayProfileService _delayProfileService;
    private readonly ILogger<AutomaticSearchService> _logger;

    public AutomaticSearchService(
        SportarrDbContext db,
        IndexerSearchService indexerSearchService,
        DownloadClientService downloadClientService,
        EventQueryService eventQueryService,
        DelayProfileService delayProfileService,
        ILogger<AutomaticSearchService> logger)
    {
        _db = db;
        _indexerSearchService = indexerSearchService;
        _downloadClientService = downloadClientService;
        _eventQueryService = eventQueryService;
        _delayProfileService = delayProfileService;
        _logger = logger;
    }

    /// <summary>
    /// Automatically search and download for a specific event (universal for all sports)
    /// </summary>
    public async Task<AutomaticSearchResult> SearchAndDownloadEventAsync(int eventId, int? qualityProfileId = null)
    {
        var result = new AutomaticSearchResult { EventId = eventId };

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

            // UNIVERSAL: Check if event is monitored (same for all sports)
            if (!evt.Monitored)
            {
                result.Success = false;
                result.Message = "Event is unmonitored";
                _logger.LogInformation("[Automatic Search] Skipping unmonitored event: {Title}", evt.Title);
                return result;
            }

            _logger.LogInformation("[Automatic Search] Starting search for event: {Title} ({Sport})",
                evt.Title, evt.Sport);

            // Load related entities for query building (universal - league/teams used for all sports)
            await _db.Entry(evt).Reference(e => e.HomeTeam).LoadAsync();
            await _db.Entry(evt).Reference(e => e.AwayTeam).LoadAsync();
            await _db.Entry(evt).Reference(e => e.League).LoadAsync();

            // UNIVERSAL: Build search queries using sport-agnostic approach
            // Works for UFC, Premier League, NBA, MLB, etc.
            var queries = _eventQueryService.BuildEventQueries(evt);

            _logger.LogInformation("[Automatic Search] Built {Count} prioritized queries for {Sport}",
                queries.Count, evt.Sport);

            // OPTIMIZATION: Intelligent fallback search (Sonarr/Radarr-style)
            // Try primary query first, only fallback if no results found
            // This reduces API hits from (queries × indexers) to just (indexers) for most searches
            var allReleases = new List<ReleaseSearchResult>();
            int queriesAttempted = 0;
            const int MinimumResults = 5; // Try next query if we get very few results

            foreach (var query in queries)
            {
                queriesAttempted++;
                _logger.LogInformation("[Automatic Search] Trying query {Attempt}/{Total}: '{Query}'",
                    queriesAttempted, queries.Count, query);

                var releases = await _indexerSearchService.SearchAllIndexersAsync(query, maxResultsPerIndexer: 100, qualityProfileId);
                allReleases.AddRange(releases);

                // Success criteria: Found enough results to make a good selection
                if (allReleases.Count >= MinimumResults)
                {
                    _logger.LogInformation("[Automatic Search] Found {Count} results with first query - skipping remaining {Remaining} fallback queries (rate limit optimization)",
                        allReleases.Count, queries.Count - queriesAttempted);
                    break;
                }

                // If we found some results but not enough, log it and try next query
                if (allReleases.Count > 0 && allReleases.Count < MinimumResults)
                {
                    _logger.LogInformation("[Automatic Search] Found {Count} results (below minimum {Min}) - trying next query",
                        allReleases.Count, MinimumResults);
                }
                else if (allReleases.Count == 0)
                {
                    _logger.LogWarning("[Automatic Search] No results for query '{Query}' - trying next fallback", query);
                }
            }

            if (!allReleases.Any())
            {
                result.Success = false;
                result.Message = $"No releases found after trying {queriesAttempted} query variations";
                _logger.LogWarning("[Automatic Search] No releases found for: {Title} ({QueriesAttempted}/{QueryCount} queries tried)",
                    evt.Title, queriesAttempted, queries.Count);
                return result;
            }

            result.ReleasesFound = allReleases.Count;
            _logger.LogInformation("[Automatic Search] Found {Count} total releases", allReleases.Count);

            // Get quality profile (use default if not specified)
            var qualityProfile = qualityProfileId.HasValue
                ? await _db.QualityProfiles.FindAsync(qualityProfileId.Value)
                : await _db.QualityProfiles.OrderBy(q => q.Id).FirstOrDefaultAsync();

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

            // Select best release using delay profile and protocol priority
            var bestRelease = _delayProfileService.SelectBestReleaseWithDelayProfile(
                allReleases, delayProfile, qualityProfile);

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

            // Get download client
            var downloadClient = await GetPreferredDownloadClientAsync();

            if (downloadClient == null)
            {
                result.Success = false;
                result.Message = "No download client configured";
                return result;
            }

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
            var queueItem = new DownloadQueueItem
            {
                EventId = eventId,
                Title = bestRelease.Title,
                DownloadId = downloadId,
                DownloadClientId = downloadClient.Id,
                Status = DownloadStatus.Queued,
                Quality = bestRelease.Quality,
                Size = bestRelease.Size,
                Downloaded = 0,
                Progress = 0,
                Indexer = bestRelease.Indexer,
                Protocol = bestRelease.Protocol,
                TorrentInfoHash = bestRelease.TorrentInfoHash,
                RetryCount = 0,
                LastUpdate = DateTime.UtcNow
            };

            _db.DownloadQueue.Add(queueItem);
            await _db.SaveChangesAsync();

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
    /// </summary>
    public async Task<List<AutomaticSearchResult>> SearchAllMonitoredEventsAsync()
    {
        _logger.LogInformation("[Automatic Search] Searching all monitored events");

        var results = new List<AutomaticSearchResult>();

        // Get all monitored events (not just those without files)
        var events = await _db.Events
            .Where(e => e.Monitored)
            .ToListAsync();

        _logger.LogInformation("[Automatic Search] Found {Count} monitored events", events.Count);

        foreach (var evt in events)
        {
            var result = await SearchAndDownloadEventAsync(evt.Id);
            results.Add(result);

            // Add delay between searches to avoid hammering indexers
            await Task.Delay(2000);
        }

        var successful = results.Count(r => r.Success);
        _logger.LogInformation("[Automatic Search] Completed: {Success}/{Total} successful",
            successful, results.Count);

        return results;
    }

    // Private helper methods


    private async Task<DownloadClient?> GetPreferredDownloadClientAsync()
    {
        // Get highest priority enabled download client
        return await _db.DownloadClients
            .Where(dc => dc.Enabled)
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
