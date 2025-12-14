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

        // First, clean up stale pending imports (files that no longer exist)
        await CleanupStalePendingImportsAsync(downloadClients);

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
    /// Clean up pending imports where the file no longer exists in the download client
    /// This handles the case when a user removes a file from their download client
    /// </summary>
    private async Task CleanupStalePendingImportsAsync(List<DownloadClient> downloadClients)
    {
        try
        {
            // Get all pending imports that haven't been rejected
            var pendingImports = await _db.PendingImports
                .Where(pi => pi.Status == PendingImportStatus.Pending)
                .ToListAsync();

            if (!pendingImports.Any())
            {
                return;
            }

            _logger.LogDebug("[External Download Scanner] Checking {Count} pending imports for stale entries", pendingImports.Count);

            // Group by download client
            var importsByClient = pendingImports.GroupBy(pi => pi.DownloadClientId).ToList();
            var staleImports = new List<PendingImport>();

            foreach (var group in importsByClient)
            {
                var client = downloadClients.FirstOrDefault(dc => dc.Id == group.Key);
                if (client == null)
                {
                    // Client no longer exists, mark all its imports as stale
                    staleImports.AddRange(group);
                    continue;
                }

                try
                {
                    // Get current downloads from the client
                    var currentDownloads = await _downloadClientService.GetCompletedDownloadsAsync(client, client.Category);
                    var currentDownloadIds = currentDownloads.Select(d => d.DownloadId).ToHashSet();
                    var currentTitles = currentDownloads.Select(d => d.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // Check each pending import for this client
                    foreach (var import in group)
                    {
                        // Check if the download still exists by ID or title
                        var stillExists = currentDownloadIds.Contains(import.DownloadId) ||
                                         currentTitles.Contains(import.Title);

                        if (!stillExists)
                        {
                            // Also check if file exists on disk (for cases where download was moved/renamed)
                            var fileExists = !string.IsNullOrEmpty(import.FilePath) &&
                                            (File.Exists(import.FilePath) || Directory.Exists(import.FilePath));

                            if (!fileExists)
                            {
                                _logger.LogInformation("[External Download Scanner] Marking stale pending import (file removed): {Title}", import.Title);
                                staleImports.Add(import);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[External Download Scanner] Error checking client {Name} for stale imports, skipping", client.Name);
                }
            }

            // Remove stale imports
            if (staleImports.Any())
            {
                _logger.LogInformation("[External Download Scanner] Removing {Count} stale pending imports", staleImports.Count);
                _db.PendingImports.RemoveRange(staleImports);
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[External Download Scanner] Error cleaning up stale pending imports");
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
