using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Deluge Web API client for Sportarr
/// Implements Deluge WebUI JSON-RPC protocol
/// </summary>
public class DelugeClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DelugeClient> _logger;
    private string? _cookie;
    private HttpClient? _customHttpClient; // For SSL bypass

    public DelugeClient(HttpClient httpClient, ILogger<DelugeClient> logger)
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
                // Copy cookie if we have one
                if (_cookie != null)
                {
                    _customHttpClient.DefaultRequestHeaders.Add("Cookie", _cookie);
                }
            }
            return _customHttpClient;
        }

        return _httpClient;
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
            var response = await SendRpcRequestAsync(config, "daemon.info", null);
            return response != null;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
        {
            _logger.LogError(ex,
                "[Deluge] SSL/TLS connection failed for {Host}:{Port}. " +
                "This usually means SSL is enabled in Sportarr but the port is serving HTTP, not HTTPS. " +
                "Please ensure HTTPS is enabled in Deluge settings, or disable SSL in Sportarr.",
                config.Host, config.Port);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] Connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Add torrent from URL
    /// NOTE: Does NOT specify download_location - Deluge uses its own configured directory
    /// This matches Sonarr/Radarr behavior
    ///
    /// Uses core.add_torrent_file instead of core.add_torrent_url to avoid SSL/HTTPS issues
    /// with Prowlarr proxy URLs. Downloads the torrent file first, then sends as base64.
    /// </summary>
    public async Task<string?> AddTorrentAsync(DownloadClient config, string torrentUrl, string category)
    {
        try
        {
            ConfigureClient(config);

            if (!await LoginAsync(config))
            {
                _logger.LogError("[Deluge] Login failed, cannot add torrent");
                return null;
            }

            // Download the torrent file first (like Sonarr/Radarr do)
            // This avoids Deluge's SSL/HTTPS issues with Prowlarr proxy URLs
            _logger.LogDebug("[Deluge] Downloading torrent file from URL: {Url}", torrentUrl);

            byte[] torrentBytes;
            try
            {
                var httpClient = GetHttpClient(config);
                torrentBytes = await httpClient.GetByteArrayAsync(torrentUrl);
                _logger.LogDebug("[Deluge] Downloaded {Bytes} bytes", torrentBytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Deluge] Failed to download torrent file from URL: {Url}", torrentUrl);
                return null;
            }

            // Convert to base64 for Deluge API
            var base64Content = Convert.ToBase64String(torrentBytes);

            // Deluge doesn't specify download location - it uses the configured default
            // Category/label could be set via label plugin, but for now we keep it simple
            // Handle initial state (Started, ForceStarted, Stopped)
            var shouldPause = config.InitialState == TorrentInitialState.Stopped;
            if (shouldPause)
            {
                _logger.LogInformation("[Deluge] Adding torrent in STOPPED state (InitialState=Stopped)");
            }
            var options = new Dictionary<string, object>
            {
                // No download_location - Deluge will use its configured default
                ["add_paused"] = shouldPause
            };

            // Send to Deluge using core.add_torrent_file (matches Sonarr/Radarr implementation)
            _logger.LogDebug("[Deluge] Adding torrent file to Deluge");
            var response = await SendRpcRequestAsync(config, "core.add_torrent_file",
                new object[] { "download.torrent", base64Content, options });

            if (response == null)
            {
                _logger.LogError("[Deluge] Add torrent request returned null response");
                return null;
            }

            var doc = JsonDocument.Parse(response);
            if (!doc.RootElement.TryGetProperty("result", out var result))
            {
                _logger.LogError("[Deluge] Response missing 'result' property: {Response}", response);
                return null;
            }

            // Handle different result types
            if (result.ValueKind == JsonValueKind.String)
            {
                var hash = result.GetString();
                _logger.LogInformation("[Deluge] Torrent added successfully: {Hash}", hash);

                // Handle ForceStarted state - resume and move to top of queue
                if (config.InitialState == TorrentInitialState.ForceStarted && hash != null)
                {
                    _logger.LogInformation("[Deluge] Force starting torrent (InitialState=ForceStarted)");
                    await ResumeTorrentAsync(config, hash);
                    await SendRpcRequestAsync(config, "core.queue_top", new object[] { new[] { hash } });
                }

                return hash;
            }
            else if (result.ValueKind == JsonValueKind.Null)
            {
                // Check if there's an error property for more details
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    _logger.LogError("[Deluge] Add torrent failed with error: {Error}", error.ToString());
                }
                else
                {
                    _logger.LogError("[Deluge] Add torrent returned null - torrent may already exist in Deluge");
                }
                return null;
            }
            else if (result.ValueKind == JsonValueKind.False)
            {
                _logger.LogError("[Deluge] Add torrent returned false - operation failed");
                return null;
            }
            else
            {
                _logger.LogError("[Deluge] Unexpected result type: {Type}, Value: {Value}",
                    result.ValueKind, result.ToString());
                return null;
            }
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

            var response = await SendRpcRequestAsync(config, "core.get_torrents_status", new object[] { new { }, fields });

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
    /// Get torrent status for download monitoring
    /// </summary>
    public async Task<DownloadClientStatus?> GetTorrentStatusAsync(DownloadClient config, string hash)
    {
        var torrent = await GetTorrentAsync(config, hash);
        if (torrent == null)
        {
            _logger.LogWarning("[Deluge] Torrent not found: {Hash}", hash);
            return null;
        }

        // Map Deluge state to standard status
        var status = torrent.State.ToLowerInvariant() switch
        {
            "downloading" => "downloading",
            "seeding" or "uploading" => "completed",
            "paused" => "paused",
            "queued" => "queued",
            "checking" or "allocating" => "queued",
            "error" => "failed",
            _ => "downloading"
        };

        // Calculate time remaining
        TimeSpan? timeRemaining = null;
        if (torrent.Eta > 0 && torrent.Eta < int.MaxValue)
        {
            timeRemaining = TimeSpan.FromSeconds(torrent.Eta);
        }

        return new DownloadClientStatus
        {
            Status = status,
            Progress = torrent.Progress * 100, // Deluge returns 0-1, convert to 0-100
            Downloaded = torrent.TotalDone,
            Size = torrent.TotalSize,
            TimeRemaining = timeRemaining,
            SavePath = torrent.SavePath,
            ErrorMessage = status == "failed" ? $"Torrent in error state: {torrent.State}" : null
        };
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

            var response = await SendRpcRequestAsync(config, "core.remove_torrent", new object[] { hash, deleteFiles });
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
        var client = GetHttpClient(config);
        var protocol = config.UseSsl ? "https" : "http";

        // Deluge Web UI defaults to root path, not /deluge
        // Use configured URL base or default to empty (root)
        // Users can set urlBase to:
        //   - null or "" (empty) for default root installations (http://host:port/json)
        //   - "/deluge" for installations with subdirectory (http://host:port/deluge/json)
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

        client.BaseAddress = new Uri($"{protocol}://{config.Host}:{config.Port}{urlBase}/json");
    }

    private async Task<bool> LoginAsync(DownloadClient config)
    {
        if (_cookie != null)
        {
            return true; // Already logged in
        }

        try
        {
            var response = await SendRpcRequestAsync(config, "auth.login", new[] { config.Password ?? "" });

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("result", out var result))
                {
                    // Handle null result (means already authenticated)
                    if (result.ValueKind == JsonValueKind.Null)
                    {
                        _logger.LogInformation("[Deluge] Already authenticated (received null result)");
                        return true;
                    }

                    // Handle boolean result
                    if (result.ValueKind == JsonValueKind.True || (result.ValueKind == JsonValueKind.False && result.GetBoolean()))
                    {
                        _logger.LogInformation("[Deluge] Login successful");
                        return true;
                    }
                }
            }

            _logger.LogWarning("[Deluge] Login failed - invalid response");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] Login error");
            return false;
        }
    }

    private async Task<string?> SendRpcRequestAsync(DownloadClient config, string method, object? parameters)
    {
        try
        {
            var client = GetHttpClient(config);
            var requestId = new Random().Next(1, 10000);
            var request = new
            {
                method = method,
                @params = parameters ?? Array.Empty<object>(),
                id = requestId
            };

            var requestJson = JsonSerializer.Serialize(request);
            _logger.LogDebug("[Deluge] RPC Request: Method={Method}, URL={Url}", method, client.BaseAddress);
            _logger.LogTrace("[Deluge] RPC Request Body: {Body}", requestJson);

            // Note: Deluge's JSON-RPC API rejects "application/json; charset=utf-8"
            // It only accepts "application/json" without the charset suffix
            // So we create StringContent without mediaType and set the header manually
            var content = new StringContent(requestJson, Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            if (!string.IsNullOrEmpty(_cookie))
            {
                client.DefaultRequestHeaders.Add("Cookie", _cookie);
            }

            var response = await client.PostAsync("", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("[Deluge] RPC Response: Status={StatusCode}, Method={Method}", response.StatusCode, method);
            _logger.LogTrace("[Deluge] RPC Response Body: {Body}", responseBody);

            // Store cookie from response
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                _cookie = cookies.FirstOrDefault();
                _logger.LogDebug("[Deluge] Session cookie updated");
                // Also update custom client if it exists
                if (_customHttpClient != null && _customHttpClient.DefaultRequestHeaders.Contains("Cookie"))
                {
                    _customHttpClient.DefaultRequestHeaders.Remove("Cookie");
                    _customHttpClient.DefaultRequestHeaders.Add("Cookie", _cookie);
                }
            }

            if (response.IsSuccessStatusCode)
            {
                // Check for JSON-RPC error in response
                try
                {
                    var doc = JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                    {
                        _logger.LogWarning("[Deluge] RPC returned error: {Error}", error.ToString());
                    }
                }
                catch { /* Ignore parse errors for logging */ }

                return responseBody;
            }

            _logger.LogWarning("[Deluge] RPC request failed: {Status} - {Response}", response.StatusCode, responseBody);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Deluge] HTTP request error for method {Method}: {Message}", method, ex.Message);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "[Deluge] Request timeout for method {Method}", method);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] RPC request error for method {Method}", method);
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

            var response = await SendRpcRequestAsync(config, method, hashes);
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
/// Deluge API returns snake_case JSON, so we use JsonPropertyName attributes
/// </summary>
public class DelugeTorrent
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("total_size")]
    public long TotalSize { get; set; }

    [JsonPropertyName("progress")]
    public double Progress { get; set; } // Deluge returns 0-1 (0.0 to 1.0)

    [JsonPropertyName("total_done")]
    public long TotalDone { get; set; }

    [JsonPropertyName("total_uploaded")]
    public long TotalUploaded { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = ""; // Downloading, Seeding, Paused, Error, etc.

    [JsonPropertyName("eta")]
    public int Eta { get; set; } // Seconds remaining

    [JsonPropertyName("download_payload_rate")]
    public long DownloadPayloadRate { get; set; } // bytes/s

    [JsonPropertyName("upload_payload_rate")]
    public long UploadPayloadRate { get; set; } // bytes/s

    [JsonPropertyName("save_path")]
    public string SavePath { get; set; } = "";

    [JsonPropertyName("time_added")]
    public long TimeAdded { get; set; } // Unix timestamp
}
