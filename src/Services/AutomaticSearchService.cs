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
    private readonly ILogger<AutomaticSearchService> _logger;

    public AutomaticSearchService(
        FightarrDbContext db,
        IndexerSearchService indexerSearchService,
        DownloadClientService downloadClientService,
        ILogger<AutomaticSearchService> logger)
    {
        _db = db;
        _indexerSearchService = indexerSearchService;
        _downloadClientService = downloadClientService;
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

            _logger.LogInformation("[Automatic Search] Starting search for event: {Title}", evt.Title);

            // Build search query
            var searchQuery = BuildSearchQuery(evt);
            _logger.LogInformation("[Automatic Search] Search query: {Query}", searchQuery);

            // Search all indexers
            var releases = await _indexerSearchService.SearchAllIndexersAsync(searchQuery);

            if (!releases.Any())
            {
                result.Success = false;
                result.Message = "No releases found";
                _logger.LogWarning("[Automatic Search] No releases found for: {Title}", evt.Title);
                return result;
            }

            result.ReleasesFound = releases.Count;
            _logger.LogInformation("[Automatic Search] Found {Count} releases", releases.Count);

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

            // Select best release
            var bestRelease = _indexerSearchService.SelectBestRelease(releases, qualityProfile);

            if (bestRelease == null)
            {
                result.Success = false;
                result.Message = "No releases match quality profile";
                _logger.LogWarning("[Automatic Search] No releases match quality profile for: {Title}", evt.Title);
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

            // Determine save path from root folders
            var rootFolder = await _db.RootFolders.OrderBy(r => r.Id).FirstOrDefaultAsync();
            var savePath = rootFolder?.Path ?? Path.Combine(Directory.GetCurrentDirectory(), "downloads");

            // Send to download client
            var downloadId = await _downloadClientService.AddDownloadAsync(
                downloadClient,
                bestRelease.DownloadUrl,
                savePath,
                downloadClient.Category
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

            // Add to download queue tracking
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
                Progress = 0
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

    private string BuildSearchQuery(Event evt)
    {
        // Build comprehensive search query
        var queryParts = new List<string>
        {
            evt.Title,
            evt.Organization
        };

        // Add year if available
        if (evt.EventDate != default)
        {
            queryParts.Add(evt.EventDate.Year.ToString());
        }

        return string.Join(" ", queryParts);
    }

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
