using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Scans download clients for external downloads (not added by Sportarr) in the sports category
/// Creates PendingImport records for files that need manual intervention
/// Similar to Sonarr's download scanning and manual import queue
/// </summary>
public class ExternalDownloadScanner
{
    private readonly SportarrDbContext _db;
    private readonly DownloadClientService _downloadClientService;
    private readonly ImportMatchingService _matchingService;
    private readonly ILogger<ExternalDownloadScanner> _logger;

    public ExternalDownloadScanner(
        SportarrDbContext db,
        DownloadClientService downloadClientService,
        ImportMatchingService matchingService,
        ILogger<ExternalDownloadScanner> logger)
    {
        _db = db;
        _downloadClientService = downloadClientService;
        _matchingService = matchingService;
        _logger = logger;
    }

    /// <summary>
    /// Scan all enabled download clients for external completed downloads in sports category
    /// </summary>
    public async Task ScanForExternalDownloadsAsync()
    {
        var downloadClients = await _db.DownloadClients
            .Where(dc => dc.Enabled)
            .ToListAsync();

        if (!downloadClients.Any())
        {
            _logger.LogDebug("[External Download Scanner] No enabled download clients");
            return;
        }

        _logger.LogInformation("[External Download Scanner] Scanning {Count} download clients for external downloads", downloadClients.Count);

        foreach (var client in downloadClients)
        {
            try
            {
                await ScanDownloadClientAsync(client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[External Download Scanner] Error scanning client {Name}", client.Name);
            }
        }
    }

    /// <summary>
    /// Scan a single download client for external downloads
    /// </summary>
    private async Task ScanDownloadClientAsync(DownloadClient client)
    {
        _logger.LogDebug("[External Download Scanner] Scanning {Name} for category '{Category}'", client.Name, client.Category);

        // Get ALL completed downloads in the sports category from the client
        var completedDownloads = await _downloadClientService.GetCompletedDownloadsAsync(client, client.Category);

        if (!completedDownloads.Any())
        {
            _logger.LogDebug("[External Download Scanner] No completed downloads in category '{Category}'", client.Category);
            return;
        }

        _logger.LogInformation("[External Download Scanner] Found {Count} completed downloads in {Client}",
            completedDownloads.Count, client.Name);

        foreach (var download in completedDownloads)
        {
            try
            {
                await ProcessExternalDownloadAsync(client, download);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[External Download Scanner] Error processing download {Title}", download.Title);
            }
        }
    }

    /// <summary>
    /// Process a single external download - check if it's already tracked, if not create PendingImport
    /// </summary>
    private async Task ProcessExternalDownloadAsync(DownloadClient client, ExternalDownloadInfo download)
    {
        // Check if this download was added by Sportarr (exists in DownloadQueue)
        // We check by DownloadId first (normal case), then by title match (Decypharr compatibility)
        // Decypharr and other debrid proxies may change the download ID/hash between add and completion
        var existingInQueue = await _db.DownloadQueue
            .AnyAsync(dq => dq.DownloadId == download.DownloadId && dq.DownloadClientId == client.Id);

        if (existingInQueue)
        {
            // This is a Sportarr-added download, skip it
            _logger.LogDebug("[External Download Scanner] Skipping Sportarr-added download (ID match): {Title}", download.Title);
            return;
        }

        // Decypharr compatibility: Check by title match for active/recent downloads
        // Debrid proxies like Decypharr may report different hashes than what was originally stored
        var existingByTitle = await _db.DownloadQueue
            .Where(dq => dq.DownloadClientId == client.Id &&
                        dq.Status != DownloadStatus.Imported &&
                        dq.Status != DownloadStatus.Failed)
            .AnyAsync(dq => dq.Title == download.Title ||
                           EF.Functions.Like(dq.Title, "%" + download.Title + "%") ||
                           EF.Functions.Like(download.Title, "%" + dq.Title + "%"));

        if (existingByTitle)
        {
            _logger.LogDebug("[External Download Scanner] Skipping Sportarr-added download (title match, debrid proxy compatibility): {Title}", download.Title);
            return;
        }

        // Check if we've already created a PendingImport for this
        var existingPending = await _db.PendingImports
            .AnyAsync(pi => (pi.DownloadId == download.DownloadId || pi.Title == download.Title) &&
                           pi.DownloadClientId == client.Id &&
                           pi.Status != PendingImportStatus.Rejected);

        if (existingPending)
        {
            // Already tracking this one
            _logger.LogDebug("[External Download Scanner] Already tracking: {Title}", download.Title);
            return;
        }

        _logger.LogInformation("[External Download Scanner] New external download detected: {Title}", download.Title);

        // Try to automatically match to an event
        var suggestion = await _matchingService.FindBestMatchAsync(download.Title, download.FilePath);

        // Create PendingImport record
        var pendingImport = new PendingImport
        {
            DownloadClientId = client.Id,
            DownloadId = download.DownloadId,
            Title = download.Title,
            FilePath = download.FilePath,
            Size = download.Size,
            Quality = suggestion?.Quality,
            QualityScore = suggestion?.QualityScore ?? 0,
            Status = PendingImportStatus.Pending,
            SuggestedEventId = suggestion?.EventId,
            SuggestedPart = suggestion?.Part,
            SuggestionConfidence = suggestion?.Confidence ?? 0,
            Protocol = download.Protocol,
            TorrentInfoHash = download.TorrentInfoHash,
            Detected = DateTime.UtcNow
        };

        _db.PendingImports.Add(pendingImport);
        await _db.SaveChangesAsync();

        if (suggestion != null && suggestion.Confidence >= 80)
        {
            _logger.LogInformation("[External Download Scanner] High-confidence match ({Confidence}%) for {Title} → {Event}",
                suggestion.Confidence, download.Title, suggestion.EventTitle);
        }
        else
        {
            _logger.LogWarning("[External Download Scanner] ⚠ Manual intervention required for: {Title} (confidence: {Confidence}%)",
                download.Title, suggestion?.Confidence ?? 0);
        }
    }
}
