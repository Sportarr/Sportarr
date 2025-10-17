using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fightarr.Api.Models;

namespace Fightarr.Api.Services;

/// <summary>
/// Deluge Web API client for Fightarr
/// Implements Deluge WebUI JSON-RPC protocol
/// </summary>
public class DelugeClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DelugeClient> _logger;
    private string? _cookie;

    public DelugeClient(HttpClient httpClient, ILogger<DelugeClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Test connection to Deluge
    /// </summary>
    public async Task<bool> TestConnectionAsync(DownloadClient config)
    {
        try
        {
            ConfigureClient(config);

            if (!await LoginAsync(config))
            {
                return false;
            }

            // Test connection with daemon.info method
            var response = await SendRpcRequestAsync("daemon.info", null);
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] Connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Add torrent from URL
    /// </summary>
    public async Task<string?> AddTorrentAsync(DownloadClient config, string torrentUrl, string downloadLocation)
    {
        try
        {
            ConfigureClient(config);

            if (!await LoginAsync(config))
            {
                return null;
            }

            var options = new
            {
                download_location = downloadLocation
            };

            var response = await SendRpcRequestAsync("core.add_torrent_url", new object[] { torrentUrl, options });

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
                {
                    var hash = result.GetString();
                    _logger.LogInformation("[Deluge] Torrent added: {Hash}", hash);
                    return hash;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] Error adding torrent");
            return null;
        }
    }

    /// <summary>
    /// Get all torrents
    /// </summary>
    public async Task<List<DelugeTorrent>?> GetTorrentsAsync(DownloadClient config)
    {
        try
        {
            ConfigureClient(config);

            if (!await LoginAsync(config))
            {
                return null;
            }

            var fields = new[] { "hash", "name", "total_size", "progress", "total_done",
                                "total_uploaded", "state", "eta", "download_payload_rate",
                                "upload_payload_rate", "save_path", "time_added" };

            var response = await SendRpcRequestAsync("core.get_torrents_status", new object[] { new { }, fields });

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("result", out var result))
                {
                    var torrents = new List<DelugeTorrent>();

                    foreach (var property in result.EnumerateObject())
                    {
                        var torrent = JsonSerializer.Deserialize<DelugeTorrent>(property.Value.GetRawText());
                        if (torrent != null)
                        {
                            torrent.Hash = property.Name;
                            torrents.Add(torrent);
                        }
                    }

                    return torrents;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] Error getting torrents");
            return null;
        }
    }

    /// <summary>
    /// Get torrent by hash
    /// </summary>
    public async Task<DelugeTorrent?> GetTorrentAsync(DownloadClient config, string hash)
    {
        var torrents = await GetTorrentsAsync(config);
        return torrents?.FirstOrDefault(t => t.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resume torrent
    /// </summary>
    public async Task<bool> ResumeTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, "core.resume_torrent", new[] { hash });
    }

    /// <summary>
    /// Pause torrent
    /// </summary>
    public async Task<bool> PauseTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, "core.pause_torrent", new[] { hash });
    }

    /// <summary>
    /// Delete torrent
    /// </summary>
    public async Task<bool> DeleteTorrentAsync(DownloadClient config, string hash, bool deleteFiles = false)
    {
        try
        {
            ConfigureClient(config);

            if (!await LoginAsync(config))
            {
                return false;
            }

            var response = await SendRpcRequestAsync("core.remove_torrent", new object[] { hash, deleteFiles });
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] Error deleting torrent");
            return false;
        }
    }

    // Private helper methods

    private void ConfigureClient(DownloadClient config)
    {
        var protocol = config.UseSsl ? "https" : "http";
        _httpClient.BaseAddress = new Uri($"{protocol}://{config.Host}:{config.Port}/json");
    }

    private async Task<bool> LoginAsync(DownloadClient config)
    {
        if (_cookie != null)
        {
            return true; // Already logged in
        }

        try
        {
            var response = await SendRpcRequestAsync("auth.login", new[] { config.Password ?? "" });

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("result", out var result) && result.GetBoolean())
                {
                    _logger.LogInformation("[Deluge] Login successful");
                    return true;
                }
            }

            _logger.LogWarning("[Deluge] Login failed");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] Login error");
            return false;
        }
    }

    private async Task<string?> SendRpcRequestAsync(string method, object? parameters)
    {
        try
        {
            var requestId = new Random().Next(1, 10000);
            var request = new
            {
                method = method,
                @params = parameters ?? Array.Empty<object>(),
                id = requestId
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json"
            );

            if (!string.IsNullOrEmpty(_cookie))
            {
                _httpClient.DefaultRequestHeaders.Add("Cookie", _cookie);
            }

            var response = await _httpClient.PostAsync("", content);

            // Store cookie from response
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                _cookie = cookies.FirstOrDefault();
            }

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            _logger.LogWarning("[Deluge] RPC request failed: {Status}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] RPC request error");
            return null;
        }
    }

    private async Task<bool> ControlTorrentAsync(DownloadClient config, string method, string[] hashes)
    {
        try
        {
            ConfigureClient(config);

            if (!await LoginAsync(config))
            {
                return false;
            }

            var response = await SendRpcRequestAsync(method, hashes);
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] Error controlling torrent");
            return false;
        }
    }
}

/// <summary>
/// Deluge torrent information
/// </summary>
public class DelugeTorrent
{
    public string Hash { get; set; } = "";
    public string Name { get; set; } = "";
    public long TotalSize { get; set; }
    public double Progress { get; set; } // 0-100
    public long TotalDone { get; set; }
    public long TotalUploaded { get; set; }
    public string State { get; set; } = ""; // Downloading, Seeding, Paused, Error, etc.
    public int Eta { get; set; } // Seconds remaining
    public long DownloadPayloadRate { get; set; } // bytes/s
    public long UploadPayloadRate { get; set; } // bytes/s
    public string SavePath { get; set; } = "";
    public long TimeAdded { get; set; } // Unix timestamp
}
