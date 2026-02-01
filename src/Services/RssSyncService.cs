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
        var releaseProfileService = scope.ServiceProvider.GetRequiredService<ReleaseProfileService>();

        var config = await configService.GetConfigAsync();

        // STEP 1: Fetch RSS feeds from all indexers (ONE query per indexer)
        var allReleases = await indexerSearchService.FetchAllRssFeedsAsync(config.MaxRssReleasesPerIndexer);

        if (!allReleases.Any())
        {
            _logger.LogDebug("[RSS Sync] No releases found in RSS feeds");
            return;
        }

        _logger.LogInformation("[RSS Sync] Fetched {Count} releases from RSS feeds", allReleases.Count);

        // Filter releases by age limit (use the more restrictive of RSS age limit and indexer retention)
        var rssAgeLimit = config.RssReleaseAgeLimit;
        var indexerRetention = config.IndexerRetention;
        var effectiveAgeLimit = indexerRetention > 0
            ? Math.Min(rssAgeLimit, indexerRetention)
            : rssAgeLimit;
        var ageCutoff = DateTime.UtcNow.AddDays(-effectiveAgeLimit);
        var recentReleases = allReleases
            .Where(r => r.PublishDate >= ageCutoff)
            .ToList();

        _logger.LogDebug("[RSS Sync] {Count} releases within {Days}-day age limit (RSS limit: {RssLimit}, Indexer retention: {Retention})",
            recentReleases.Count, effectiveAgeLimit, rssAgeLimit, indexerRetention > 0 ? indexerRetention : "disabled");

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

        // Pre-load quality profiles, custom formats, and release profiles for evaluation (like Sonarr does)
        // Note: Specifications is stored as JSON in CustomFormat, not a navigation property, so no Include needed
        var qualityProfiles = await db.QualityProfiles.ToListAsync(cancellationToken);
        var customFormats = await db.CustomFormats.ToListAsync(cancellationToken);
        var releaseProfiles = await releaseProfileService.LoadReleaseProfilesAsync();

        _logger.LogDebug("[RSS Sync] Loaded {ProfileCount} quality profiles, {FormatCount} custom formats, {ReleaseProfileCount} release profiles for evaluation",
            qualityProfiles.Count, customFormats.Count, releaseProfiles.Count);

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

                    // Apply release profile filtering (Required/Ignored keywords, Preferred score)
                    if (releaseProfiles.Any())
                    {
                        var profileEval = releaseProfileService.EvaluateRelease(release, releaseProfiles);

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

                    _logger.LogDebug("[RSS Sync] Evaluated '{Release}': Quality={Quality} ({QScore}), CF={CScore}, Approved={Approved}",
                        release.Title, release.Quality, release.QualityScore, release.CustomFormatScore, release.Approved);

                    // Skip if evaluation rejected the release
                    if (release.Rejections.Any())
                    {
                        _logger.LogDebug("[RSS Sync] Skipping {Release}: {Rejections}",
                            release.Title, string.Join(", ", release.Rejections));
                        continue;
                    }
                }

                // Check if we should grab this release (now returns part info too)
                var shouldGrab = await ShouldGrabReleaseAsync(
                    db, matchedEvent, release, config, partDetector, delayProfileService, downloadClientService, cancellationToken);

                if (!shouldGrab.Grab)
                {
                    _logger.LogDebug("[RSS Sync] Skipping {Release}: {Reason}", release.Title, shouldGrab.Reason);
                    continue;
                }

                // GRAB IT! (pass the detected part)
                var grabbed = await GrabReleaseAsync(
                    db, matchedEvent, release, downloadClientService, shouldGrab.ReleasePart, cancellationToken);

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
    /// Check if we should grab this release for the matched event.
    /// Now part-aware and uses total score (QualityScore + CustomFormatScore) for comparisons.
    /// Can upgrade queued items if a higher-scored release is found.
    /// </summary>
    private async Task<(bool Grab, string Reason, string? ReleasePart)> ShouldGrabReleaseAsync(
        SportarrDbContext db,
        Event evt,
        ReleaseSearchResult release,
        Config config,
        EventPartDetector partDetector,
        DelayProfileService delayProfileService,
        DownloadClientService downloadClientService,
        CancellationToken cancellationToken)
    {
        // 1. Detect part FIRST (for fighting sports) - needed for all subsequent checks
        string? releasePart = null;
        if (EventPartDetector.IsFightingSport(evt.Sport ?? ""))
        {
            var partInfo = partDetector.DetectPart(release.Title, evt.Sport ?? "");

            if (config.EnableMultiPartEpisodes)
            {
                // Multi-part ENABLED: Skip full event files, only download parts
                if (partInfo == null)
                    return (false, "Full event file (multi-part enabled)", null);

                releasePart = partInfo.SegmentName;

                // Check if this part is monitored
                var monitoredParts = evt.MonitoredParts ?? evt.League?.MonitoredParts;
                if (!string.IsNullOrEmpty(monitoredParts))
                {
                    var partsArray = monitoredParts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!partsArray.Contains(releasePart, StringComparer.OrdinalIgnoreCase))
                        return (false, $"Part '{releasePart}' not monitored", null);
                }
            }
            else
            {
                // Multi-part DISABLED: Skip part files, only download full event files
                if (partInfo != null)
                    return (false, $"Part file '{partInfo.SegmentName}' (multi-part disabled)", null);
            }
        }

        // 2. Check if already in queue (PART-AWARE) - with upgrade logic
        var existingQueueItem = await db.DownloadQueue
            .Where(d => d.EventId == evt.Id &&
                       (d.Status == DownloadStatus.Queued ||
                        d.Status == DownloadStatus.Downloading))
            .Where(d => releasePart == null ? d.Part == null : d.Part == releasePart)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingQueueItem != null)
        {
            // Calculate total scores for comparison (QualityScore + CustomFormatScore)
            var existingTotalScore = existingQueueItem.QualityScore + existingQueueItem.CustomFormatScore;
            var newTotalScore = release.QualityScore + release.CustomFormatScore;

            if (newTotalScore > existingTotalScore)
            {
                // New release is better - remove old one and allow new grab
                await RemoveAndCancelQueueItemAsync(db, existingQueueItem, downloadClientService, cancellationToken);
                _logger.LogInformation("[RSS Sync] Replacing queued item with better release: {OldScore} -> {NewScore} for {Part}",
                    existingTotalScore, newTotalScore, releasePart ?? "full event");
            }
            else
            {
                return (false, $"Better or equal release already queued (score: {existingTotalScore})", releasePart);
            }
        }

        // Also check for items being imported (don't replace those)
        var importingItem = await db.DownloadQueue
            .Where(d => d.EventId == evt.Id &&
                       (d.Status == DownloadStatus.Completed ||
                        d.Status == DownloadStatus.Importing))
            .Where(d => releasePart == null ? d.Part == null : d.Part == releasePart)
            .FirstOrDefaultAsync(cancellationToken);

        if (importingItem != null)
            return (false, $"Already importing ({releasePart ?? "full event"})", releasePart);

        // 3. Check blocklist - supports both torrent (by hash) and Usenet (by title+indexer)
        bool isBlocklisted = false;

        if (!string.IsNullOrEmpty(release.TorrentInfoHash))
        {
            // Torrent: check by info hash
            isBlocklisted = await db.Blocklist
                .AnyAsync(b => b.TorrentInfoHash == release.TorrentInfoHash, cancellationToken);
        }
        else if (release.Protocol == "Usenet")
        {
            // Usenet: check by title + indexer combination
            isBlocklisted = await db.Blocklist
                .AnyAsync(b => b.Title == release.Title &&
                              b.Indexer == release.Indexer &&
                              (b.Protocol == "Usenet" || string.IsNullOrEmpty(b.TorrentInfoHash)), cancellationToken);
        }

        if (isBlocklisted)
            return (false, "Blocklisted", releasePart);

        // 4. Check for recent failed downloads with backoff (part-aware)
        var recentFailedDownload = await db.DownloadQueue
            .Where(d => d.EventId == evt.Id && d.Status == DownloadStatus.Failed)
            .Where(d => releasePart == null ? d.Part == null : d.Part == releasePart)
            .OrderByDescending(d => d.LastUpdate)
            .FirstOrDefaultAsync(cancellationToken);

        if (recentFailedDownload != null)
        {
            var retryDelays = new[] { 30, 60, 120, 240, 480 }; // minutes
            var retryCount = recentFailedDownload.RetryCount ?? 0;
            var delayMinutes = retryCount < retryDelays.Length ? retryDelays[retryCount] : retryDelays[^1];
            var nextRetryTime = (recentFailedDownload.LastUpdate ?? DateTime.UtcNow).AddMinutes(delayMinutes);

            if (DateTime.UtcNow < nextRetryTime)
                return (false, $"Backoff until {nextRetryTime:HH:mm}", releasePart);
        }

        // 5. Check existing files (PART-AWARE, SCORE-BASED)
        await db.Entry(evt).Collection(e => e.Files).LoadAsync(cancellationToken);
        var existingFile = releasePart != null
            ? evt.Files.FirstOrDefault(f => f.PartName == releasePart && f.Exists)
            : evt.Files.FirstOrDefault(f => f.PartName == null && f.Exists);

        if (existingFile != null)
        {
            // Use total score (QualityScore + CustomFormatScore) for comparison
            var existingTotalScore = existingFile.QualityScore + existingFile.CustomFormatScore;
            var newTotalScore = release.QualityScore + release.CustomFormatScore;

            if (newTotalScore <= existingTotalScore)
            {
                return (false, $"Existing file has same or better score ({existingTotalScore})", releasePart);
            }
            _logger.LogInformation("[RSS Sync] File upgrade detected: {OldScore} -> {NewScore} for {Part}",
                existingTotalScore, newTotalScore, releasePart ?? "full event");
        }

        // 5b. CASCADING UPGRADE: When downloading a higher quality part, search for other parts at the new quality
        // This ensures all parts of a multi-part event have consistent quality for Plex compatibility
        if (releasePart != null && config.EnableMultiPartEpisodes)
        {
            // Check if other parts exist with LOWER quality than this release
            var otherPartFiles = evt.Files
                .Where(f => f.PartName != null && f.PartName != releasePart && f.Exists)
                .ToList();

            if (otherPartFiles.Any())
            {
                var newResolution = ExtractResolution(release.Quality);
                var newTotalScore = release.QualityScore + release.CustomFormatScore;

                // Find parts that need upgrading to match the new release quality
                var partsNeedingUpgrade = otherPartFiles
                    .Where(f =>
                    {
                        var existingScore = f.QualityScore + f.CustomFormatScore;
                        return newTotalScore > existingScore;
                    })
                    .Select(f => f.PartName!)
                    .ToList();

                if (partsNeedingUpgrade.Any())
                {
                    _logger.LogInformation(
                        "[RSS Sync] Cascading upgrade: Found {Part} at {Quality}, triggering search for {Count} other parts: {Parts}",
                        releasePart, release.Quality, partsNeedingUpgrade.Count, string.Join(", ", partsNeedingUpgrade));

                    // Trigger immediate searches for other parts at the new quality (fire-and-forget)
                    _ = TriggerCascadingPartSearchesAsync(evt, partsNeedingUpgrade, release.Quality ?? "Unknown", newResolution);
                }
            }
        }

        // 6. Check quality profile
        var qualityProfile = evt.QualityProfileId.HasValue
            ? await db.QualityProfiles.FirstOrDefaultAsync(p => p.Id == evt.QualityProfileId.Value, cancellationToken)
            : await db.QualityProfiles.OrderBy(q => q.Id).FirstOrDefaultAsync(cancellationToken);

        if (qualityProfile == null)
            return (false, "No quality profile", releasePart);

        // 7. Check delay profile
        var delayProfile = await delayProfileService.GetDelayProfileForEventAsync(evt.Id);
        if (delayProfile != null && delayProfile.UsenetDelay > 0 && release.Protocol == "Usenet")
        {
            // Check if we should wait for better release
            // (Simplified - full implementation would track pending releases)
        }

        // 8. Check if release quality is allowed
        if (!release.Approved)
            return (false, "Quality not approved", releasePart);

        return (true, "OK", releasePart);
    }

    /// <summary>
    /// Remove a queue item and cancel its download in the download client.
    /// Used when a higher-scored release is found to replace a queued item.
    /// </summary>
    private async Task RemoveAndCancelQueueItemAsync(
        SportarrDbContext db,
        DownloadQueueItem queueItem,
        DownloadClientService downloadClientService,
        CancellationToken cancellationToken)
    {
        // Get download client to cancel the download
        var downloadClient = await db.DownloadClients
            .FirstOrDefaultAsync(dc => dc.Id == queueItem.DownloadClientId, cancellationToken);

        if (downloadClient != null && !string.IsNullOrEmpty(queueItem.DownloadId))
        {
            try
            {
                await downloadClientService.RemoveDownloadAsync(downloadClient, queueItem.DownloadId, deleteFiles: true);
                _logger.LogInformation("[RSS Sync] Cancelled download {DownloadId} to upgrade to better release",
                    queueItem.DownloadId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RSS Sync] Failed to cancel download {DownloadId}, proceeding anyway",
                    queueItem.DownloadId);
            }
        }

        // Remove from queue
        db.DownloadQueue.Remove(queueItem);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Send release to download client and add to queue
    /// </summary>
    private async Task<bool> GrabReleaseAsync(
        SportarrDbContext db,
        Event evt,
        ReleaseSearchResult release,
        DownloadClientService downloadClientService,
        string? releasePart,
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
            CustomFormatScore = release.CustomFormatScore,
            Part = releasePart  // Use the part passed from ShouldGrabReleaseAsync
        };

        db.DownloadQueue.Add(queueItem);

        // Save grab history for potential re-grabbing (Sportarr-exclusive feature)
        // This allows users to re-download the exact same release if they lose their media files
        var indexerRecord = await db.Indexers
            .FirstOrDefaultAsync(i => i.Name == release.Indexer, cancellationToken);

        // Use the releasePart passed from ShouldGrabReleaseAsync (no need to re-detect)
        var partName = releasePart;

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

    #region Cascading Part Upgrade Helpers

    // Track active cascading searches to prevent circular triggers
    private static readonly HashSet<string> _activeCascadeSearches = new();
    private static readonly object _cascadeLock = new();

    /// <summary>
    /// Extract resolution from quality string (e.g., "HDTV-1080p" -> "1080p")
    /// </summary>
    private static string? ExtractResolution(string? quality)
    {
        if (string.IsNullOrEmpty(quality)) return null;

        var resolutions = new[] { "2160p", "1080p", "720p", "576p", "540p", "480p", "360p" };
        foreach (var res in resolutions)
        {
            if (quality.Contains(res, StringComparison.OrdinalIgnoreCase))
                return res;
        }
        return null;
    }

    /// <summary>
    /// Extract source/quality group from quality string (e.g., "WEBDL-1080p" -> "WEB")
    /// </summary>
    private static string? ExtractQualityGroup(string? quality)
    {
        if (string.IsNullOrEmpty(quality)) return null;

        var upperQuality = quality.ToUpperInvariant();
        if (upperQuality.Contains("WEBDL") || upperQuality.Contains("WEB-DL") || upperQuality.Contains("WEBRIP"))
            return "WEB";
        if (upperQuality.Contains("BLURAY") || upperQuality.Contains("BLU-RAY") || upperQuality.Contains("BDRIP"))
            return "BLURAY";
        if (upperQuality.Contains("HDTV"))
            return "HDTV";
        if (upperQuality.Contains("DVD"))
            return "DVD";
        return null;
    }

    /// <summary>
    /// Trigger searches for other parts when a higher quality release is found.
    /// Runs in background (fire-and-forget) to not block RSS sync.
    /// </summary>
    private async Task TriggerCascadingPartSearchesAsync(
        Event evt,
        List<string> partsToSearch,
        string targetQuality,
        string? targetResolution)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var autoSearchService = scope.ServiceProvider.GetRequiredService<AutomaticSearchService>();

            foreach (var partName in partsToSearch)
            {
                // Check for circular cascade
                var cascadeKey = $"{evt.Id}_{partName}_{targetResolution}";
                lock (_cascadeLock)
                {
                    if (_activeCascadeSearches.Contains(cascadeKey))
                    {
                        _logger.LogDebug("[Cascading Upgrade] Skipping {Part} - cascade already in progress", partName);
                        continue;
                    }
                    _activeCascadeSearches.Add(cascadeKey);
                }

                try
                {
                    _logger.LogDebug("[Cascading Upgrade] Searching for {Event} - {Part} at {Quality}",
                        evt.Title, partName, targetQuality);

                    // Use AutomaticSearchService which already has part-aware search with quality consistency
                    var result = await autoSearchService.SearchAndDownloadEventAsync(
                        evt.Id,
                        qualityProfileId: null,
                        part: partName,
                        isManualSearch: false);

                    if (result.Success && !string.IsNullOrEmpty(result.DownloadId))
                    {
                        _logger.LogInformation("[Cascading Upgrade] Successfully grabbed {Event} - {Part}",
                            evt.Title, partName);
                    }
                    else
                    {
                        _logger.LogDebug("[Cascading Upgrade] No suitable release found for {Event} - {Part}: {Reason}",
                            evt.Title, partName, result.Message);
                    }

                    // Rate limiting between cascading searches
                    await Task.Delay(2000);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Cascading Upgrade] Failed to search for {Event} - {Part}",
                        evt.Title, partName);
                }
                finally
                {
                    lock (_cascadeLock)
                    {
                        _activeCascadeSearches.Remove(cascadeKey);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Cascading Upgrade] Error during cascading search for {Event}", evt.Title);
        }
    }

    #endregion
}
