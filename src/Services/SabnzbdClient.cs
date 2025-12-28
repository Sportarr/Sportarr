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
            // Use POST request for addurl (required by Decypharr and some SABnzbd configurations)
            var response = await SendAddUrlRequestAsync(config, nzbUrl, category);

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
    /// Get queue filtered by specific nzo_id(s) - more efficient for monitoring specific downloads
    /// SABnzbd API: ?mode=queue&nzo_ids=NZO_ID_1,NZO_ID_2&output=json
    /// </summary>
    public async Task<List<SabnzbdItem>?> GetQueueByNzoIdsAsync(DownloadClient config, string nzoId)
    {
        try
        {
            // Use SABnzbd's nzo_ids parameter to filter queue to specific download
            var response = await SendApiRequestAsync(config, $"?mode=queue&nzo_ids={nzoId}&output=json");

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
            _logger.LogError(ex, "[SABnzbd] Error getting queue by nzo_ids");
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
    /// Get completed downloads filtered by category (for external import detection)
    /// </summary>
    public async Task<List<ExternalDownloadInfo>> GetCompletedDownloadsByCategoryAsync(DownloadClient config, string category)
    {
        var history = await GetHistoryAsync(config);
        if (history == null)
            return new List<ExternalDownloadInfo>();

        // Filter for completed downloads in the specified category
        // Only include successful downloads, not failed ones
        var completedDownloads = history.Where(h =>
            h.category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
            h.status.Equals("Completed", StringComparison.OrdinalIgnoreCase));

        return completedDownloads.Select(h => new ExternalDownloadInfo
        {
            DownloadId = h.nzo_id,
            Title = h.name,
            Category = h.category,
            FilePath = h.storage,
            Size = h.bytes,
            IsCompleted = true,
            Protocol = "Usenet",
            TorrentInfoHash = null,
            CompletedDate = h.completed > 0
                ? DateTimeOffset.FromUnixTimeSeconds(h.completed).UtcDateTime
                : (DateTime?)null
        }).ToList();
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
    /// Get download status for monitoring (optimized with nzo_id filtering)
    /// </summary>
    public async Task<DownloadClientStatus?> GetDownloadStatusAsync(DownloadClient config, string nzoId)
    {
        try
        {
            _logger.LogDebug("[SABnzbd] GetDownloadStatusAsync: Looking for NZO ID: {NzoId}", nzoId);

            // OPTIMIZATION: Use SABnzbd's nzo_ids parameter to query specific download
            // This is more efficient than fetching entire queue and filtering
            var queue = await GetQueueByNzoIdsAsync(config, nzoId);
            _logger.LogDebug("[SABnzbd] Filtered queue query returned {Count} items", queue?.Count ?? 0);

            var queueItem = queue?.FirstOrDefault(q => q.nzo_id == nzoId);

            // FALLBACK: If filtered query returns nothing, try full queue
            // This helps diagnose if the issue is filtering or if download isn't in queue at all
            if (queueItem == null)
            {
                _logger.LogDebug("[SABnzbd] Not found in filtered queue, checking full queue...");
                var fullQueue = await GetQueueAsync(config);
                _logger.LogDebug("[SABnzbd] Full queue contains {Count} items", fullQueue?.Count ?? 0);

                if (fullQueue != null && fullQueue.Count > 0)
                {
                    _logger.LogDebug("[SABnzbd] Full queue NZO IDs: {Ids}", string.Join(", ", fullQueue.Select(q => q.nzo_id)));
                    queueItem = fullQueue.FirstOrDefault(q => q.nzo_id == nzoId);

                    if (queueItem != null)
                    {
                        _logger.LogWarning("[SABnzbd] Found in full queue but NOT in filtered queue - possible SABnzbd API issue");
                    }
                }
            }

            if (queueItem != null)
            {
                _logger.LogDebug("[SABnzbd] Found download in queue: {NzoId}, Status: {Status}, Progress: {Progress}%",
                    nzoId, queueItem.status, queueItem.percentage);

                var status = queueItem.status.ToLowerInvariant() switch
                {
                    "downloading" => "downloading",
                    "paused" => "paused",
                    "queued" => "queued",
                    _ => "downloading"
                };

                // Parse percentage (SABnzbd returns as string)
                var progress = 0.0;
                if (double.TryParse(queueItem.percentage, out var pct))
                {
                    progress = pct;
                }

                // Parse size fields (SABnzbd returns as strings like "1277.65")
                double.TryParse(queueItem.mb, out var totalMb);
                double.TryParse(queueItem.mbleft, out var remainingMb);
                var downloadedMb = totalMb - remainingMb;

                _logger.LogDebug("[SABnzbd] Download progress: {Downloaded:F2} MB / {Total:F2} MB ({Progress:F1}%)",
                    downloadedMb, totalMb, progress);

                // Parse time remaining (SABnzbd format: "0:16:44" = HH:MM:SS)
                TimeSpan? timeRemaining = null;
                if (!string.IsNullOrEmpty(queueItem.timeleft) && TimeSpan.TryParse(queueItem.timeleft, out var ts))
                {
                    timeRemaining = ts;
                }

                return new DownloadClientStatus
                {
                    Status = status,
                    Progress = progress,
                    Downloaded = (long)(downloadedMb * 1024 * 1024), // Convert MB to bytes
                    Size = (long)(totalMb * 1024 * 1024),
                    TimeRemaining = timeRemaining,
                    SavePath = null // Not available in queue data
                };
            }

            // If not in queue, check history
            var history = await GetHistoryAsync(config);
            _logger.LogDebug("[SABnzbd] History contains {Count} items", history?.Count ?? 0);
            var historyItem = history?.FirstOrDefault(h => h.nzo_id == nzoId);

            if (historyItem != null)
            {
                _logger.LogDebug("[SABnzbd] Found download in history: {NzoId}, Status: {Status}, Storage: {Storage}, FailMessage: {FailMessage}",
                    nzoId, historyItem.status, historyItem.storage ?? "(empty)", historyItem.fail_message ?? "none");

                var reportedStatus = historyItem.status.ToLowerInvariant();
                var status = "completed";
                string? errorMessage = null;

                // Handle intermediate states - SABnzbd moves downloads to history during extraction/repair
                // These states have empty storage paths, so we must NOT trigger import yet
                // "Running" = post-processing script is executing
                if (reportedStatus == "extracting" || reportedStatus == "repairing" ||
                    reportedStatus == "verifying" || reportedStatus == "moving" ||
                    reportedStatus == "running")
                {
                    _logger.LogDebug("[SABnzbd] Download {NzoId} is still processing: {Status}", nzoId, historyItem.status);
                    return new DownloadClientStatus
                    {
                        Status = "downloading", // Treat as still downloading so we don't trigger import
                        Progress = 99, // Almost done
                        Downloaded = historyItem.bytes,
                        Size = historyItem.bytes,
                        TimeRemaining = TimeSpan.FromSeconds(30), // Estimate
                        SavePath = null // Not ready yet
                    };
                }

                // CRITICAL: Even if status is "Completed", verify storage path is actually available
                // SABnzbd may report "Completed" before the storage path is fully populated
                // This prevents race conditions where we try to import before files are in final location
                if (reportedStatus == "completed" && string.IsNullOrEmpty(historyItem.storage))
                {
                    _logger.LogDebug("[SABnzbd] Download {NzoId} completed but storage path not yet available, waiting...", nzoId);
                    return new DownloadClientStatus
                    {
                        Status = "downloading", // Treat as still processing
                        Progress = 99.5, // Almost done
                        Downloaded = historyItem.bytes,
                        Size = historyItem.bytes,
                        TimeRemaining = TimeSpan.FromSeconds(10), // Estimate
                        SavePath = null // Not ready yet
                    };
                }

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

    /// <summary>
    /// Send POST request for addurl mode (required by Decypharr and some SABnzbd configurations)
    /// </summary>
    private async Task<string?> SendAddUrlRequestAsync(DownloadClient config, string nzbUrl, string category)
    {
        try
        {
            var protocol = config.UseSsl ? "https" : "http";
            var urlBase = config.UrlBase ?? "";

            if (!string.IsNullOrEmpty(urlBase))
            {
                if (!urlBase.StartsWith("/"))
                {
                    urlBase = "/" + urlBase;
                }
                urlBase = urlBase.TrimEnd('/');
            }

            var baseUrl = $"{protocol}://{config.Host}:{config.Port}{urlBase}/api";

            // Build form data for POST request
            var formData = new Dictionary<string, string>
            {
                { "mode", "addurl" },
                { "name", nzbUrl },
                { "cat", category },
                { "output", "json" }
            };

            // Add authentication
            if (!string.IsNullOrWhiteSpace(config.ApiKey))
            {
                formData["apikey"] = config.ApiKey;
            }
            else if (!string.IsNullOrWhiteSpace(config.Username) && !string.IsNullOrWhiteSpace(config.Password))
            {
                formData["ma_username"] = config.Username;
                formData["ma_password"] = config.Password;
            }

            _logger.LogDebug("[SABnzbd] POST addurl request to: {Url}", baseUrl);

            HttpResponseMessage response;
            var content = new FormUrlEncodedContent(formData);

            if (config.UseSsl && config.DisableSslCertificateValidation)
            {
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(100) };
                response = await client.PostAsync(baseUrl, content);
            }
            else
            {
                response = await _httpClient.PostAsync(baseUrl, content);
            }

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("[SABnzbd] POST addurl response: {Response}", result);
                return result;
            }

            // If POST fails with MethodNotAllowed, try GET as fallback (for standard SABnzbd)
            if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                _logger.LogDebug("[SABnzbd] POST not allowed, falling back to GET request");
                var query = $"?mode=addurl&name={Uri.EscapeDataString(nzbUrl)}&cat={Uri.EscapeDataString(category)}&output=json";
                return await SendApiRequestAsync(config, query);
            }

            _logger.LogWarning("[SABnzbd] POST addurl request failed: {Status}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SABnzbd] POST addurl request error");
            return null;
        }
    }

    private async Task<string?> SendApiRequestAsync(DownloadClient config, string query)
    {
        try
        {
            // Build full URL without modifying HttpClient.BaseAddress
            // This prevents InvalidOperationException when HttpClient has already been used
            var protocol = config.UseSsl ? "https" : "http";

            // Use configured URL base or default to empty (root) for SABnzbd 4.4+
            // SABnzbd 4.4+ defaults to root path, not /sabnzbd
            // Users can set urlBase to:
            //   - null or "" (empty) for default root installations (http://host:port/api)
            //   - "/sabnzbd" for older installations with subdirectory (http://host:port/sabnzbd/api)
            //   - "/custom" for custom URL base (http://host:port/custom/api)
            var urlBase = config.UrlBase ?? "";

            // Ensure urlBase starts with / and doesn't end with / (only if not empty)
            if (!string.IsNullOrEmpty(urlBase))
            {
                if (!urlBase.StartsWith("/"))
                {
                    urlBase = "/" + urlBase;
                }
                urlBase = urlBase.TrimEnd('/');
            }

            var baseUrl = $"{protocol}://{config.Host}:{config.Port}{urlBase}/api";

            // Add authentication - prefer API key, fallback to username/password (matches Sonarr implementation)
            string url;
            if (!string.IsNullOrWhiteSpace(config.ApiKey))
            {
                // Use API key authentication (preferred method)
                url = query.Contains("apikey") ? query : $"{query}&apikey={config.ApiKey}";
            }
            else if (!string.IsNullOrWhiteSpace(config.Username) && !string.IsNullOrWhiteSpace(config.Password))
            {
                // Use username/password authentication (fallback method)
                url = $"{query}&ma_username={Uri.EscapeDataString(config.Username)}&ma_password={Uri.EscapeDataString(config.Password)}";
            }
            else
            {
                // No authentication provided - attempt anyway (SABnzbd might not require auth)
                url = query;
            }

            var fullUrl = $"{baseUrl}{url}";

            // Safely log the URL, redacting sensitive values
            var logUrl = fullUrl;
            if (!string.IsNullOrEmpty(config.ApiKey))
                logUrl = logUrl.Replace(config.ApiKey, "***API_KEY***");
            if (!string.IsNullOrEmpty(config.Password))
                logUrl = logUrl.Replace(config.Password, "***PASSWORD***");
            _logger.LogDebug("[SABnzbd] API request: {FullUrl}", logUrl);

            // Use custom HttpClient with SSL validation disabled if option is enabled
            HttpResponseMessage response;
            if (config.UseSsl && config.DisableSslCertificateValidation)
            {
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(100) };
                response = await client.GetAsync(fullUrl);
            }
            else
            {
                response = await _httpClient.GetAsync(fullUrl);
            }

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            _logger.LogWarning("[SABnzbd] API request failed: {Status} for URL: {Url}",
                response.StatusCode,
                string.IsNullOrEmpty(config.ApiKey) ? fullUrl : fullUrl.Replace(config.ApiKey, "***API_KEY***"));
            return null;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
        {
            _logger.LogError(ex,
                "[SABnzbd] SSL/TLS connection failed for {Host}:{Port}. " +
                "This usually means SSL is enabled in Sportarr but the port is serving HTTP, not HTTPS. " +
                "Please ensure HTTPS is enabled in SABnzbd's Config→General settings, or disable SSL in Sportarr.",
                config.Host, config.Port);
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
    // SABnzbd API returns these as strings (e.g., "1277.65"), not numbers
    public string mb { get; set; } = "0";
    public string mbleft { get; set; } = "0";
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
