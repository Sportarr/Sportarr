using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// RSS Sync background service - Sonarr-style passive discovery
///
/// CRITICAL ARCHITECTURAL CHANGE:
/// - OLD APPROACH: Search per monitored event = N queries per sync (thousands of queries/day)
/// - NEW APPROACH: Fetch RSS feeds once per indexer = M queries per sync (24-100 queries/day)
///
/// How Sonarr/Radarr RSS sync works:
/// 1. Every X minutes (default 15), fetch RSS feeds from all RSS-enabled indexers
/// 2. RSS feeds return the latest 50-100 releases WITHOUT a search query
/// 3. Locally compare those releases against ALL monitored items
/// 4. If a release matches a monitored event, grab it
///
/// This is much more efficient because:
/// - 10 indexers = 10 queries every 15 min = 960 queries/day
/// - vs 100 events * 10 indexers = 1000 queries every 15 min = 96,000 queries/day
/// </summary>
public class RssSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RssSyncService> _logger;

    // Track when we last did a sync for catch-up logic
    private DateTime _lastSyncTime = DateTime.MinValue;

    public RssSyncService(
        IServiceProvider serviceProvider,
        ILogger<RssSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[RSS Sync] Service started - Sonarr-style passive discovery enabled");

        // Wait before starting to allow app to fully initialize
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Load config to get current interval
                using var scope = _serviceProvider.CreateScope();
                var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
                var config = await configService.GetConfigAsync();

                // Validate and clamp interval to safe bounds (Sonarr: min 10, max 120 minutes)
                var intervalMinutes = Math.Clamp(config.RssSyncInterval, 10, 120);
                var syncInterval = TimeSpan.FromMinutes(intervalMinutes);

                _logger.LogInformation("[RSS Sync] Starting RSS sync cycle (interval: {Interval} min)", intervalMinutes);

                await PerformRssSyncAsync(stoppingToken);

                _lastSyncTime = DateTime.UtcNow;

                await Task.Delay(syncInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RSS Sync] Error during RSS sync");
                // Wait 5 minutes before retrying on error
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("[RSS Sync] Service stopped");
    }

    /// <summary>
    /// Perform Sonarr-style RSS sync:
    /// 1. Fetch all RSS feeds (ONE query per indexer)
    /// 2. Match releases locally against monitored events
    /// 3. Grab matching releases
    /// </summary>
    private async Task PerformRssSyncAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var indexerSearchService = scope.ServiceProvider.GetRequiredService<IndexerSearchService>();
        var downloadClientService = scope.ServiceProvider.GetRequiredService<DownloadClientService>();
        var delayProfileService = scope.ServiceProvider.GetRequiredService<DelayProfileService>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
        var partDetector = scope.ServiceProvider.GetRequiredService<EventPartDetector>();
        var releaseMatchingService = scope.ServiceProvider.GetRequiredService<ReleaseMatchingService>();
        var releaseEvaluator = scope.ServiceProvider.GetRequiredService<ReleaseEvaluator>();

        var config = await configService.GetConfigAsync();

        // STEP 1: Fetch RSS feeds from all indexers (ONE query per indexer)
        var allReleases = await indexerSearchService.FetchAllRssFeedsAsync(config.MaxRssReleasesPerIndexer);

        if (!allReleases.Any())
        {
            _logger.LogDebug("[RSS Sync] No releases found in RSS feeds");
            return;
        }

        _logger.LogInformation("[RSS Sync] Fetched {Count} releases from RSS feeds", allReleases.Count);

        // Filter releases by age limit
        var ageLimit = DateTime.UtcNow.AddDays(-config.RssReleaseAgeLimit);
        var recentReleases = allReleases
            .Where(r => r.PublishDate >= ageLimit)
            .ToList();

        _logger.LogDebug("[RSS Sync] {Count} releases within {Days}-day age limit",
            recentReleases.Count, config.RssReleaseAgeLimit);

        // STEP 2: Get all monitored events that need content
        // Include both missing files AND files that might need quality upgrades
        var monitoredEvents = await db.Events
            .Include(e => e.League)
            .Where(e => e.Monitored && e.League != null)
            .ToListAsync(cancellationToken);

        if (!monitoredEvents.Any())
        {
            _logger.LogDebug("[RSS Sync] No monitored events");
            return;
        }

        // Split into missing vs upgrade candidates
        var missingEvents = monitoredEvents.Where(e => !e.HasFile).ToList();
        var upgradeEvents = monitoredEvents.Where(e => e.HasFile).ToList();

        _logger.LogInformation("[RSS Sync] Matching {ReleaseCount} releases against {Missing} missing + {Upgrade} upgrade candidates",
            recentReleases.Count, missingEvents.Count, upgradeEvents.Count);

        int newDownloadsAdded = 0;
        int upgradesFound = 0;

        // Pre-load quality profiles and custom formats for evaluation (like Sonarr does)
        // Note: Specifications is stored as JSON in CustomFormat, not a navigation property, so no Include needed
        var qualityProfiles = await db.QualityProfiles.ToListAsync(cancellationToken);
        var customFormats = await db.CustomFormats.ToListAsync(cancellationToken);

        _logger.LogDebug("[RSS Sync] Loaded {ProfileCount} quality profiles, {FormatCount} custom formats for evaluation",
            qualityProfiles.Count, customFormats.Count);

        // STEP 3: For each release, check if it matches any monitored event
        // This is the inverse of the old approach (per-event search)
        foreach (var release in recentReleases)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // Try to match this release to a monitored event
                var matchedEvent = FindMatchingEvent(release, monitoredEvents, releaseMatchingService, config.EnableMultiPartEpisodes);

                if (matchedEvent == null)
                    continue;

                // SONARR PARITY: Evaluate release against quality profile and custom formats
                // This is the SAME evaluation that manual search uses - identical decision engine
                var qualityProfile = matchedEvent.QualityProfileId.HasValue
                    ? qualityProfiles.FirstOrDefault(p => p.Id == matchedEvent.QualityProfileId.Value)
                    : qualityProfiles.OrderBy(q => q.Id).FirstOrDefault();

                if (qualityProfile != null)
                {
                    var evaluation = releaseEvaluator.EvaluateRelease(
                        release,
                        qualityProfile,
                        customFormats,
                        requestedPart: null, // RSS sync doesn't request specific parts
                        sport: matchedEvent.Sport,
                        enableMultiPartEpisodes: config.EnableMultiPartEpisodes);

                    // Apply evaluation results to release (same as IndexerSearchService does)
                    release.Quality = evaluation.Quality;
                    release.QualityScore = evaluation.QualityScore;
                    release.CustomFormatScore = evaluation.CustomFormatScore;
                    release.Score = evaluation.TotalScore;
                    release.Approved = evaluation.Approved && !evaluation.Rejections.Any();
                    release.Rejections = evaluation.Rejections;

                    _logger.LogDebug("[RSS Sync] Evaluated '{Release}': Quality={Quality} ({QScore}), CF={CScore}, Approved={Approved}",
                        release.Title, release.Quality, release.QualityScore, release.CustomFormatScore, release.Approved);

                    // Skip if evaluation rejected the release
                    if (evaluation.Rejections.Any())
                    {
                        _logger.LogDebug("[RSS Sync] Skipping {Release}: {Rejections}",
                            release.Title, string.Join(", ", evaluation.Rejections));
                        continue;
                    }
                }

                // Check if we should grab this release
                var shouldGrab = await ShouldGrabReleaseAsync(
                    db, matchedEvent, release, config, partDetector, delayProfileService, cancellationToken);

                if (!shouldGrab.Grab)
                {
                    _logger.LogDebug("[RSS Sync] Skipping {Release}: {Reason}", release.Title, shouldGrab.Reason);
                    continue;
                }

                // GRAB IT!
                var grabbed = await GrabReleaseAsync(
                    db, matchedEvent, release, downloadClientService, cancellationToken);

                if (grabbed)
                {
                    if (matchedEvent.HasFile)
                    {
                        upgradesFound++;
                        _logger.LogInformation("[RSS Sync] 🔄 Quality upgrade grabbed: {Release} for {Event}",
                            release.Title, matchedEvent.Title);
                    }
                    else
                    {
                        newDownloadsAdded++;
                        _logger.LogInformation("[RSS Sync] ✓ Grabbed: {Release} for {Event}",
                            release.Title, matchedEvent.Title);
                    }

                    // Rate limiting between grabs
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RSS Sync] Error processing release: {Release}", release.Title);
            }
        }

        _logger.LogInformation("[RSS Sync] Completed - {New} new downloads, {Upgrades} quality upgrades",
            newDownloadsAdded, upgradesFound);
    }

    /// <summary>
    /// Find a monitored event that matches this release
    /// Uses the ReleaseMatchingService for Sonarr-style validation
    /// </summary>
    private Event? FindMatchingEvent(
        ReleaseSearchResult release,
        List<Event> monitoredEvents,
        ReleaseMatchingService matchingService,
        bool enableMultiPartEpisodes)
    {
        // Quick pre-filter: extract potential event identifiers from release title
        var releaseTitle = release.Title.ToLowerInvariant();

        foreach (var evt in monitoredEvents)
        {
            // Quick check: does release title contain key words from event title?
            var eventKeywords = ExtractKeywords(evt.Title);
            if (!eventKeywords.Any(kw => releaseTitle.Contains(kw)))
                continue;

            // Full validation using ReleaseMatchingService
            var matchResult = matchingService.ValidateRelease(release, evt, null, enableMultiPartEpisodes);

            if (matchResult.IsMatch && !matchResult.IsHardRejection)
            {
                _logger.LogDebug("[RSS Sync] Release '{Release}' matches event '{Event}' (confidence: {Confidence}%)",
                    release.Title, evt.Title, matchResult.Confidence);
                return evt;
            }
        }

        return null;
    }

    /// <summary>
    /// Extract searchable keywords from event title
    /// </summary>
    private List<string> ExtractKeywords(string title)
    {
        // Remove common noise words and extract significant terms
        var normalized = title.ToLowerInvariant();

        // Split on non-alphanumeric characters
        var words = Regex.Split(normalized, @"[^a-z0-9]+")
            .Where(w => w.Length >= 2)
            .Where(w => !IsNoiseWord(w))
            .ToList();

        return words;
    }

    private bool IsNoiseWord(string word)
    {
        var noiseWords = new HashSet<string> { "the", "vs", "at", "in", "on", "and", "or", "for" };
        return noiseWords.Contains(word);
    }

    /// <summary>
    /// Check if we should grab this release for the matched event
    /// </summary>
    private async Task<(bool Grab, string Reason)> ShouldGrabReleaseAsync(
        SportarrDbContext db,
        Event evt,
        ReleaseSearchResult release,
        Config config,
        EventPartDetector partDetector,
        DelayProfileService delayProfileService,
        CancellationToken cancellationToken)
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
            return (false, "Already downloading");

        // Check blocklist
        var isBlocklisted = await db.Blocklist
            .AnyAsync(b => b.EventId == evt.Id &&
                          !string.IsNullOrEmpty(release.TorrentInfoHash) &&
                          b.TorrentInfoHash == release.TorrentInfoHash, cancellationToken);

        if (isBlocklisted)
            return (false, "Blocklisted");

        // Check for recent failed downloads with backoff
        var recentFailedDownload = await db.DownloadQueue
            .Where(d => d.EventId == evt.Id && d.Status == DownloadStatus.Failed)
            .OrderByDescending(d => d.LastUpdate)
            .FirstOrDefaultAsync(cancellationToken);

        if (recentFailedDownload != null)
        {
            var retryDelays = new[] { 30, 60, 120, 240, 480 }; // minutes
            var retryCount = recentFailedDownload.RetryCount ?? 0;
            var delayMinutes = retryCount < retryDelays.Length ? retryDelays[retryCount] : retryDelays[^1];
            var nextRetryTime = (recentFailedDownload.LastUpdate ?? DateTime.UtcNow).AddMinutes(delayMinutes);

            if (DateTime.UtcNow < nextRetryTime)
                return (false, $"Backoff until {nextRetryTime:HH:mm}");
        }

        // Fighting sports multi-part handling
        if (EventPartDetector.IsFightingSport(evt.Sport ?? ""))
        {
            var partInfo = partDetector.DetectPart(release.Title, evt.Sport ?? "");

            if (config.EnableMultiPartEpisodes)
            {
                // Multi-part ENABLED: Skip full event files, only download parts
                if (partInfo == null)
                    return (false, "Full event file (multi-part enabled)");

                // Check if this part is monitored
                var monitoredParts = evt.MonitoredParts ?? evt.League?.MonitoredParts;
                if (!string.IsNullOrEmpty(monitoredParts))
                {
                    var partsArray = monitoredParts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!partsArray.Contains(partInfo.SegmentName, StringComparer.OrdinalIgnoreCase))
                        return (false, $"Part '{partInfo.SegmentName}' not monitored");
                }
            }
            else
            {
                // Multi-part DISABLED: Skip part files, only download full event files
                if (partInfo != null)
                    return (false, $"Part file '{partInfo.SegmentName}' (multi-part disabled)");
            }
        }

        // Quality upgrade check
        if (evt.HasFile && !string.IsNullOrEmpty(evt.Quality))
        {
            var existingQualityScore = CalculateQualityScore(evt.Quality);
            var newReleaseScore = release.Score;

            if (newReleaseScore <= existingQualityScore)
                return (false, $"Existing quality sufficient ({evt.Quality})");
        }

        // Check quality profile
        var qualityProfile = evt.QualityProfileId.HasValue
            ? await db.QualityProfiles.FirstOrDefaultAsync(p => p.Id == evt.QualityProfileId.Value, cancellationToken)
            : await db.QualityProfiles.OrderBy(q => q.Id).FirstOrDefaultAsync(cancellationToken);

        if (qualityProfile == null)
            return (false, "No quality profile");

        // Check delay profile
        var delayProfile = await delayProfileService.GetDelayProfileForEventAsync(evt.Id);
        if (delayProfile != null && delayProfile.UsenetDelay > 0 && release.Protocol == "Usenet")
        {
            // Check if we should wait for better release
            // (Simplified - full implementation would track pending releases)
        }

        // Check if release quality is allowed
        if (!release.Approved)
            return (false, "Quality not approved");

        return (true, "OK");
    }

    /// <summary>
    /// Send release to download client and add to queue
    /// </summary>
    private async Task<bool> GrabReleaseAsync(
        SportarrDbContext db,
        Event evt,
        ReleaseSearchResult release,
        DownloadClientService downloadClientService,
        CancellationToken cancellationToken)
    {
        // Get download client that supports this protocol
        var supportedTypes = DownloadClientService.GetClientTypesForProtocol(release.Protocol);

        if (supportedTypes.Count == 0)
        {
            _logger.LogWarning("[RSS Sync] Unknown protocol: {Protocol}", release.Protocol);
            return false;
        }

        var downloadClient = await db.DownloadClients
            .Where(dc => dc.Enabled && supportedTypes.Contains(dc.Type))
            .OrderBy(dc => dc.Priority)
            .FirstOrDefaultAsync(cancellationToken);

        if (downloadClient == null)
        {
            _logger.LogWarning("[RSS Sync] No {Protocol} download client for {Event}", release.Protocol, evt.Title);
            return false;
        }

        // Send to download client
        var downloadId = await downloadClientService.AddDownloadAsync(
            downloadClient,
            release.DownloadUrl,
            downloadClient.Category,
            release.Title
        );

        if (downloadId == null)
        {
            _logger.LogError("[RSS Sync] Failed to add to download client: {Client}", downloadClient.Name);
            return false;
        }

        // Add to download queue
        var queueItem = new DownloadQueueItem
        {
            EventId = evt.Id,
            Title = release.Title,
            DownloadId = downloadId,
            DownloadClientId = downloadClient.Id,
            Status = DownloadStatus.Queued,
            Quality = release.Quality,
            Codec = release.Codec,
            Source = release.Source,
            Size = release.Size,
            Downloaded = 0,
            Progress = 0,
            Indexer = release.Indexer,
            Protocol = release.Protocol,
            TorrentInfoHash = release.TorrentInfoHash,
            RetryCount = 0,
            LastUpdate = DateTime.UtcNow,
            QualityScore = release.QualityScore,
            CustomFormatScore = release.CustomFormatScore
        };

        db.DownloadQueue.Add(queueItem);

        // Save grab history for potential re-grabbing (Sportarr-exclusive feature)
        // This allows users to re-download the exact same release if they lose their media files
        var indexerRecord = await db.Indexers
            .FirstOrDefaultAsync(i => i.Name == release.Indexer, cancellationToken);

        // Detect part for this release (for fighting sports)
        string? partName = null;
        if (EventPartDetector.IsFightingSport(evt.Sport ?? ""))
        {
            var partDetector = new EventPartDetector(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EventPartDetector>.Instance);
            var partInfo = partDetector.DetectPart(release.Title, evt.Sport ?? "");
            partName = partInfo?.SegmentName;
        }

        // Mark any previous grabs for the same event+part as superseded
        // This prevents users from re-grabbing an old file that was replaced
        var previousGrabs = await db.GrabHistory
            .Where(g => g.EventId == evt.Id && g.PartName == partName && !g.Superseded)
            .ToListAsync(cancellationToken);
        foreach (var oldGrab in previousGrabs)
        {
            oldGrab.Superseded = true;
            _logger.LogDebug("[RSS Sync] Marked previous grab as superseded: {Title}", oldGrab.Title);
        }

        var grabHistory = new GrabHistory
        {
            EventId = evt.Id,
            Title = release.Title,
            Indexer = release.Indexer,
            IndexerId = indexerRecord?.Id,
            DownloadUrl = release.DownloadUrl,
            Guid = release.Guid,
            Protocol = release.Protocol,
            TorrentInfoHash = release.TorrentInfoHash,
            Size = release.Size,
            Quality = release.Quality,
            Codec = release.Codec,
            Source = release.Source,
            QualityScore = release.QualityScore,
            CustomFormatScore = release.CustomFormatScore,
            PartName = partName,
            GrabbedAt = DateTime.UtcNow,
            DownloadClientId = downloadClient.Id
        };
        db.GrabHistory.Add(grabHistory);

        await db.SaveChangesAsync(cancellationToken);

        return true;
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
