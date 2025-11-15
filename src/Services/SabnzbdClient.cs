using System.Net.Http.Json;
using System.Text.Json;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// SABnzbd API client for Sportarr
/// Implements SABnzbd HTTP API for NZB downloads
/// </summary>
public class SabnzbdClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SabnzbdClient> _logger;

    public SabnzbdClient(HttpClient httpClient, ILogger<SabnzbdClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Test connection to SABnzbd
    /// </summary>
    public async Task<bool> TestConnectionAsync(DownloadClient config)
    {
        try
        {
            var response = await SendApiRequestAsync(config, "?mode=version&output=json");
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SABnzbd] Connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Add NZB from URL
    /// </summary>
    public async Task<string?> AddNzbAsync(DownloadClient config, string nzbUrl, string category)
    {
        try
        {
            var url = $"?mode=addurl&name={Uri.EscapeDataString(nzbUrl)}&cat={Uri.EscapeDataString(category)}&apikey={config.ApiKey}&output=json";
            var response = await SendApiRequestAsync(config, url);

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("nzo_ids", out var ids) &&
                    ids.GetArrayLength() > 0)
                {
                    var nzoId = ids[0].GetString();
                    _logger.LogInformation("[SABnzbd] NZB added: {NzoId}", nzoId);
                    return nzoId;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SABnzbd] Error adding NZB");
            return null;
        }
    }

    /// <summary>
    /// Get queue status
    /// </summary>
    public async Task<List<SabnzbdItem>?> GetQueueAsync(DownloadClient config)
    {
        try
        {
            var response = await SendApiRequestAsync(config, "?mode=queue&output=json");

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("queue", out var queue) &&
                    queue.TryGetProperty("slots", out var slots))
                {
                    return JsonSerializer.Deserialize<List<SabnzbdItem>>(slots.GetRawText());
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SABnzbd] Error getting queue");
            return null;
        }
    }

    /// <summary>
    /// Get history (with expanded limit for better progress tracking)
    /// </summary>
    public async Task<List<SabnzbdHistoryItem>?> GetHistoryAsync(DownloadClient config)
    {
        try
        {
            // Request last 100 items instead of default (10-20) to ensure we find recent downloads
            // This matches Sonarr/Radarr behavior for reliable progress tracking
            var response = await SendApiRequestAsync(config, "?mode=history&limit=100&output=json");

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("history", out var history) &&
                    history.TryGetProperty("slots", out var slots))
                {
                    return JsonSerializer.Deserialize<List<SabnzbdHistoryItem>>(slots.GetRawText());
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SABnzbd] Error getting history");
            return null;
        }
    }

    /// <summary>
    /// Pause download
    /// </summary>
    public async Task<bool> PauseDownloadAsync(DownloadClient config, string nzoId)
    {
        try
        {
            var response = await SendApiRequestAsync(config, $"?mode=queue&name=pause&value={nzoId}");
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SABnzbd] Error pausing download");
            return false;
        }
    }

    /// <summary>
    /// Resume download
    /// </summary>
    public async Task<bool> ResumeDownloadAsync(DownloadClient config, string nzoId)
    {
        try
        {
            var response = await SendApiRequestAsync(config, $"?mode=queue&name=resume&value={nzoId}");
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SABnzbd] Error resuming download");
            return false;
        }
    }

    /// <summary>
    /// Get download status for monitoring
    /// </summary>
    public async Task<DownloadClientStatus?> GetDownloadStatusAsync(DownloadClient config, string nzoId)
    {
        try
        {
            _logger.LogInformation("[SABnzbd] GetDownloadStatusAsync: Looking for NZO ID: {NzoId}", nzoId);

            // First check queue
            var queue = await GetQueueAsync(config);
            _logger.LogInformation("[SABnzbd] Queue contains {Count} items", queue?.Count ?? 0);
            if (queue != null && queue.Count > 0)
            {
                _logger.LogInformation("[SABnzbd] Queue NZO IDs: {Ids}", string.Join(", ", queue.Select(q => q.nzo_id)));
            }
            var queueItem = queue?.FirstOrDefault(q => q.nzo_id == nzoId);

            if (queueItem != null)
            {
                _logger.LogInformation("[SABnzbd] Found download in queue: {NzoId}, Status: {Status}, Progress: {Progress}%",
                    nzoId, queueItem.status, queueItem.percentage);

                var status = queueItem.status.ToLowerInvariant() switch
                {
                    "downloading" => "downloading",
                    "paused" => "paused",
                    "queued" => "queued",
                    _ => "downloading"
                };

                // Parse percentage
                var progress = 0.0;
                if (double.TryParse(queueItem.percentage, out var pct))
                {
                    progress = pct;
                }

                // Calculate downloaded size
                var totalMb = queueItem.mb;
                var remainingMb = queueItem.mbleft;
                var downloadedMb = totalMb - remainingMb;

                return new DownloadClientStatus
                {
                    Status = status,
                    Progress = progress,
                    Downloaded = downloadedMb * 1024 * 1024, // Convert MB to bytes
                    Size = totalMb * 1024 * 1024,
                    TimeRemaining = null, // SABnzbd timeleft is string format, would need parsing
                    SavePath = null // Not available in queue data
                };
            }

            // If not in queue, check history
            var history = await GetHistoryAsync(config);
            _logger.LogInformation("[SABnzbd] History contains {Count} items", history?.Count ?? 0);
            if (history != null && history.Count > 0)
            {
                _logger.LogInformation("[SABnzbd] History NZO IDs (first 10): {Ids}",
                    string.Join(", ", history.Take(10).Select(h => h.nzo_id)));
            }
            var historyItem = history?.FirstOrDefault(h => h.nzo_id == nzoId);

            if (historyItem != null)
            {
                _logger.LogInformation("[SABnzbd] Found download in history: {NzoId}, Status: {Status}, FailMessage: {FailMessage}",
                    nzoId, historyItem.status, historyItem.fail_message ?? "none");

                var reportedStatus = historyItem.status.ToLowerInvariant();
                var status = "completed";
                string? errorMessage = null;

                // Handle failed downloads - distinguish between download failures and post-processing failures
                if (reportedStatus == "failed")
                {
                    var failMessage = historyItem.fail_message?.ToLowerInvariant() ?? "";

                    // Post-processing script failures should not prevent import (Sonarr/Radarr behavior)
                    // SABnzbd marks download as "failed" even if download succeeded but post-processing script failed
                    var isPostProcessingFailure =
                        failMessage.Contains("post") ||
                        failMessage.Contains("script") ||
                        failMessage.Contains("aborted") ||
                        failMessage.Contains("moving failed") ||
                        failMessage.Contains("unpacking failed");

                    if (isPostProcessingFailure)
                    {
                        // Download succeeded, only post-processing failed - treat as warning, not failure
                        _logger.LogWarning("[SABnzbd] Download {NzoId} completed but post-processing failed: {FailMessage}. Will attempt import anyway.",
                            nzoId, historyItem.fail_message);
                        status = "completed"; // Override to completed so import can proceed
                        errorMessage = $"Post-processing warning: {historyItem.fail_message}";
                    }
                    else
                    {
                        // Actual download failure (not just post-processing)
                        _logger.LogError("[SABnzbd] Download {NzoId} failed: {FailMessage}", nzoId, historyItem.fail_message);
                        status = "failed";
                        errorMessage = historyItem.fail_message ?? "Download failed";
                    }
                }

                return new DownloadClientStatus
                {
                    Status = status,
                    Progress = 100,
                    Downloaded = historyItem.bytes,
                    Size = historyItem.bytes,
                    TimeRemaining = null,
                    SavePath = historyItem.storage,
                    ErrorMessage = errorMessage
                };
            }

            _logger.LogWarning("[SABnzbd] Download {NzoId} not found in queue ({QueueCount} items) or history ({HistoryCount} items)",
                nzoId, queue?.Count ?? 0, history?.Count ?? 0);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SABnzbd] Error getting download status");
            return null;
        }
    }

    /// <summary>
    /// Delete download from queue or history (Sonarr/Radarr behavior)
    /// </summary>
    public async Task<bool> DeleteDownloadAsync(DownloadClient config, string nzoId, bool deleteFiles = false)
    {
        try
        {
            // Try to remove from queue first
            var mode = deleteFiles ? "delete" : "remove";
            var delFilesParam = deleteFiles ? "&del_files=1" : "";
            var queueResponse = await SendApiRequestAsync(config, $"?mode=queue&name={mode}&value={nzoId}{delFilesParam}");

            if (queueResponse != null)
            {
                _logger.LogDebug("[SABnzbd] Removed {NzoId} from queue", nzoId);
                return true;
            }

            // If not in queue, try to remove from history
            // Note: SABnzbd's history delete always removes files
            var historyResponse = await SendApiRequestAsync(config, $"?mode=history&name=delete&value={nzoId}");

            if (historyResponse != null)
            {
                _logger.LogDebug("[SABnzbd] Removed {NzoId} from history", nzoId);
                return true;
            }

            _logger.LogWarning("[SABnzbd] Download {NzoId} not found in queue or history for deletion", nzoId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SABnzbd] Error deleting download {NzoId}", nzoId);
            return false;
        }
    }

    // Private helper methods

    private async Task<string?> SendApiRequestAsync(DownloadClient config, string query)
    {
        try
        {
            // Build full URL without modifying HttpClient.BaseAddress
            // This prevents InvalidOperationException when HttpClient has already been used
            var protocol = config.UseSsl ? "https" : "http";
            var baseUrl = $"{protocol}://{config.Host}:{config.Port}/sabnzbd/api";
            var url = query.Contains("apikey") ? query : $"{query}&apikey={config.ApiKey}";
            var fullUrl = $"{baseUrl}{url}";

            var response = await _httpClient.GetAsync(fullUrl);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            _logger.LogWarning("[SABnzbd] API request failed: {Status}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SABnzbd] API request error");
            return null;
        }
    }
}

/// <summary>
/// SABnzbd queue item
/// </summary>
public class SabnzbdItem
{
    public string nzo_id { get; set; } = "";
    public string filename { get; set; } = "";
    public string status { get; set; } = "";
    public long mb { get; set; }
    public long mbleft { get; set; }
    public string percentage { get; set; } = "";
    public string timeleft { get; set; } = "";
    public string category { get; set; } = "";
}

/// <summary>
/// SABnzbd history item
/// </summary>
public class SabnzbdHistoryItem
{
    public string nzo_id { get; set; } = "";
    public string name { get; set; } = "";
    public string status { get; set; } = "";
    public long bytes { get; set; }
    public string category { get; set; } = "";
    public string storage { get; set; } = "";
    public long completed { get; set; } // Unix timestamp
    public string fail_message { get; set; } = ""; // Why it failed (if status is Failed)
}
