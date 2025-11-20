using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// qBittorrent Web API client for Sportarr
/// Implements qBittorrent WebUI API v2 for torrent management
/// </summary>
public class QBittorrentClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<QBittorrentClient> _logger;
    private string? _cookie;
    private HttpClient? _customHttpClient; // For SSL bypass

    public QBittorrentClient(HttpClient httpClient, ILogger<QBittorrentClient> logger)
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
    /// Test connection to qBittorrent
    /// </summary>
    public async Task<bool> TestConnectionAsync(DownloadClient config)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);
            var client = GetHttpClient(config);

            // Login
            if (!await LoginAsync(config, baseUrl, config.Username, config.Password))
            {
                return false;
            }

            // Test API version
            var response = await client.GetAsync($"{baseUrl}/api/v2/app/version");
            if (response.IsSuccessStatusCode)
            {
                var version = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("[qBittorrent] Connected successfully. Version: {Version}", version);
                return true;
            }

            return false;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
        {
            _logger.LogError(ex,
                "[qBittorrent] SSL/TLS connection failed for {Host}:{Port}. " +
                "This usually means SSL is enabled in Sportarr but the port is serving HTTP, not HTTPS. " +
                "Please ensure HTTPS is enabled in qBittorrent settings, or disable SSL in Sportarr.",
                config.Host, config.Port);
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
    public async Task<string?> AddTorrentAsync(DownloadClient config, string torrentUrl, string category, string? expectedName = null)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);
            _logger.LogInformation("[qBittorrent] ========== STARTING TORRENT ADD ==========");
            _logger.LogInformation("[qBittorrent] Base URL: {BaseUrl}", baseUrl);
            _logger.LogInformation("[qBittorrent] Torrent URL: {Url}", torrentUrl);
            _logger.LogInformation("[qBittorrent] Category: {Category}", category);

            var client = GetHttpClient(config);

            if (!await LoginAsync(config, baseUrl, config.Username, config.Password))
            {
                _logger.LogError("[qBittorrent] Login failed - check username/password in Settings > Download Clients");
                return null;
            }

            _logger.LogInformation("[qBittorrent] Login successful, ensuring category exists...");

            // Ensure category exists before adding torrent
            if (!await EnsureCategoryExistsAsync(config, baseUrl, category))
            {
                _logger.LogWarning("[qBittorrent] Could not ensure category exists, but continuing anyway...");
            }

            _logger.LogInformation("[qBittorrent] Sending add request...");

            // NOTE: We do NOT specify savepath - qBittorrent uses its own configured download directory
            // The category will create a subdirectory within the download client's save path
            // This matches Sonarr/Radarr behavior
            var content = new MultipartFormDataContent
            {
                { new StringContent(torrentUrl), "urls" },
                { new StringContent(category), "category" },
                { new StringContent("false"), "paused" } // Start immediately (Sonarr behavior)
            };

            _logger.LogInformation("[qBittorrent] POSTing to {Endpoint}", $"{baseUrl}/api/v2/torrents/add");
            var response = await client.PostAsync($"{baseUrl}/api/v2/torrents/add", content);
            _logger.LogInformation("[qBittorrent] Response status: {StatusCode} ({StatusCodeInt})", response.StatusCode, (int)response.StatusCode);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("[qBittorrent] Add response body: '{Response}'", responseContent);
                _logger.LogInformation("[qBittorrent] Torrent add request accepted. Waiting 2 seconds for torrent to appear...");

                // Get torrent hash from recent torrents
                await Task.Delay(2000); // Wait for torrent to be added (increased from 1s to 2s)
                var torrents = await GetTorrentsAsync(config);

                if (torrents == null || torrents.Count == 0)
                {
                    _logger.LogWarning("[qBittorrent] WARNING: No torrents found in client after adding!");
                    _logger.LogWarning("[qBittorrent] Possible causes:");
                    _logger.LogWarning("[qBittorrent]   1. Invalid torrent/magnet URL");
                    _logger.LogWarning("[qBittorrent]   2. qBittorrent rejected the torrent (check qBittorrent logs)");
                    _logger.LogWarning("[qBittorrent]   3. Torrent added but immediately removed");
                    _logger.LogWarning("[qBittorrent]   4. qBittorrent download directory not configured or inaccessible");
                    return null;
                }

                _logger.LogInformation("[qBittorrent] Found {Count} total torrents in client", torrents.Count);

                // Check for torrents added in the last 10 seconds to help debug
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var recentlyAdded = torrents.Where(t => now - t.AddedOn < 10).ToList();
                _logger.LogInformation("[qBittorrent] Found {Count} torrents added in last 10 seconds", recentlyAdded.Count);

                foreach (var recent in recentlyAdded.Take(3))
                {
                    _logger.LogInformation("[qBittorrent]   Recent: {Name} | Category: '{Category}' | Added: {AddedOn}",
                        recent.Name, recent.Category, recent.AddedOn);
                }

                // Try to find the torrent using multiple criteria for robustness
                QBittorrentTorrent? recentTorrent = null;

                // Strategy 1: Filter by category + recently added (most reliable if category is set correctly)
                var categoryTorrents = torrents.Where(t => t.Category == category && recentlyAdded.Contains(t)).ToList();
                _logger.LogInformation("[qBittorrent] Found {Count} recently added torrents in category '{Category}'", categoryTorrents.Count, category);

                if (categoryTorrents.Count == 1)
                {
                    // Perfect - exactly one torrent in our category was just added
                    recentTorrent = categoryTorrents[0];
                    _logger.LogInformation("[qBittorrent] Match Strategy: Single torrent in category");
                }
                else if (categoryTorrents.Count > 1 && !string.IsNullOrEmpty(expectedName))
                {
                    // Multiple torrents in category - use name matching
                    _logger.LogInformation("[qBittorrent] Multiple torrents in category, using name matching. Expected: {ExpectedName}", expectedName);
                    recentTorrent = categoryTorrents
                        .Where(t => t.Name.Contains(expectedName, StringComparison.OrdinalIgnoreCase) ||
                                    expectedName.Contains(t.Name, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(t => t.AddedOn)
                        .FirstOrDefault();

                    if (recentTorrent != null)
                    {
                        _logger.LogInformation("[qBittorrent] Match Strategy: Category + Name match");
                    }
                }
                else if (categoryTorrents.Count > 1)
                {
                    // Multiple in category but no expected name - use most recent (risky)
                    _logger.LogWarning("[qBittorrent] Multiple torrents in category but no expected name provided - using most recent");
                    recentTorrent = categoryTorrents.OrderByDescending(t => t.AddedOn).FirstOrDefault();
                    _logger.LogInformation("[qBittorrent] Match Strategy: Most recent in category (RISKY)");
                }

                // Strategy 2: Category not set correctly - try name matching across all recent torrents
                if (recentTorrent == null && recentlyAdded.Any() && !string.IsNullOrEmpty(expectedName))
                {
                    _logger.LogWarning("[qBittorrent] No torrent found in category '{Category}', trying name matching across all recent torrents", category);
                    _logger.LogWarning("[qBittorrent] Expected name: {ExpectedName}", expectedName);

                    recentTorrent = recentlyAdded
                        .Where(t => t.Name.Contains(expectedName, StringComparison.OrdinalIgnoreCase) ||
                                    expectedName.Contains(t.Name, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(t => t.AddedOn)
                        .FirstOrDefault();

                    if (recentTorrent != null)
                    {
                        _logger.LogWarning("[qBittorrent] Match Strategy: Name match only (category mismatch - '{Category}' vs '{ExpectedCategory}')",
                            recentTorrent.Category, category);
                        _logger.LogWarning("[qBittorrent] Check qBittorrent settings - category may not be applying correctly");
                    }
                }

                // Strategy 3: Fallback - just use most recent (very risky, but better than failing)
                if (recentTorrent == null && recentlyAdded.Count == 1)
                {
                    _logger.LogWarning("[qBittorrent] No matches found, but exactly 1 torrent was just added - using it as fallback");
                    recentTorrent = recentlyAdded[0];
                    _logger.LogWarning("[qBittorrent] Match Strategy: Single recent torrent fallback (VERY RISKY if multiple clients share qBittorrent)");
                }

                if (recentTorrent != null)
                {
                    _logger.LogInformation("[qBittorrent] Most recent torrent found:");
                    _logger.LogInformation("[qBittorrent]   Name: {Name}", recentTorrent.Name);
                    _logger.LogInformation("[qBittorrent]   Hash: {Hash}", recentTorrent.Hash);
                    _logger.LogInformation("[qBittorrent]   State: {State}", recentTorrent.State);
                    _logger.LogInformation("[qBittorrent]   Save Path: {SavePath}", recentTorrent.SavePath);
                    _logger.LogInformation("[qBittorrent]   Category: {Category}", recentTorrent.Category);
                    _logger.LogInformation("[qBittorrent]   Size: {Size} bytes", recentTorrent.Size);
                    _logger.LogInformation("[qBittorrent]   Progress: {Progress}%", recentTorrent.Progress * 100);
                    _logger.LogInformation("[qBittorrent] ========== TORRENT ADD SUCCESSFUL ==========");
                    return recentTorrent.Hash;
                }
                else
                {
                    _logger.LogError("[qBittorrent] ERROR: Could not find any torrent after adding!");
                    return null;
                }
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("[qBittorrent] ========== TORRENT ADD FAILED ==========");
                _logger.LogError("[qBittorrent] Status Code: {StatusCode} ({StatusCodeInt})", response.StatusCode, (int)response.StatusCode);
                _logger.LogError("[qBittorrent] Error Response: {Error}", error);
                _logger.LogError("[qBittorrent] Possible causes:");
                _logger.LogError("[qBittorrent]   1. Invalid torrent/magnet URL format");
                _logger.LogError("[qBittorrent]   2. qBittorrent configuration issue");
                _logger.LogError("[qBittorrent]   3. Network connectivity problem");
                _logger.LogError("[qBittorrent]   4. qBittorrent API permissions");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] ========== EXCEPTION DURING TORRENT ADD ==========");
            _logger.LogError(ex, "[qBittorrent] Exception: {Message}", ex.Message);
            _logger.LogError(ex, "[qBittorrent] Exception Type: {Type}", ex.GetType().Name);
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
            var client = GetHttpClient(config);

            if (!await LoginAsync(config, baseUrl, config.Username, config.Password))
            {
                return null;
            }

            var response = await client.GetAsync($"{baseUrl}/api/v2/torrents/info");

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

            if (!await LoginAsync(config, baseUrl, config.Username, config.Password))
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

    public async Task<bool> SetCategoryAsync(DownloadClient config, string hash, string category)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);

            if (!await LoginAsync(config, baseUrl, config.Username, config.Password))
            {
                return false;
            }

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("category", category)
            });

            var response = await _httpClient.PostAsync($"{baseUrl}/api/v2/torrents/setCategory", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Error setting category");
            return false;
        }
    }

    // Private helper methods

    private async Task<bool> LoginAsync(DownloadClient config, string baseUrl, string? username, string? password)
    {
        if (_cookie != null)
        {
            return true; // Already logged in
        }

        try
        {
            var client = GetHttpClient(config);
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", username ?? "admin"),
                new KeyValuePair<string, string>("password", password ?? "")
            });

            var response = await client.PostAsync($"{baseUrl}/api/v2/auth/login", content);

            if (response.IsSuccessStatusCode)
            {
                // Store cookie for subsequent requests
                if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    _cookie = cookies.FirstOrDefault();
                    _httpClient.DefaultRequestHeaders.Add("Cookie", _cookie);
                    // Also add to custom client if it exists
                    if (_customHttpClient != null)
                    {
                        _customHttpClient.DefaultRequestHeaders.Add("Cookie", _cookie);
                    }
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

            if (!await LoginAsync(config, baseUrl, config.Username, config.Password))
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

    private async Task<bool> EnsureCategoryExistsAsync(DownloadClient config, string baseUrl, string category)
    {
        try
        {
            var client = GetHttpClient(config);
            _logger.LogInformation("[qBittorrent] Ensuring category '{Category}' exists", category);

            // Create category (this is idempotent - won't fail if category already exists)
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("category", category),
                new KeyValuePair<string, string>("savePath", "") // Empty = use default
            });

            var response = await client.PostAsync($"{baseUrl}/api/v2/torrents/createCategory", content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[qBittorrent] Category '{Category}' is ready", category);
                return true;
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("[qBittorrent] Category creation response: {Error}", error);

            // Even if creation "fails", it might be because the category already exists, which is fine
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Error ensuring category exists");
            return false;
        }
    }

    private static string GetBaseUrl(DownloadClient config)
    {
        var protocol = config.UseSsl ? "https" : "http";

        // qBittorrent Web UI typically runs at root, but supports URL path prefix in settings
        // Use configured URL base or empty (root) by default
        var urlBase = config.UrlBase ?? "";

        // Ensure urlBase starts with / and doesn't end with /
        if (!string.IsNullOrEmpty(urlBase))
        {
            if (!urlBase.StartsWith("/"))
            {
                urlBase = "/" + urlBase;
            }
            urlBase = urlBase.TrimEnd('/');
        }

        return $"{protocol}://{config.Host}:{config.Port}{urlBase}";
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
