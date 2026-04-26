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
    private string? _baseUrl;
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

            // Get version to verify full connectivity (matches Radarr behavior)
            // Try daemon.get_version first (Deluge 2.x), fall back to daemon.info (Deluge 1.x)
            var version = await GetVersionAsync(config);
            if (version == null)
            {
                _logger.LogError("[Deluge] Failed to get daemon version - connection test failed");
                return false;
            }

            _logger.LogInformation("[Deluge] Connected successfully, daemon version: {Version}", version);

            // Validate Label plugin is available if a category is configured
            if (!string.IsNullOrWhiteSpace(config.Category))
            {
                var labelAvailable = await IsLabelPluginEnabledAsync(config);
                if (!labelAvailable)
                {
                    _logger.LogWarning("[Deluge] Label plugin is not enabled in Deluge. " +
                        "Enable the Label plugin in Deluge preferences to use categories. " +
                        "Without it, Sportarr cannot filter its own downloads from other applications");
                }
                else
                {
                    // Ensure the configured category label exists
                    await EnsureLabelExistsAsync(config, config.Category);
                }
            }

            return true;
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
    public async Task<string?> AddTorrentAsync(DownloadClient config, string torrentUrl, string category, double? seedRatioLimit = null, int? seedTimeLimitMinutes = null)
    {
        try
        {
            ConfigureClient(config);

            if (!await LoginAsync(config))
            {
                _logger.LogError("[Deluge] Login failed, cannot add torrent");
                return null;
            }

            // Build Deluge options (shared between add_torrent_file and add_torrent_url)
            var shouldPause = config.InitialState == TorrentInitialState.Stopped;
            if (shouldPause)
            {
                _logger.LogInformation("[Deluge] Adding torrent in STOPPED state (InitialState=Stopped)");
            }
            var options = new Dictionary<string, object>
            {
                ["add_paused"] = shouldPause
            };

            if (!string.IsNullOrWhiteSpace(config.Directory))
            {
                options["download_location"] = config.Directory;
                _logger.LogInformation("[Deluge] Using directory override: {Directory}", config.Directory);
            }

            // Try to download the torrent file first and send as base64 (preferred - avoids
            // Deluge's SSL/HTTPS issues with Prowlarr proxy URLs). If the download fails
            // (e.g. redirect to a URL with an unparseable hostname from certain indexers),
            // fall back to core.add_torrent_url and let Deluge handle the download itself.
            _logger.LogDebug("[Deluge] Downloading torrent file from URL: {Url}", torrentUrl);

            string? response;
            byte[]? torrentBytes = null;
            try
            {
                var httpClient = GetHttpClient(config);
                torrentBytes = await httpClient.GetByteArrayAsync(torrentUrl);
                _logger.LogDebug("[Deluge] Downloaded {Bytes} bytes", torrentBytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Deluge] Failed to download torrent file from URL (will fall back to add_torrent_url): {Url}", torrentUrl);
            }

            if (torrentBytes != null && torrentBytes.Length > 0)
            {
                // Preferred path: send torrent file as base64 via core.add_torrent_file
                var base64Content = Convert.ToBase64String(torrentBytes);
                _logger.LogDebug("[Deluge] Adding torrent file to Deluge via add_torrent_file");
                response = await SendRpcRequestAsync(config, "core.add_torrent_file",
                    new object[] { "download.torrent", base64Content, options });
            }
            else
            {
                // Fallback: let Deluge download the torrent from the URL directly
                _logger.LogInformation("[Deluge] Falling back to add_torrent_url: {Url}", torrentUrl);
                response = await SendRpcRequestAsync(config, "core.add_torrent_url",
                    new object[] { torrentUrl, options });
            }

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

                // Set label via Label plugin (like Radarr does)
                if (hash != null && !string.IsNullOrWhiteSpace(category))
                {
                    await EnsureLabelExistsAsync(config, category);
                    await SetTorrentLabelAsync(config, hash, category);
                    _logger.LogInformation("[Deluge] Applied label '{Category}' to torrent {Hash}", category, hash);
                }

                // Apply per-torrent seed limits from indexer settings (matches Sonarr behavior)
                if (hash != null && seedRatioLimit.HasValue && seedRatioLimit.Value > 0)
                {
                    await SetTorrentSeedingConfigurationAsync(config, hash, seedRatioLimit.Value);
                }

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
    /// Set per-torrent seeding configuration (matches Sonarr's DelugeProxy.SetTorrentSeedingConfiguration)
    /// </summary>
    public async Task SetTorrentSeedingConfigurationAsync(DownloadClient config, string hash, double seedRatioLimit)
    {
        try
        {
            var ratioOptions = new Dictionary<string, object>
            {
                ["stop_ratio"] = seedRatioLimit,
                ["stop_at_ratio"] = true
            };

            await SendRpcRequestAsync(config, "core.set_torrent_options",
                new object[] { new[] { hash }, ratioOptions });

            _logger.LogInformation("[Deluge] Set seed ratio limit: {Ratio} for {Hash}", seedRatioLimit, hash);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Deluge] Failed to set seeding configuration for {Hash}", hash);
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

            // Request both save_path (Deluge 1.x) and download_location (Deluge 2.x canonical)
            // so we can tolerate either version. See DelugeTorrent.EffectiveSavePath below for
            // the selection logic — matches Radarr/Sonarr's DelugeProxy behavior.
            var fields = new[] { "hash", "name", "total_size", "progress", "total_done",
                                "total_uploaded", "state", "eta", "download_payload_rate",
                                "upload_payload_rate", "save_path", "download_location",
                                "ratio", "time_added", "label" };

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
    /// Returns the effective save path for a torrent, preferring download_location
    /// (Deluge 2.x canonical) over save_path (Deluge 1.x / deprecated alias).
    /// Also validates the path looks absolute and warns loudly if it doesn't —
    /// a relative/weird value usually means the Deluge Label plugin has an
    /// invalid "Move Completed" path configured (often the label name itself,
    /// which Deluge's UI auto-fills).
    /// </summary>
    private string GetEffectiveSavePath(DelugeTorrent torrent)
    {
        // Prefer download_location (Deluge 2.x), fall back to save_path (Deluge 1.x).
        var path = !string.IsNullOrWhiteSpace(torrent.DownloadLocation)
            ? torrent.DownloadLocation
            : torrent.SavePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("[Deluge] Torrent '{Name}' ({Hash}) has no download_location or save_path reported by Deluge",
                torrent.Name, torrent.Hash);
            return path ?? string.Empty;
        }

        // A legitimate save path is always absolute: starts with / on POSIX or a
        // drive letter on Windows. If neither, the value is almost certainly the
        // Label plugin's auto-filled label name or a malformed move-completed path.
        var looksAbsolute = path.StartsWith('/')
            || path.StartsWith('\\')
            || (path.Length >= 2 && path[1] == ':');

        if (!looksAbsolute)
        {
            _logger.LogError(
                "[Deluge] Torrent '{Name}' ({Hash}) reported a non-absolute save path from Deluge: '{Path}'. " +
                "This usually means the Deluge Label plugin is configured with an invalid 'Move Completed' path " +
                "(commonly the label name itself, which Deluge's UI auto-fills). " +
                "Open Deluge → Preferences → Label → {Label} → Download Settings and set 'Move Completed' to a " +
                "full absolute path (e.g. /downloads/completed), or disable the move-completed override. " +
                "Sportarr imports will fail until this is fixed.",
                torrent.Name, torrent.Hash, path,
                string.IsNullOrEmpty(torrent.Label) ? "<your label>" : torrent.Label);
        }

        return path;
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

        // Pick the effective base path (handles Deluge 2.x download_location vs
        // save_path, and warns on invalid Label plugin misconfigurations), then
        // append the torrent name so callers get the full file/folder path for
        // import rather than just the parent download directory.
        var basePath = GetEffectiveSavePath(torrent);
        var computedSavePath = !string.IsNullOrEmpty(torrent.Name)
            ? Path.Combine(basePath, torrent.Name)
            : basePath;

        return new DownloadClientStatus
        {
            Status = status,
            Progress = torrent.Progress, // Deluge returns 0-100 already
            Downloaded = torrent.TotalDone,
            Size = torrent.TotalSize,
            TimeRemaining = timeRemaining,
            SavePath = computedSavePath,
            ErrorMessage = status == "failed" ? $"Torrent in error state: {torrent.State}" : null,
            Ratio = torrent.Ratio
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

    /// <summary>
    /// Get all torrents filtered by label (category)
    /// Returns only torrents that belong to Sportarr
    /// </summary>
    public async Task<List<DelugeTorrent>?> GetTorrentsByLabelAsync(DownloadClient config, string label)
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
                                "upload_payload_rate", "save_path", "download_location",
                                "ratio", "time_added", "label" };

            // Filter by label server-side (like Radarr's web.update_ui approach)
            var filter = new Dictionary<string, object> { ["label"] = label };

            var response = await SendRpcRequestAsync(config, "core.get_torrents_status", new object[] { filter, fields });

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

                    _logger.LogDebug("[Deluge] Found {Count} torrents with label '{Label}'", torrents.Count, label);
                    return torrents;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] Error getting torrents by label '{Label}'", label);
            return null;
        }
    }

    /// <summary>
    /// Get all downloads matching a category label for external download detection
    /// </summary>
    public async Task<List<ExternalDownloadInfo>> GetAllDownloadsByCategoryAsync(DownloadClient config, string category)
    {
        var results = new List<ExternalDownloadInfo>();

        try
        {
            var torrents = await GetTorrentsByLabelAsync(config, category);
            if (torrents == null) return results;

            foreach (var torrent in torrents)
            {
                var state = torrent.State.ToLowerInvariant();
                var isCompleted = state is "seeding" or "uploading" || torrent.Progress >= 99.9;

                results.Add(new ExternalDownloadInfo
                {
                    DownloadId = torrent.Hash,
                    Title = torrent.Name,
                    Category = category,
                    FilePath = GetEffectiveSavePath(torrent),
                    Size = torrent.TotalSize,
                    IsCompleted = isCompleted,
                    Protocol = "Torrent",
                    TorrentInfoHash = torrent.Hash
                });
            }

            _logger.LogDebug("[Deluge] Found {Count} downloads in category '{Category}'", results.Count, category);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] Error getting downloads by category '{Category}'", category);
        }

        return results;
    }

    // Label plugin methods (matches Radarr's DelugeProxy label support)

    /// <summary>
    /// Check if the Label plugin is enabled in Deluge
    /// </summary>
    private async Task<bool> IsLabelPluginEnabledAsync(DownloadClient config)
    {
        try
        {
            var response = await SendRpcRequestAsync(config, "system.listMethods", Array.Empty<object>());
            if (response == null) return false;

            var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
            {
                foreach (var method in result.EnumerateArray())
                {
                    if (method.GetString()?.StartsWith("label.") == true)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Deluge] Error checking for Label plugin");
            return false;
        }
    }

    /// <summary>
    /// Get all available labels from the Label plugin
    /// </summary>
    private async Task<List<string>> GetAvailableLabelsAsync(DownloadClient config)
    {
        try
        {
            var response = await SendRpcRequestAsync(config, "label.get_labels", Array.Empty<object>());
            if (response == null) return new List<string>();

            var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
            {
                return result.EnumerateArray()
                    .Select(l => l.GetString() ?? "")
                    .Where(l => !string.IsNullOrEmpty(l))
                    .ToList();
            }

            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Deluge] Error getting available labels");
            return new List<string>();
        }
    }

    /// <summary>
    /// Ensure a label exists in Deluge, creating it if needed
    /// </summary>
    private async Task EnsureLabelExistsAsync(DownloadClient config, string label)
    {
        try
        {
            var labels = await GetAvailableLabelsAsync(config);
            if (labels.Contains(label, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogDebug("[Deluge] Label '{Label}' already exists", label);
                return;
            }

            _logger.LogInformation("[Deluge] Creating label '{Label}'", label);
            var response = await SendRpcRequestAsync(config, "label.add", new object[] { label });
            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                {
                    _logger.LogWarning("[Deluge] Error creating label '{Label}': {Error}", label, error.ToString());
                }
                else
                {
                    _logger.LogInformation("[Deluge] Label '{Label}' created successfully", label);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Deluge] Error ensuring label '{Label}' exists", label);
        }
    }

    /// <summary>
    /// Set the label on a torrent (assigns it to a category)
    /// </summary>
    private async Task SetTorrentLabelAsync(DownloadClient config, string hash, string label)
    {
        try
        {
            var response = await SendRpcRequestAsync(config, "label.set_torrent", new object[] { hash, label });
            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                {
                    _logger.LogWarning("[Deluge] Error setting label '{Label}' on torrent {Hash}: {Error}",
                        label, hash, error.ToString());
                }
                else
                {
                    _logger.LogDebug("[Deluge] Set label '{Label}' on torrent {Hash}", label, hash);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Deluge] Error setting label '{Label}' on torrent {Hash}", label, hash);
        }
    }

    /// <summary>
    /// Get Deluge daemon version, trying daemon.get_version first (Deluge 2.x),
    /// falling back to daemon.info (Deluge 1.x). Matches Radarr's GetVersion behavior.
    /// </summary>
    private async Task<string?> GetVersionAsync(DownloadClient config)
    {
        try
        {
            // Check available methods to determine correct version call
            var methodsResponse = await SendRpcRequestAsync(config, "system.listMethods", Array.Empty<object>());
            var useDaemonGetVersion = false;

            if (methodsResponse != null)
            {
                var methodsDoc = JsonDocument.Parse(methodsResponse);
                if (methodsDoc.RootElement.TryGetProperty("result", out var methods) && methods.ValueKind == JsonValueKind.Array)
                {
                    foreach (var method in methods.EnumerateArray())
                    {
                        if (method.GetString() == "daemon.get_version")
                        {
                            useDaemonGetVersion = true;
                            break;
                        }
                    }
                }
            }

            var versionMethod = useDaemonGetVersion ? "daemon.get_version" : "daemon.info";
            var response = await SendRpcRequestAsync(config, versionMethod, Array.Empty<object>());
            if (response == null) return null;

            var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            {
                _logger.LogError("[Deluge] {Method} returned error: {Error}", versionMethod, error.ToString());
                return null;
            }

            if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
            {
                return result.GetString();
            }

            return "unknown";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] Error getting daemon version");
            return null;
        }
    }

    // Private helper methods

    private void ConfigureClient(DownloadClient config)
    {
        var protocol = config.UseSsl ? "https" : "http";
        var urlBase = config.UrlBase ?? "";

        if (!string.IsNullOrEmpty(urlBase))
        {
            if (!urlBase.StartsWith("/"))
                urlBase = "/" + urlBase;
            urlBase = urlBase.TrimEnd('/');
        }

        _baseUrl = $"{protocol}://{config.Host}:{config.Port}{urlBase}/json";
    }

    private async Task<bool> LoginAsync(DownloadClient config)
    {
        if (_cookie != null)
        {
            // Session exists, but daemon may have disconnected
            return await EnsureDaemonConnectedAsync(config);
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
                        return await EnsureDaemonConnectedAsync(config);
                    }

                    // Handle boolean result
                    if (result.ValueKind == JsonValueKind.True || (result.ValueKind == JsonValueKind.False && result.GetBoolean()))
                    {
                        _logger.LogInformation("[Deluge] Login successful");
                        return await EnsureDaemonConnectedAsync(config);
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

    /// <summary>
    /// Ensure the WebUI is connected to the Deluge daemon.
    /// The WebUI proxies core.* calls to the daemon; if the connection is lost
    /// (common with binhex-delugevpn), all core.* methods fail with "Unknown method".
    /// </summary>
    private async Task<bool> EnsureDaemonConnectedAsync(DownloadClient config)
    {
        try
        {
            // Check if WebUI is connected to daemon
            var connectedResponse = await SendRpcRequestAsync(config, "web.connected", Array.Empty<object>());
            if (connectedResponse != null)
            {
                var doc = JsonDocument.Parse(connectedResponse);
                if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.True)
                {
                    return true;
                }
            }

            _logger.LogWarning("[Deluge] WebUI not connected to daemon, attempting reconnection...");

            // Get available daemon hosts
            var hostsResponse = await SendRpcRequestAsync(config, "web.get_hosts", Array.Empty<object>());
            if (hostsResponse == null)
            {
                _logger.LogError("[Deluge] Failed to get daemon hosts");
                return false;
            }

            var hostsDoc = JsonDocument.Parse(hostsResponse);
            if (!hostsDoc.RootElement.TryGetProperty("result", out var hosts)
                || hosts.ValueKind != JsonValueKind.Array
                || hosts.GetArrayLength() == 0)
            {
                _logger.LogError("[Deluge] No daemon hosts available");
                return false;
            }

            // Connect to first available host (each host is [host_id, host, port, status])
            var firstHost = hosts[0];
            var hostId = firstHost[0].GetString();

            _logger.LogInformation("[Deluge] Connecting to daemon host: {HostId}", hostId);
            await SendRpcRequestAsync(config, "web.connect", new object[] { hostId! });

            // Brief delay for connection to establish
            await Task.Delay(500);

            // Verify connection
            var verifyResponse = await SendRpcRequestAsync(config, "web.connected", Array.Empty<object>());
            if (verifyResponse != null)
            {
                var verifyDoc = JsonDocument.Parse(verifyResponse);
                if (verifyDoc.RootElement.TryGetProperty("result", out var verifyResult)
                    && verifyResult.ValueKind == JsonValueKind.True)
                {
                    _logger.LogInformation("[Deluge] Successfully reconnected to daemon");
                    return true;
                }
            }

            _logger.LogError("[Deluge] Failed to verify daemon connection after reconnect");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Deluge] Error ensuring daemon connection");
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
            _logger.LogDebug("[Deluge] RPC Request: Method={Method}, URL={Url}", method, _baseUrl);
            _logger.LogTrace("[Deluge] RPC Request Body: {Body}", requestJson);

            // Note: Deluge's JSON-RPC API rejects "application/json; charset=utf-8"
            // It only accepts "application/json" without the charset suffix
            var content = new StringContent(requestJson, Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, _baseUrl) { Content = content };
            if (!string.IsNullOrEmpty(_cookie))
            {
                requestMessage.Headers.Add("Cookie", _cookie);
            }

            using var response = await client.SendAsync(requestMessage);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("[Deluge] RPC Response: Status={StatusCode}, Method={Method}", response.StatusCode, method);
            _logger.LogTrace("[Deluge] RPC Response Body: {Body}", responseBody);

            // Store cookie from response
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                _cookie = cookies.FirstOrDefault();
                _logger.LogDebug("[Deluge] Session cookie updated");
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
    public double Progress { get; set; } // Deluge returns 0-100 (percentage)

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

    // Deluge 2.x canonical field. save_path is kept as a deprecated alias
    // but can return stale/wrong values when the Label plugin is active,
    // so we request both and prefer download_location.
    [JsonPropertyName("download_location")]
    public string DownloadLocation { get; set; } = "";

    [JsonPropertyName("ratio")]
    public double Ratio { get; set; } // Upload/download ratio

    [JsonPropertyName("time_added")]
    public long TimeAdded { get; set; } // Unix timestamp

    [JsonPropertyName("label")]
    public string Label { get; set; } = ""; // Label plugin category
}
