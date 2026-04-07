using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Transmission RPC client for Sportarr
/// Implements Transmission RPC protocol for torrent management
/// </summary>
public class TransmissionClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TransmissionClient> _logger;
    // Session IDs are keyed by server URL + auth credentials so concurrent
    // requests sharing a cached TransmissionClient instance do not stomp on
    // each other's session state.
    private static readonly ConcurrentDictionary<string, string> _sessionIds = new();
    private static readonly JsonSerializerOptions _transmissionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private string? _baseUrl;
    private string? _authCredentials;
    private HttpClient? _customHttpClient; // For SSL bypass

    public TransmissionClient(HttpClient httpClient, ILogger<TransmissionClient> logger)
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
        catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
        {
            _logger.LogError(ex,
                "[Transmission] SSL/TLS connection failed for {Host}:{Port}. " +
                "This usually means SSL is enabled in Sportarr but the port is serving HTTP, not HTTPS. " +
                "Please ensure HTTPS is enabled in Transmission settings, or disable SSL in Sportarr.",
                config.Host, config.Port);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Transmission] Connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Add torrent from URL
    /// NOTE: Does NOT specify download-dir - Transmission uses its own configured directory
    /// This matches Sonarr/Radarr behavior
    /// </summary>
    public async Task<string?> AddTorrentAsync(DownloadClient config, string torrentUrl, string category, double? seedRatioLimit = null, int? seedTimeLimitMinutes = null)
    {
        try
        {
            ConfigureClient(config);

            // Note: Transmission doesn't have built-in category support like qBittorrent
            // Categories would need to be handled via labels (which requires transmission-daemon 3.0+)
            // For now, we don't set downloadDir - Transmission will use its configured default
            // Handle initial state (Started, ForceStarted, Stopped)
            var shouldPause = config.InitialState == TorrentInitialState.Stopped;
            if (shouldPause)
            {
                _logger.LogInformation("[Transmission] Adding torrent in STOPPED state (InitialState=Stopped)");
            }
            object arguments;
            if (!string.IsNullOrWhiteSpace(config.Directory))
            {
                arguments = new { filename = torrentUrl, paused = shouldPause, download_dir = config.Directory };
                _logger.LogInformation("[Transmission] Using directory override: {Directory}", config.Directory);
            }
            else
            {
                arguments = new { filename = torrentUrl, paused = shouldPause };
            }

            var response = await SendRpcRequestAsync(config, "torrent-add", arguments);

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("arguments", out var args) &&
                    args.TryGetProperty("torrent-added", out var torrent) &&
                    torrent.TryGetProperty("hashString", out var hash))
                {
                    var hashString = hash.GetString();
                    _logger.LogInformation("[Transmission] Torrent added: {Hash}", hashString);

                    // Apply per-torrent seed limits from indexer settings (matches Sonarr behavior)
                    if (hashString != null && (seedRatioLimit.HasValue || seedTimeLimitMinutes.HasValue))
                    {
                        await SetTorrentSeedingConfigurationAsync(config, hashString, seedRatioLimit, seedTimeLimitMinutes);
                    }

                    // Handle ForceStarted state - use torrent-start-now to bypass queue
                    if (config.InitialState == TorrentInitialState.ForceStarted && hashString != null)
                    {
                        _logger.LogInformation("[Transmission] Force starting torrent (InitialState=ForceStarted)");
                        await ForceStartTorrentAsync(config, hashString);
                    }

                    return hashString;
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
    /// Set per-torrent seeding configuration (matches Sonarr's TransmissionProxy.SetTorrentSeedingConfiguration)
    /// </summary>
    public async Task SetTorrentSeedingConfigurationAsync(DownloadClient config, string hash, double? seedRatioLimit, int? seedTimeLimitMinutes)
    {
        try
        {
            var torrents = await GetTorrentsAsync(config);
            var torrent = torrents?.FirstOrDefault(t => t.HashString.Equals(hash, StringComparison.OrdinalIgnoreCase));
            if (torrent == null) return;

            var setArgs = new Dictionary<string, object>
            {
                ["ids"] = new[] { torrent.Id }
            };

            if (seedRatioLimit.HasValue && seedRatioLimit.Value > 0)
            {
                setArgs["seedRatioLimit"] = seedRatioLimit.Value;
                setArgs["seedRatioMode"] = 1; // 1 = use per-torrent limit
                _logger.LogInformation("[Transmission] Setting seed ratio limit: {Ratio} for {Hash}", seedRatioLimit.Value, hash);
            }

            if (seedTimeLimitMinutes.HasValue && seedTimeLimitMinutes.Value > 0)
            {
                setArgs["seedIdleLimit"] = seedTimeLimitMinutes.Value;
                setArgs["seedIdleMode"] = 1; // 1 = use per-torrent limit
                _logger.LogInformation("[Transmission] Setting seed idle limit: {Minutes} min for {Hash}", seedTimeLimitMinutes.Value, hash);
            }

            // Only send if we have limits to set (not just ids)
            if (setArgs.Count > 1)
            {
                await SendRpcRequestAsync(config, "torrent-set", setArgs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Transmission] Failed to set seeding configuration for {Hash}", hash);
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
                    return JsonSerializer.Deserialize<List<TransmissionTorrent>>(torrents.GetRawText(), _transmissionJsonOptions);
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
    /// Get torrent by hash (fetches only the matching torrent via ids filter)
    /// </summary>
    private async Task<List<TransmissionTorrent>?> GetTorrentsByHashAsync(DownloadClient config, string hash)
    {
        try
        {
            ConfigureClient(config);

            var arguments = new
            {
                ids = new[] { hash },
                fields = new[] { "id", "hashString", "name", "totalSize", "percentDone",
                                "downloadedEver", "uploadedEver", "status", "eta",
                                "rateDownload", "rateUpload", "downloadDir", "addedDate",
                                "doneDate" }
            };

            var requestJson = JsonSerializer.Serialize(new { method = "torrent-get", arguments });
            var response = await SendRpcRequestAsync(config, "torrent-get", arguments);

            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("arguments", out var args) &&
                    args.TryGetProperty("torrents", out var torrents))
                {
                    return JsonSerializer.Deserialize<List<TransmissionTorrent>>(torrents.GetRawText(), _transmissionJsonOptions);
                }
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[Transmission] Get torrents by hash cancelled (app shutting down or timeout): {Hash}", hash);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Transmission] Error getting torrent by hash: {Hash}", hash);
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
    /// Force start torrent (bypass queue limits)
    /// </summary>
    public async Task<bool> ForceStartTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, "torrent-start-now", hash);
    }

    /// <summary>
    /// Stop torrent
    /// </summary>
    public async Task<bool> StopTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, "torrent-stop", hash);
    }

    /// <summary>
    /// Pause torrent (same as stop in Transmission)
    /// </summary>
    public async Task<bool> PauseTorrentAsync(DownloadClient config, string hash)
    {
        return await StopTorrentAsync(config, hash);
    }

    /// <summary>
    /// Resume torrent (same as start in Transmission)
    /// </summary>
    public async Task<bool> ResumeTorrentAsync(DownloadClient config, string hash)
    {
        return await StartTorrentAsync(config, hash);
    }

    /// <summary>
    /// Get torrent status for download monitoring
    /// </summary>
    public async Task<DownloadClientStatus?> GetTorrentStatusAsync(DownloadClient config, string hash)
    {
        try
        {
            var torrents = await GetTorrentsByHashAsync(config, hash);
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

            // Append torrent name so the returned path points at the actual
            // torrent file/folder rather than the parent download directory.
            var computedSavePath = !string.IsNullOrEmpty(torrent.Name)
                ? Path.Combine(torrent.DownloadDir, torrent.Name)
                : torrent.DownloadDir;

            return new DownloadClientStatus
            {
                Status = status,
                Progress = torrent.PercentDone * 100, // Convert 0-1 to 0-100
                Downloaded = torrent.DownloadedEver,
                Size = torrent.TotalSize,
                TimeRemaining = timeRemaining,
                SavePath = computedSavePath,
                Ratio = torrent.DownloadedEver > 0
                    ? (double)torrent.UploadedEver / torrent.DownloadedEver
                    : 0,
                CompletedAt = torrent.DoneDate > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(torrent.DoneDate).UtcDateTime
                    : null
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
        var urlBase = string.IsNullOrEmpty(config.UrlBase) ? "/transmission" : config.UrlBase;

        if (!urlBase.StartsWith("/"))
            urlBase = "/" + urlBase;
        urlBase = urlBase.TrimEnd('/');

        _baseUrl = $"{protocol}://{config.Host}:{config.Port}{urlBase}/rpc";

        if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
        {
            _authCredentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.Username}:{config.Password}"));
        }
    }

    private async Task<string?> SendRpcRequestAsync(DownloadClient config, string method, object? arguments)
    {
        try
        {
            var client = GetHttpClient(config);
            var request = new
            {
                method = method,
                arguments = arguments ?? new { }
            };

            var requestJson = JsonSerializer.Serialize(request);

            var sessionKey = $"{_baseUrl ?? string.Empty}\0{_authCredentials ?? string.Empty}";
            _sessionIds.TryGetValue(sessionKey, out var sessionId);

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(sessionId))
                requestMessage.Headers.Add("X-Transmission-Session-Id", sessionId);
            if (!string.IsNullOrEmpty(_authCredentials))
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authCredentials);

            var response = await client.SendAsync(requestMessage);

            // Handle session ID requirement (409 Conflict)
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                if (response.Headers.TryGetValues("X-Transmission-Session-Id", out var sessionIds))
                {
                    var newSessionId = sessionIds.FirstOrDefault();
                    if (string.IsNullOrEmpty(newSessionId))
                    {
                        _logger.LogWarning("[Transmission] 409 received but no session ID in response headers");
                        return null;
                    }

                    _sessionIds[sessionKey] = newSessionId;
                    _logger.LogInformation("[Transmission] Got new session ID");

                    // Retry with new session ID (must create new request message)
                    var retryMessage = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
                    {
                        Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                    };
                    retryMessage.Headers.Add("X-Transmission-Session-Id", newSessionId);
                    if (!string.IsNullOrEmpty(_authCredentials))
                        retryMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authCredentials);

                    response = await client.SendAsync(retryMessage);
                }
            }

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            _logger.LogWarning("[Transmission] RPC request failed: {Status}", response.StatusCode);
            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[Transmission] RPC request cancelled (app shutting down or timeout): {Method}", method);
            return null;
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("[Transmission] RPC request cancelled - HttpClient disposed (app shutting down): {Method}", method);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Transmission] RPC request error for method: {Method}", method);
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
