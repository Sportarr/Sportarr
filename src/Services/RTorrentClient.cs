using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// rTorrent/ruTorrent XML-RPC client for Sportarr
/// Implements rTorrent XML-RPC protocol
/// </summary>
public class RTorrentClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RTorrentClient> _logger;
    private readonly Sportarr.Api.Services.Interfaces.IRemotePathMappingService? _pathMappingService;
    private string? _baseUrl;
    private string? _authCredentials;
    private HttpClient? _customHttpClient; // For SSL bypass

    public RTorrentClient(HttpClient httpClient, ILogger<RTorrentClient> logger,
        Sportarr.Api.Services.Interfaces.IRemotePathMappingService? pathMappingService = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _pathMappingService = pathMappingService;
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
            }
            return _customHttpClient;
        }

        return _httpClient;
    }

    /// <summary>
    /// Test connection to rTorrent
    /// </summary>
    public async Task<bool> TestConnectionAsync(DownloadClient config)
    {
        try
        {
            ConfigureClient(config);

            // Test with system.client_version
            var response = await SendXmlRpcRequestAsync(config, "system.client_version", Array.Empty<object>());
            return Succeeded(response, "system.client_version");
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
        {
            _logger.LogError(ex,
                "[rTorrent] SSL/TLS connection failed for {Host}:{Port}. " +
                "This usually means SSL is enabled in Sportarr but the port is serving HTTP, not HTTPS. " +
                "Please ensure HTTPS is enabled in rTorrent/ruTorrent settings, or disable SSL in Sportarr.",
                config.Host, config.Port);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Add a torrent whose v1 infohash has already been computed locally (from the
    /// .torrent bytes or magnet link). Sends the content to rTorrent, then confirms the
    /// download registered under the expected hash before returning it. This replaces
    /// guessing the hash from the torrent list (which could return an unrelated torrent
    /// and later cause the wrong data to be removed).
    ///
    /// Pass either <paramref name="torrentBytes"/> (sent via load.raw[_start]) or
    /// <paramref name="magnetUrl"/> (sent via load.start/load.normal), not both.
    /// Returns the confirmed hash, or null if the add could not be confirmed (in which
    /// case the caller must NOT track the download, to avoid acting on the wrong one).
    /// </summary>
    public async Task<string?> AddTorrentWithHashAsync(
        DownloadClient config, byte[]? torrentBytes, string? magnetUrl, string knownHash, string category)
    {
        try
        {
            ConfigureClient(config);

            var startStopped = config.InitialState == TorrentInitialState.Stopped;

            // Trailing d.*.set commands applied atomically at load time: label (rTorrent's
            // category equivalent) and the optional directory override.
            var commands = new List<object>();
            if (!string.IsNullOrWhiteSpace(category))
            {
                commands.Add($"d.custom1.set={category}");
            }
            if (!string.IsNullOrWhiteSpace(config.Directory))
            {
                _logger.LogInformation("[rTorrent] Using directory override: {Directory}", config.Directory);
                commands.Add($"d.directory.set={config.Directory}");
            }

            string? response;
            if (torrentBytes != null)
            {
                // First arg "" is the load target (default); then the raw bytes; then commands.
                var command = startStopped ? "load.raw" : "load.raw_start";
                var args = new object[] { "", torrentBytes }.Concat(commands).ToArray();
                response = await SendXmlRpcRequestAsync(config, command, args);
                _logger.LogInformation("[rTorrent] Loaded torrent from file ({Bytes} bytes), expecting hash {Hash}",
                    torrentBytes.Length, knownHash);
            }
            else if (!string.IsNullOrEmpty(magnetUrl))
            {
                var command = startStopped ? "load.normal" : "load.start";
                var args = new object[] { "", magnetUrl }.Concat(commands).ToArray();
                response = await SendXmlRpcRequestAsync(config, command, args);
                _logger.LogInformation("[rTorrent] Loaded magnet, expecting hash {Hash}", knownHash);
            }
            else
            {
                _logger.LogError("[rTorrent] AddTorrentWithHashAsync called with neither bytes nor magnet");
                return null;
            }

            if (response == null)
            {
                _logger.LogWarning("[rTorrent] Add command returned no response for hash {Hash}", knownHash);
                return null;
            }

            // Confirm rTorrent registered the download under the expected hash. rTorrent
            // keys downloads by their infohash on load, so a magnet registers immediately
            // even before its metadata resolves.
            if (await WaitForTorrentAsync(config, knownHash, tries: 10, delayMs: 500))
            {
                _logger.LogInformation("[rTorrent] Torrent added and confirmed under hash {Hash}", knownHash);
                return knownHash;
            }

            _logger.LogWarning(
                "[rTorrent] Could not confirm torrent {Hash} after add; refusing to track it to avoid acting on the wrong download",
                knownHash);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Error adding torrent with known hash {Hash}", knownHash);
            return null;
        }
    }

    /// <summary>
    /// Poll rTorrent until a download with the given hash is present, or tries are exhausted.
    /// </summary>
    private async Task<bool> WaitForTorrentAsync(DownloadClient config, string hash, int tries, int delayMs)
    {
        for (var i = 0; i < tries; i++)
        {
            var torrent = await GetTorrentAsync(config, hash);
            if (torrent != null)
            {
                return true;
            }

            await Task.Delay(delayMs);
        }

        _logger.LogDebug("[rTorrent] Hash {Hash} not found after {Tries} tries at {Delay}ms intervals", hash, tries, delayMs);
        return false;
    }

    /// <summary>
    /// Get all torrents
    /// </summary>
    public async Task<List<RTorrentTorrent>?> GetTorrentsAsync(DownloadClient config)
    {
        try
        {
            ConfigureClient(config);

            // Use d.multicall2 to get all torrents with multiple fields
            var fields = new[] { "d.hash=", "d.name=", "d.size_bytes=", "d.completed_bytes=",
                                "d.up.total=", "d.state=", "d.down.rate=", "d.up.rate=",
                                // d.creation_date is when the torrent FILE was made, often
                                // years before anyone downloaded it, and it was being reported
                                // as the completion time. d.timestamp.finished is the real one.
                                "d.directory=", "d.base_path=", "d.custom1=", "d.timestamp.finished=" };

            var response = await SendXmlRpcRequestAsync(config, "d.multicall2", new object[] { "", "main" }.Concat(fields).ToArray());

            if (response != null)
            {
                var torrents = ParseMulticallResponse(response);
                return torrents;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Error getting torrents");
            return null;
        }
    }

    /// <summary>
    /// Get torrent by hash
    /// </summary>
    public async Task<RTorrentTorrent?> GetTorrentAsync(DownloadClient config, string hash)
    {
        var torrents = await GetTorrentsAsync(config);
        return torrents?.FirstOrDefault(t => t.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Start torrent
    /// </summary>
    public async Task<bool> StartTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, "d.start", hash);
    }

    /// <summary>
    /// Stop torrent
    /// </summary>
    public async Task<bool> StopTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, "d.stop", hash);
    }

    /// <summary>
    /// Pause torrent (same as stop in rTorrent)
    /// </summary>
    public async Task<bool> PauseTorrentAsync(DownloadClient config, string hash)
    {
        return await StopTorrentAsync(config, hash);
    }

    /// <summary>
    /// Resume torrent (same as start in rTorrent)
    /// </summary>
    public async Task<bool> ResumeTorrentAsync(DownloadClient config, string hash)
    {
        return await StartTorrentAsync(config, hash);
    }

    /// <summary>
    /// Set a torrent's queue priority (recent/older event queue priority,
    /// issue #220). Scale: RTorrentQueuePriority (VeryLow=0, Low=1, Normal=2,
    /// High=3). Sonarr sets this at load time via a trailing d.priority.set
    /// command; Sportarr's AddTorrentWithHashAsync already confirms the hash
    /// before returning, so this applies it as a normal post-add call like
    /// the other torrent clients instead of threading it through the load
    /// command.
    /// </summary>
    public async Task<bool> SetPriorityAsync(DownloadClient config, string hash, int priority)
    {
        try
        {
            ConfigureClient(config);
            var response = await SendXmlRpcRequestAsync(config, "d.priority.set", new object[] { hash, priority });
            return Succeeded(response, "d.priority.set");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Error setting priority for {Hash}", hash);
            return false;
        }
    }

    /// <summary>
    /// Move a torrent to a category (the free-form custom1 label, rTorrent's
    /// category equivalent) for the post-import category move. Labels are
    /// free-form, so there's no separate "create" step; an empty category
    /// clears the label.
    /// </summary>
    public async Task<bool> SetCategoryAsync(DownloadClient config, string hash, string category)
    {
        try
        {
            ConfigureClient(config);
            var response = await SendXmlRpcRequestAsync(config, "d.custom1.set", new object[] { hash, category ?? string.Empty });
            return Succeeded(response, "d.custom1.set");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Error setting category '{Category}' on torrent {Hash}", category, hash);
            return false;
        }
    }

    /// <summary>
    /// List all torrents matching a category (rTorrent label / custom1) for external download detection.
    /// rTorrent has no native category concept; Sportarr maps "category" to the d.custom1 label.
    /// Falls back to filtering by directory if no torrents match by label.
    /// </summary>
    public async Task<List<ExternalDownloadInfo>> GetAllDownloadsByCategoryAsync(DownloadClient config, string category)
    {
        var results = new List<ExternalDownloadInfo>();

        // rTorrent matches by label (category) first, falling back to directory. With
        // neither configured it would include every unlabelled torrent, so require at
        // least one identifier and otherwise match nothing.
        if (string.IsNullOrWhiteSpace(category) && string.IsNullOrWhiteSpace(config.Directory))
            return results;

        try
        {
            var torrents = await GetTorrentsAsync(config);
            if (torrents == null) return results;

            var filterDir = config.Directory;

            foreach (var torrent in torrents)
            {
                // Match by label first (the rTorrent equivalent of qBittorrent's category).
                // If the user hasn't been setting labels, fall back to directory matching like Transmission does.
                var labelMatches = !string.IsNullOrEmpty(torrent.Label) &&
                                    torrent.Label.Equals(category, StringComparison.OrdinalIgnoreCase);
                var dirMatches = !string.IsNullOrWhiteSpace(filterDir) &&
                                  torrent.Directory.StartsWith(filterDir, StringComparison.OrdinalIgnoreCase);

                if (!labelMatches && !dirMatches && (!string.IsNullOrEmpty(torrent.Label) || !string.IsNullOrWhiteSpace(filterDir)))
                {
                    // Either label or directory was set somewhere and didn't match → skip
                    continue;
                }

                // State: 0=stopped, 1=started. CompletedBytes >= TotalSize means done.
                var isCompleted = torrent.TotalSize > 0 && torrent.CompletedBytes >= torrent.TotalSize;

                // d.base_path is the file (single-file) or data root (multi-file); Path.Combine(dir, name) would yield dir/dir for multi-file.
                var savePath = !string.IsNullOrEmpty(torrent.BasePath)
                    ? torrent.BasePath
                    : torrent.Directory;

                results.Add(new ExternalDownloadInfo
                {
                    DownloadId = torrent.Hash,
                    Title = torrent.Name,
                    Category = category,
                    FilePath = savePath,
                    Size = torrent.TotalSize,
                    IsCompleted = isCompleted,
                    Protocol = "Torrent",
                    TorrentInfoHash = torrent.Hash,
                    CompletedDate = torrent.TimeFinished > 0 && isCompleted
                        ? DateTimeOffset.FromUnixTimeSeconds(torrent.TimeFinished).UtcDateTime
                        : (DateTime?)null
                });
            }

            _logger.LogDebug("[rTorrent] Found {Count} torrents matching label/directory '{Category}'",
                results.Count, category);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Error listing torrents by category");
        }

        return results;
    }

    /// <summary>
    /// Get torrent status for download monitoring
    /// </summary>
    /// <param name="expectedCategory">
    /// The category (custom1 label) this torrent was actually grabbed under (falls
    /// back to config.Category when null). A hash still existing in rTorrent doesn't
    /// mean it's still Sportarr's - rTorrent is commonly shared across multiple
    /// *arr-style apps, each scoped to its own label. If the torrent's current label
    /// doesn't match, it's reported as not found here rather than matched by hash
    /// alone, so download monitoring stops tracking it instead of polling another
    /// app's torrent forever - the hash never disappears, only its owner does. Safe
    /// against the post-add WaitForTorrentAsync poll (which calls the unscoped
    /// GetTorrentAsync directly, not this method): custom1 is set in the same
    /// multicall that loads the torrent (see AddTorrentAsync), not a separate
    /// follow-up call, so there's no window where a freshly-added torrent is
    /// temporarily unlabeled. A blank expected category (no scoping in use, on
    /// either side) skips the check and preserves the previous hash-only match.
    /// </param>
    public async Task<DownloadClientStatus?> GetTorrentStatusAsync(DownloadClient config, string hash, string? expectedCategory = null)
    {
        var torrent = await GetTorrentAsync(config, hash);
        if (torrent == null)
        {
            _logger.LogWarning("[RTorrent] Torrent not found: {Hash}", hash);
            return null;
        }

        var categoryToMatch = expectedCategory ?? config.Category;
        if (!string.IsNullOrWhiteSpace(categoryToMatch) &&
            !string.Equals(torrent.Label, categoryToMatch, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Determine status based on state and progress
        // rTorrent state: 0=stopped, 1=started
        var isComplete = torrent.CompletedBytes >= torrent.TotalSize && torrent.TotalSize > 0;
        var isStarted = torrent.State == 1;

        string status;
        if (!isStarted)
            status = "paused";
        else if (isComplete)
            status = "completed";
        else if (torrent.DownloadRate > 0)
            status = "downloading";
        else
            status = "queued";

        // Calculate progress (0-100)
        var progress = torrent.TotalSize > 0
            ? (double)torrent.CompletedBytes / torrent.TotalSize * 100
            : 0;

        // Calculate time remaining
        TimeSpan? timeRemaining = null;
        if (torrent.DownloadRate > 0 && !isComplete)
        {
            var remainingBytes = torrent.TotalSize - torrent.CompletedBytes;
            var secondsRemaining = remainingBytes / torrent.DownloadRate;
            timeRemaining = TimeSpan.FromSeconds(secondsRemaining);
        }

        // d.base_path is the file (single-file) or data root (multi-file); Path.Combine(dir, name) would yield dir/dir for multi-file.
        var computedSavePath = !string.IsNullOrEmpty(torrent.BasePath)
            ? torrent.BasePath
            : torrent.Directory;

        return new DownloadClientStatus
        {
            Status = status,
            Progress = progress,
            Downloaded = torrent.CompletedBytes,
            Size = torrent.TotalSize,
            TimeRemaining = timeRemaining,
            SavePath = computedSavePath,
            ErrorMessage = null,
            Ratio = torrent.CompletedBytes > 0
                ? (double)torrent.TotalUploaded / torrent.CompletedBytes
                : 0
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

            // Neither mode used to do what it says. "Leave the files" ran
            // d.close, which only stops the torrent and leaves it registered
            // in rTorrent forever. "Delete the files" ran d.erase, which
            // removes the torrent and leaves every byte on disk, because
            // rTorrent has no call that deletes data.
            string? basePath = null;
            if (deleteFiles)
            {
                var pathResponse = await SendXmlRpcRequestAsync(config, "d.base_path", new object[] { hash });
                if (Succeeded(pathResponse, "d.base_path"))
                {
                    basePath = ExtractSingleValue(pathResponse!);
                }
            }

            var eraseResponse = await SendXmlRpcRequestAsync(config, "d.erase", new object[] { hash });
            if (!Succeeded(eraseResponse, "d.erase"))
            {
                return false;
            }

            if (!deleteFiles)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(basePath))
            {
                _logger.LogWarning(
                    "[rTorrent] Removed {Hash} but could not find out where its data lives, so the files were left in place",
                    hash);
                return true;
            }

            // rTorrent reports where it thinks the data lives. Erasing that
            // path as given deletes whatever the local host happens to have at
            // the same place, which for a remote rTorrent is somebody else's
            // data entirely. Translate it, then require it to sit inside a
            // folder this client was actually configured to download into.
            var localPath = _pathMappingService != null
                ? await _pathMappingService.RemapRemoteToLocalAsync(config.Host ?? string.Empty, basePath)
                : basePath;

            var approvedRoots = await GetApprovedDeletionRootsAsync(config);

            if (approvedRoots.Count == 0)
            {
                _logger.LogWarning(
                    "[rTorrent] Removed {Hash} but left its files in place: nothing says where this client downloads. " +
                    "Set the client's download directory or add a remote path mapping to allow deletion.",
                    hash);
                return true;
            }

            if (!IsUnderApprovedRoot(localPath, approvedRoots))
            {
                _logger.LogWarning(
                    "[rTorrent] Removed {Hash} but refused to delete {Path}: it is outside every configured download folder",
                    hash, localPath);
                return true;
            }

            try
            {
                if (Directory.Exists(localPath))
                {
                    Directory.Delete(localPath, recursive: true);
                }
                else if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                }
                else
                {
                    _logger.LogWarning(
                        "[rTorrent] Removed {Hash} but {Path} is not reachable from here, so its files were left in place. " +
                        "A remote rTorrent needs a remote path mapping for this to work.",
                        hash, localPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[rTorrent] Removed {Hash} but could not delete {Path}", hash, localPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Error deleting torrent");
            return false;
        }
    }

    // Private helper methods

    private void ConfigureClient(DownloadClient config)
    {
        var protocol = config.UseSsl ? "https" : "http";
        var urlBase = string.IsNullOrEmpty(config.UrlBase) ? "/rutorrent" : config.UrlBase;

        if (!urlBase.StartsWith("/"))
            urlBase = "/" + urlBase;
        urlBase = urlBase.TrimEnd('/');

        _baseUrl = $"{protocol}://{config.Host}:{config.Port}{urlBase}/RPC2";

        if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
        {
            _authCredentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.Username}:{config.Password}"));
        }
    }

    private async Task<string?> SendXmlRpcRequestAsync(DownloadClient config, string method, object[] parameters)
    {
        try
        {
            var client = GetHttpClient(config);
            var xmlRequest = BuildXmlRpcRequest(method, parameters);

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
            {
                Content = new StringContent(xmlRequest, Encoding.UTF8, "text/xml")
            };
            if (!string.IsNullOrEmpty(_authCredentials))
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authCredentials);

            using var response = await client.SendAsync(requestMessage);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            _logger.LogWarning("[rTorrent] XML-RPC request failed: {Status}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] XML-RPC request error");
            return null;
        }
    }

    private async Task<bool> ControlTorrentAsync(DownloadClient config, string method, string hash)
    {
        try
        {
            ConfigureClient(config);
            var response = await SendXmlRpcRequestAsync(config, method, new object[] { hash });
            return Succeeded(response, method);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Error controlling torrent");
            return false;
        }
    }

    /// <summary>
    /// Decide whether an XML-RPC reply actually did what was asked.
    ///
    /// rTorrent answers a refused call with HTTP 200 and a fault struct in the
    /// body, so treating any non-null response as success reported every
    /// failed start, stop, delete, label and priority change as done. Callers
    /// then discarded tracking state for a deletion that never happened.
    /// </summary>
    private bool Succeeded(string? response, string method)
    {
        if (response == null) return false;

        if (response.Contains("<fault>", StringComparison.OrdinalIgnoreCase))
        {
            var reason = ExtractFaultString(response);
            _logger.LogWarning("[rTorrent] {Method} was refused: {Reason}", method, reason ?? "no reason given");
            return false;
        }

        return true;
    }

    private static string? ExtractFaultString(string response)
    {
        try
        {
            var doc = XDocument.Parse(response);
            return doc.Descendants("member")
                .Where(m => (string?)m.Element("name") == "faultString")
                .Select(m => m.Element("value")?.Value)
                .FirstOrDefault();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Pull the single scalar out of an XML-RPC reply.
    /// </summary>
    private static string? ExtractSingleValue(string response)
    {
        try
        {
            var doc = XDocument.Parse(response);
            return doc.Descendants("param")
                .Select(p => p.Element("value")?.Value)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                ?.Trim();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string BuildXmlRpcRequest(string method, object[] parameters)
    {
        var methodCall = new XElement("methodCall",
            new XElement("methodName", method),
            new XElement("params",
                parameters.Select(p => new XElement("param",
                    new XElement("value", BuildXmlRpcValue(p))
                ))
            )
        );

        return $"<?xml version=\"1.0\"?>{methodCall}";
    }

    // Most rTorrent params are strings, but load.raw / load.raw_start needs the raw
    // .torrent bytes sent as an XML-RPC <base64> value, and d.priority.set expects
    // a real integer (<i4>) rather than a numeric string. Fall back to <string>
    // for everything else.
    private static XElement BuildXmlRpcValue(object p)
    {
        return p switch
        {
            byte[] bytes => new XElement("base64", Convert.ToBase64String(bytes)),
            int i => new XElement("i4", i),
            _ => new XElement("string", p)
        };
    }

    private List<RTorrentTorrent> ParseMulticallResponse(string xml)
    {
        var torrents = new List<RTorrentTorrent>();

        try
        {
            var doc = XDocument.Parse(xml);
            var arrays = doc.Descendants("array").FirstOrDefault();

            if (arrays == null) return torrents;

            foreach (var data in arrays.Descendants("data"))
            {
                var values = data.Descendants("value").Select(v => v.Value).ToArray();

                if (values.Length >= 12)
                {
                    torrents.Add(new RTorrentTorrent
                    {
                        Hash = values[0],
                        Name = values[1],
                        TotalSize = long.TryParse(values[2], out var size) ? size : 0,
                        CompletedBytes = long.TryParse(values[3], out var completed) ? completed : 0,
                        TotalUploaded = long.TryParse(values[4], out var uploaded) ? uploaded : 0,
                        State = int.TryParse(values[5], out var state) ? state : 0,
                        DownloadRate = long.TryParse(values[6], out var dlRate) ? dlRate : 0,
                        UploadRate = long.TryParse(values[7], out var ulRate) ? ulRate : 0,
                        Directory = values[8],
                        BasePath = values[9],
                        Label = values[10],
                        TimeFinished = long.TryParse(values[11], out var finished) ? finished : 0
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Error parsing multicall response");
        }

        return torrents;
    }

    /// <summary>
    /// The folders this client may delete inside: its own download directory
    /// and the local side of any remote path mapping for its host. An empty
    /// list means nothing is known, and deletion is refused rather than
    /// guessed at.
    /// </summary>
    private async Task<List<string>> GetApprovedDeletionRootsAsync(DownloadClient config)
    {
        var roots = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                roots.Add(Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            }
            catch
            {
                // Not a path this host can make sense of.
            }
        }

        Add(config.Directory);

        if (_pathMappingService != null)
        {
            foreach (var root in await _pathMappingService.GetLocalRootsAsync(config.Host ?? string.Empty))
            {
                Add(root);
            }
        }

        return roots;
    }

    /// <summary>
    /// Whether a path really sits inside a folder this client downloads into.
    ///
    /// Comparing the text alone was not enough. A path under an approved root
    /// can lead through a link to somewhere else entirely, and this decides
    /// whether a recursive delete runs, so both sides are resolved first.
    /// </summary>
    private static bool IsUnderApprovedRoot(string path, List<string> roots) =>
        Sportarr.Api.Helpers.PathResolution.IsInsideAny(path, roots);
}

/// <summary>
/// rTorrent torrent information
/// </summary>
public class RTorrentTorrent
{
    public string Hash { get; set; } = "";
    public string Name { get; set; } = "";
    public long TotalSize { get; set; }
    public long CompletedBytes { get; set; }
    public long TotalUploaded { get; set; }
    public int State { get; set; } // 0=stopped, 1=started
    public long DownloadRate { get; set; } // bytes/s
    public long UploadRate { get; set; } // bytes/s
    public string Directory { get; set; } = "";
    public string BasePath { get; set; } = ""; // d.base_path: file path (single-file) or data root (multi-file)
    public string Label { get; set; } = "";
    /// <summary>
    /// Unix timestamp of when the download finished, from d.timestamp.finished.
    /// Zero when rTorrent has no record of it, which is the case for a torrent
    /// that has not finished.
    /// </summary>
    public long TimeFinished { get; set; }

}
