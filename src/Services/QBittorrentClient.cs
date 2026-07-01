using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Sportarr.Api.Helpers;
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
    private string? _apiKey; // qBittorrent 5.2+ Bearer API key (when configured)
    private System.Version? _webApiVersion; // cached /api/v2/app/webapiVersion (instances are per-config); System.Version, not Sportarr.Api.Version
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
                // SocketsHttpHandler (not HttpClientHandler) so we get
                // PooledConnectionLifetime - without it the handler pins DNS to
                // the IP resolved on first call and never releases sockets,
                // exhausting connections after a day of operation.
                var handler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                    SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
                    }
                };
                _customHttpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(100) };

                // Copy cookie if we have one
                if (_cookie != null)
                {
                    _customHttpClient.DefaultRequestHeaders.Add("Cookie", _cookie);
                }
                // Carry the Bearer API key (qBittorrent 5.2+) onto the
                // lazily-created SSL-bypass client too — LoginAsync may have
                // set it on _httpClient before this client existed.
                if (_apiKey != null)
                {
                    SetBearerHeader(_customHttpClient, _apiKey);
                }
            }
            return _customHttpClient;
        }

        return _httpClient;
    }

    /// <summary>
    /// Set the Bearer Authorization header for qBittorrent 5.2+ API-key
    /// auth. Uses the typed Authorization property so re-applying it across
    /// per-operation calls (or after a key rotation) replaces rather than
    /// throwing on a duplicate header.
    /// </summary>
    private static void SetBearerHeader(HttpClient client, string apiKey)
    {
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Fetch and cache the server's WebAPI version (e.g. "2.11.4") from
    /// /api/v2/app/webapiVersion. Cached on the instance, and client
    /// instances are themselves cached per download-client config, so this
    /// costs one request per config rather than one per operation. Returns
    /// null when the version can't be determined. Caller must have
    /// authenticated first.
    /// </summary>
    private async Task<System.Version?> GetWebApiVersionAsync(DownloadClient config, string baseUrl)
    {
        if (_webApiVersion != null)
        {
            return _webApiVersion;
        }

        try
        {
            var client = GetHttpClient(config);
            using var response = await client.GetAsync($"{baseUrl}/api/v2/app/webapiVersion");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var raw = (await response.Content.ReadAsStringAsync()).Trim();
            if (System.Version.TryParse(raw, out var version))
            {
                _webApiVersion = version;
                _logger.LogDebug("[qBittorrent] WebAPI version {Version}", version);
                return version;
            }

            _logger.LogDebug("[qBittorrent] Could not parse WebAPI version '{Raw}'", raw);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[qBittorrent] Failed to fetch WebAPI version");
            return null;
        }
    }

    /// <summary>
    /// True when the server speaks the qBittorrent 5.0+ WebAPI (>= 2.11.0),
    /// which renamed torrents/pause -> torrents/stop, torrents/resume ->
    /// torrents/start, and the add 'paused' parameter -> 'stopped'. This is
    /// the same version gate Sonarr/Radarr use. Compared on Major/Minor so a
    /// two-component version string ("2.11") isn't mistaken for older than
    /// "2.11.0". Defaults to true (modern) when the version is unknown -
    /// qBittorrent 5.x is the current/supported line and 4.x is end-of-life.
    /// </summary>
    private async Task<bool> SupportsStartStopAsync(DownloadClient config, string baseUrl)
    {
        var version = await GetWebApiVersionAsync(config, baseUrl);
        return version == null
            || version.Major > 2
            || (version.Major == 2 && version.Minor >= 11);
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
            using var response = await client.GetAsync($"{baseUrl}/api/v2/app/version");
            if (response.IsSuccessStatusCode)
            {
                var version = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("[qBittorrent] Connected successfully. Version: {Version}", version);
                return true;
            }

            // Bearer auth returns true from LoginAsync without validating the
            // key (there's no login round-trip), so a bad/rotated key first
            // surfaces here as a 401/403 on the actual request - call it out.
            if (!string.IsNullOrEmpty(config.ApiKey) &&
                (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                 response.StatusCode == System.Net.HttpStatusCode.Forbidden))
            {
                _logger.LogWarning(
                    "[qBittorrent] API key rejected (HTTP {Status}). Re-generate the key under qBittorrent Web UI -> API Key and paste the current one (it changes on rotation).",
                    (int)response.StatusCode);
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
    /// Add torrent from URL with detailed result.
    /// Downloads the torrent file bytes first, then sends the bytes to qBittorrent.
    /// This is critical for Prowlarr URLs which require authentication that qBittorrent doesn't have.
    /// </summary>
    public async Task<AddDownloadResult> AddTorrentWithResultAsync(DownloadClient config, string torrentUrl, string category, string? expectedName = null, double? seedRatioLimit = null, int? seedTimeLimitMinutes = null)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);
            _logger.LogInformation("[qBittorrent] ========== STARTING TORRENT ADD ==========");
            _logger.LogInformation("[qBittorrent] Base URL: {BaseUrl}", baseUrl);
            _logger.LogInformation("[qBittorrent] Torrent URL: {Url}", torrentUrl);
            _logger.LogInformation("[qBittorrent] Category: {Category}", category);

            var client = GetHttpClient(config);

            // For magnet links, pass URL directly to qBittorrent.
            // For torrent URLs (especially Prowlarr), download bytes first then send to qBittorrent.
            // Downloading the bytes server-side fixes issues with Prowlarr authentication.
            byte[]? torrentBytes = null;
            string? torrentFilename = null;

            if (!torrentUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[qBittorrent] Downloading torrent file from URL...");

                var downloadResult = await DownloadTorrentFileAsync(torrentUrl);
                if (!downloadResult.IsSuccess)
                {
                    _logger.LogError("[qBittorrent] ========== TORRENT DOWNLOAD FAILED ==========");
                    _logger.LogError("[qBittorrent] {Error}", downloadResult.ErrorMessage);
                    return AddDownloadResult.Failed(downloadResult.ErrorMessage!, AddDownloadErrorType.InvalidTorrent);
                }

                // Handle magnet redirect (some indexers redirect to magnet links)
                if (downloadResult.IsMagnetRedirect)
                {
                    _logger.LogInformation("[qBittorrent] Indexer redirected to magnet link - will pass magnet to qBittorrent");
                    torrentUrl = downloadResult.MagnetLink!;
                    // torrentBytes stays null, we'll pass the magnet URL instead
                }
                else
                {
                    torrentBytes = downloadResult.TorrentData;
                    torrentFilename = downloadResult.Filename ?? "download.torrent";
                    _logger.LogInformation("[qBittorrent] Downloaded torrent file: {Size} bytes, filename: {Filename}",
                        torrentBytes?.Length ?? 0, torrentFilename);
                }
            }
            else
            {
                _logger.LogInformation("[qBittorrent] Magnet link - will pass URL directly to qBittorrent");
            }

            // Compute the real v1 infohash locally (from the downloaded .torrent bytes or the
            // magnet link) so the torrent can be identified deterministically after adding,
            // rather than guessing which recently-added torrent is ours. A wrong id later
            // drives status/removal against the wrong download. The infohash is stable for
            // qBittorrent; debrid passthrough (Decypharr) mutates the id after caching, so it
            // is not relied upon there (see the gate further down).
            var knownHash = torrentBytes != null
                ? TorrentHashHelper.TryGetHashFromTorrentBytes(torrentBytes)
                : TorrentHashHelper.TryGetHashFromMagnet(torrentUrl);
            if (string.IsNullOrEmpty(knownHash))
            {
                _logger.LogWarning("[qBittorrent] Could not compute a v1 infohash for this release; will fall back to heuristic identification after add");
            }
            else
            {
                _logger.LogInformation("[qBittorrent] Computed infohash for add: {Hash}", knownHash);
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

            // NOTE: We do NOT specify savepath - qBittorrent uses its own configured download directory.
            // The category will create a subdirectory within the download client's save path.
            var content = new MultipartFormDataContent();

            // If we have torrent bytes, send them as file; otherwise send URL (for magnet links)
            if (torrentBytes != null)
            {
                // Send torrent file bytes
                var fileContent = new ByteArrayContent(torrentBytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-bittorrent");
                content.Add(fileContent, "torrents", torrentFilename!);
                _logger.LogInformation("[qBittorrent] Sending torrent as file upload ({Size} bytes)", torrentBytes.Length);
            }
            else
            {
                // Magnet link - send as URL
                content.Add(new StringContent(torrentUrl), "urls");
                _logger.LogInformation("[qBittorrent] Sending magnet link as URL");
            }

            content.Add(new StringContent(category), "category");
            if (!string.IsNullOrWhiteSpace(config.Directory))
            {
                content.Add(new StringContent(config.Directory), "savepath");
                _logger.LogInformation("[qBittorrent] Using directory override: {Directory}", config.Directory);
            }

            // Handle initial state (Started, ForceStarted, Stopped) — useful for testing automation.
            // qBittorrent 5.0 (WebAPI 2.11) renamed the add parameter 'paused' to
            // 'stopped'; pick the right name by the server's WebAPI version, same
            // as Sonarr and the pause/resume endpoint selection.
            var shouldPause = config.InitialState == TorrentInitialState.Stopped;
            var stoppedParam = await SupportsStartStopAsync(config, baseUrl) ? "stopped" : "paused";
            content.Add(new StringContent(shouldPause ? "true" : "false"), stoppedParam);
            if (config.InitialState == TorrentInitialState.Stopped)
            {
                _logger.LogInformation("[qBittorrent] Adding torrent in STOPPED state (InitialState=Stopped)");
            }

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

            // Apply per-torrent seed limits from indexer settings.
            // qBittorrent API: ratioLimit (-2=global, -1=unlimited, >=0=specific limit)
            // qBittorrent API: seedingTimeLimit (-2=global, -1=unlimited, >=0=minutes)
            if (seedRatioLimit.HasValue && seedRatioLimit.Value > 0)
            {
                content.Add(new StringContent(seedRatioLimit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), "ratioLimit");
                _logger.LogInformation("[qBittorrent] Seed ratio limit: {Ratio}", seedRatioLimit.Value);
            }
            if (seedTimeLimitMinutes.HasValue && seedTimeLimitMinutes.Value > 0)
            {
                content.Add(new StringContent(seedTimeLimitMinutes.Value.ToString()), "seedingTimeLimit");
                _logger.LogInformation("[qBittorrent] Seed time limit: {Minutes} minutes", seedTimeLimitMinutes.Value);
            }

            _logger.LogInformation("[qBittorrent] POSTing to {Endpoint}", $"{baseUrl}/api/v2/torrents/add");
            using var response = await client.PostAsync($"{baseUrl}/api/v2/torrents/add", content);
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

                // Deterministic identification: for real qBittorrent the infohash is stable, so
                // if we computed it above we look the torrent up by that exact hash instead of
                // guessing the most-recently-added one. Guessing has caused the wrong torrent's
                // data to be removed later. Decypharr/debrid mutate the id after caching, so they
                // are excluded here and continue to use the heuristics below.
                if (config.Type == DownloadClientType.QBittorrent && !string.IsNullOrEmpty(knownHash))
                {
                    var matchedByHash = torrents.FirstOrDefault(t =>
                        string.Equals(t.Hash, knownHash, StringComparison.OrdinalIgnoreCase));

                    // The torrent may take a moment to register; poll a little before giving up.
                    for (var attempt = 0; matchedByHash == null && attempt < 3; attempt++)
                    {
                        await Task.Delay(1000);
                        var refreshed = await GetTorrentsAsync(config);
                        matchedByHash = refreshed?.FirstOrDefault(t =>
                            string.Equals(t.Hash, knownHash, StringComparison.OrdinalIgnoreCase));
                    }

                    if (matchedByHash != null)
                    {
                        _logger.LogInformation("[qBittorrent] Identified torrent by computed infohash {Hash}: {Name}",
                            matchedByHash.Hash, matchedByHash.Name);

                        if (config.InitialState == TorrentInitialState.ForceStarted)
                        {
                            _logger.LogInformation("[qBittorrent] Setting torrent to Force Start (InitialState=ForceStarted)");
                            await SetForceStartAsync(config, matchedByHash.Hash, true);
                        }

                        // Return qBittorrent's own hash value so later status lookups match exactly.
                        return AddDownloadResult.Succeeded(matchedByHash.Hash);
                    }

                    _logger.LogWarning(
                        "[qBittorrent] Computed infohash {Hash} not found after add; falling back to heuristic identification",
                        knownHash);
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

                    // Handle ForceStarted state - need to call setForceStart API
                    if (config.InitialState == TorrentInitialState.ForceStarted)
                    {
                        _logger.LogInformation("[qBittorrent] Setting torrent to Force Start (InitialState=ForceStarted)");
                        await SetForceStartAsync(config, recentTorrent.Hash, true);
                    }

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
    public async Task<string?> AddTorrentAsync(DownloadClient config, string torrentUrl, string category, string? expectedName = null, double? seedRatioLimit = null, int? seedTimeLimitMinutes = null)
    {
        var result = await AddTorrentWithResultAsync(config, torrentUrl, category, expectedName, seedRatioLimit, seedTimeLimitMinutes);
        return result.Success ? result.DownloadId : null;
    }

    /// <summary>
    /// Get all torrents.
    ///
    /// Wrapped in a per-call CancellationToken with a tight timeout because the
    /// EnhancedDownloadMonitorService polls this every 30 seconds. The named
    /// HttpClient's overall Timeout is 100 seconds; with a 30s poll cadence
    /// up to three or four requests can stack up against a hung qBittorrent
    /// before the global timeout reaps the oldest. A managed-dump capture
    /// caught this happening — 491 inbound TCP backlog entries piled up on a
    /// stalled remote qbit instance. Failing fast (15s for login + listing
    /// combined) keeps at most one request in flight at a time.
    /// </summary>
    public async Task<List<QBittorrentTorrent>?> GetTorrentsAsync(DownloadClient config)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var baseUrl = GetBaseUrl(config);
            var client = GetHttpClient(config);

            if (!await LoginAsync(config, baseUrl, config.Username, config.Password))
            {
                return null;
            }

            using var response = await client.GetAsync($"{baseUrl}/api/v2/torrents/info", cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var torrents = await response.Content.ReadFromJsonAsync<List<QBittorrentTorrent>>(cts.Token);
                return torrents;
            }

            return null;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning("[qBittorrent] GetTorrents timed out after 15s for {Host}:{Port} - server may be unreachable",
                config.Host, config.Port);
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
    public async Task<List<ExternalDownloadInfo>> GetAllDownloadsByCategoryAsync(DownloadClient config, string category)
    {
        // An empty category would match every UNCATEGORISED torrent below, pulling
        // unrelated downloads into external-download detection and the Activity page.
        // Without a category Sportarr cannot identify its own downloads, so match nothing.
        if (string.IsNullOrWhiteSpace(category))
            return new List<ExternalDownloadInfo>();

        var torrents = await GetTorrentsAsync(config);
        if (torrents == null)
            return new List<ExternalDownloadInfo>();

        // Return ALL torrents in the specified category (downloading + completed + seeding)
        var categoryTorrents = torrents.Where(t =>
            t.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        return categoryTorrents.Select(t =>
        {
            var state = t.State.ToLowerInvariant();
            // qBittorrent 4.x reports pausedUP, 5.x reports stoppedUP for a
            // completed-then-stopped torrent; accept both.
            var isCompleted = state == "uploading" || state == "stalledup" ||
                              state == "pausedup" || state == "stoppedup" ||
                              t.Progress >= 0.999;

            return new ExternalDownloadInfo
            {
                DownloadId = t.Hash,
                Title = t.Name,
                Category = t.Category,
                FilePath = t.SavePath,
                Size = t.Size,
                IsCompleted = isCompleted,
                Protocol = "Torrent",
                TorrentInfoHash = t.Hash,
                CompletedDate = t.CompletedOn > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(t.CompletedOn).UtcDateTime
                    : (DateTime?)null
            };
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
    /// Get torrent files list from qBittorrent API
    /// </summary>
    public async Task<List<QBittorrentTorrentFile>?> GetTorrentFilesAsync(DownloadClient config, string hash)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);
            var client = GetHttpClient(config);

            if (!await LoginAsync(config, baseUrl, config.Username, config.Password))
            {
                return null;
            }

            using var response = await client.GetAsync($"{baseUrl}/api/v2/torrents/files?hash={hash}");

            if (response.IsSuccessStatusCode)
            {
                var files = await response.Content.ReadFromJsonAsync<List<QBittorrentTorrentFile>>();
                return files;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Error getting torrent files for hash {Hash}", hash);
            return null;
        }
    }

    /// <summary>
    /// Get the actual output path for a torrent.
    /// Uses ContentPath if available, otherwise constructs from SavePath + torrent structure.
    /// This is critical for debrid services where ContentPath differs from SavePath.
    /// </summary>
    public async Task<string> GetTorrentOutputPathAsync(DownloadClient config, QBittorrentTorrent torrent)
    {
        // If ContentPath is available and not empty, use it (most accurate)
        if (!string.IsNullOrEmpty(torrent.ContentPath))
        {
            _logger.LogDebug("[qBittorrent] Using ContentPath for output: {ContentPath}", torrent.ContentPath);
            return torrent.ContentPath;
        }

        // Fallback: Try to determine path from torrent files
        var files = await GetTorrentFilesAsync(config, torrent.Hash);
        if (files != null && files.Count > 0)
        {
            // For single-file torrents, the file name is the content
            // For multi-file torrents, the first path segment is the root folder
            var firstFile = files[0].Name;
            var pathSeparator = firstFile.Contains('/') ? '/' : '\\';
            var segments = firstFile.Split(pathSeparator);

            if (segments.Length > 1)
            {
                // Multi-file torrent - root folder is first segment
                var rootFolder = segments[0];
                var outputPath = Path.Combine(torrent.SavePath, rootFolder);
                _logger.LogDebug("[qBittorrent] Constructed output path from files: {OutputPath}", outputPath);
                return outputPath;
            }
            else
            {
                // Single file torrent
                var outputPath = Path.Combine(torrent.SavePath, firstFile);
                _logger.LogDebug("[qBittorrent] Single file output path: {OutputPath}", outputPath);
                return outputPath;
            }
        }

        // Ultimate fallback: just use SavePath
        _logger.LogDebug("[qBittorrent] Falling back to SavePath: {SavePath}", torrent.SavePath);
        return torrent.SavePath;
    }

    /// <summary>
    /// Get torrent status for download monitoring.
    /// Maps qBittorrent states to Sportarr status.
    /// </summary>
    public async Task<DownloadClientStatus?> GetTorrentStatusAsync(DownloadClient config, string hash)
    {
        var torrent = await GetTorrentAsync(config, hash);
        if (torrent == null)
            return null;

        // Get the actual output path (uses ContentPath or constructs from files)
        var outputPath = await GetTorrentOutputPathAsync(config, torrent);

        // Decypharr (debrid passthrough) reports state="downloading" while the
        // upstream debrid service (Real-Debrid, Torbox) is still caching the
        // torrent server-side. During this phase there's no on-disk progress and
        // the user sees a misleading "Downloading 0%" forever. Surface a queued
        // state with a clear message so the UI shows what's actually happening.
        var isDecypharrClient = config.Type == DownloadClientType.Decypharr ||
                                config.Type == DownloadClientType.DecypharrUsenet;
        var lowerState = torrent.State.ToLowerInvariant();
        if (isDecypharrClient &&
            (lowerState == "downloading" || lowerState == "forceddl" || lowerState == "stalleddl") &&
            torrent.Downloaded == 0 &&
            torrent.Progress < 0.001)
        {
            return new DownloadClientStatus
            {
                Status = "queued",
                Progress = 0,
                Downloaded = 0,
                Size = torrent.Size,
                TimeRemaining = null,
                SavePath = outputPath,
                ErrorMessage = "Waiting for debrid service to cache torrent (Real-Debrid / Torbox queue)",
                Ratio = 0,
                CompletedAt = null
            };
        }

        // Comprehensive qBittorrent state mapping.
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

        // Handle ETA: qBittorrent returns 8640000 for infinity, negative values for unknown.
        // Treat anything over 365 days as infinity.
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
            SavePath = outputPath, // Use the determined output path instead of raw SavePath
            ErrorMessage = warningMessage,
            Ratio = torrent.Ratio,
            CompletedAt = torrent.CompletedOn > 0
                ? DateTimeOffset.FromUnixTimeSeconds(torrent.CompletedOn).UtcDateTime
                : null
        };
    }

    /// <summary>
    /// Resume torrent. qBittorrent 5.0 (WebAPI 2.11) renamed torrents/resume
    /// to torrents/start; the verb is chosen by the server's WebAPI version
    /// (see ControlTorrentAsync / SupportsStartStopAsync).
    /// </summary>
    public async Task<bool> ResumeTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, hash, modernAction: "start", legacyAction: "resume");
    }

    /// <summary>
    /// Pause torrent. qBittorrent 5.0 renamed torrents/pause to torrents/stop;
    /// the verb is chosen by the server's WebAPI version.
    /// </summary>
    public async Task<bool> PauseTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, hash, modernAction: "stop", legacyAction: "pause");
    }

    /// <summary>
    /// Set force start state for torrent (bypasses queue limits)
    /// </summary>
    public async Task<bool> SetForceStartAsync(DownloadClient config, string hash, bool value)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);
            var client = GetHttpClient(config);

            if (!await LoginAsync(config, baseUrl, config.Username, config.Password))
            {
                return false;
            }

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("value", value.ToString().ToLower())
            });

            using var response = await client.PostAsync($"{baseUrl}/api/v2/torrents/setForceStart", content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[qBittorrent] Force start set to {Value} for torrent {Hash}", value, hash);
                return true;
            }

            _logger.LogWarning("[qBittorrent] Failed to set force start: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Error setting force start");
            return false;
        }
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

            using var response = await _httpClient.PostAsync($"{baseUrl}/api/v2/torrents/delete", content);
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

            // qBittorrent's setCategory rejects an unknown category with 409, so the
            // move silently fails until the user creates it by hand. Create it first
            // (no-op if it already exists), matching how the other *arr apps behave.
            // Skipped for the empty category, which just clears the torrent's category.
            if (!string.IsNullOrWhiteSpace(category))
            {
                await EnsureCategoryExistsAsync(config, baseUrl, category);
            }

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("category", category)
            });

            using var response = await _httpClient.PostAsync($"{baseUrl}/api/v2/torrents/setCategory", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Error setting category");
            return false;
        }
    }

    /// <summary>
    /// Download torrent file from URL.
    /// Critical for Prowlarr URLs which require the request to come from Sportarr,
    /// not from qBittorrent which doesn't have Prowlarr's authentication.
    /// </summary>
    private Task<TorrentDownloadResult> DownloadTorrentFileAsync(string torrentUrl)
        // Shared with the Transmission and rTorrent paths via TorrentFileResolver,
        // which follows redirects manually so a magnet redirect is detected rather
        // than handed to an HTTP client that cannot follow a cross-scheme redirect.
        => TorrentFileResolver.ResolveAsync(torrentUrl, skipSslValidation: false, _logger);

    /// <summary>
    /// Pre-validate a torrent URL by fetching headers to check if it returns valid torrent data.
    /// NOTE: This is OPTIONAL validation. We do light validation to provide better error
    /// messages, but we NEVER block downloads due to validation failures — we let
    /// qBittorrent try anyway.
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

            // Check for HTTP errors from HEAD request.
            // We NEVER block downloads based on HEAD response — only log warnings for user info.
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
        // qBittorrent 5.2+ API key: stateless Bearer auth. When a key is
        // configured we attach it to the client(s) and skip the cookie
        // auth/login round-trip entirely (the login/logout endpoints reject
        // API-key auth anyway). The key is generated in qBittorrent under
        // Web UI -> API Key. Assigning DefaultRequestHeaders.Authorization
        // is idempotent across the per-operation calls and survives a key
        // rotation (it replaces, never duplicates).
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            _apiKey = config.ApiKey;
            SetBearerHeader(_httpClient, _apiKey);
            if (_customHttpClient != null)
            {
                SetBearerHeader(_customHttpClient, _apiKey);
            }
            return true;
        }

        if (_cookie != null)
        {
            return true;
        }

        // Tight per-call timeout — see the comment on GetTorrentsAsync for the
        // motivation. A hung remote qBittorrent must not be allowed to occupy
        // the HttpClient for the full 100s configured timeout while monitor
        // polls keep arriving every 30s.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var client = GetHttpClient(config);
            var loginUrl = $"{baseUrl}/api/v2/auth/login";
            _logger.LogDebug("[qBittorrent] Login attempt: URL={Url}, User={User}", loginUrl, username ?? "admin");

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", username ?? "admin"),
                new KeyValuePair<string, string>("password", password ?? "")
            });

            using var response = await client.PostAsync(loginUrl, content, cts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

            _logger.LogDebug("[qBittorrent] Login response: Status={StatusCode}", response.StatusCode);
            _logger.LogTrace("[qBittorrent] Login response body: {Body}", responseBody);

            // qBittorrent returns HTTP 200 for BOTH a successful and a rejected
            // login; the body is the real signal ("Ok." vs "Fails."). A 403
            // means the Web UI temporarily banned this IP after repeated failed
            // attempts. Branch on all three so the log says what actually
            // happened instead of a misleading "appeared successful".
            var trimmedBody = (responseBody ?? string.Empty).Trim();
            var hasSessionCookie = response.Headers.TryGetValues("Set-Cookie", out var cookies);

            // A valid session cookie is, on its own, definitive proof the login
            // succeeded - accept it regardless of the body text. Only fall back to
            // the body ("Ok.") for the auth-bypass case where qBittorrent issues no
            // cookie. Gating success on the body alone regressed setups behind a
            // reverse proxy or auth portal that return a good cookie but a body that
            // isn't readable as "Ok." (empty, compressed, or HTML-wrapped).
            //
            // qBittorrent 5.2 returns HTTP 204 (No Content) with an empty body for
            // a successful login instead of 200 + "Ok.". The credentialed flow
            // still sets the SID cookie, so the cookie check above already covers
            // it; this also treats a bare 204 as success for the auth-bypass case
            // (whitelisted IP / localhost) where no cookie is issued and the body
            // is now empty. A 204 is never returned for a rejected login - failures
            // are 200 + "Fails." or a 403 ban - so it is a safe success signal.
            var loginOk = response.IsSuccessStatusCode &&
                          (hasSessionCookie ||
                           trimmedBody.StartsWith("Ok.", StringComparison.OrdinalIgnoreCase) ||
                           response.StatusCode == System.Net.HttpStatusCode.NoContent);

            if (loginOk)
            {
                if (hasSessionCookie)
                {
                    _cookie = cookies.FirstOrDefault();
                    _httpClient.DefaultRequestHeaders.Add("Cookie", _cookie);
                    // Also add to custom client if it exists
                    if (_customHttpClient != null)
                    {
                        _customHttpClient.DefaultRequestHeaders.Add("Cookie", _cookie);
                    }
                    _logger.LogDebug("[qBittorrent] Login successful, session cookie stored");
                }
                else
                {
                    // "Ok." with no cookie = qBittorrent bypassed authentication
                    // (this client IP is whitelisted, or "Bypass authentication
                    // for clients on localhost" is enabled). Subsequent requests
                    // work without a SID, so treat it as a success.
                    _logger.LogInformation(
                        "[qBittorrent] Authentication bypassed for this client (whitelisted IP or localhost); proceeding without a session cookie");
                }
                return true;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "[qBittorrent] Login blocked (HTTP 403): this IP is temporarily banned after repeated failed logins. " +
                    "Fix the credentials, then wait out the ban or restart qBittorrent.");
            }
            else if (trimmedBody.StartsWith("Fails.", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[qBittorrent] Login rejected (\"Fails.\"): the Web UI username/password is wrong. " +
                    "Check Tools -> Options -> Web UI -> Authentication in qBittorrent and re-enter it in Sportarr. " +
                    "Fresh installs use a random temporary password printed in qBittorrent's own log.");
            }
            else
            {
                _logger.LogWarning("[qBittorrent] Login failed: Status={Status}, Response={Response}",
                    response.StatusCode, trimmedBody);
            }
            return false;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning("[qBittorrent] Login timed out after 10s for {Host}:{Port}",
                config.Host, config.Port);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[qBittorrent] HTTP error during login: {Message}", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Login error");
            return false;
        }
    }

    /// <summary>
    /// POST a torrents control verb for one hash, choosing the verb by the
    /// server's WebAPI version: qBittorrent 5.0 (>= 2.11.0) renamed
    /// pause->stop and resume->start. We version-detect (same as Sonarr)
    /// rather than calling one name and reacting to a 404.
    /// </summary>
    private async Task<bool> ControlTorrentAsync(
        DownloadClient config, string hash, string modernAction, string legacyAction)
    {
        try
        {
            var baseUrl = GetBaseUrl(config);

            if (!await LoginAsync(config, baseUrl, config.Username, config.Password))
            {
                return false;
            }

            var action = await SupportsStartStopAsync(config, baseUrl) ? modernAction : legacyAction;

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash)
            });

            using var response = await GetHttpClient(config).PostAsync($"{baseUrl}/api/v2/torrents/{action}", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[qBittorrent] Error controlling torrent: {Modern}/{Legacy}", modernAction, legacyAction);
            return false;
        }
    }

    private async Task<bool> EnsureCategoryExistsAsync(DownloadClient config, string baseUrl, string category)
    {
        try
        {
            var client = GetHttpClient(config);

            // Check if category already exists before creating - preserves user's save path and TMM settings
            using var response = await client.GetAsync($"{baseUrl}/api/v2/torrents/categories");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var categories = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json);
                if (categories != null && categories.ContainsKey(category))
                {
                    _logger.LogInformation("[qBittorrent] Category '{Category}' already exists, preserving user settings", category);
                    return true;
                }
            }

            // Category doesn't exist, create it without specifying savePath so qBittorrent uses its default
            _logger.LogInformation("[qBittorrent] Creating category '{Category}'", category);
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("category", category)
            });

            await client.PostAsync($"{baseUrl}/api/v2/torrents/createCategory", content);
            _logger.LogInformation("[qBittorrent] Category '{Category}' created", category);
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

    /// <summary>
    /// Full path to the torrent content (file or folder).
    /// For single-file torrents: path to the file
    /// For multi-file torrents: path to the root folder
    /// This is more accurate than SavePath for determining the actual download location.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("content_path")]
    public string ContentPath { get; set; } = "";

    public double Ratio { get; set; } // Upload/download ratio
    public string Category { get; set; } = "";
    public long AddedOn { get; set; } // Unix timestamp
    public long CompletedOn { get; set; } // Unix timestamp
}

/// <summary>
/// qBittorrent torrent file information (for multi-file torrents)
/// </summary>
public class QBittorrentTorrentFile
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("size")]
    public long Size { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("progress")]
    public double Progress { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("priority")]
    public int Priority { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("is_seed")]
    public bool IsSeed { get; set; }
}

/// <summary>
/// Response from qBittorrent torrent files API
/// </summary>
public class QBittorrentTorrentFilesResponse : List<QBittorrentTorrentFile>
{
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

/// <summary>
/// Result of downloading a torrent file from a URL
/// </summary>
public class TorrentDownloadResult
{
    public bool IsSuccess { get; set; }
    public byte[]? TorrentData { get; set; }
    public string? Filename { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsMagnetRedirect { get; set; }
    public string? MagnetLink { get; set; }

    public static TorrentDownloadResult Success(byte[] data, string filename) => new()
    {
        IsSuccess = true,
        TorrentData = data,
        Filename = filename
    };

    public static TorrentDownloadResult Failure(string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };

    public static TorrentDownloadResult MagnetRedirect(string magnetLink) => new()
    {
        IsSuccess = true,
        IsMagnetRedirect = true,
        MagnetLink = magnetLink
    };
}
