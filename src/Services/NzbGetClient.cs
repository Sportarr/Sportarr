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
    /// Add NZB from URL - fetches NZB content and uploads to NZBGet
    /// Uses raw byte handling to preserve encoding (ISO-8859-1 NZB files)
    /// Falls back to appendurl mode if fetch fails or content is invalid
    /// </summary>
    public async Task<int?> AddNzbAsync(DownloadClient config, string nzbUrl, string category)
    {
        try
        {
            _logger.LogInformation("[NZBGet] Fetching NZB from: {Url}", nzbUrl);

            // Fetch the NZB file as raw bytes to preserve encoding
            var response = await _httpClient.GetAsync(nzbUrl);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[NZBGet] Failed to fetch NZB: HTTP {StatusCode}. Falling back to appendurl mode.", response.StatusCode);
                return await AddNzbViaUrlAsync(config, nzbUrl, category);
            }

            // Read as raw bytes - do NOT convert to string to avoid encoding issues
            var nzbBytes = await response.Content.ReadAsByteArrayAsync();
            var filename = GetNzbFilename(response, nzbUrl);

            _logger.LogInformation("[NZBGet] Downloaded NZB: {Filename} ({Size} bytes)", filename, nzbBytes.Length);

            // Validate the NZB content - check if it's actually an NZB file
            var validationResult = ValidateNzbContent(nzbBytes);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[NZBGet] Invalid NZB content: {Reason}. Content preview: {Preview}. Falling back to appendurl mode.",
                    validationResult.Reason, validationResult.ContentPreview);
                return await AddNzbViaUrlAsync(config, nzbUrl, category);
            }

            // Upload the raw bytes to NZBGet
            var result = await AddNzbViaContentAsync(config, nzbBytes, filename, category);

            // If append fails, fall back to appendurl mode
            if (result == null)
            {
                _logger.LogWarning("[NZBGet] append mode failed. Falling back to appendurl mode.");
                return await AddNzbViaUrlAsync(config, nzbUrl, category);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NZBGet] Error adding NZB via append. Falling back to appendurl mode.");
            try
            {
                return await AddNzbViaUrlAsync(config, nzbUrl, category);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "[NZBGet] Fallback to appendurl also failed");
                return null;
            }
        }
    }

    /// <summary>
    /// Validate that the downloaded content is actually an NZB file
    /// </summary>
    private (bool IsValid, string Reason, string ContentPreview) ValidateNzbContent(byte[] content)
    {
        // Minimum size check - a valid NZB file should be at least 100 bytes
        if (content.Length < 100)
        {
            var preview = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 200));
            return (false, $"Content too small ({content.Length} bytes)", preview);
        }

        // Check for NZB markers - look for XML declaration or nzb root element
        var contentStart = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 500));
        var contentStartLower = contentStart.ToLowerInvariant();

        var hasXmlDeclaration = contentStartLower.Contains("<?xml");
        var hasNzbElement = contentStartLower.Contains("<nzb");
        var hasDoctype = contentStartLower.Contains("<!doctype nzb");

        if (!hasXmlDeclaration && !hasNzbElement && !hasDoctype)
        {
            var isJsonError = contentStart.TrimStart().StartsWith("{") || contentStart.TrimStart().StartsWith("[");
            var isHtmlError = contentStartLower.Contains("<html") || contentStartLower.Contains("<!doctype html");

            if (isJsonError)
            {
                return (false, "Content appears to be JSON error response", contentStart.Substring(0, Math.Min(contentStart.Length, 200)));
            }
            if (isHtmlError)
            {
                return (false, "Content appears to be HTML error page", contentStart.Substring(0, Math.Min(contentStart.Length, 200)));
            }

            return (false, "Content missing NZB markers", contentStart.Substring(0, Math.Min(contentStart.Length, 100)));
        }

        return (true, string.Empty, string.Empty);
    }

    /// <summary>
    /// Add NZB via URL - fallback method that lets NZBGet fetch the NZB directly
    /// Uses NZBGet's appendurl JSON-RPC method
    /// </summary>
    private async Task<int?> AddNzbViaUrlAsync(DownloadClient config, string nzbUrl, string category)
    {
        _logger.LogInformation("[NZBGet] Adding NZB via URL (appendurl mode): {Url}", nzbUrl);

        // appendurl parameters: NZBFilename, URL, Category, Priority, AddToTop, AddPaused, DupeKey, DupeScore, DupeMode, PPParameters
        var parameters = new object[]
        {
            "", // NZBFilename - empty to use server default
            nzbUrl,
            category,
            0, // Priority (0 = normal)
            false, // AddToTop
            false, // AddPaused
            "", // DupeKey
            0, // DupeScore
            "SCORE", // DupeMode
            new object[] { } // PPParameters
        };

        var response = await SendJsonRpcRequestAsync(config, "appendurl", parameters);
        if (response != null)
        {
            var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                var nzbId = result.GetInt32();
                if (nzbId > 0)
                {
                    _logger.LogInformation("[NZBGet] NZB added via appendurl: ID {NzbId}", nzbId);
                    return nzbId;
                }
            }
        }

        _logger.LogError("[NZBGet] Failed to add NZB via appendurl");
        return null;
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
    /// Add NZB via content - uploads raw bytes to NZBGet
    /// Uses base64 encoding of raw bytes to preserve original file encoding (ISO-8859-1)
    /// </summary>
    private async Task<int?> AddNzbViaContentAsync(DownloadClient config, byte[] nzbBytes, string filename, string category)
    {
        // Convert raw bytes to base64 - this preserves the original encoding
        // NZBGet will decode the base64 and get the exact original bytes
        var base64Content = Convert.ToBase64String(nzbBytes);

        // NZBGet append method parameters
        var parameters = new object[]
        {
            filename,      // 1. NZBFilename
            base64Content, // 2. NZBContent - base64 encoded NZB file
            category,      // 3. Category
            0,             // 4. Priority (0 = normal)
            false,         // 5. AddToTop
            false,         // 6. AddPaused
            "",            // 7. DupeKey
            0,             // 8. DupeScore
            "SCORE",       // 9. DupeMode
            new string[][] { new[] { "*Unpack:", "yes" } }  // 10. PPParameters
        };

        var rpcUrl = BuildBaseUrl(config);
        _logger.LogDebug("[NZBGet] JSON-RPC endpoint: {RpcUrl}", rpcUrl);
        _logger.LogInformation("[NZBGet] Uploading NZB to NZBGet: {Filename}, Category: {Category}", filename, category);

        var response = await SendJsonRpcRequestAsync(config, "append", parameters);

        return ParseAppendResponse(response);
    }

    /// <summary>
    /// Parse the response from NZBGet append command
    /// </summary>
    private int? ParseAppendResponse(string? response)
    {
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

            // NZBGet has a primitive JSON parser - "id" must come before "params"
            // Use Dictionary to control serialization order
            var requestBody = new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = requestId,
                ["method"] = method,
                ["params"] = parameters ?? Array.Empty<object>()
            };

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            _logger.LogDebug("[NZBGet] JSON-RPC request to {Method}: {Payload}", method, jsonPayload);

            var content = new StringContent(
                jsonPayload,
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

            _logger.LogWarning("[NZBGet] JSON-RPC request '{Method}' to {Url} failed: {Status} - {Response}",
                method, url, response.StatusCode, responseContent);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogError("[NZBGet] 404 Not Found - The JSON-RPC endpoint was not found at {Url}. " +
                    "Check that NZBGet is running and the URL Base setting is correct. " +
                    "Common URL formats: http://host:6789/jsonrpc (default) or http://host:6789/nzbget/jsonrpc (with URL base)", url);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError("[NZBGet] 401 Unauthorized - Check username and password in Settings > Download Clients");
            }

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
