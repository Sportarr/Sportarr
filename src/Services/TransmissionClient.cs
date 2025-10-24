using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fightarr.Api.Models;

namespace Fightarr.Api.Services;

/// <summary>
/// Transmission RPC client for Fightarr
/// Implements Transmission RPC protocol for torrent management
/// </summary>
public class TransmissionClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TransmissionClient> _logger;
    private string? _sessionId;

    public TransmissionClient(HttpClient httpClient, ILogger<TransmissionClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Test connection to Transmission
    /// </summary>
    public async Task<bool> TestConnectionAsync(DownloadClient config)
    {
        try
        {
            ConfigureClient(config);

            // Get session stats to test connection
            var response = await SendRpcRequestAsync(config, "session-stats", null);
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Transmission] Connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Add torrent from URL
    /// </summary>
    public async Task<string?> AddTorrentAsync(DownloadClient config, string torrentUrl, string downloadDir)
    {
        try
        {
            ConfigureClient(config);

            var arguments = new
            {
                filename = torrentUrl,
                downloadDir = downloadDir,
                paused = false
            };

            var response = await SendRpcRequestAsync(config, "torrent-add", arguments);

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("arguments", out var args) &&
                    args.TryGetProperty("torrent-added", out var torrent) &&
                    torrent.TryGetProperty("hashString", out var hash))
                {
                    _logger.LogInformation("[Transmission] Torrent added: {Hash}", hash.GetString());
                    return hash.GetString();
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Transmission] Error adding torrent");
            return null;
        }
    }

    /// <summary>
    /// Get all torrents
    /// </summary>
    public async Task<List<TransmissionTorrent>?> GetTorrentsAsync(DownloadClient config)
    {
        try
        {
            ConfigureClient(config);

            var arguments = new
            {
                fields = new[] { "id", "hashString", "name", "totalSize", "percentDone",
                                "downloadedEver", "uploadedEver", "status", "eta",
                                "rateDownload", "rateUpload", "downloadDir", "addedDate",
                                "doneDate" }
            };

            var response = await SendRpcRequestAsync(config, "torrent-get", arguments);

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("arguments", out var args) &&
                    args.TryGetProperty("torrents", out var torrents))
                {
                    return JsonSerializer.Deserialize<List<TransmissionTorrent>>(torrents.GetRawText());
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Transmission] Error getting torrents");
            return null;
        }
    }

    /// <summary>
    /// Get torrent by hash
    /// </summary>
    public async Task<TransmissionTorrent?> GetTorrentAsync(DownloadClient config, string hash)
    {
        var torrents = await GetTorrentsAsync(config);
        return torrents?.FirstOrDefault(t => t.HashString.Equals(hash, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Start torrent
    /// </summary>
    public async Task<bool> StartTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, "torrent-start", hash);
    }

    /// <summary>
    /// Stop torrent
    /// </summary>
    public async Task<bool> StopTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, "torrent-stop", hash);
    }

    /// <summary>
    /// Get torrent status for download monitoring
    /// </summary>
    public async Task<DownloadClientStatus?> GetTorrentStatusAsync(DownloadClient config, string hash)
    {
        try
        {
            var torrents = await GetTorrentsAsync(config);
            var torrent = torrents?.FirstOrDefault(t => t.HashString.Equals(hash, StringComparison.OrdinalIgnoreCase));

            if (torrent == null)
                return null;

            var status = torrent.Status switch
            {
                0 => "paused",  // stopped
                1 or 2 => "queued",  // check pending or checking
                3 => "queued",  // download pending
                4 => "downloading",  // downloading
                5 => "completed",  // seed pending
                6 => "completed",  // seeding
                _ => "downloading"
            };

            var timeRemaining = torrent.Eta > 0 && torrent.Eta < int.MaxValue
                ? TimeSpan.FromSeconds(torrent.Eta)
                : (TimeSpan?)null;

            return new DownloadClientStatus
            {
                Status = status,
                Progress = torrent.PercentDone * 100, // Convert 0-1 to 0-100
                Downloaded = torrent.DownloadedEver,
                Size = torrent.TotalSize,
                TimeRemaining = timeRemaining,
                SavePath = torrent.DownloadDir
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Transmission] Error getting torrent status");
            return null;
        }
    }

    /// <summary>
    /// Delete torrent
    /// </summary>
    public async Task<bool> DeleteTorrentAsync(DownloadClient config, string hash, bool deleteFiles = false)
    {
        try
        {
            ConfigureClient(config);

            // Find torrent ID by hash
            var torrents = await GetTorrentsAsync(config);
            var torrent = torrents?.FirstOrDefault(t => t.HashString.Equals(hash, StringComparison.OrdinalIgnoreCase));

            if (torrent == null) return false;

            var arguments = new
            {
                ids = new[] { torrent.Id },
                deleteLocalData = deleteFiles
            };

            var response = await SendRpcRequestAsync(config, "torrent-remove", arguments);
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Transmission] Error deleting torrent");
            return false;
        }
    }

    // Private helper methods

    private void ConfigureClient(DownloadClient config)
    {
        var protocol = config.UseSsl ? "https" : "http";
        _httpClient.BaseAddress = new Uri($"{protocol}://{config.Host}:{config.Port}/transmission/rpc");

        if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.Username}:{config.Password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    private async Task<string?> SendRpcRequestAsync(DownloadClient config, string method, object? arguments)
    {
        try
        {
            var request = new
            {
                method = method,
                arguments = arguments ?? new { }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json"
            );

            if (!string.IsNullOrEmpty(_sessionId))
            {
                content.Headers.Add("X-Transmission-Session-Id", _sessionId);
            }

            var response = await _httpClient.PostAsync("", content);

            // Handle session ID requirement (409 Conflict)
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                if (response.Headers.TryGetValues("X-Transmission-Session-Id", out var sessionIds))
                {
                    _sessionId = sessionIds.FirstOrDefault();
                    _logger.LogInformation("[Transmission] Got new session ID");

                    // Retry with session ID
                    content.Headers.Remove("X-Transmission-Session-Id");
                    content.Headers.Add("X-Transmission-Session-Id", _sessionId);
                    response = await _httpClient.PostAsync("", content);
                }
            }

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            _logger.LogWarning("[Transmission] RPC request failed: {Status}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Transmission] RPC request error");
            return null;
        }
    }

    private async Task<bool> ControlTorrentAsync(DownloadClient config, string method, string hash)
    {
        try
        {
            ConfigureClient(config);

            var torrents = await GetTorrentsAsync(config);
            var torrent = torrents?.FirstOrDefault(t => t.HashString.Equals(hash, StringComparison.OrdinalIgnoreCase));

            if (torrent == null) return false;

            var arguments = new
            {
                ids = new[] { torrent.Id }
            };

            var response = await SendRpcRequestAsync(config, method, arguments);
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Transmission] Error controlling torrent");
            return false;
        }
    }
}

/// <summary>
/// Transmission torrent information
/// </summary>
public class TransmissionTorrent
{
    public int Id { get; set; }
    public string HashString { get; set; } = "";
    public string Name { get; set; } = "";
    public long TotalSize { get; set; }
    public double PercentDone { get; set; } // 0-1
    public long DownloadedEver { get; set; }
    public long UploadedEver { get; set; }
    public int Status { get; set; } // 0=stopped, 1=check pending, 2=checking, 3=download pending, 4=downloading, 5=seed pending, 6=seeding
    public int Eta { get; set; } // Seconds remaining
    public long RateDownload { get; set; } // bytes/s
    public long RateUpload { get; set; } // bytes/s
    public string DownloadDir { get; set; } = "";
    public long AddedDate { get; set; } // Unix timestamp
    public long DoneDate { get; set; } // Unix timestamp
}
