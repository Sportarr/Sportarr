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
    /// Add NZB from URL - fetches NZB content first, then uploads to SABnzbd using addfile mode
    /// This matches Sonarr/Radarr behavior and works when SABnzbd is on a different network than the indexer
    /// </summary>
    public async Task<string?> AddNzbAsync(DownloadClient config, string nzbUrl, string category)
    {
        try
        {
            _logger.LogDebug("[SABnzbd] Config: Id={Id}, Name={Name}, Host={Host}, Port={Port}, HasApiKey={HasApiKey}, ApiKeyLength={ApiKeyLength}",
                config.Id, config.Name, config.Host, config.Port,
                !string.IsNullOrWhiteSpace(config.ApiKey),
                config.ApiKey?.Length ?? 0);

            // Step 1: Fetch the NZB content from the indexer URL (Sportarr fetches it)
            // This is how Sonarr/Radarr work - they download the NZB file themselves
            // then upload it to SABnzbd, so SABnzbd never needs to contact the indexer
            _logger.LogDebug("[SABnzbd] Fetching NZB content from: {Url}", nzbUrl);

            byte[] nzbData;
            string filename;

            try
            {
                using var fetchClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                fetchClient.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
                var nzbResponse = await fetchClient.GetAsync(nzbUrl);

                if (!nzbResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("[SABnzbd] Failed to fetch NZB from indexer: HTTP {StatusCode}", nzbResponse.StatusCode);
                    return null;
                }

                nzbData = await nzbResponse.Content.ReadAsByteArrayAsync();
                _logger.LogDebug("[SABnzbd] Fetched NZB content: {Size} bytes", nzbData.Length);

                // Validate that we received actual NZB content (XML starting with <?xml or containing <nzb)
                // Prowlarr may return an error page or JSON error instead of the NZB file
                var contentPreview = System.Text.Encoding.UTF8.GetString(nzbData, 0, Math.Min(nzbData.Length, 500));
                if (nzbData.Length < 100 || (!contentPreview.Contains("<?xml") && !contentPreview.Contains("<nzb")))
                {
                    _logger.LogError("[SABnzbd] Indexer returned invalid NZB content (size: {Size} bytes). Response: {Preview}",
                        nzbData.Length, contentPreview.Length > 200 ? contentPreview[..200] + "..." : contentPreview);
                    return null; // Fail - don't try URL mode, the indexer returned an error
                }

                // Extract filename from Content-Disposition header or URL
                filename = GetNzbFilename(nzbResponse, nzbUrl);
            }
            catch (Exception fetchEx)
            {
                _logger.LogError(fetchEx, "[SABnzbd] Failed to fetch NZB content from indexer");
                return null;
            }

            // Step 2: Upload NZB content to SABnzbd using addfile mode
            var (response, shouldFallbackToUrl) = await SendAddFileRequestAsync(config, nzbData, filename, category, nzbUrl);

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("nzo_ids", out var ids) &&
                    ids.GetArrayLength() > 0)
                {
                    var nzoId = ids[0].GetString();
                    _logger.LogInformation("[SABnzbd] NZB added via addfile: {NzoId}", nzoId);
                    return nzoId;
                }
            }

            // Fallback to addurl mode if addfile failed with "Invalid mode" (NZBdav compatibility)
            if (shouldFallbackToUrl)
            {
                _logger.LogInformation("[SABnzbd] addfile mode not supported, falling back to addurl mode");
                return await AddNzbViaUrlAsync(config, nzbUrl, category);
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
    /// Extract filename from response headers or URL
    /// </summary>
    private string GetNzbFilename(HttpResponseMessage response, string url)
    {
        // Try to get filename from Content-Disposition header
        if (response.Content.Headers.ContentDisposition?.FileName != null)
        {
            var filename = response.Content.Headers.ContentDisposition.FileName.Trim('"');
            if (!string.IsNullOrEmpty(filename))
            {
                return filename.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase)
                    ? filename
                    : filename + ".nzb";
            }
        }

        // Try to extract from URL (look for 'file=' parameter common in Prowlarr URLs)
        try
        {
            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var fileParam = query["file"];
            if (!string.IsNullOrEmpty(fileParam))
            {
                return fileParam.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase)
                    ? fileParam
                    : fileParam + ".nzb";
            }
        }
        catch { /* Ignore URL parsing errors */ }

        // Default filename
        return $"sportarr-{DateTime.UtcNow:yyyyMMddHHmmss}.nzb";
    }

    /// <summary>
    /// Add NZB via URL only - for use with Decypharr and other proxies that need to intercept the URL
    /// This skips the fetch-and-upload approach and directly passes the URL to the download client
    /// </summary>
    public async Task<string?> AddNzbViaUrlOnlyAsync(DownloadClient config, string nzbUrl, string category)
    {
        _logger.LogInformation("[SABnzbd] Adding NZB via URL (proxy mode) to {Name}", config.Name);
        _logger.LogInformation("[SABnzbd] Config check: Id={Id}, Host={Host}, Port={Port}, HasApiKey={HasApiKey}, ApiKeyLength={ApiKeyLength}",
            config.Id, config.Host, config.Port,
            !string.IsNullOrWhiteSpace(config.ApiKey),
            config.ApiKey?.Length ?? 0);
        return await AddNzbViaUrlAsync(config, nzbUrl, category);
    }

    /// <summary>
    /// Fallback method: Add NZB via URL (original behavior for when fetch fails)
    /// </summary>
    private async Task<string?> AddNzbViaUrlAsync(DownloadClient config, string nzbUrl, string category)
    {
        _logger.LogDebug("[SABnzbd] Using addurl fallback mode");
        var response = await SendAddUrlRequestAsync(config, nzbUrl, category);

        if (response != null)
        {
            var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("nzo_ids", out var ids) &&
                ids.GetArrayLength() > 0)
            {
                var nzoId = ids[0].GetString();
                _logger.LogInformation("[SABnzbd] NZB added via addurl: {NzoId}", nzoId);
                return nzoId;
            }
        }

        return null;
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

                    // CRITICAL: Repair failures are REAL failures - do NOT import these
                    // PAR2 repair fails when there aren't enough recovery blocks to reconstruct missing data
                    // The files are incomplete/corrupted and should NOT be imported
                    var isRepairFailure =
                        failMessage.Contains("repair") ||
                        failMessage.Contains("par2") ||
                        failMessage.Contains("blocks");

                    if (isRepairFailure)
                    {
                        _logger.LogError("[SABnzbd] Download {NzoId} REPAIR FAILED: {FailMessage}. Files are incomplete/corrupted - NOT importing.",
                            nzoId, historyItem.fail_message);
                        status = "failed";
                        errorMessage = historyItem.fail_message ?? "Repair failed - files are incomplete";
                    }
                    else
                    {
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
                            // Other download failures (network, missing files on server, etc.)
                            _logger.LogError("[SABnzbd] Download {NzoId} failed: {FailMessage}", nzoId, historyItem.fail_message);
                            status = "failed";
                            errorMessage = historyItem.fail_message ?? "Download failed";
                        }
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
    /// Send POST request for addfile mode - uploads NZB content directly to SABnzbd
    /// This matches Sonarr/Radarr behavior and works when SABnzbd is on a different network than the indexer
    /// Returns: (response, shouldFallbackToUrl) - shouldFallbackToUrl is true when addfile mode isn't supported
    /// </summary>
    private async Task<(string? Response, bool ShouldFallbackToUrl)> SendAddFileRequestAsync(
        DownloadClient config, byte[] nzbData, string filename, string category, string originalUrl)
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

            // Build multipart form data for addfile request (matches Sonarr implementation)
            using var content = new MultipartFormDataContent();

            // Add mode parameter
            content.Add(new StringContent("addfile"), "mode");

            // Add category
            content.Add(new StringContent(category), "cat");

            // Add output format
            content.Add(new StringContent("json"), "output");

            // Add authentication
            if (!string.IsNullOrWhiteSpace(config.ApiKey))
            {
                content.Add(new StringContent(config.ApiKey), "apikey");
            }
            else if (!string.IsNullOrWhiteSpace(config.Username) && !string.IsNullOrWhiteSpace(config.Password))
            {
                content.Add(new StringContent(config.Username), "ma_username");
                content.Add(new StringContent(config.Password), "ma_password");
            }

            // Add NZB file content
            var fileContent = new ByteArrayContent(nzbData);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-nzb");
            content.Add(fileContent, "name", filename);

            _logger.LogDebug("[SABnzbd] POST addfile request to: {Url} (file: {Filename}, size: {Size} bytes)",
                baseUrl, filename, nzbData.Length);

            HttpResponseMessage response;
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

            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("[SABnzbd] POST addfile response: {Response}", responseBody);
                return (responseBody, false);
            }

            // Check if the error indicates addfile mode isn't supported (NZBdav compatibility)
            // This allows automatic fallback to addurl mode
            var shouldFallback = responseBody.Contains("Invalid mode", StringComparison.OrdinalIgnoreCase);

            _logger.LogWarning("[SABnzbd] POST addfile request failed: {Status} - Response: {Response}",
                response.StatusCode, responseBody);
            return (null, shouldFallback);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SABnzbd] POST addfile request error");
            return (null, false);
        }
    }

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
            var hasApiKey = !string.IsNullOrWhiteSpace(config.ApiKey);
            var hasCredentials = !string.IsNullOrWhiteSpace(config.Username) && !string.IsNullOrWhiteSpace(config.Password);
            _logger.LogInformation("[SABnzbd] Authentication check: HasApiKey={HasApiKey}, HasCredentials={HasCredentials}", hasApiKey, hasCredentials);

            if (hasApiKey)
            {
                formData["apikey"] = config.ApiKey!;
                _logger.LogInformation("[SABnzbd] Using API key authentication (length: {Length})", config.ApiKey!.Length);
            }
            else if (hasCredentials)
            {
                formData["ma_username"] = config.Username!;
                formData["ma_password"] = config.Password!;
                _logger.LogInformation("[SABnzbd] Using username/password authentication");
            }
            else
            {
                _logger.LogWarning("[SABnzbd] No API key or credentials configured for download client '{Name}'", config.Name);
            }

            _logger.LogInformation("[SABnzbd] POST addurl request to: {Url}", baseUrl);

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

            // If POST fails, fall back to GET (SABnzbd API actually uses GET for addurl)
            // This handles both MethodNotAllowed (405) and BadRequest (400) responses
            // NZBdav and some SABnzbd configurations require GET requests
            if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed ||
                response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                _logger.LogInformation("[SABnzbd] POST addurl failed with {Status}, falling back to GET request", response.StatusCode);

                // Build query with authentication included directly
                // We use the hasApiKey/hasCredentials variables captured earlier in this method
                // to ensure authentication is included in the GET fallback request
                var queryBuilder = new System.Text.StringBuilder();
                queryBuilder.Append($"?mode=addurl&name={Uri.EscapeDataString(nzbUrl)}&cat={Uri.EscapeDataString(category)}&output=json");

                // Add authentication to GET request using the variables we captured at the start
                if (hasApiKey)
                {
                    queryBuilder.Append($"&apikey={Uri.EscapeDataString(config.ApiKey!)}");
                    _logger.LogInformation("[SABnzbd] GET fallback: Including API key in request");
                }
                else if (hasCredentials)
                {
                    queryBuilder.Append($"&ma_username={Uri.EscapeDataString(config.Username!)}&ma_password={Uri.EscapeDataString(config.Password!)}");
                    _logger.LogInformation("[SABnzbd] GET fallback: Including username/password in request");
                }
                else
                {
                    _logger.LogWarning("[SABnzbd] GET fallback: No authentication credentials available");
                }

                return await SendApiRequestAsync(config, queryBuilder.ToString());
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

            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("[SABnzbd] API response: Status={StatusCode}", response.StatusCode);
            _logger.LogTrace("[SABnzbd] API response body: {Body}", responseBody);

            if (response.IsSuccessStatusCode)
            {
                // Check for SABnzbd error response
                try
                {
                    var doc = JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("error", out var error) && !string.IsNullOrEmpty(error.GetString()))
                    {
                        _logger.LogWarning("[SABnzbd] API returned error: {Error}", error.GetString());
                    }
                }
                catch { /* Ignore parse errors */ }

                return responseBody;
            }

            _logger.LogWarning("[SABnzbd] API request failed: {Status} - Response: {Response}",
                response.StatusCode, responseBody);
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
