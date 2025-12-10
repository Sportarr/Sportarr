using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// NZBGet JSON-RPC client for Sportarr
/// Implements NZBGet JSON-RPC API for NZB downloads
/// </summary>
public class NzbGetClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NzbGetClient> _logger;
    private HttpClient? _customHttpClient; // For SSL bypass

    public NzbGetClient(HttpClient httpClient, ILogger<NzbGetClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Get HttpClient for requests - creates custom client with SSL bypass if needed
    /// </summary>
    private HttpClient GetHttpClient(DownloadClient config)
    {
        // Use custom client with SSL validation disabled if option is enabled
        if (config.UseSsl && config.DisableSslCertificateValidation)
        {
            if (_customHttpClient == null)
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };
                _customHttpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(100) };
            }
            return _customHttpClient;
        }

        return _httpClient;
    }

    /// <summary>
    /// Test connection to NZBGet
    /// </summary>
    public async Task<bool> TestConnectionAsync(DownloadClient config)
    {
        try
        {
            
            var response = await SendJsonRpcRequestAsync(config, "version", null);
            return response != null;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
        {
            _logger.LogError(ex,
                "[NZBGet] SSL/TLS connection failed for {Host}:{Port}. " +
                "This usually means SSL is enabled in Sportarr but the port is serving HTTP, not HTTPS. " +
                "Please ensure HTTPS is enabled in NZBGet settings, or disable SSL in Sportarr.",
                config.Host, config.Port);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NZBGet] Connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Add NZB from URL
    /// </summary>
    public async Task<int?> AddNzbAsync(DownloadClient config, string nzbUrl, string category)
    {
        try
        {
            // NZBGet append method parameters (order is critical!):
            // 1. NZBFilename (string) - empty when using URL
            // 2. NZBContent (string) - URL or base64-encoded NZB content
            // 3. Category (string)
            // 4. Priority (int) - 0 = normal
            // 5. AddToTop (bool)
            // 6. AddPaused (bool)
            // 7. DupeKey (string)
            // 8. DupeScore (int)
            // 9. DupeMode (string) - "SCORE", "ALL", "FORCE"
            // 10. AutoCategory (bool) - let NZBGet auto-categorize
            // 11. PPParameters (array) - post-processing parameters
            var parameters = new object[]
            {
                "",        // 1. NZBFilename (empty - will be read from URL headers)
                nzbUrl,    // 2. NZBContent - THE URL GOES HERE
                category,  // 3. Category
                0,         // 4. Priority (0 = normal)
                false,     // 5. AddToTop
                false,     // 6. AddPaused
                "",        // 7. DupeKey
                0,         // 8. DupeScore
                "SCORE",   // 9. DupeMode
                false,     // 10. AutoCategory (false - use our category)
                new[]      // 11. PPParameters (post-processing)
                {
                    new { Name = "*Unpack:", Value = "yes" }
                }
            };

            _logger.LogInformation("[NZBGet] Sending append request - URL: {Url}, Category: {Category}", nzbUrl, category);

            var response = await SendJsonRpcRequestAsync(config, "append", parameters);

            if (response != null)
            {
                _logger.LogDebug("[NZBGet] Append response: {Response}", response);

                var doc = JsonDocument.Parse(response);

                // Check for error in response
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    var errorMsg = error.ToString();
                    _logger.LogError("[NZBGet] NZB add failed with error: {Error}", errorMsg);
                    return null;
                }

                if (doc.RootElement.TryGetProperty("result", out var result))
                {
                    var nzbId = result.GetInt32();

                    // NZBGet returns -1 (or negative values) when the add operation fails
                    // This can happen due to permissions issues, disk space, or other errors
                    if (nzbId <= 0)
                    {
                        _logger.LogError("[NZBGet] NZB add failed - NZBGet returned ID {NzbId}. Check NZBGet logs for details (common causes: permissions, disk space, temp directory issues)", nzbId);
                        return null;
                    }

                    _logger.LogInformation("[NZBGet] NZB added successfully: {NzbId}", nzbId);
                    return nzbId;
                }
                else
                {
                    _logger.LogError("[NZBGet] NZB add failed - response has no 'result' field: {Response}", response);
                }
            }
            else
            {
                _logger.LogError("[NZBGet] NZB add failed - SendJsonRpcRequestAsync returned null (check previous logs for HTTP status)");
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NZBGet] Error adding NZB");
            return null;
        }
    }

    /// <summary>
    /// Get list of downloads
    /// </summary>
    public async Task<List<NzbGetItem>?> GetListAsync(DownloadClient config)
    {
        try
        {
            
            var response = await SendJsonRpcRequestAsync(config, "listgroups", null);

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("result", out var result))
                {
                    return JsonSerializer.Deserialize<List<NzbGetItem>>(result.GetRawText());
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NZBGet] Error getting list");
            return null;
        }
    }

    /// <summary>
    /// Get history
    /// </summary>
    public async Task<List<NzbGetHistoryItem>?> GetHistoryAsync(DownloadClient config)
    {
        try
        {
            
            var response = await SendJsonRpcRequestAsync(config, "history", new object[] { false });

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("result", out var result))
                {
                    return JsonSerializer.Deserialize<List<NzbGetHistoryItem>>(result.GetRawText());
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NZBGet] Error getting history");
            return null;
        }
    }

    /// <summary>
    /// Pause download
    /// </summary>
    public async Task<bool> PauseDownloadAsync(DownloadClient config, int nzbId)
    {
        try
        {
                        var response = await SendJsonRpcRequestAsync(config, "editqueue", new object[] { "GroupPause", 0, "", new[] { nzbId } });
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NZBGet] Error pausing download");
            return false;
        }
    }

    /// <summary>
    /// Resume download
    /// </summary>
    public async Task<bool> ResumeDownloadAsync(DownloadClient config, int nzbId)
    {
        try
        {
                        var response = await SendJsonRpcRequestAsync(config, "editqueue", new object[] { "GroupResume", 0, "", new[] { nzbId } });
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NZBGet] Error resuming download");
            return false;
        }
    }

    /// <summary>
    /// Get download status for monitoring
    /// </summary>
    public async Task<DownloadClientStatus?> GetDownloadStatusAsync(DownloadClient config, int nzbId)
    {
        try
        {
            // First check active queue
            var queue = await GetListAsync(config);
            var queueItem = queue?.FirstOrDefault(q => q.NZBID == nzbId);

            if (queueItem != null)
            {
                var status = queueItem.Status.ToLowerInvariant() switch
                {
                    "downloading" or "queued" => "downloading",
                    "paused" => "paused",
                    _ => "downloading"
                };

                // Calculate file size from Hi/Lo parts (NZBGet uses split 64-bit integers)
                var totalSize = ((long)queueItem.FileSizeHi << 32) | queueItem.FileSizeLo;
                var remainingSize = ((long)queueItem.RemainingSizeHi << 32) | queueItem.RemainingSizeLo;
                var downloaded = totalSize - remainingSize;

                var progress = totalSize > 0 ? (downloaded / (double)totalSize * 100) : 0;

                return new DownloadClientStatus
                {
                    Status = status,
                    Progress = progress,
                    Downloaded = downloaded,
                    Size = totalSize,
                    TimeRemaining = null, // Would need download rate calculation
                    SavePath = null // Not available in queue data
                };
            }

            // If not in queue, check history
            var history = await GetHistoryAsync(config);
            var historyItem = history?.FirstOrDefault(h => h.NZBID == nzbId);

            if (historyItem != null)
            {
                var status = historyItem.Status.ToLowerInvariant() switch
                {
                    "success" or "success/all" or "success/par" => "completed",
                    "failure" or "failure/par" or "failure/unpack" => "failed",
                    _ => "completed"
                };

                var totalSize = ((long)historyItem.FileSizeHi << 32) | historyItem.FileSizeLo;

                return new DownloadClientStatus
                {
                    Status = status,
                    Progress = 100,
                    Downloaded = totalSize,
                    Size = totalSize,
                    TimeRemaining = null,
                    SavePath = historyItem.DestDir,
                    ErrorMessage = status == "failed" ? $"Download failed: {historyItem.Status}" : null
                };
            }

            _logger.LogWarning("[NzbGet] Download {NzbId} not found in queue or history", nzbId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NzbGet] Error getting download status");
            return null;
        }
    }

    /// <summary>
    /// Delete download
    /// </summary>
    public async Task<bool> DeleteDownloadAsync(DownloadClient config, int nzbId, bool deleteFiles = false)
    {
        try
        {
            
            var action = deleteFiles ? "GroupFinalDelete" : "GroupDelete";
            var response = await SendJsonRpcRequestAsync(config, "editqueue", new object[] { action, 0, "", new[] { nzbId } });
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NZBGet] Error deleting download");
            return false;
        }
    }

    // Private helper methods

    private string BuildBaseUrl(DownloadClient config)
    {
        var protocol = config.UseSsl ? "https" : "http";

        // NZBGet defaults to root path, not /nzbget
        // Use configured URL base or default to empty (root)
        // Users can set urlBase to:
        //   - null or "" (empty) for default root installations (http://host:port/jsonrpc)
        //   - "/nzbget" for installations with subdirectory (http://host:port/nzbget/jsonrpc)
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

        return $"{protocol}://{config.Host}:{config.Port}{urlBase}/jsonrpc";
    }

    private async Task<string?> SendJsonRpcRequestAsync(DownloadClient config, string method, object? parameters)
    {
        try
        {
            var client = GetHttpClient(config);
            var url = BuildBaseUrl(config);

            var requestId = new Random().Next(1, 10000);
            var requestBody = new
            {
                jsonrpc = "2.0",
                method = method,
                @params = parameters ?? Array.Empty<object>(),
                id = requestId
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            // Create request message to set per-request headers (avoids modifying shared HttpClient)
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;

            // Add Basic auth header per-request if credentials are configured
            if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
            {
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.Username}:{config.Password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            var response = await client.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("[NZBGet] JSON-RPC response for {Method}: {Response}", method,
                    responseContent.Length > 500 ? responseContent[..500] + "..." : responseContent);
                return responseContent;
            }

            _logger.LogWarning("[NZBGet] JSON-RPC request '{Method}' failed: {Status} - {Response}",
                method, response.StatusCode, responseContent);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NZBGet] JSON-RPC request error");
            return null;
        }
    }
}

/// <summary>
/// NZBGet download item
/// </summary>
public class NzbGetItem
{
    public int NZBID { get; set; }
    public string NZBName { get; set; } = "";
    public string Status { get; set; } = "";
    public long FileSizeLo { get; set; }
    public long FileSizeHi { get; set; }
    public long RemainingSizeLo { get; set; }
    public long RemainingSizeHi { get; set; }
    public int DownloadRate { get; set; }
    public string Category { get; set; } = "";
}

/// <summary>
/// NZBGet history item
/// </summary>
public class NzbGetHistoryItem
{
    public int NZBID { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string DestDir { get; set; } = "";
    public string Category { get; set; } = "";
    public long FileSizeLo { get; set; }
    public long FileSizeHi { get; set; }
    public int HistoryTime { get; set; } // Unix timestamp
}
