using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Downloads finished events from the IPTV provider's catchup/timeshift
/// archive instead of capturing them live.
///
/// Live DVR has to predict when an event starts and ends, must be running
/// for the whole broadcast, and gets exactly one attempt. Catchup flips
/// the model: once the event has aired, the already-recorded window is
/// pulled from the provider's archive — no timing guesswork, app downtime
/// doesn't lose the event, overtime can be re-grabbed with a wider
/// window, and a failed download can be retried for as long as the
/// archive retains the footage (typically several days).
///
/// The scheduler decides per recording which method applies: channels
/// with an Xtream archive (tv_archive=1) get Method=Catchup rows that
/// this service picks up after the event window closes; channels without
/// an archive keep the existing live recording path untouched. The live
/// scheduler and watchdog exclude catchup rows — their wall-clock rules
/// assume a live window that is, for catchup, always in the past.
///
/// Downloads are sequential by design: a single archive pull saturates
/// most providers' per-connection throughput, and serializing keeps the
/// extra connection load on the provider to one stream regardless of how
/// many events finished at the same time (live capture would have needed
/// N simultaneous tuners for N overlapping games).
///
/// Catchup/timeshift download method ported from timeshifter by
/// scottrobertson (github.com/scottrobertson/timeshifter).
/// </summary>
public class CatchupDownloadService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);

    // After this many failed download attempts on the current channel,
    // rotate to the next fallback channel that also has an archive.
    private const int MaxAttemptsPerChannel = 4;

    // Same default the live recorder sends - many panels drop clients
    // that don't look like a real player.
    private const string DefaultUserAgent = "VLC/3.0.18 LibVLC/3.0.18";

    private readonly IServiceProvider _services;
    private readonly ILogger<CatchupDownloadService> _logger;

    // server url -> resolved timezone, cached for the service lifetime.
    // A provider's timezone doesn't change between ticks, and resolving
    // it costs an authenticate round-trip.
    private readonly Dictionary<string, TimeZoneInfo> _serverTimezones = new();

    // Archive flags refresh at startup and then daily - see
    // RefreshArchiveFlagsAsync for why this exists at all.
    private static readonly TimeSpan ArchiveFlagRefreshInterval = TimeSpan.FromHours(24);
    private DateTime _lastArchiveFlagRefresh = DateTime.MinValue;

    public CatchupDownloadService(IServiceProvider services, ILogger<CatchupDownloadService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Catchup] Started; tick interval {Interval}", TickInterval);

        // Let startup settle before the first pass (mirrors the watchdog).
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            // Keep HasArchive/ArchiveDays current without any user action.
            // Channel syncs only run when a source is added or manually
            // re-synced, so installs upgrading to a catchup-capable build
            // would otherwise sit with stale false flags until someone
            // happened to re-sync. Runs at startup (the upgrade restart
            // itself triggers it) and daily, since providers add and
            // remove archive coverage over time.
            if (DateTime.UtcNow - _lastArchiveFlagRefresh >= ArchiveFlagRefreshInterval)
            {
                try
                {
                    await RefreshArchiveFlagsAsync(stoppingToken);
                    _lastArchiveFlagRefresh = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Catchup] Archive flag refresh failed; will retry next tick");
                }
            }

            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Catchup] Tick failed");
            }

            try { await Task.Delay(TickInterval, stoppingToken); } catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("[Catchup] Stopped");
    }

    /// <summary>
    /// Populate / refresh the archive flags on existing channels straight
    /// from the provider's stream list, WITHOUT a full channel re-sync.
    /// A full sync deletes and recreates every channel row (new ids,
    /// remapped leagues); this is a surgical UPDATE matched by stream id,
    /// so channel ids, league mappings, favorites and scheduled
    /// recordings are untouched. A failed or empty fetch never clears
    /// existing flags - downgrading catchup capability on a provider
    /// hiccup would silently flip recordings back to the live path.
    /// </summary>
    private async Task RefreshArchiveFlagsAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var client = scope.ServiceProvider.GetRequiredService<XtreamCodesClient>();

        var sources = await db.IptvSources
            .Where(s => s.IsActive && s.Type == IptvSourceType.Xtream)
            .Where(s => s.Username != null && s.Password != null)
            .ToListAsync(ct);

        foreach (var source in sources)
        {
            if (ct.IsCancellationRequested)
                break;

            var streams = await client.GetLiveStreamsAsync(source.Url, source.Username!, source.Password!);
            if (streams.Count == 0)
            {
                // Empty means the fetch failed or the panel hiccuped
                // (the client returns an empty list on error) - keep
                // whatever flags we already have.
                continue;
            }

            // Manual dictionary fill: last entry wins if a panel ever
            // lists a stream id twice (ToDictionary would throw).
            var archiveById = new Dictionary<int, (bool HasArchive, int Days)>();
            foreach (var s in streams)
            {
                archiveById[s.StreamId] = (s.TvArchive > 0, s.TvArchiveDuration);
            }

            var channels = await db.IptvChannels
                .Where(c => c.SourceId == source.Id)
                .ToListAsync(ct);

            var updated = 0;
            foreach (var channel in channels)
            {
                if (!XtreamCodesClient.TryParseStreamId(channel.StreamUrl, out var streamId))
                    continue;
                if (!archiveById.TryGetValue(streamId, out var info))
                    continue;

                if (channel.HasArchive != info.HasArchive || channel.ArchiveDays != info.Days)
                {
                    channel.HasArchive = info.HasArchive;
                    channel.ArchiveDays = info.Days;
                    updated++;
                }
            }

            if (updated > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "[Catchup] Refreshed archive flags for source '{Name}': {Updated} channel(s) changed, {Total} now catchup-capable",
                    source.Name, updated, channels.Count(c => c.HasArchive));
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();

        var now = DateTime.UtcNow;
        var grace = TimeSpan.FromMinutes(Math.Max(0, config.DvrCatchupReadyGraceMinutes));

        // Crash recovery. Downloads run synchronously inside this tick, so
        // a catchup row already in Recording state when a tick STARTS can
        // only be a leftover from an app crash/restart mid-download. The
        // watchdog deliberately ignores catchup rows (its process/stall
        // rules assume the live recorder), so we recover our own: back to
        // Scheduled, and the next pass re-downloads from the archive -
        // which is the whole point of catchup, the footage is still there.
        var orphaned = await db.DvrRecordings
            .Where(r => r.Status == DvrRecordingStatus.Recording)
            .Where(r => r.Method == DvrRecordingMethod.Catchup)
            .ToListAsync(ct);
        foreach (var row in orphaned)
        {
            _logger.LogWarning(
                "[Catchup] Recovering recording {Id} ('{Title}') left in Recording state by an interrupted download; re-queueing",
                row.Id, row.Title);
            row.Status = DvrRecordingStatus.Scheduled;
            row.ActualStart = null;
            row.LastUpdated = now;
            if (!string.IsNullOrEmpty(row.OutputPath))
            {
                TryDelete(row.OutputPath + ".part");
            }
        }
        if (orphaned.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        // Catchup rows whose recording window has fully closed (plus the
        // ready grace, so the provider has had time to finalize the tail
        // of the archive). Existing rows are processed even if the user
        // has since turned DvrUseCatchupWhenAvailable off - that setting
        // gates NEW scheduling, not work already queued.
        var due = await db.DvrRecordings
            .Include(r => r.Event)
            .Include(r => r.Channel)
            .ThenInclude(c => c!.Source)
            .Where(r => r.Status == DvrRecordingStatus.Scheduled)
            .Where(r => r.Method == DvrRecordingMethod.Catchup)
            .OrderBy(r => r.ScheduledEnd)
            .ToListAsync(ct);

        foreach (var recording in due)
        {
            if (ct.IsCancellationRequested)
                break;

            var readyAt = recording.ScheduledEnd.AddMinutes(recording.PostPadding).Add(grace);
            if (now < readyAt)
                continue;

            await ProcessRecordingAsync(scope.ServiceProvider, db, config, recording, ct);
        }
    }

    private async Task ProcessRecordingAsync(
        IServiceProvider scoped,
        SportarrDbContext db,
        Config config,
        DvrRecording recording,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var channel = recording.Channel;
        var source = channel?.Source;

        // Catchup needs Xtream credentials to build the timeshift URL.
        // A catchup row pointing at a non-Xtream channel shouldn't exist
        // (scheduling gates on it), but guard anyway.
        if (channel == null || source == null || source.Type != IptvSourceType.Xtream ||
            string.IsNullOrEmpty(source.Username) || string.IsNullOrEmpty(source.Password))
        {
            await FailAsync(db, recording, "catchup requires an Xtream channel with credentials", ct);
            return;
        }

        if (!XtreamCodesClient.TryParseStreamId(channel.StreamUrl, out var streamId))
        {
            await FailAsync(db, recording, $"could not parse stream id from channel URL '{channel.StreamUrl}'", ct);
            return;
        }

        // The window we want from the archive. End is capped at "now" the
        // same way the live recorder can't capture footage that doesn't
        // exist yet (relevant when grace is 0 and padding overshoots).
        var windowStartUtc = recording.ScheduledStart.AddMinutes(-recording.PrePadding);
        var windowEndUtc = recording.ScheduledEnd.AddMinutes(recording.PostPadding);
        if (windowEndUtc > now) windowEndUtc = now;
        var minutes = Math.Max(1, (int)Math.Ceiling((windowEndUtc - windowStartUtc).TotalMinutes));

        // Out of retention? The archive no longer holds the start of the
        // window; no retry can ever succeed. Try a fallback channel with a
        // deeper archive before giving up.
        var retentionDays = Math.Max(0, channel.ArchiveDays);
        if (retentionDays > 0 && windowStartUtc < now.AddDays(-retentionDays))
        {
            _logger.LogWarning(
                "[Catchup] Recording {Id} ('{Title}') window start {Start} fell out of channel '{Channel}' archive retention ({Days}d)",
                recording.Id, recording.Title, windowStartUtc, channel.Name, retentionDays);
            if (!await TryRotateToArchiveFallbackAsync(db, recording, "window fell out of archive retention", ct))
            {
                await FailAsync(db, recording,
                    $"window fell out of the provider's {retentionDays}-day archive retention before download succeeded", ct);
            }
            return;
        }

        // The timeshift endpoint expects the start time in the SERVER's
        // local timezone, which the provider reports at authenticate time.
        var serverTz = await ResolveServerTimezoneAsync(scoped, source);
        var serverLocalStart = TimeZoneInfo.ConvertTimeFromUtc(windowStartUtc, serverTz);

        // Which URL style(s) to attempt. In auto mode (the default) the
        // provider's previously-detected style goes first with the other
        // as fallback, so users never have to know what kind of panel
        // their provider runs; an explicit setting pins a single style.
        var modes = TimeshiftModesToTry(config.DvrCatchupTimeshiftMode, source.DetectedCatchupMode);

        // Resolve the output path with the same event-aware naming the
        // live recorder uses, so imports look identical downstream.
        var dvrService = scoped.GetRequiredService<DvrRecordingService>();
        string outputPath;
        try
        {
            outputPath = await dvrService.GenerateOutputPathAsync(recording);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Catchup] Failed to resolve output path for recording {Id}", recording.Id);
            recording.AutoRetryCount++;
            recording.LastUpdated = now;
            await db.SaveChangesAsync(ct);
            return;
        }

        recording.OutputPath = outputPath;
        recording.Status = DvrRecordingStatus.Recording;
        recording.ActualStart = now;
        recording.LastUpdated = now;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[Catchup] Downloading recording {Id} ('{Title}') from archive: channel '{Channel}', window {Start:yyyy-MM-dd HH:mm}+{Minutes}m (server-local {Local:yyyy-MM-dd HH:mm}) -> {Path}",
            recording.Id, recording.Title, channel.Name, windowStartUtc, minutes, serverLocalStart, outputPath);

        var userAgent = string.IsNullOrEmpty(source.UserAgent) ? DefaultUserAgent : source.UserAgent;

        // Attempt each candidate URL style in order. A wrong style fails
        // fast (HTTP error or a tiny body), so the fallback costs little;
        // a real download failure on the right style is indistinguishable
        // here and simply retries next tick like any other failure.
        var success = false;
        string? error = null;
        foreach (var mode in modes)
        {
            var url = XtreamCodesClient.BuildTimeshiftUrl(
                source.Url, source.Username, source.Password, streamId,
                serverLocalStart, minutes, phpMode: mode == "php");

            (success, error) = await DownloadAsync(scoped, url, outputPath, minutes, userAgent, ct);
            if (success)
            {
                // Remember what this provider's panel actually serves so
                // auto mode goes straight to it from now on.
                if (!string.Equals(source.DetectedCatchupMode, mode, StringComparison.OrdinalIgnoreCase))
                {
                    source.DetectedCatchupMode = mode;
                    _logger.LogInformation(
                        "[Catchup] Source '{Source}' serves the '{Mode}' timeshift URL style; remembered for future downloads",
                        source.Name, mode);
                }
                break;
            }

            if (ct.IsCancellationRequested)
                break;

            if (mode != modes[^1])
            {
                _logger.LogInformation(
                    "[Catchup] '{Mode}' timeshift URL style failed for source '{Source}' ({Error}); trying '{Next}'",
                    mode, source.Name, error, modes[^1]);
            }
        }

        if (success)
        {
            recording.Status = DvrRecordingStatus.Completed;
            recording.ActualEnd = DateTime.UtcNow;
            recording.DurationSeconds = minutes * 60;
            recording.ErrorMessage = null;
            try
            {
                recording.FileSize = new FileInfo(outputPath).Length;
            }
            catch { /* size is cosmetic; the import probe re-reads the file */ }
            recording.LastUpdated = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("[Catchup] Downloaded recording {Id} ({Size:F2} GB)",
                recording.Id, (recording.FileSize ?? 0) / 1e9);

            // Same handoff the live scheduler does after a stop: probe the
            // file, score quality, create the EventFile, move into the
            // library. All quality/codec metadata comes from the probe.
            if (recording.EventId.HasValue)
            {
                try
                {
                    var eventDvr = scoped.GetRequiredService<EventDvrService>();
                    await eventDvr.ImportCompletedRecordingAsync(recording.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Catchup] Failed to auto-import recording {Id}", recording.Id);
                }
            }
            return;
        }

        // Failed - the archive still has the window, so put the row back
        // in Scheduled for the next tick. After enough failures on this
        // channel, rotate to a fallback channel that also has an archive.
        recording.AutoRetryCount++;
        recording.Status = DvrRecordingStatus.Scheduled;
        recording.ActualStart = null;
        recording.ErrorMessage = $"catchup attempt {recording.AutoRetryCount} failed: {error}";
        recording.LastUpdated = DateTime.UtcNow;

        _logger.LogWarning("[Catchup] Download failed for recording {Id} (attempt {Attempt}): {Error}",
            recording.Id, recording.AutoRetryCount, error);

        if (recording.AutoRetryCount >= MaxAttemptsPerChannel)
        {
            if (!await TryRotateToArchiveFallbackAsync(db, recording, error ?? "download failed", ct))
            {
                recording.Status = DvrRecordingStatus.Failed;
                recording.ActualEnd = DateTime.UtcNow;
                recording.ErrorMessage =
                    $"catchup failed after {recording.AutoRetryCount} attempts with no archive-capable fallback channel: {error}";
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Swap the recording onto the next fallback channel that also has a
    /// catchup archive, resetting the per-channel attempt counter. Unlike
    /// the live recorder's fallback (which must create a new row because a
    /// live run already consumed this one), nothing has been captured yet,
    /// so mutating the same row keeps history simple. Returns false when
    /// no archive-capable fallback remains.
    /// </summary>
    private async Task<bool> TryRotateToArchiveFallbackAsync(
        SportarrDbContext db, DvrRecording recording, string reason, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(recording.FallbackChannelIds))
            return false;

        List<int> fallbackIds;
        try
        {
            fallbackIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(recording.FallbackChannelIds) ?? new();
        }
        catch
        {
            return false;
        }

        while (fallbackIds.Count > 0)
        {
            var nextId = fallbackIds[0];
            fallbackIds.RemoveAt(0);

            var candidate = await db.IptvChannels
                .Include(c => c.Source)
                .FirstOrDefaultAsync(c => c.Id == nextId, ct);

            if (candidate == null || !candidate.IsEnabled || !candidate.HasArchive ||
                candidate.Source?.Type != IptvSourceType.Xtream)
            {
                continue;
            }

            _logger.LogInformation(
                "[Catchup] Rotating recording {Id} to fallback channel '{Channel}' after: {Reason}",
                recording.Id, candidate.Name, reason);

            recording.ChannelId = candidate.Id;
            recording.Channel = candidate;
            recording.FallbackChannelIds = fallbackIds.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(fallbackIds)
                : null;
            recording.AutoRetryCount = 0;
            recording.Status = DvrRecordingStatus.Scheduled;
            recording.ErrorMessage = $"rotated to fallback channel after: {reason}";
            recording.LastUpdated = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }

        // Exhausted the list - persist the now-empty fallbacks so we don't
        // re-parse the same dead ids next tick.
        recording.FallbackChannelIds = null;
        return false;
    }

    /// <summary>
    /// Order of timeshift URL styles to attempt for a download.
    ///
    /// An explicit setting ("path" / "php") pins that single style for
    /// unusual panels. "auto" - the default, and what normal users should
    /// never have to think about - tries the provider's previously
    /// detected style first and keeps the other as fallback, so a wrong
    /// or stale detection self-heals on the next download instead of
    /// hard-failing.
    /// </summary>
    public static string[] TimeshiftModesToTry(string? configMode, string? detectedMode)
    {
        if (string.Equals(configMode, "path", StringComparison.OrdinalIgnoreCase))
            return new[] { "path" };
        if (string.Equals(configMode, "php", StringComparison.OrdinalIgnoreCase))
            return new[] { "php" };

        // Auto: detected style first, the other as fallback. Path leads
        // when nothing has been detected yet - it's what most panels use.
        return string.Equals(detectedMode, "php", StringComparison.OrdinalIgnoreCase)
            ? new[] { "php", "path" }
            : new[] { "path", "php" };
    }

    /// <summary>
    /// Resolve the provider's local timezone from its auth response
    /// (server_info.timezone, an IANA name). The timeshift endpoint
    /// interprets start times in this zone. Falls back to UTC when the
    /// provider doesn't report one or the id doesn't resolve - for the
    /// many providers that run their panels on UTC this is exact.
    /// </summary>
    private async Task<TimeZoneInfo> ResolveServerTimezoneAsync(IServiceProvider scoped, IptvSource source)
    {
        var key = source.Url;
        if (_serverTimezones.TryGetValue(key, out var cached))
            return cached;

        var tz = TimeZoneInfo.Utc;
        try
        {
            var client = scoped.GetRequiredService<XtreamCodesClient>();
            var auth = await client.AuthenticateAsync(source.Url, source.Username!, source.Password!);
            var tzName = auth?.ServerInfo?.Timezone;
            if (!string.IsNullOrEmpty(tzName))
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(tzName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Catchup] Could not resolve timezone for source '{Url}', assuming UTC: {Error}",
                source.Url, ex.Message);
        }

        _serverTimezones[key] = tz;
        return tz;
    }

    /// <summary>
    /// Download the timeshift URL and remux it in a single ffmpeg pass.
    ///
    /// The raw archive .ts has discontinuous timestamps (the provider
    /// stitches stored segments), which leaves the file unseekable and
    /// reporting a nonsense duration - which would also corrupt the
    /// quality probe at import. -fflags +genpts with stream copy rewrites
    /// the timestamps without re-encoding. The work file uses a .part
    /// extension that library scanners ignore, and the finished file is
    /// moved into place atomically so a media server never sees a
    /// partial recording.
    /// </summary>
    private async Task<(bool Success, string? Error)> DownloadAsync(
        IServiceProvider scoped,
        string url,
        string outputPath,
        int windowMinutes,
        string userAgent,
        CancellationToken ct)
    {
        var recorder = scoped.GetRequiredService<FFmpegRecorderService>();
        var ffmpegPath = recorder.GetFFmpegPath();
        if (string.IsNullOrEmpty(ffmpegPath))
        {
            return (false, "ffmpeg not found");
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var partPath = outputPath + ".part";

        // Container by output extension (set by GenerateOutputPathAsync
        // from DvrContainer). The .part work file hides the extension
        // from ffmpeg, so the muxer is always passed explicitly.
        var extension = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
        var (muxer, extraArgs) = extension switch
        {
            "mp4" => ("mp4", "-movflags +faststart"),
            "mkv" => ("matroska", ""),
            _ => ("mpegts", "")
        };

        var args = $"-y -hide_banner -loglevel error -nostats " +
                   $"-fflags +genpts " +
                   $"-user_agent \"{userAgent}\" " +
                   $"-i \"{url}\" " +
                   $"-c copy " +
                   $"-avoid_negative_ts make_zero " +
                   (string.IsNullOrEmpty(extraArgs) ? "" : extraArgs + " ") +
                   $"-f {muxer} \"{partPath}\"";

        // An archive download normally runs much faster than realtime,
        // but a throttled provider can drip-feed at ~1x. Give it the
        // window duration plus half again before declaring it hung.
        var timeout = TimeSpan.FromMinutes(windowMinutes * 1.5 + 10);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "failed to start ffmpeg");
            }

            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                TryDelete(partPath);
                return ct.IsCancellationRequested
                    ? (false, "shutdown requested")
                    : (false, $"download timed out after {(int)timeout.TotalMinutes} minutes");
            }

            if (process.ExitCode != 0)
            {
                var stderr = (await stderrTask).Trim();
                TryDelete(partPath);
                var detail = string.IsNullOrEmpty(stderr) ? $"ffmpeg exited {process.ExitCode}" : stderr;
                // Keep the tail - ffmpeg puts the actionable error last.
                if (detail.Length > 500) detail = "..." + detail[^500..];
                return (false, detail);
            }

            // A 2xx response with a tiny body is how panels signal
            // "window not in archive" without an HTTP error.
            var size = new FileInfo(partPath).Length;
            if (size < 1024 * 1024)
            {
                TryDelete(partPath);
                return (false, $"archive returned only {size} bytes - window likely not available yet");
            }

            File.Move(partPath, outputPath, overwrite: true);
            return (true, null);
        }
        catch (Exception ex)
        {
            TryDelete(partPath);
            return (false, ex.Message);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private async Task FailAsync(SportarrDbContext db, DvrRecording recording, string error, CancellationToken ct)
    {
        _logger.LogWarning("[Catchup] Recording {Id} ('{Title}') failed: {Error}",
            recording.Id, recording.Title, error);
        recording.Status = DvrRecordingStatus.Failed;
        recording.ActualEnd = DateTime.UtcNow;
        recording.ErrorMessage = "catchup: " + error;
        recording.LastUpdated = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
