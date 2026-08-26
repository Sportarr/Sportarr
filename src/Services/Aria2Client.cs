using System.Text;
using System.Text.Json;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Aria2 JSON-RPC client for Sportarr. Torrent-only (Aria2 is a generic
/// downloader with no usenet/PAR2-repair awareness, matching how Sonarr/
/// Radarr scope their own Aria2 provider).
///
/// Two protocol details that matter for correctness, not just "does it
/// compile": aria2 has no category concept (same gap as Transmission - see
/// TransmissionClient's comments), so category maps to <see cref="DownloadClient.Directory"/>
/// via aria2's own "dir" option, exactly like Transmission does. And magnet
/// links resolve in two stages: the gid returned by addUri is a metadata-
/// fetch task that completes in seconds and hands off to a NEW gid for the
/// actual content download via aria2's own "followedBy" field. Every status/
/// pause/resume/remove call below follows that chain so Sportarr keeps
/// polling the metadata gid (the one it stored) while actually reporting/
/// acting on the real download - getting this wrong means a magnet reports
/// "complete" after a few seconds while the real content is still
/// downloading, and FileImportService tries to import an empty directory.
/// </summary>
public class Aria2Client
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<Aria2Client> _logger;
    private HttpClient? _customHttpClient;

    public Aria2Client(HttpClient httpClient, ILogger<Aria2Client> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    private HttpClient GetHttpClient(DownloadClient config)
    {
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

    private static string BuildUrl(DownloadClient config)
    {
        var protocol = config.UseSsl ? "https" : "http";
        var urlBase = string.IsNullOrEmpty(config.UrlBase) ? "" : config.UrlBase;
        if (!string.IsNullOrEmpty(urlBase) && !urlBase.StartsWith('/')) urlBase = "/" + urlBase;
        urlBase = urlBase.TrimEnd('/');
        return $"{protocol}://{config.Host}:{config.Port}{urlBase}/jsonrpc";
    }

    /// <summary>
    /// Aria2's RPC secret, reusing the generic ApiKey field (same convention
    /// as every other API-key-authenticated client here) rather than adding
    /// a new column/frontend field. Sent as the first element of "params"
    /// per aria2's own JSON-RPC auth scheme - not a header, not the URL.
    /// </summary>
    private static string? GetSecretToken(DownloadClient config) =>
        string.IsNullOrEmpty(config.ApiKey) ? null : $"token:{config.ApiKey}";

    private async Task<JsonElement?> SendRpcAsync(DownloadClient config, string method, params object?[] methodParams)
    {
        try
        {
            var client = GetHttpClient(config);
            var url = BuildUrl(config);

            var paramsList = new List<object?>();
            var secret = GetSecretToken(config);
            if (secret != null) paramsList.Add(secret);
            paramsList.AddRange(methodParams);

            var requestBody = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = "sportarr",
                ["method"] = method,
                ["params"] = paramsList
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Aria2] RPC {Method} failed: {Status} {Body}", method, response.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var errorMessage = error.TryGetProperty("message", out var msg) ? msg.GetString() : "unknown error";
                _logger.LogWarning("[Aria2] RPC {Method} returned error: {Error}", method, errorMessage);
                return null;
            }

            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                return result.Clone();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Aria2] RPC request error for method: {Method}", method);
            return null;
        }
    }

    public async Task<bool> TestConnectionAsync(DownloadClient config)
    {
        var result = await SendRpcAsync(config, "aria2.getVersion");
        return result != null;
    }

    /// <summary>
    /// Add a magnet link or any URI aria2 can resolve itself via aria2.addUri.
    /// Returns the gid - for a magnet this is the metadata-fetch task's gid,
    /// which GetTorrentStatusAsync/DeleteTorrentAsync/etc. transparently
    /// follow to the real content download (see class-level comment).
    /// </summary>
    public async Task<string?> AddUriAsync(DownloadClient config, string uri, string category)
    {
        var options = BuildAddOptions(config);
        var result = await SendRpcAsync(config, "aria2.addUri", new[] { uri }, options);
        return result?.GetString();
    }

    /// <summary>
    /// Add a .torrent file's bytes directly (base64-encoded per aria2's API)
    /// rather than handing aria2 the download URL, so a slow/one-time-use
    /// indexer link is fetched once by Sportarr instead of blocking inside
    /// the RPC call - same rationale as TransmissionClient's metainfo path.
    /// </summary>
    public async Task<string?> AddTorrentAsync(DownloadClient config, byte[] torrentBytes, string category)
    {
        var options = BuildAddOptions(config);
        var base64 = Convert.ToBase64String(torrentBytes);
        var result = await SendRpcAsync(config, "aria2.addTorrent", base64, Array.Empty<string>(), options);
        return result?.GetString();
    }

    private Dictionary<string, object> BuildAddOptions(DownloadClient config)
    {
        var options = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(config.Directory))
        {
            options["dir"] = config.Directory;
        }
        if (config.InitialState == TorrentInitialState.Stopped)
        {
            options["pause"] = "true";
        }
        return options;
    }

    /// <summary>
    /// Follows aria2's "followedBy" hand-off chain (magnet metadata task ->
    /// real content task) to the final, currently-relevant gid and its
    /// tellStatus result. Bounded to 5 hops - aria2 only ever hands off once
    /// in practice (metadata -> content), the bound is defense against an
    /// unexpected loop rather than an expected depth.
    /// </summary>
    private async Task<JsonElement?> ResolveActiveStatusAsync(DownloadClient config, string gid)
    {
        var currentGid = gid;
        for (var hop = 0; hop < 5; hop++)
        {
            var status = await SendRpcAsync(config, "aria2.tellStatus", currentGid);
            if (status == null) return null;

            if (status.Value.TryGetProperty("followedBy", out var followedBy) &&
                followedBy.ValueKind == JsonValueKind.Array &&
                followedBy.GetArrayLength() > 0)
            {
                var nextGid = followedBy[0].GetString();
                if (string.IsNullOrEmpty(nextGid) || nextGid == currentGid) return status;
                currentGid = nextGid;
                continue;
            }

            return status;
        }

        _logger.LogWarning("[Aria2] followedBy chain exceeded 5 hops starting from gid {Gid}, using last-seen status", gid);
        return await SendRpcAsync(config, "aria2.tellStatus", currentGid);
    }

    public async Task<DownloadClientStatus?> GetTorrentStatusAsync(DownloadClient config, string gid)
    {
        var status = await ResolveActiveStatusAsync(config, gid);
        if (status == null) return null;

        return MapStatus(status.Value);
    }

    private DownloadClientStatus? MapStatus(JsonElement status)
    {
        var statusStr = status.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (statusStr == null) return null;

        var mapped = statusStr switch
        {
            "active" => "downloading",
            "waiting" => "queued",
            "paused" => "paused",
            "complete" => "completed",
            "error" => "error",
            "removed" => "failed",
            _ => "downloading"
        };

        long ParseLong(string prop) => status.TryGetProperty(prop, out var v) && long.TryParse(v.GetString(), out var n) ? n : 0;

        var totalLength = ParseLong("totalLength");
        var completedLength = ParseLong("completedLength");
        var uploadLength = ParseLong("uploadLength");
        var downloadSpeed = ParseLong("downloadSpeed");

        var dir = status.TryGetProperty("dir", out var dirEl) ? dirEl.GetString() : null;
        var name = GetDownloadName(status);
        var savePath = !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(dir)
            ? Path.Combine(dir, name)
            : dir;

        var errorMessage = status.TryGetProperty("errorMessage", out var em) ? em.GetString() : null;

        TimeSpan? timeRemaining = null;
        if (downloadSpeed > 0 && totalLength > completedLength)
        {
            timeRemaining = TimeSpan.FromSeconds((totalLength - completedLength) / (double)downloadSpeed);
        }

        return new DownloadClientStatus
        {
            Status = mapped,
            Progress = totalLength > 0 ? (completedLength * 100.0 / totalLength) : 0,
            Downloaded = completedLength,
            Size = totalLength,
            TimeRemaining = timeRemaining,
            SavePath = savePath,
            ErrorMessage = errorMessage,
            Ratio = completedLength > 0 ? (double)uploadLength / completedLength : 0,
            CompletedAt = mapped == "completed" ? DateTime.UtcNow : null
        };
    }

    /// <summary>
    /// aria2 has no single "name" field - a torrent download names itself
    /// via bittorrent.info.name, a plain URI download via the first file's
    /// path basename.
    /// </summary>
    private static string? GetDownloadName(JsonElement status)
    {
        if (status.TryGetProperty("bittorrent", out var bt) &&
            bt.TryGetProperty("info", out var info) &&
            info.TryGetProperty("name", out var nameEl))
        {
            var btName = nameEl.GetString();
            if (!string.IsNullOrEmpty(btName)) return btName;
        }

        if (status.TryGetProperty("files", out var files) &&
            files.ValueKind == JsonValueKind.Array &&
            files.GetArrayLength() > 0 &&
            files[0].TryGetProperty("path", out var pathEl))
        {
            var path = pathEl.GetString();
            if (!string.IsNullOrEmpty(path)) return Path.GetFileName(path);
        }

        return null;
    }

    public async Task<bool> DeleteTorrentAsync(DownloadClient config, string gid, bool deleteFiles)
    {
        try
        {
            // Resolve to the currently-active gid first - removing the
            // original metadata gid after a magnet has handed off does
            // nothing to the still-downloading real content.
            var status = await ResolveActiveStatusAsync(config, gid);
            var activeGid = status?.TryGetProperty("gid", out var g) == true ? g.GetString() : gid;
            if (string.IsNullOrEmpty(activeGid)) activeGid = gid;

            var statusStr = status?.TryGetProperty("status", out var s) == true ? s.GetString() : null;
            var isActive = statusStr is "active" or "waiting" or "paused";

            // aria2.remove only works on active/waiting/paused downloads;
            // a finished/errored/already-removed one needs removeDownloadResult
            // instead to purge it from aria2's in-memory result list.
            var result = isActive
                ? await SendRpcAsync(config, "aria2.remove", activeGid)
                : await SendRpcAsync(config, "aria2.removeDownloadResult", activeGid);

            if (result == null && isActive)
            {
                // Already-stopped-but-not-yet-in-result-list edge case: fall
                // back to forceRemove before giving up.
                result = await SendRpcAsync(config, "aria2.forceRemove", activeGid);
            }

            var removed = result != null;

            if (removed && deleteFiles && !string.IsNullOrEmpty(status?.GetRawText()))
            {
                await TryDeleteFilesAsync(status!.Value);
            }

            return removed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Aria2] Error deleting download {Gid}", gid);
            return false;
        }
    }

    /// <summary>
    /// aria2's remove/removeDownloadResult only drop the download from
    /// aria2's own queue/history - they never touch disk. deleteFiles=true
    /// means Sportarr owns cleanup here.
    /// </summary>
    private Task TryDeleteFilesAsync(JsonElement status)
    {
        try
        {
            var dir = status.TryGetProperty("dir", out var dirEl) ? dirEl.GetString() : null;
            if (string.IsNullOrEmpty(dir)) return Task.CompletedTask;

            // A multi-file torrent owns a folder named after itself, so that
            // folder goes as a whole.
            var name = GetDownloadName(status);
            if (!string.IsNullOrEmpty(name))
            {
                var folder = Path.Combine(dir, name);
                if (Directory.Exists(folder) && IsInside(dir, folder))
                {
                    try
                    {
                        Directory.Delete(folder, recursive: true);
                        return Task.CompletedTask;
                    }
                    catch (Exception ex)
                    {
                        // The recursive delete stops at the first file that
                        // refuses, which left everything after it on disk
                        // with nothing tracking it. Fall through to the
                        // per-file pass, which carries on past a refusal.
                        _logger.LogWarning(ex,
                            "[aria2] Could not delete {Folder} whole; removing its files one by one", folder);
                    }
                }
            }

            // Otherwise delete exactly the files aria2 reports. Rebuilding a
            // path from the directory and a bare file name hit whatever
            // happened to carry that name in the download directory, which is
            // shared with every other download in the category.
            if (status.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                foreach (var file in files.EnumerateArray())
                {
                    var filePath = file.TryGetProperty("path", out var p) ? p.GetString() : null;
                    if (string.IsNullOrEmpty(filePath)) continue;

                    var full = Path.IsPathRooted(filePath) ? filePath : Path.Combine(dir, filePath);

                    // One file refusing to go is not a reason to leave the
                    // rest behind. A locked or read-only file used to end the
                    // loop, so everything after it stayed on disk with nothing
                    // tracking it.
                    try
                    {
                        if (IsInside(dir, full) && File.Exists(full))
                        {
                            File.Delete(full);
                        }
                    }
                    catch (Exception fileEx)
                    {
                        _logger.LogWarning(fileEx, "[Aria2] Could not delete {Path}; continuing with the rest", full);
                    }
                }
            }

            // The folder itself, once the per-file pass has taken what it
            // can. Only when it is empty; a refusing file still holds it.
            if (!string.IsNullOrEmpty(name))
            {
                var folder = Path.Combine(dir, name);
                if (Directory.Exists(folder) && IsInside(dir, folder) &&
                    !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Aria2] Failed to delete local files after removing download");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// True when candidate sits under root. Guards every delete, so a path
    /// aria2 reports can never reach outside the download directory.
    /// </summary>
    private static bool IsInside(string root, string candidate)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullCandidate = Path.GetFullPath(candidate);

            // Windows and macOS treat two spellings of one name as the same
            // path, so comparing case-sensitively there refused a file that is
            // genuinely inside the folder and left it on disk.
            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return fullCandidate.StartsWith(fullRoot, comparison);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> PauseTorrentAsync(DownloadClient config, string gid)
    {
        var status = await ResolveActiveStatusAsync(config, gid);
        var activeGid = status?.TryGetProperty("gid", out var g) == true ? g.GetString() : gid;
        var result = await SendRpcAsync(config, "aria2.pause", activeGid ?? gid);
        return result != null;
    }

    public async Task<bool> ResumeTorrentAsync(DownloadClient config, string gid)
    {
        var status = await ResolveActiveStatusAsync(config, gid);
        var activeGid = status?.TryGetProperty("gid", out var g) == true ? g.GetString() : gid;
        var result = await SendRpcAsync(config, "aria2.unpause", activeGid ?? gid);
        return result != null;
    }

    /// <summary>
    /// Lists active + waiting + stopped downloads for external-download
    /// detection. aria2 has no category concept, so - same as Transmission -
    /// this filters by <see cref="DownloadClient.Directory"/> (aria2's "dir"
    /// option) rather than the Sportarr "category" string. Without a
    /// configured directory, nothing is returned rather than everything, to
    /// avoid falsely attributing unrelated downloads to Sportarr.
    /// </summary>
    public async Task<List<ExternalDownloadInfo>> GetAllDownloadsByCategoryAsync(DownloadClient config, string category)
    {
        var results = new List<ExternalDownloadInfo>();
        if (string.IsNullOrWhiteSpace(config.Directory)) return results;

        try
        {
            var active = await SendRpcAsync(config, "aria2.tellActive");
            var waiting = await SendRpcAsync(config, "aria2.tellWaiting", 0, 1000);
            var stopped = await SendRpcAsync(config, "aria2.tellStopped", 0, 1000);

            foreach (var batch in new[] { active, waiting, stopped })
            {
                if (batch == null || batch.Value.ValueKind != JsonValueKind.Array) continue;

                foreach (var item in batch.Value.EnumerateArray())
                {
                    var dir = item.TryGetProperty("dir", out var dirEl) ? dirEl.GetString() : null;
                    if (!string.Equals(dir, config.Directory, StringComparison.OrdinalIgnoreCase)) continue;

                    var gid = item.TryGetProperty("gid", out var gidEl) ? gidEl.GetString() : null;
                    var name = GetDownloadName(item);
                    if (string.IsNullOrEmpty(gid) || string.IsNullOrEmpty(name)) continue;

                    var statusStr = item.TryGetProperty("status", out var s) ? s.GetString() : null;
                    long ParseLong(string prop) => item.TryGetProperty(prop, out var v) && long.TryParse(v.GetString(), out var n) ? n : 0;

                    var infoHash = item.TryGetProperty("infoHash", out var ih) ? ih.GetString() : null;

                    results.Add(new ExternalDownloadInfo
                    {
                        DownloadId = gid,
                        Title = name,
                        Category = category,
                        FilePath = Path.Combine(dir!, name),
                        Size = ParseLong("totalLength"),
                        IsCompleted = statusStr == "complete",
                        Protocol = "Torrent",
                        TorrentInfoHash = infoHash
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Aria2] Error listing downloads by directory");
        }

        return results;
    }
}
