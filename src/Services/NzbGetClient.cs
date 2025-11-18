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

    public NzbGetClient(HttpClient httpClient, ILogger<NzbGetClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Test connection to NZBGet
    /// </summary>
    public async Task<bool> TestConnectionAsync(DownloadClient config)
    {
        try
        {
            ConfigureClient(config);

            var response = await SendJsonRpcRequestAsync("version", null);
            return response != null;
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
            ConfigureClient(config);

            var parameters = new object[]
            {
                "",  // NZBFilename (empty for URL)
                "",  // Content (empty for URL)
                category,
                0,   // Priority
                false, // AddToTop
                false, // AddPaused
                "",    // DupeKey
                0,     // DupeScore
                "SCORE", // DupeMode
                new[]  // PPParameters (post-processing)
                {
                    new { Name = "*Unpack:DeleteSource", Value = "yes" }
                },
                nzbUrl // URL parameter
            };

            var response = await SendJsonRpcRequestAsync("append", parameters);

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("result", out var result))
                {
                    var nzbId = result.GetInt32();
                    _logger.LogInformation("[NZBGet] NZB added: {NzbId}", nzbId);
                    return nzbId;
                }
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
            ConfigureClient(config);

            var response = await SendJsonRpcRequestAsync("listgroups", null);

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
            ConfigureClient(config);

            var response = await SendJsonRpcRequestAsync("history", new object[] { false });

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
            ConfigureClient(config);
            var response = await SendJsonRpcRequestAsync("editqueue", new object[] { "GroupPause", 0, "", new[] { nzbId } });
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
            ConfigureClient(config);
            var response = await SendJsonRpcRequestAsync("editqueue", new object[] { "GroupResume", 0, "", new[] { nzbId } });
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
            ConfigureClient(config);

            var action = deleteFiles ? "GroupFinalDelete" : "GroupDelete";
            var response = await SendJsonRpcRequestAsync("editqueue", new object[] { action, 0, "", new[] { nzbId } });
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NZBGet] Error deleting download");
            return false;
        }
    }

    // Private helper methods

    private void ConfigureClient(DownloadClient config)
    {
        var protocol = config.UseSsl ? "https" : "http";

        // NZBGet typically runs at /nzbget, but can be configured differently
        // Use configured URL base or default to "/nzbget" for backward compatibility
        var urlBase = string.IsNullOrEmpty(config.UrlBase) ? "/nzbget" : config.UrlBase;

        // Ensure urlBase starts with / and doesn't end with /
        if (!urlBase.StartsWith("/"))
        {
            urlBase = "/" + urlBase;
        }
        urlBase = urlBase.TrimEnd('/');

        _httpClient.BaseAddress = new Uri($"{protocol}://{config.Host}:{config.Port}{urlBase}/jsonrpc");

        if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.Username}:{config.Password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    private async Task<string?> SendJsonRpcRequestAsync(string method, object? parameters)
    {
        try
        {
            var requestId = new Random().Next(1, 10000);
            var request = new
            {
                jsonrpc = "2.0",
                method = method,
                @params = parameters ?? Array.Empty<object>(),
                id = requestId
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("", content);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            _logger.LogWarning("[NZBGet] JSON-RPC request failed: {Status}", response.StatusCode);
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
