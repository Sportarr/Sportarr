using Fightarr.Api.Data;
using Fightarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Fightarr.Api.Services;

/// <summary>
/// RSS Sync background service - periodically checks indexers for new releases
/// Implements Sonarr/Radarr-style RSS sync for automatic download detection
/// </summary>
public class RssSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RssSyncService> _logger;
    private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(15); // Default RSS sync interval

    public RssSyncService(
        IServiceProvider serviceProvider,
        ILogger<RssSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[RSS Sync] Service started - Sync interval: {Interval} minutes", _syncInterval.TotalMinutes);

        // Wait before starting to allow app to fully initialize
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformRssSyncAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RSS Sync] Error during RSS sync");
            }

            await Task.Delay(_syncInterval, stoppingToken);
        }

        _logger.LogInformation("[RSS Sync] Service stopped");
    }

    private async Task PerformRssSyncAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();
        var indexerSearchService = scope.ServiceProvider.GetRequiredService<IndexerSearchService>();
        var downloadClientService = scope.ServiceProvider.GetRequiredService<DownloadClientService>();
        var delayProfileService = scope.ServiceProvider.GetRequiredService<DelayProfileService>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();

        var config = await configService.GetConfigAsync();

        // Get all RSS-enabled indexers
        var indexers = await db.Indexers
            .Where(i => i.Enabled && i.EnableRss)
            .OrderBy(i => i.Priority)
            .ToListAsync(cancellationToken);

        if (indexers.Count == 0)
        {
            _logger.LogDebug("[RSS Sync] No RSS-enabled indexers configured");
            return;
        }

        _logger.LogInformation("[RSS Sync] Starting RSS sync for {Count} indexers", indexers.Count);

        // Get all monitored events without files (with league for query building)
        var monitoredEvents = await db.Events
            .Include(e => e.League)
            .Where(e => e.Monitored && !e.HasFile)
            .ToListAsync(cancellationToken);

        if (monitoredEvents.Count == 0)
        {
            _logger.LogDebug("[RSS Sync] No monitored events without files");
            return;
        }

        _logger.LogInformation("[RSS Sync] Checking for new releases for {Count} monitored events", monitoredEvents.Count);

        int newDownloadsAdded = 0;
        int releasesProcessed = 0;

        // For each monitored event, check recent releases
        foreach (var evt in monitoredEvents)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // Check if event already has a queued/downloading item
                var existingDownload = await db.DownloadQueue
                    .Where(d => d.EventId == evt.Id &&
                               (d.Status == DownloadStatus.Queued ||
                                d.Status == DownloadStatus.Downloading ||
                                d.Status == DownloadStatus.Completed ||
                                d.Status == DownloadStatus.Importing))
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingDownload != null)
                {
                    _logger.LogDebug("[RSS Sync] Skipping {Title} - already downloading", evt.Title);
                    continue;
                }

                // Build search query
                var searchQuery = BuildSearchQuery(evt);

                // Search all RSS-enabled indexers
                var releases = await indexerSearchService.SearchAllIndexersAsync(searchQuery);

                // Filter out blocklisted releases
                var blocklist = await db.Blocklist
                    .Where(b => b.EventId == evt.Id)
                    .Select(b => b.TorrentInfoHash)
                    .ToListAsync(cancellationToken);

                var filteredReleases = releases
                    .Where(r => string.IsNullOrEmpty(r.TorrentInfoHash) || !blocklist.Contains(r.TorrentInfoHash))
                    .ToList();

                releasesProcessed += filteredReleases.Count;

                if (!filteredReleases.Any())
                {
                    continue;
                }

                // Get event's quality profile (or use default)
                var qualityProfile = evt.QualityProfileId.HasValue
                    ? await db.QualityProfiles.FindAsync(evt.QualityProfileId.Value)
                    : await db.QualityProfiles.OrderBy(q => q.Id).FirstOrDefaultAsync(cancellationToken);

                if (qualityProfile == null)
                {
                    _logger.LogWarning("[RSS Sync] No quality profile available for {Title}", evt.Title);
                    continue;
                }

                // Get delay profile for this event
                var delayProfile = await delayProfileService.GetDelayProfileForEventAsync(evt.Id);
                if (delayProfile == null)
                {
                    delayProfile = new DelayProfile(); // Use defaults
                }

                // Select best release using delay profile and protocol priority
                var bestRelease = delayProfileService.SelectBestReleaseWithDelayProfile(
                    filteredReleases, delayProfile, qualityProfile);

                if (bestRelease == null || !bestRelease.Approved)
                {
                    continue;
                }

                _logger.LogInformation("[RSS Sync] Found new release for {Event}: {Release} from {Indexer}",
                    evt.Title, bestRelease.Title, bestRelease.Indexer);

                // Get preferred download client
                var downloadClient = await db.DownloadClients
                    .Where(dc => dc.Enabled)
                    .OrderBy(dc => dc.Priority)
                    .FirstOrDefaultAsync(cancellationToken);

                if (downloadClient == null)
                {
                    _logger.LogWarning("[RSS Sync] No download client configured");
                    break;
                }

                // Send to download client
                var downloadId = await downloadClientService.AddDownloadAsync(
                    downloadClient,
                    bestRelease.DownloadUrl,
                    downloadClient.Category,
                    bestRelease.Title  // Pass release title for better matching
                );

                if (downloadId == null)
                {
                    _logger.LogError("[RSS Sync] Failed to add to download client: {Client}", downloadClient.Name);
                    continue;
                }

                // Add to download queue
                var queueItem = new DownloadQueueItem
                {
                    EventId = evt.Id,
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

                db.DownloadQueue.Add(queueItem);
                await db.SaveChangesAsync(cancellationToken);

                newDownloadsAdded++;

                _logger.LogInformation("[RSS Sync] Grabbed: {Release} for {Event}", bestRelease.Title, evt.Title);

                // Delay between grabs to avoid overwhelming indexers/download clients
                await Task.Delay(2000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RSS Sync] Error processing event {Title}", evt.Title);
            }
        }

        _logger.LogInformation("[RSS Sync] Completed - Processed {Releases} releases, added {Downloads} new downloads",
            releasesProcessed, newDownloadsAdded);
    }

    private string BuildSearchQuery(Event evt)
    {
        // UNIVERSAL: Build comprehensive search query for all sports
        var queryParts = new List<string> { evt.Title };

        // Add league name if available (UFC, Premier League, NBA, etc.)
        if (evt.League != null)
        {
            queryParts.Add(evt.League.Name);
        }

        // Add year if available
        if (evt.EventDate != default)
        {
            queryParts.Add(evt.EventDate.Year.ToString());
        }

        return string.Join(" ", queryParts.Where(p => !string.IsNullOrEmpty(p)));
    }
}
