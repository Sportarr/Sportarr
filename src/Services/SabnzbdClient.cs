using System.Net.Http.Json;
using System.Text.Json;
using Fightarr.Api.Models;

namespace Fightarr.Api.Services;

/// <summary>
/// SABnzbd API client for Fightarr
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
            ConfigureClient(config);

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
            ConfigureClient(config);

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
            ConfigureClient(config);

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
    /// Get history
    /// </summary>
    public async Task<List<SabnzbdHistoryItem>?> GetHistoryAsync(DownloadClient config)
    {
        try
        {
            ConfigureClient(config);

            var response = await SendApiRequestAsync(config, "?mode=history&output=json");

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
            ConfigureClient(config);
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
            ConfigureClient(config);
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
            // First check queue
            var queue = await GetQueueAsync(config);
            var queueItem = queue?.FirstOrDefault(q => q.Nzo_id == nzoId);

            if (queueItem != null)
            {
                var status = queueItem.Status.ToLowerInvariant() switch
                {
                    "downloading" => "downloading",
                    "paused" => "paused",
                    "queued" => "queued",
                    _ => "downloading"
                };

                // Parse percentage
                var progress = 0.0;
                if (double.TryParse(queueItem.Percentage, out var pct))
                {
                    progress = pct;
                }

                // Calculate downloaded size
                var totalMb = queueItem.Mb;
                var remainingMb = queueItem.Mbleft;
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
            var historyItem = history?.FirstOrDefault(h => h.Nzo_id == nzoId);

            if (historyItem != null)
            {
                var status = historyItem.Status.ToLowerInvariant() switch
                {
                    "completed" => "completed",
                    "failed" => "failed",
                    _ => "completed"
                };

                return new DownloadClientStatus
                {
                    Status = status,
                    Progress = 100,
                    Downloaded = historyItem.Bytes,
                    Size = historyItem.Bytes,
                    TimeRemaining = null,
                    SavePath = historyItem.Storage,
                    ErrorMessage = status == "failed" ? "Download failed" : null
                };
            }

            _logger.LogWarning("[SABnzbd] Download {NzoId} not found in queue or history", nzoId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SABnzbd] Error getting download status");
            return null;
        }
    }

    /// <summary>
    /// Delete download
    /// </summary>
    public async Task<bool> DeleteDownloadAsync(DownloadClient config, string nzoId, bool deleteFiles = false)
    {
        try
        {
            ConfigureClient(config);

            var mode = deleteFiles ? "delete" : "remove";
            var response = await SendApiRequestAsync(config, $"?mode=queue&name={mode}&value={nzoId}&del_files=1");
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SABnzbd] Error deleting download");
            return false;
        }
    }

    // Private helper methods

    private void ConfigureClient(DownloadClient config)
    {
        var protocol = config.UseSsl ? "https" : "http";
        _httpClient.BaseAddress = new Uri($"{protocol}://{config.Host}:{config.Port}/sabnzbd/api");
    }

    private async Task<string?> SendApiRequestAsync(DownloadClient config, string query)
    {
        try
        {
            var url = query.Contains("apikey") ? query : $"{query}&apikey={config.ApiKey}";
            var response = await _httpClient.GetAsync(url);

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
    public string Nzo_id { get; set; } = "";
    public string Filename { get; set; } = "";
    public string Status { get; set; } = "";
    public long Mb { get; set; }
    public long Mbleft { get; set; }
    public string Percentage { get; set; } = "";
    public string Timeleft { get; set; } = "";
    public string Category { get; set; } = "";
}

/// <summary>
/// SABnzbd history item
/// </summary>
public class SabnzbdHistoryItem
{
    public string Nzo_id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public long Bytes { get; set; }
    public string Category { get; set; } = "";
    public string Storage { get; set; } = "";
    public long Completed { get; set; } // Unix timestamp
}
