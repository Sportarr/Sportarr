using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Synology Download Station client for Sportarr, via the DSM Web API
/// (SYNO.API.Auth for session login, SYNO.DownloadStation2.Task for task
/// management - the newer v2 Task API, used deliberately instead of the
/// older v1 SYNO.DownloadStation.Task: v1's "create" call does not return
/// the new task's id at all, only {"success":true} - the only way to learn
/// the id would be listing tasks and guessing which one is ours by title
/// match. v2's "create" returns task_id directly, which is what lets every
/// downstream Sportarr operation (status polling, pause/resume, delete)
/// target the exact right task instead of a best-effort name match.
///
/// Handles both torrent and NZB tasks through the same "url" task type -
/// Download Station auto-detects the content from the URL/file, matching
/// how a user would add either from the DS web UI itself. Usenet routing
/// happens one layer up in DownloadClientService (DownloadClientType.
/// SynologyDownloadStationUsenet), not here - this class doesn't care which
/// protocol a given call is for.
///
/// Two Synology-specific protocol quirks handled explicitly because getting
/// them wrong silently breaks every call: (1) the DSM Web API always
/// responds HTTP 200 even on failure - success/failure and the reason live
/// in the JSON body's "success"/"error.code" fields, never the HTTP status.
/// (2) SYNO.DownloadStation2.Task's own parameters (type, url, destination,
/// create_list) must be JSON-encoded STRINGS/ARRAYS even though the outer
/// request is a normal form POST - e.g. url=["magnet:?xt=..."] as literal
/// JSON text in a form field, not a native array. Missing either of these
/// looks like it should work and doesn't.
/// </summary>
public class SynologyDownloadStationClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SynologyDownloadStationClient> _logger;
    private HttpClient? _customHttpClient;

    // Session IDs keyed by host:port:username, same rationale as
    // TransmissionClient's _sessionIds - a shared cached client instance
    // must not let concurrent requests stomp each other's session.
    private static readonly ConcurrentDictionary<string, string> _sessionIds = new();

    // DSM error codes meaning "the session is no good, log in again" -
    // covers both auth.cgi's own codes and entry.cgi's shared/session codes.
    private static readonly HashSet<int> AuthErrorCodes = new() { 105, 106, 107, 119 };

    public SynologyDownloadStationClient(HttpClient httpClient, ILogger<SynologyDownloadStationClient> logger)
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

    private static string BuildBaseUrl(DownloadClient config)
    {
        var protocol = config.UseSsl ? "https" : "http";
        var urlBase = string.IsNullOrEmpty(config.UrlBase) ? "" : config.UrlBase;
        if (!string.IsNullOrEmpty(urlBase) && !urlBase.StartsWith('/')) urlBase = "/" + urlBase;
        urlBase = urlBase.TrimEnd('/');
        return $"{protocol}://{config.Host}:{config.Port}{urlBase}";
    }

    private static string SessionKey(DownloadClient config) => $"{config.Host}:{config.Port}:{config.Username}";

    private async Task<string?> LoginAsync(DownloadClient config, bool forceRefresh = false)
    {
        var key = SessionKey(config);
        if (!forceRefresh && _sessionIds.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            var client = GetHttpClient(config);
            var url = $"{BuildBaseUrl(config)}/webapi/auth.cgi" +
                $"?api=SYNO.API.Auth&version=6&method=login" +
                $"&account={Uri.EscapeDataString(config.Username ?? "")}" +
                $"&passwd={Uri.EscapeDataString(config.Password ?? "")}" +
                $"&session=DownloadStation&format=sid";

            using var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("success", out var success) && success.GetBoolean() &&
                doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("sid", out var sidEl))
            {
                var sid = sidEl.GetString();
                if (!string.IsNullOrEmpty(sid))
                {
                    _sessionIds[key] = sid;
                    return sid;
                }
            }

            var errorCode = doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("code", out var codeEl)
                ? codeEl.GetInt32()
                : (int?)null;
            _logger.LogWarning("[Synology] Login failed for {Host} (error code {Code})", config.Host, errorCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Synology] Login request failed for {Host}", config.Host);
            return null;
        }
    }

    /// <summary>
    /// Calls SYNO.DownloadStation2.Task via entry.cgi. Logs in (using the
    /// cached SID if we have one) before the first attempt; if the response
    /// carries a session-related error code, forces a fresh login and
    /// retries exactly once - covers a SID that expired between calls
    /// without re-authenticating on every single request.
    /// </summary>
    private async Task<JsonElement?> CallTaskApiAsync(DownloadClient config, string method, Dictionary<string, string> extraParams, HttpMethod? httpMethod = null)
    {
        var sid = await LoginAsync(config);
        if (sid == null) return null;

        var result = await SendTaskRequestAsync(config, method, extraParams, sid, httpMethod ?? HttpMethod.Post);
        if (result.HasValue) return result;

        // Might have been a session-expiry failure specifically (not a
        // network/other error, which SendTaskRequestAsync already logged
        // and returns null for either way) - force a fresh SID and retry once.
        var freshSid = await LoginAsync(config, forceRefresh: true);
        if (freshSid == null || freshSid == sid) return null;

        return await SendTaskRequestAsync(config, method, extraParams, freshSid, httpMethod ?? HttpMethod.Post);
    }

    private async Task<JsonElement?> SendTaskRequestAsync(DownloadClient config, string method, Dictionary<string, string> extraParams, string sid, HttpMethod httpMethod)
    {
        try
        {
            var client = GetHttpClient(config);
            var url = $"{BuildBaseUrl(config)}/webapi/entry.cgi";

            var form = new Dictionary<string, string>
            {
                ["api"] = "SYNO.DownloadStation2.Task",
                ["version"] = "2",
                ["method"] = method,
                ["_sid"] = sid
            };
            foreach (var (k, v) in extraParams) form[k] = v;

            HttpResponseMessage response;
            if (httpMethod == HttpMethod.Get)
            {
                var query = string.Join("&", form.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
                response = await client.GetAsync($"{url}?{query}");
            }
            else
            {
                response = await client.PostAsync(url, new FormUrlEncodedContent(form));
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[Synology] {Method} HTTP failure: {Status}", method, response.StatusCode);
                    return null;
                }

                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("success", out var success) && success.GetBoolean())
                {
                    // "data" is absent on some successful calls (e.g. delete)
                    // - fall back to an empty object so callers can still
                    // distinguish "call succeeded" (HasValue) from "failed"
                    // (null) without needing a populated payload. Cloned so
                    // the value survives after "doc" is disposed below.
                    return doc.RootElement.TryGetProperty("data", out var data)
                        ? data.Clone()
                        : JsonDocument.Parse("{}").RootElement.Clone();
                }

                var errorCode = doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("code", out var codeEl)
                    ? codeEl.GetInt32()
                    : (int?)null;

                if (errorCode.HasValue && AuthErrorCodes.Contains(errorCode.Value))
                {
                    _logger.LogDebug("[Synology] {Method} session error (code {Code}), will retry with fresh login", method, errorCode);
                    return null; // signals CallTaskApiAsync to refresh + retry
                }

                _logger.LogWarning("[Synology] {Method} failed (error code {Code})", method, errorCode);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Synology] {Method} request error", method);
            return null;
        }
    }

    public async Task<bool> TestConnectionAsync(DownloadClient config)
    {
        var sid = await LoginAsync(config, forceRefresh: true);
        if (sid == null) return false;

        // Exercise the actual task-list call the rest of this class depends
        // on, not just login - a valid login with a user lacking Download
        // Station permission would otherwise report success and then fail
        // on every real operation (the qBittorrent/Plex "issue #21 pattern"
        // of a green test that doesn't prove the thing that matters works).
        var result = await CallTaskApiAsync(config, "list", new Dictionary<string, string> { ["additional"] = "[]" });
        return result != null;
    }

    /// <summary>
    /// Adds a torrent (magnet or .torrent URL) or NZB URL as a Download
    /// Station task. Destination maps Sportarr's category concept onto DS's
    /// own per-task destination folder (a subpath under the shared download
    /// folder) - <see cref="DownloadClient.Directory"/> overrides it when set,
    /// same "explicit override wins" convention used by every other client.
    /// </summary>
    public async Task<string?> AddTaskAsync(DownloadClient config, string url, string category)
    {
        var destination = !string.IsNullOrWhiteSpace(config.Directory) ? config.Directory : category;

        var extraParams = new Dictionary<string, string>
        {
            ["type"] = "\"url\"",
            ["create_list"] = "false",
            ["url"] = JsonSerializer.Serialize(new[] { url }),
            ["destination"] = JsonSerializer.Serialize(destination)
        };

        var result = await CallTaskApiAsync(config, "create", extraParams);
        if (result == null) return null;

        if (result.Value.TryGetProperty("task_id", out var taskIds) &&
            taskIds.ValueKind == JsonValueKind.Array &&
            taskIds.GetArrayLength() > 0)
        {
            var taskId = taskIds[0].GetString();
            _logger.LogInformation("[Synology] Task created: {TaskId}", taskId);
            return taskId;
        }

        // Download Station does not always return the id it just created. The
        // caller reads a null as a failed add and tries again, so the release
        // went on twice and the first task was never tracked: bandwidth spent
        // twice and an orphan left behind. Find the task it made instead.
        _logger.LogWarning("[Synology] create succeeded but no task_id in response; looking the task up by its URL");
        var recovered = await FindTaskIdByUrlAsync(config, url);
        if (recovered != null)
        {
            _logger.LogInformation("[Synology] Matched the created task by URL: {TaskId}", recovered);
        }
        return recovered;
    }

    /// <summary>
    /// Find a task by the URL it was created from, for the case where the
    /// create call did not hand back an id.
    /// </summary>
    private async Task<string?> FindTaskIdByUrlAsync(DownloadClient config, string url)
    {
        try
        {
            var listResult = await CallTaskApiAsync(config, "list",
                new Dictionary<string, string> { ["additional"] = JsonSerializer.Serialize(new[] { "detail" }) },
                HttpMethod.Get);
            if (listResult == null) return null;
            if (!listResult.Value.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var task in tasks.EnumerateArray())
            {
                if (!task.TryGetProperty("additional", out var additional) ||
                    !additional.TryGetProperty("detail", out var detail) ||
                    !detail.TryGetProperty("uri", out var uriEl))
                {
                    continue;
                }

                if (string.Equals(uriEl.GetString(), url, StringComparison.OrdinalIgnoreCase) &&
                    task.TryGetProperty("id", out var idEl))
                {
                    return idEl.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Synology] Could not look the created task up by URL");
        }

        return null;
    }

    /// <summary>
    /// Look up a task by id, optionally scoped to a destination (Directory override,
    /// else the given expected category - same precedence AddTaskAsync uses to
    /// compute destination in the first place).
    /// </summary>
    /// <param name="expectedCategory">
    /// The category this task was actually grabbed under, when the caller wants
    /// destination scoping (falls back to config.Category when non-null but the
    /// caller wants scoping and no per-grab value is known). Pass null to skip
    /// scoping entirely (e.g. the delete-files lookup, which only ever wants to
    /// clean up whatever task id Sportarr itself decided to remove).
    /// </param>
    private async Task<JsonElement?> GetTaskAsync(DownloadClient config, string taskId, string? expectedCategory = null)
    {
        var extraParams = new Dictionary<string, string>
        {
            ["id"] = JsonSerializer.Serialize(new[] { taskId }),
            ["additional"] = JsonSerializer.Serialize(new[] { "detail", "transfer", "file" })
        };

        var result = await CallTaskApiAsync(config, "list", extraParams, HttpMethod.Get);
        if (result == null) return null;

        var wantDestination = !string.IsNullOrWhiteSpace(config.Directory) ? config.Directory : expectedCategory;

        if (result.Value.TryGetProperty("task", out var tasks) && tasks.ValueKind == JsonValueKind.Array)
        {
            foreach (var task in tasks.EnumerateArray())
            {
                if (!task.TryGetProperty("id", out var idEl) || idEl.GetString() != taskId)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(wantDestination))
                {
                    return task;
                }

                string? destination = null;
                if (task.TryGetProperty("additional", out var additional) &&
                    additional.TryGetProperty("detail", out var detail) &&
                    detail.TryGetProperty("destination", out var destEl))
                {
                    destination = destEl.GetString();
                }

                return string.Equals(destination, wantDestination, StringComparison.OrdinalIgnoreCase)
                    ? task
                    : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Get task status for download monitoring.
    /// </summary>
    /// <param name="expectedCategory">
    /// The category (Download Station destination, unless a Directory override is
    /// configured - same precedence AddTaskAsync uses) this task was actually
    /// grabbed under (falls back to config.Category when null). A task id still
    /// existing in Download Station doesn't mean it's still Sportarr's - DS is
    /// commonly shared across multiple *arr-style apps, each scoped to its own
    /// destination folder. If a task's destination doesn't match, it's reported as
    /// not found here rather than matched by id alone, so download monitoring stops
    /// tracking it instead of polling another app's download forever - the task id
    /// never disappears, only its destination does. A blank expected category (no
    /// scoping in use, on either side, and no Directory override) skips the check
    /// and preserves the previous id-only match.
    /// </param>
    public async Task<DownloadClientStatus?> GetTaskStatusAsync(DownloadClient config, string taskId, string? expectedCategory = null)
    {
        var task = await GetTaskAsync(config, taskId, expectedCategory ?? config.Category);
        if (task == null) return null;

        return MapStatus(task.Value);
    }

    private DownloadClientStatus? MapStatus(JsonElement task)
    {
        var statusStr = task.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (statusStr == null) return null;

        // finished/seeding both mean "the video content is fully on disk" -
        // seeding is only relevant to the torrent's continued upload, not
        // whether Sportarr can import it. finishing = post-processing
        // (checksum/extraction), not yet safe to import.
        var mapped = statusStr switch
        {
            "downloading" or "hash_checking" or "extracting" or "filehosting_waiting" => "downloading",
            "waiting" => "queued",
            "paused" => "paused",
            "finished" or "seeding" => "completed",
            "finishing" => "downloading",
            "error" => "error",
            _ => "downloading"
        };

        var title = task.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
        var size = task.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var sz) ? sz : 0;

        long downloaded = 0, uploaded = 0, speed = 0;
        string? destination = null;
        DateTime? completedAt = null;

        if (task.TryGetProperty("additional", out var additional))
        {
            if (additional.TryGetProperty("transfer", out var transfer))
            {
                downloaded = transfer.TryGetProperty("size_downloaded", out var sd) && sd.TryGetInt64(out var d) ? d : 0;
                uploaded = transfer.TryGetProperty("size_uploaded", out var su) && su.TryGetInt64(out var u) ? u : 0;
                speed = transfer.TryGetProperty("speed_download", out var spd) && spd.TryGetInt64(out var sp) ? sp : 0;
            }
            if (additional.TryGetProperty("detail", out var detail))
            {
                destination = detail.TryGetProperty("destination", out var destEl) ? destEl.GetString() : null;
                if (detail.TryGetProperty("completed_time", out var ct) && ct.TryGetInt64(out var completedUnix) && completedUnix > 0)
                {
                    completedAt = DateTimeOffset.FromUnixTimeSeconds(completedUnix).UtcDateTime;
                }
            }
        }

        var savePath = !string.IsNullOrEmpty(destination) && !string.IsNullOrEmpty(title)
            ? Path.Combine(destination, title)
            : destination;

        TimeSpan? timeRemaining = speed > 0 && size > downloaded
            ? TimeSpan.FromSeconds((size - downloaded) / (double)speed)
            : null;

        return new DownloadClientStatus
        {
            Status = mapped,
            Progress = size > 0 ? (downloaded * 100.0 / size) : 0,
            Downloaded = downloaded,
            Size = size,
            TimeRemaining = timeRemaining,
            SavePath = savePath,
            Ratio = downloaded > 0 ? (double)uploaded / downloaded : 0,
            CompletedAt = mapped == "completed" ? completedAt : null
        };
    }

    public async Task<bool> DeleteTaskAsync(DownloadClient config, string taskId, bool deleteFiles)
    {
        // Download Station's own delete has no "and delete files" flag - it
        // only ever removes the task entry. When Sportarr needs the files
        // gone too, fetch the destination path first and remove it directly,
        // same responsibility split as Aria2Client.TryDeleteFilesAsync.
        JsonElement? task = deleteFiles ? await GetTaskAsync(config, taskId) : null;

        var extraParams = new Dictionary<string, string>
        {
            ["id"] = JsonSerializer.Serialize(new[] { taskId }),
            ["force_complete"] = "false"
        };
        var result = await CallTaskApiAsync(config, "delete", extraParams);
        var removed = result != null;

        if (removed && deleteFiles && task.HasValue)
        {
            TryDeleteFiles(task.Value);
        }

        return removed;
    }

    private void TryDeleteFiles(JsonElement task)
    {
        try
        {
            var title = task.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            string? destination = null;
            if (task.TryGetProperty("additional", out var additional) &&
                additional.TryGetProperty("detail", out var detail) &&
                detail.TryGetProperty("destination", out var destEl))
            {
                destination = destEl.GetString();
            }

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(destination)) return;

            // Download Station reports a destination relative to its own
            // shares, such as "video/sports". Joining that to a title and
            // deleting it resolved against Sportarr's working directory
            // instead: usually nothing was there and the files were quietly
            // left behind, and on an unlucky name collision it recursively
            // deleted a local directory that had nothing to do with the
            // download. Only an absolute path can mean anything here.
            if (!Path.IsPathRooted(destination))
            {
                _logger.LogInformation(
                    "[Synology] Removed the task but left its files. Download Station reports '{Destination}' relative to its own shares, " +
                    "which is not a path this machine can act on. Set the download client's directory to the absolute local path if Sportarr should delete them.",
                    destination);
                return;
            }

            var path = Path.Combine(destination, title);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Synology] Failed to delete local files after removing task");
        }
    }

    public async Task<bool> PauseTaskAsync(DownloadClient config, string taskId)
    {
        var extraParams = new Dictionary<string, string> { ["id"] = JsonSerializer.Serialize(new[] { taskId }) };
        var result = await CallTaskApiAsync(config, "pause", extraParams);
        return result != null;
    }

    public async Task<bool> ResumeTaskAsync(DownloadClient config, string taskId)
    {
        var extraParams = new Dictionary<string, string> { ["id"] = JsonSerializer.Serialize(new[] { taskId }) };
        var result = await CallTaskApiAsync(config, "resume", extraParams);
        return result != null;
    }

    /// <summary>
    /// Lists tasks whose destination folder matches Sportarr's configured
    /// category/directory, for external-download detection. Unlike
    /// Transmission/Aria2 (which fall back to "nothing" when no directory is
    /// configured), Download Station's destination genuinely IS the category
    /// concept, so this can filter by config.Category alone with no
    /// directory override required.
    /// </summary>
    public async Task<List<ExternalDownloadInfo>> GetAllDownloadsByCategoryAsync(DownloadClient config, string category, string protocol)
    {
        var results = new List<ExternalDownloadInfo>();
        var wantDestination = !string.IsNullOrWhiteSpace(config.Directory) ? config.Directory : category;

        try
        {
            var extraParams = new Dictionary<string, string>
            {
                ["additional"] = JsonSerializer.Serialize(new[] { "detail", "transfer", "file" })
            };
            var result = await CallTaskApiAsync(config, "list", extraParams, HttpMethod.Get);
            if (result == null || !result.Value.TryGetProperty("task", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var task in tasks.EnumerateArray())
            {
                string? destination = null;
                if (task.TryGetProperty("additional", out var additional) &&
                    additional.TryGetProperty("detail", out var detail) &&
                    detail.TryGetProperty("destination", out var destEl))
                {
                    destination = destEl.GetString();
                }

                if (!string.Equals(destination, wantDestination, StringComparison.OrdinalIgnoreCase)) continue;

                var taskId = task.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var title = task.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
                if (string.IsNullOrEmpty(taskId) || string.IsNullOrEmpty(title)) continue;

                var statusStr = task.TryGetProperty("status", out var s) ? s.GetString() : null;
                var size = task.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var sz) ? sz : 0;

                results.Add(new ExternalDownloadInfo
                {
                    DownloadId = taskId,
                    Title = title,
                    Category = category,
                    FilePath = Path.Combine(destination!, title),
                    Size = size,
                    IsCompleted = statusStr is "finished" or "seeding",
                    Protocol = protocol
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Synology] Error listing downloads by category");
        }

        return results;
    }
}
