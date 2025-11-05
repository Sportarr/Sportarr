using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fightarr.Api.Models;

namespace Fightarr.Api.Services;

/// <summary>
/// qBittorrent Web API client for Fightarr
/// Implements qBittorrent WebUI API v2 for torrent management
/// </summary>
public class QBittorrentClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<QBittorrentClient> _logger;
    private string? _cookie;

    public QBittorrentClient(HttpClient httpClient, ILogger<QBittorrentClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Test connection to qBittorrent
    /// </summary>
    public async Task<bool> TestConnectionAsync(DownloadClient config)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);

            // Login
            if (!await LoginAsync(baseUrl, config.Username, config.Password))
            {
                return false;
            }

            // Test API version
            var response = await _httpClient.GetAsync($"{baseUrl}/api/v2/app/version");
            if (response.IsSuccessStatusCode)
            {
                var version = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("[qBittorrent] Connected successfully. Version: {Version}", version);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Add torrent from URL
    /// </summary>
    public async Task<string?> AddTorrentAsync(DownloadClient config, string torrentUrl, string category)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);

            if (!await LoginAsync(baseUrl, config.Username, config.Password))
            {
                _logger.LogError("[qBittorrent] Login failed");
                return null;
            }

            // NOTE: We do NOT specify savepath - qBittorrent uses its own configured download directory
            // The category will create a subdirectory within the download client's save path
            // This matches Sonarr/Radarr behavior
            var content = new MultipartFormDataContent
            {
                { new StringContent(torrentUrl), "urls" },
                { new StringContent(category), "category" },
                { new StringContent("false"), "paused" } // Start immediately (Sonarr behavior)
            };

            var response = await _httpClient.PostAsync($"{baseUrl}/api/v2/torrents/add", content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[qBittorrent] Torrent added successfully: {Url}", torrentUrl);

                // Get torrent hash from recent torrents
                await Task.Delay(500); // Wait for torrent to be added
                var torrents = await GetTorrentsAsync(config);
                var recentTorrent = torrents?.OrderByDescending(t => t.AddedOn).FirstOrDefault();

                return recentTorrent?.Hash;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("[qBittorrent] Failed to add torrent: {Error}", error);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Error adding torrent");
            return null;
        }
    }

    /// <summary>
    /// Get all torrents
    /// </summary>
    public async Task<List<QBittorrentTorrent>?> GetTorrentsAsync(DownloadClient config)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);

            if (!await LoginAsync(baseUrl, config.Username, config.Password))
            {
                return null;
            }

            var response = await _httpClient.GetAsync($"{baseUrl}/api/v2/torrents/info");

            if (response.IsSuccessStatusCode)
            {
                var torrents = await response.Content.ReadFromJsonAsync<List<QBittorrentTorrent>>();
                return torrents;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Error getting torrents");
            return null;
        }
    }

    /// <summary>
    /// Get torrent by hash
    /// </summary>
    public async Task<QBittorrentTorrent?> GetTorrentAsync(DownloadClient config, string hash)
    {
        var torrents = await GetTorrentsAsync(config);
        return torrents?.FirstOrDefault(t => t.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get torrent status for download monitoring
    /// </summary>
    public async Task<DownloadClientStatus?> GetTorrentStatusAsync(DownloadClient config, string hash)
    {
        var torrent = await GetTorrentAsync(config, hash);
        if (torrent == null)
            return null;

        var status = torrent.State.ToLowerInvariant() switch
        {
            "downloading" => "downloading",
            "uploading" or "stalledup" => "completed",
            "pauseddl" or "pausedup" => "paused",
            "queueddl" or "queuedup" or "allocating" or "metadl" => "queued",
            "error" or "missingfiles" => "failed",
            _ => "downloading"
        };

        var timeRemaining = torrent.Eta > 0 && torrent.Eta < int.MaxValue
            ? TimeSpan.FromSeconds(torrent.Eta)
            : (TimeSpan?)null;

        return new DownloadClientStatus
        {
            Status = status,
            Progress = torrent.Progress * 100, // Convert 0-1 to 0-100
            Downloaded = torrent.Downloaded,
            Size = torrent.Size,
            TimeRemaining = timeRemaining,
            SavePath = torrent.SavePath,
            ErrorMessage = status == "failed" ? $"Torrent in error state: {torrent.State}" : null
        };
    }

    /// <summary>
    /// Resume torrent
    /// </summary>
    public async Task<bool> ResumeTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, hash, "resume");
    }

    /// <summary>
    /// Pause torrent
    /// </summary>
    public async Task<bool> PauseTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, hash, "pause");
    }

    /// <summary>
    /// Delete torrent
    /// </summary>
    public async Task<bool> DeleteTorrentAsync(DownloadClient config, string hash, bool deleteFiles = false)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);

            if (!await LoginAsync(baseUrl, config.Username, config.Password))
            {
                return false;
            }

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("deleteFiles", deleteFiles.ToString().ToLower())
            });

            var response = await _httpClient.PostAsync($"{baseUrl}/api/v2/torrents/delete", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Error deleting torrent");
            return false;
        }
    }

    // Private helper methods

    private async Task<bool> LoginAsync(string baseUrl, string? username, string? password)
    {
        if (_cookie != null)
        {
            return true; // Already logged in
        }

        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", username ?? "admin"),
                new KeyValuePair<string, string>("password", password ?? "")
            });

            var response = await _httpClient.PostAsync($"{baseUrl}/api/v2/auth/login", content);

            if (response.IsSuccessStatusCode)
            {
                // Store cookie for subsequent requests
                if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    _cookie = cookies.FirstOrDefault();
                    _httpClient.DefaultRequestHeaders.Add("Cookie", _cookie);
                    _logger.LogInformation("[qBittorrent] Login successful");
                    return true;
                }
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("[qBittorrent] Login failed: {Error}", error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Login error");
            return false;
        }
    }

    private async Task<bool> ControlTorrentAsync(DownloadClient config, string hash, string action)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);

            if (!await LoginAsync(baseUrl, config.Username, config.Password))
            {
                return false;
            }

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash)
            });

            var response = await _httpClient.PostAsync($"{baseUrl}/api/v2/torrents/{action}", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Error controlling torrent: {Action}", action);
            return false;
        }
    }

    private static string GetBaseUrl(DownloadClient config)
    {
        var protocol = config.UseSsl ? "https" : "http";
        return $"{protocol}://{config.Host}:{config.Port}";
    }
}

/// <summary>
/// qBittorrent torrent information
/// </summary>
public class QBittorrentTorrent
{
    public string Hash { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public double Progress { get; set; } // 0-1
    public long Downloaded { get; set; }
    public long Uploaded { get; set; }
    public string State { get; set; } = ""; // downloading, uploading, pausedDL, etc.
    public long Eta { get; set; } // Estimated time remaining in seconds (can be 8640000 for infinity)
    public long DlSpeed { get; set; } // Download speed in bytes/s
    public long UpSpeed { get; set; } // Upload speed in bytes/s
    public string SavePath { get; set; } = "";
    public string Category { get; set; } = "";
    public long AddedOn { get; set; } // Unix timestamp
    public long CompletedOn { get; set; } // Unix timestamp
}
