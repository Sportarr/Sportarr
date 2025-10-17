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
            _httpClient.BaseAddress = new Uri(baseUrl);

            // Login
            if (!await LoginAsync(config.Username, config.Password))
            {
                return false;
            }

            // Test API version
            var response = await _httpClient.GetAsync("/api/v2/app/version");
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
    public async Task<string?> AddTorrentAsync(DownloadClient config, string torrentUrl, string savePath, string category)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);
            _httpClient.BaseAddress = new Uri(baseUrl);

            if (!await LoginAsync(config.Username, config.Password))
            {
                _logger.LogError("[qBittorrent] Login failed");
                return null;
            }

            var content = new MultipartFormDataContent
            {
                { new StringContent(torrentUrl), "urls" },
                { new StringContent(savePath), "savepath" },
                { new StringContent(category), "category" },
                { new StringContent("true"), "paused" } // Add paused initially
            };

            var response = await _httpClient.PostAsync("/api/v2/torrents/add", content);

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
            _httpClient.BaseAddress = new Uri(baseUrl);

            if (!await LoginAsync(config.Username, config.Password))
            {
                return null;
            }

            var response = await _httpClient.GetAsync("/api/v2/torrents/info");

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
            _httpClient.BaseAddress = new Uri(baseUrl);

            if (!await LoginAsync(config.Username, config.Password))
            {
                return false;
            }

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("deleteFiles", deleteFiles.ToString().ToLower())
            });

            var response = await _httpClient.PostAsync("/api/v2/torrents/delete", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Error deleting torrent");
            return false;
        }
    }

    // Private helper methods

    private async Task<bool> LoginAsync(string? username, string? password)
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

            var response = await _httpClient.PostAsync("/api/v2/auth/login", content);

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
            _httpClient.BaseAddress = new Uri(baseUrl);

            if (!await LoginAsync(config.Username, config.Password))
            {
                return false;
            }

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash)
            });

            var response = await _httpClient.PostAsync($"/api/v2/torrents/{action}", content);
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
    public int Eta { get; set; } // Estimated time remaining in seconds
    public int DlSpeed { get; set; } // Download speed in bytes/s
    public int UpSpeed { get; set; } // Upload speed in bytes/s
    public string SavePath { get; set; } = "";
    public string Category { get; set; } = "";
    public long AddedOn { get; set; } // Unix timestamp
    public long CompletedOn { get; set; } // Unix timestamp
}
