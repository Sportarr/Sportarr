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
    /// Add torrent from URL with detailed result
    /// </summary>
    public async Task<AddDownloadResult> AddTorrentWithResultAsync(DownloadClient config, string torrentUrl, string category, string? expectedName = null)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);
            _logger.LogInformation("[qBittorrent] ========== STARTING TORRENT ADD ==========");
            _logger.LogInformation("[qBittorrent] Base URL: {BaseUrl}", baseUrl);
            _logger.LogInformation("[qBittorrent] Torrent URL: {Url}", torrentUrl);
            _logger.LogInformation("[qBittorrent] Category: {Category}", category);

            var client = GetHttpClient(config);

            // Pre-validate torrent URL if it's not a magnet link
            // This catches expired/invalid links before sending to qBittorrent
            if (!torrentUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                var validationResult = await ValidateTorrentUrlAsync(torrentUrl);
                if (!validationResult.IsValid)
                {
                    _logger.LogError("[qBittorrent] ========== TORRENT URL VALIDATION FAILED ==========");
                    _logger.LogError("[qBittorrent] {Error}", validationResult.ErrorMessage);
                    return AddDownloadResult.Failed(validationResult.ErrorMessage!, AddDownloadErrorType.InvalidTorrent);
                }
                _logger.LogInformation("[qBittorrent] Torrent URL pre-validation passed (Content-Type: {ContentType}, Size: {Size} bytes)",
                    validationResult.ContentType, validationResult.ContentLength);
            }
            else
            {
                _logger.LogInformation("[qBittorrent] Skipping URL validation for magnet link");
            }

            if (!await LoginAsync(config, baseUrl, config.Username, config.Password))
            {
                _logger.LogError("[qBittorrent] Login failed - check username/password in Settings > Download Clients");
                return AddDownloadResult.Failed("Login failed - check username/password", AddDownloadErrorType.LoginFailed);
            }

            _logger.LogInformation("[qBittorrent] Login successful, ensuring category exists...");

            // Ensure category exists before adding torrent
            if (!await EnsureCategoryExistsAsync(config, baseUrl, category))
            {
                _logger.LogWarning("[qBittorrent] Could not ensure category exists, but continuing anyway...");
            }

            // Get current torrents before adding to detect duplicates
            var torrentsBefore = await GetTorrentsAsync(config);
            var torrentCountBefore = torrentsBefore?.Count ?? 0;
            _logger.LogInformation("[qBittorrent] Torrents before add: {Count}", torrentCountBefore);

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

            // Add sequential download options (useful for debrid services like Decypharr)
            if (config.SequentialDownload)
            {
                content.Add(new StringContent("true"), "sequentialDownload");
                _logger.LogInformation("[qBittorrent] Sequential download enabled");
            }
            if (config.FirstAndLastFirst)
            {
                content.Add(new StringContent("true"), "firstLastPiecePrio");
                _logger.LogInformation("[qBittorrent] First and last piece priority enabled");
            }

            _logger.LogInformation("[qBittorrent] POSTing to {Endpoint}", $"{baseUrl}/api/v2/torrents/add");
            var response = await client.PostAsync($"{baseUrl}/api/v2/torrents/add", content);
            _logger.LogInformation("[qBittorrent] Response status: {StatusCode} ({StatusCodeInt})", response.StatusCode, (int)response.StatusCode);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("[qBittorrent] Add response body: '{Response}'", responseContent);

                // qBittorrent returns "Fails." if the torrent URL returned invalid data (e.g., HTML error page)
                if (responseContent.Contains("Fails", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("[qBittorrent] ========== TORRENT ADD FAILED ==========");
                    _logger.LogError("[qBittorrent] qBittorrent reported 'Fails' - the torrent URL returned invalid data");
                    _logger.LogError("[qBittorrent] Possible causes:");
                    _logger.LogError("[qBittorrent]   1. The torrent link has expired or is invalid");
                    _logger.LogError("[qBittorrent]   2. The indexer returned an HTML error page instead of a .torrent file");
                    _logger.LogError("[qBittorrent]   3. Authentication required to access the torrent");
                    _logger.LogError("[qBittorrent]   4. The indexer API key in Prowlarr may need to be refreshed");
                    return AddDownloadResult.Failed("Indexer returned invalid torrent data (possibly HTML error page). The torrent link may have expired or the indexer API key needs to be refreshed.", AddDownloadErrorType.InvalidTorrent);
                }

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
                    return AddDownloadResult.Failed("No torrents found in client after adding. The torrent may have been rejected - check qBittorrent logs.", AddDownloadErrorType.TorrentRejected);
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

                // Strategy 4: Decypharr fallback - AddedOn may be 0 or unreliable
                // If torrent count increased and we have an expected name, try matching on ALL torrents
                if (recentTorrent == null && torrents.Count > torrentCountBefore && !string.IsNullOrEmpty(expectedName))
                {
                    _logger.LogWarning("[qBittorrent] No recently added torrents found (AddedOn may be 0 - common with Decypharr)");
                    _logger.LogWarning("[qBittorrent] Torrent count increased from {Before} to {After}, trying name match on all torrents",
                        torrentCountBefore, torrents.Count);

                    recentTorrent = torrents
                        .Where(t => t.Name.Contains(expectedName, StringComparison.OrdinalIgnoreCase) ||
                                    expectedName.Contains(t.Name, StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault();

                    if (recentTorrent != null)
                    {
                        _logger.LogInformation("[qBittorrent] Match Strategy: Name match fallback (Decypharr compatibility)");
                    }
                }

                // Strategy 5: Ultimate fallback - if count increased by exactly 1, find the new torrent by elimination
                if (recentTorrent == null && torrents.Count == torrentCountBefore + 1 && torrentsBefore != null)
                {
                    _logger.LogWarning("[qBittorrent] Trying elimination strategy - finding the one new torrent");
                    var beforeHashes = torrentsBefore.Select(t => t.Hash).ToHashSet();
                    var newTorrent = torrents.FirstOrDefault(t => !beforeHashes.Contains(t.Hash));

                    if (newTorrent != null)
                    {
                        recentTorrent = newTorrent;
                        _logger.LogInformation("[qBittorrent] Match Strategy: Elimination (found torrent not in previous list)");
                    }
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
                    return AddDownloadResult.Succeeded(recentTorrent.Hash);
                }
                else
                {
                    // Check if torrent count is the same - could be duplicate OR invalid torrent data
                    if (torrents.Count == torrentCountBefore)
                    {
                        _logger.LogError("[qBittorrent] ERROR: Torrent count unchanged ({Count})", torrents.Count);
                        _logger.LogError("[qBittorrent] Possible causes:");
                        _logger.LogError("[qBittorrent]   1. DUPLICATE: qBittorrent silently ignores duplicate torrents (same info hash)");
                        _logger.LogError("[qBittorrent]   2. INVALID DATA: The torrent URL returned invalid/expired data that qBittorrent silently rejected");
                        _logger.LogError("[qBittorrent]   3. INDEXER ERROR: Prowlarr may have returned an expired or rate-limited torrent link");

                        // Try to find existing torrent by name match to determine if it's a real duplicate
                        if (!string.IsNullOrEmpty(expectedName))
                        {
                            var existingMatch = torrents
                                .Where(t => t.Name.Contains(expectedName, StringComparison.OrdinalIgnoreCase) ||
                                            expectedName.Contains(t.Name, StringComparison.OrdinalIgnoreCase))
                                .FirstOrDefault();

                            if (existingMatch != null)
                            {
                                _logger.LogWarning("[qBittorrent] Found existing torrent matching name: {Name} (Hash: {Hash}, Progress: {Progress}%)",
                                    existingMatch.Name, existingMatch.Hash, existingMatch.Progress * 100);
                                _logger.LogWarning("[qBittorrent] This IS a duplicate - returning existing torrent hash");
                                return AddDownloadResult.Succeeded(existingMatch.Hash);
                            }
                            else
                            {
                                // No matching torrent found - this is NOT a duplicate, it's invalid torrent data
                                _logger.LogError("[qBittorrent] No existing torrent matches '{ExpectedName}' - this is NOT a duplicate!", expectedName);
                                _logger.LogError("[qBittorrent] The indexer likely returned invalid/expired torrent data");
                                _logger.LogError("[qBittorrent] Try: 1) Refresh indexer in Prowlarr, 2) Wait and retry, 3) Try a different indexer");
                                return AddDownloadResult.Failed(
                                    "Indexer returned invalid torrent data (not a duplicate - no matching torrent exists). " +
                                    "The torrent link may have expired or the indexer needs to be refreshed in Prowlarr.",
                                    AddDownloadErrorType.InvalidTorrent);
                            }
                        }
                        else
                        {
                            // No expected name provided - can't distinguish between duplicate and invalid data
                            _logger.LogWarning("[qBittorrent] Cannot determine if duplicate or invalid (no expected name provided)");
                        }
                    }
                    else
                    {
                        _logger.LogError("[qBittorrent] ERROR: Could not find any torrent after adding!");
                        _logger.LogError("[qBittorrent] Torrent count increased from {Before} to {After} but couldn't identify which torrent was added",
                            torrentCountBefore, torrents.Count);
                    }
                    return AddDownloadResult.Failed("Could not identify the added torrent. Check qBittorrent logs for errors.", AddDownloadErrorType.Unknown);
                }
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("[qBittorrent] ========== TORRENT ADD FAILED ==========");
                _logger.LogError("[qBittorrent] Status Code: {StatusCode} ({StatusCodeInt})", response.StatusCode, (int)response.StatusCode);
                _logger.LogError("[qBittorrent] Error Response: {Error}", error);

                // Parse specific error types for better user feedback
                var errorMessage = $"Download client returned HTTP {(int)response.StatusCode}";
                var errorType = AddDownloadErrorType.Unknown;

                if (error.Contains("unsupported protocol scheme", StringComparison.OrdinalIgnoreCase) &&
                    error.Contains("magnet", StringComparison.OrdinalIgnoreCase))
                {
                    // Download client (like Decypharr) doesn't support magnet links
                    errorMessage = "This download client does not support magnet links. The indexer provided a magnet link instead of a .torrent file. Try a different indexer or configure your indexer to provide torrent files.";
                    errorType = AddDownloadErrorType.InvalidTorrent;
                    _logger.LogError("[qBittorrent] Download client does not support magnet links - indexer returned a magnet URI instead of a torrent file");
                }
                else if (error.Contains("bencode", StringComparison.OrdinalIgnoreCase) &&
                         error.Contains("unknown value type", StringComparison.OrdinalIgnoreCase) &&
                         error.Contains("<", StringComparison.OrdinalIgnoreCase))
                {
                    // The indexer returned HTML (starts with '<') instead of a torrent file
                    errorMessage = "The indexer returned an HTML page instead of a torrent file. This usually means: (1) The torrent link has expired, (2) The indexer session timed out - try re-adding the indexer in Prowlarr, or (3) The indexer is blocking automated downloads.";
                    errorType = AddDownloadErrorType.InvalidTorrent;
                    _logger.LogError("[qBittorrent] Indexer returned HTML instead of torrent data - session may have expired");
                }
                else if (error.Contains("bencode", StringComparison.OrdinalIgnoreCase) ||
                         error.Contains("syntax error", StringComparison.OrdinalIgnoreCase))
                {
                    // Generic bencode parsing error
                    errorMessage = "The indexer returned invalid torrent data. The link may have expired or the indexer requires re-authentication in Prowlarr.";
                    errorType = AddDownloadErrorType.InvalidTorrent;
                }
                else if (error.Contains("Fails", StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = "Download client rejected the torrent. The torrent file may be corrupted or the link has expired.";
                    errorType = AddDownloadErrorType.TorrentRejected;
                }
                else if (!string.IsNullOrEmpty(error))
                {
                    errorMessage = $"Download client error: {error}";
                }

                return AddDownloadResult.Failed(errorMessage, errorType);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[qBittorrent] ========== CONNECTION ERROR ==========");
            _logger.LogError(ex, "[qBittorrent] Could not connect to qBittorrent: {Message}", ex.Message);
            return AddDownloadResult.Failed($"Could not connect to qBittorrent: {ex.Message}", AddDownloadErrorType.ConnectionFailed);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "[qBittorrent] ========== TIMEOUT ==========");
            return AddDownloadResult.Failed("Request to qBittorrent timed out", AddDownloadErrorType.Timeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] ========== EXCEPTION DURING TORRENT ADD ==========");
            _logger.LogError(ex, "[qBittorrent] Exception: {Message}", ex.Message);
            _logger.LogError(ex, "[qBittorrent] Exception Type: {Type}", ex.GetType().Name);
            return AddDownloadResult.Failed($"Unexpected error: {ex.Message}", AddDownloadErrorType.Unknown);
        }
    }

    /// <summary>
    /// Add torrent from URL (legacy method for backward compatibility)
    /// </summary>
    public async Task<string?> AddTorrentAsync(DownloadClient config, string torrentUrl, string category, string? expectedName = null)
    {
        var result = await AddTorrentWithResultAsync(config, torrentUrl, category, expectedName);
        return result.Success ? result.DownloadId : null;
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
    /// Get completed downloads filtered by category (for external import detection)
    /// </summary>
    public async Task<List<ExternalDownloadInfo>> GetCompletedDownloadsByCategoryAsync(DownloadClient config, string category)
    {
        var torrents = await GetTorrentsAsync(config);
        if (torrents == null)
            return new List<ExternalDownloadInfo>();

        // Filter for completed torrents in the specified category
        var completedTorrents = torrents.Where(t =>
            t.Category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
            (t.State.ToLowerInvariant() == "uploading" ||  // Seeding = completed
             t.State.ToLowerInvariant() == "stalledup" ||  // Stalled upload = completed but no peers
             t.State.ToLowerInvariant() == "pausedup" ||   // Paused after completion
             t.Progress >= 0.999));                        // 99.9% progress counts as completed

        return completedTorrents.Select(t => new ExternalDownloadInfo
        {
            DownloadId = t.Hash,
            Title = t.Name,
            Category = t.Category,
            FilePath = t.SavePath,
            Size = t.Size,
            IsCompleted = true,
            Protocol = "Torrent",
            TorrentInfoHash = t.Hash,
            CompletedDate = t.CompletedOn > 0
                ? DateTimeOffset.FromUnixTimeSeconds(t.CompletedOn).UtcDateTime
                : (DateTime?)null
        }).ToList();
    }

    /// <summary>
    /// Find torrent by title and category, returning its status and hash
    /// Used for Decypharr/debrid proxy compatibility where the hash may change
    /// </summary>
    public async Task<(DownloadClientStatus? Status, string? NewDownloadId)> FindTorrentByTitleAsync(
        DownloadClient config, string title, string category)
    {
        try
        {
            var torrents = await GetTorrentsAsync(config);
            if (torrents == null || torrents.Count == 0)
                return (null, null);

            // Find torrent by title match in the specified category
            // Try exact match first, then partial/contains match
            var matchingTorrent = torrents
                .Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(t =>
                    string.Equals(t.Name, title, StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Contains(title, StringComparison.OrdinalIgnoreCase) ||
                    title.Contains(t.Name, StringComparison.OrdinalIgnoreCase));

            if (matchingTorrent == null)
            {
                _logger.LogDebug("[qBittorrent] No torrent found matching title '{Title}' in category '{Category}'",
                    title, category);
                return (null, null);
            }

            _logger.LogInformation("[qBittorrent] Found torrent by title match: '{Name}' (Hash: {Hash})",
                matchingTorrent.Name, matchingTorrent.Hash);

            var status = await GetTorrentStatusAsync(config, matchingTorrent.Hash);
            return (status, matchingTorrent.Hash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Error finding torrent by title");
            return (null, null);
        }
    }

    /// <summary>
    /// Get torrent status for download monitoring
    /// Maps qBittorrent states to Sportarr status (matches Sonarr implementation)
    /// </summary>
    public async Task<DownloadClientStatus?> GetTorrentStatusAsync(DownloadClient config, string hash)
    {
        var torrent = await GetTorrentAsync(config, hash);
        if (torrent == null)
            return null;

        // Comprehensive state mapping matching Sonarr's implementation
        // See: https://github.com/qbittorrent/qBittorrent/wiki/WebUI-API-(qBittorrent-4.1)#get-torrent-list
        var (status, warningMessage) = torrent.State.ToLowerInvariant() switch
        {
            // Downloading states
            "downloading" or "forceddl" or "moving" => ("downloading", (string?)null),

            // Completed/seeding states
            "uploading" or "stalledup" or "forcedup" or "queuedup" => ("completed", (string?)null),

            // Paused states (qBittorrent 4.x uses pausedDL/pausedUP, 5.x uses stoppedDL/stoppedUP)
            "pauseddl" or "stoppeddl" => ("paused", (string?)null),
            "pausedup" or "stoppedup" => ("completed", (string?)null), // Paused after completion = still completed

            // Queued/checking states
            "queueddl" or "allocating" => ("queued", (string?)null),
            "checkingdl" or "checkingup" or "checkingresumedata" => ("queued", (string?)null),

            // Metadata downloading (might indicate DHT issue if stuck)
            "metadl" or "forcedmetadl" => ("queued", "Downloading metadata"),

            // Error states
            "error" => ("failed", $"qBittorrent error: {torrent.State}"),
            "missingfiles" => ("failed", "Missing files - torrent data was deleted or moved"),

            // Stalled downloading - warning state (might need more seeders)
            "stalleddl" => ("warning", "Download stalled - waiting for peers"),

            // Unknown state - default to downloading but log it
            _ => ("downloading", (string?)null)
        };

        // Handle ETA: qBittorrent returns 8640000 for infinity, negative values for unknown
        // Sonarr considers anything over 365 days as infinity
        const long MaxEtaSeconds = 365 * 24 * 3600; // 1 year
        const long QBittorrentInfinityEta = 8640000;

        TimeSpan? timeRemaining = null;
        if (torrent.Eta > 0 && torrent.Eta < MaxEtaSeconds && torrent.Eta != QBittorrentInfinityEta)
        {
            timeRemaining = TimeSpan.FromSeconds(torrent.Eta);
        }

        return new DownloadClientStatus
        {
            Status = status,
            Progress = torrent.Progress * 100, // Convert 0-1 to 0-100
            Downloaded = torrent.Downloaded,
            Size = torrent.Size,
            TimeRemaining = timeRemaining,
            SavePath = torrent.SavePath,
            ErrorMessage = warningMessage
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

    /// <summary>
    /// Pre-validate a torrent URL by fetching headers to check if it returns valid torrent data
    /// NOTE: This is OPTIONAL validation. Sonarr/Radarr do NOT pre-validate URLs - they let
    /// the download client handle errors. We do light validation to provide better error messages,
    /// but we NEVER block downloads due to validation failures - we let qBittorrent try anyway.
    /// </summary>
    private async Task<TorrentUrlValidationResult> ValidateTorrentUrlAsync(string torrentUrl)
    {
        try
        {
            // CRITICAL: Validate URL format first to prevent UriFormatException
            // Some indexers return malformed URLs that crash HttpRequestMessage constructor
            if (!Uri.TryCreate(torrentUrl, UriKind.Absolute, out var uri))
            {
                _logger.LogWarning("[qBittorrent] Invalid URL format: {Url} - letting qBittorrent try anyway",
                    torrentUrl.Length > 100 ? torrentUrl.Substring(0, 100) + "..." : torrentUrl);
                // Return valid=true to let qBittorrent handle it - it might be able to parse it
                return new TorrentUrlValidationResult
                {
                    IsValid = true,
                    ContentType = "unknown",
                    ContentLength = 0,
                    Warning = "URL format validation failed, but letting download client try"
                };
            }

            // Only validate HTTP/HTTPS URLs - skip validation for other schemes
            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                _logger.LogDebug("[qBittorrent] Skipping validation for non-HTTP URL: {Scheme}", uri.Scheme);
                return new TorrentUrlValidationResult
                {
                    IsValid = true,
                    ContentType = uri.Scheme,
                    ContentLength = 0
                };
            }

            using var validationClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            // Try HEAD request first to check content type without downloading
            // Some indexers (like Prowlarr) don't support HEAD, so we fall back to partial GET
            var headRequest = new HttpRequestMessage(HttpMethod.Head, uri);
            var headResponse = await validationClient.SendAsync(headRequest);

            string contentType = "";
            long contentLength = 0;
            bool needsPartialGet = false;

            // Check for HTTP errors from HEAD request
            // NOTE: Unlike before, we now NEVER block downloads based on HEAD response
            // Sonarr doesn't pre-validate at all - we only log warnings for user info
            if (!headResponse.IsSuccessStatusCode)
            {
                var statusCode = (int)headResponse.StatusCode;

                // 405 = Method Not Allowed - indexer doesn't support HEAD, fall back to partial GET
                if (statusCode == 405)
                {
                    _logger.LogDebug("[qBittorrent] Indexer doesn't support HEAD requests, falling back to partial GET");
                    needsPartialGet = true;
                }
                else
                {
                    // Log warning but let qBittorrent try anyway - it may succeed
                    _logger.LogWarning("[qBittorrent] HEAD request returned HTTP {StatusCode} - letting qBittorrent try anyway", statusCode);
                    // Return valid with warning - don't block the download
                    return new TorrentUrlValidationResult
                    {
                        IsValid = true,
                        ContentType = "unknown",
                        ContentLength = 0,
                        Warning = $"Pre-validation returned HTTP {statusCode}, but download client may still succeed"
                    };
                }
            }
            else
            {
                // HEAD succeeded, get content info
                contentType = headResponse.Content.Headers.ContentType?.MediaType ?? "";
                contentLength = headResponse.Content.Headers.ContentLength ?? 0;
            }

            _logger.LogDebug("[qBittorrent] URL validation - Content-Type: {ContentType}, Content-Length: {Length}, NeedsPartialGet: {NeedsGet}",
                contentType, contentLength, needsPartialGet);

            // If HEAD doesn't give us content type, or indexer doesn't support HEAD, do a partial GET to check the content
            if (needsPartialGet || string.IsNullOrEmpty(contentType) || contentType == "application/octet-stream")
            {
                // Download first 100 bytes to check if it's a valid torrent or HTML
                var getRequest = new HttpRequestMessage(HttpMethod.Get, uri);
                getRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 100);

                var getResponse = await validationClient.SendAsync(getRequest);

                // Check for HTTP errors on GET request
                // NOTE: We no longer block on HTTP errors - let qBittorrent try anyway
                if (!getResponse.IsSuccessStatusCode && getResponse.StatusCode != System.Net.HttpStatusCode.PartialContent)
                {
                    var getStatusCode = (int)getResponse.StatusCode;
                    _logger.LogWarning("[qBittorrent] GET validation returned HTTP {StatusCode} - letting qBittorrent try anyway", getStatusCode);
                    return new TorrentUrlValidationResult
                    {
                        IsValid = true,
                        ContentType = "unknown",
                        ContentLength = 0,
                        Warning = $"Pre-validation returned HTTP {getStatusCode}, but download client may still succeed"
                    };
                }

                if (getResponse.IsSuccessStatusCode || getResponse.StatusCode == System.Net.HttpStatusCode.PartialContent)
                {
                    var bytes = await getResponse.Content.ReadAsByteArrayAsync();
                    var preview = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 50));

                    // Check for HTML content (error pages) - warn but don't block
                    if (preview.TrimStart().StartsWith("<", StringComparison.OrdinalIgnoreCase) ||
                        preview.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                        preview.Contains("<html", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("[qBittorrent] Torrent URL returned HTML instead of torrent data. Preview: {Preview}",
                            preview.Substring(0, Math.Min(preview.Length, 30)));
                        // Still return valid - let qBittorrent handle it
                        // qBittorrent will return "Fails." and we'll get a proper error message
                    }

                    // Valid torrents start with 'd' (bencode dictionary)
                    if (bytes.Length > 0 && bytes[0] == (byte)'d')
                    {
                        contentType = "application/x-bittorrent";
                        contentLength = getResponse.Content.Headers.ContentLength ?? bytes.Length;
                    }

                    // Update content type from GET response if we didn't have it
                    if (string.IsNullOrEmpty(contentType))
                    {
                        contentType = getResponse.Content.Headers.ContentType?.MediaType ?? "";
                    }
                }
            }

            // Check if content type indicates HTML (error page) - warn but don't block
            if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[qBittorrent] Content-Type indicates HTML - indexer may have returned error page");
                // Still return valid - let qBittorrent handle it
            }

            // Check for suspiciously small content - warn but don't block
            if (contentLength > 0 && contentLength < 100)
            {
                _logger.LogWarning("[qBittorrent] Torrent file is suspiciously small ({ContentLength} bytes) - may be error page", contentLength);
                // Still let qBittorrent try - it will give a proper error if it fails
            }

            return new TorrentUrlValidationResult
            {
                IsValid = true,
                ContentType = contentType,
                ContentLength = contentLength
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[qBittorrent] Failed to validate torrent URL: {Message} - letting qBittorrent try anyway", ex.Message);

            // ALL network errors should allow qBittorrent to try
            // Sportarr's validation uses a separate HttpClient - qBittorrent may have different network access
            return new TorrentUrlValidationResult
            {
                IsValid = true,  // Always allow qBittorrent to try
                ContentType = "unknown",
                ContentLength = 0,
                Warning = $"Could not pre-validate torrent URL: {ex.Message}"
            };
        }
        catch (TaskCanceledException)
        {
            // Timeout - allow qBittorrent to try anyway
            _logger.LogWarning("[qBittorrent] Torrent URL validation timed out, proceeding anyway");
            return new TorrentUrlValidationResult
            {
                IsValid = true,
                ContentType = "unknown",
                ContentLength = 0,
                Warning = "URL validation timed out"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[qBittorrent] Unexpected error validating torrent URL");
            // For unexpected errors, allow qBittorrent to try anyway
            return new TorrentUrlValidationResult
            {
                IsValid = true,
                ContentType = "unknown",
                ContentLength = 0,
                Warning = $"Could not validate: {ex.Message}"
            };
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

/// <summary>
/// Result of pre-validating a torrent URL before sending to qBittorrent
/// </summary>
public class TorrentUrlValidationResult
{
    public bool IsValid { get; set; }
    public string? ContentType { get; set; }
    public long ContentLength { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Warning { get; set; }

    public static TorrentUrlValidationResult Invalid(string errorMessage) => new()
    {
        IsValid = false,
        ErrorMessage = errorMessage
    };
}
