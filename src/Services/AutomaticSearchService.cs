using Fightarr.Api.Data;
using Fightarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Fightarr.Api.Services;

/// <summary>
/// Automatic search and download service for monitored events
/// Implements Sonarr/Radarr-style automation: search → select → download
/// </summary>
public class AutomaticSearchService
{
    private readonly FightarrDbContext _db;
    private readonly IndexerSearchService _indexerSearchService;
    private readonly DownloadClientService _downloadClientService;
    private readonly FightCardService _fightCardService;
    private readonly FightCardQueryService _fightCardQueryService;
    private readonly DelayProfileService _delayProfileService;
    private readonly ILogger<AutomaticSearchService> _logger;

    public AutomaticSearchService(
        FightarrDbContext db,
        IndexerSearchService indexerSearchService,
        DownloadClientService downloadClientService,
        FightCardService fightCardService,
        FightCardQueryService fightCardQueryService,
        DelayProfileService delayProfileService,
        ILogger<AutomaticSearchService> logger)
    {
        _db = db;
        _indexerSearchService = indexerSearchService;
        _downloadClientService = downloadClientService;
        _fightCardService = fightCardService;
        _fightCardQueryService = fightCardQueryService;
        _delayProfileService = delayProfileService;
        _logger = logger;
    }

    /// <summary>
    /// Automatically search and download for a specific event
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

            // Check if event or any fight cards are monitored
            var hasMonitoredCards = await _fightCardService.HasAnyMonitoredFightCardsAsync(eventId);
            if (!evt.Monitored && !hasMonitoredCards)
            {
                result.Success = false;
                result.Message = "Event and all fight cards are unmonitored";
                _logger.LogInformation("[Automatic Search] Skipping unmonitored event: {Title}", evt.Title);
                return result;
            }

            var monitoredCards = await _fightCardService.GetMonitoredFightCardsAsync(eventId);
            _logger.LogInformation("[Automatic Search] Starting search for event: {Title} ({MonitoredCards} monitored cards)",
                evt.Title, monitoredCards.Count);

            if (!monitoredCards.Any())
            {
                result.Success = false;
                result.Message = "No monitored fight cards";
                return result;
            }

            // Build card-type specific queries (Sonarr-style)
            var cardQueries = _fightCardQueryService.BuildCardTypeQueries(evt, monitoredCards);
            _logger.LogInformation("[Automatic Search] Searching for {Count} card types", cardQueries.Count);

            // Search all indexers for each card type
            var allReleases = new List<ReleaseSearchResult>();

            foreach (var (cardType, query) in cardQueries)
            {
                _logger.LogInformation("[Automatic Search] Searching {CardType}: '{Query}'",
                    _fightCardQueryService.GetCardTypeDisplayName(cardType), query);

                var cardReleases = await _indexerSearchService.SearchAllIndexersAsync(query);

                // Detect and tag card types
                foreach (var release in cardReleases)
                {
                    release.CardType = _fightCardQueryService.DetectCardType(release.Title);
                }

                allReleases.AddRange(cardReleases);
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
            _logger.LogInformation("[Automatic Search] Selected: {Release} from {Indexer}",
                bestRelease.Title, bestRelease.Indexer);

            // Get download client
            var downloadClient = await GetPreferredDownloadClientAsync();

            if (downloadClient == null)
            {
                result.Success = false;
                result.Message = "No download client configured";
                return result;
            }

            // NOTE: We do NOT specify download path - download client uses its own configured directory
            // The category is used to track Fightarr downloads
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

            // Find matching fight card for this release (Sonarr-style episode tracking)
            int? fightCardId = null;
            if (bestRelease.CardType.HasValue)
            {
                var matchingCard = monitoredCards.FirstOrDefault(fc => fc.CardType == bestRelease.CardType.Value);
                if (matchingCard != null)
                {
                    fightCardId = matchingCard.Id;
                    _logger.LogInformation("[Automatic Search] Linked download to {CardType} (FightCardId: {FightCardId})",
                        _fightCardQueryService.GetCardTypeDisplayName(bestRelease.CardType.Value), fightCardId);
                }
            }

            // Add to download queue tracking
            var queueItem = new DownloadQueueItem
            {
                EventId = eventId,
                FightCardId = fightCardId,
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
    /// Search for all monitored events that don't have files
    /// </summary>
    public async Task<List<AutomaticSearchResult>> SearchAllMonitoredEventsAsync()
    {
        _logger.LogInformation("[Automatic Search] Searching all monitored events");

        var results = new List<AutomaticSearchResult>();

        // Get all monitored events without files
        var events = await _db.Events
            .Where(e => e.Monitored && !e.HasFile)
            .ToListAsync();

        _logger.LogInformation("[Automatic Search] Found {Count} monitored events without files", events.Count);

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
