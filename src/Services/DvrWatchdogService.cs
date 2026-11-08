using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Reconciles DvrRecording.Status against the actual FFmpeg process
/// state and the wall clock. Without this, three failure modes leave
/// recordings stuck in Status=Recording forever:
///
/// 1. The FFmpeg child process crashed silently (segfault, OOM,
///    network drop with no auto-reconnect) - the row stays Recording
///    but no process exists.
/// 2. The whole app crashed mid-recording and restarted - the
///    in-memory _activeRecordings dictionary lost the process handle
///    but the row still says Recording.
/// 3. The source stream is alive but the file isn't growing - frozen
///    upstream that ffmpeg keeps "reading" but produces no output.
/// 4. The file grows at a normal rate but holds the same few seconds
///    over and over - a provider looping a clip in place of a channel
///    it lost (see <see cref="StreamLoopMonitor"/>).
///
/// On every tick we walk the DB rows in Recording state, compare them
/// to FFmpegRecorderService's in-memory map and the output file's
/// growth, and either gracefully stop runs that overran or fail-mark
/// runs that have no live writer behind them.
/// </summary>
public class DvrWatchdogService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    // Allow scheduled-end + global post-padding + this much grace
    // before we forcibly stop a run. Leaves room for end-of-stream
    // flush and slow disk sync without us racing the recorder.
    private static readonly TimeSpan OverrunGrace = TimeSpan.FromMinutes(5);
    // If the output file hasn't grown by this many bytes in this
    // window after recording start, assume the writer is dead and
    // mark the row Failed.
    private const long StalledMinBytesGrowth = 4096;
    private static readonly TimeSpan StalledNoGrowthWindow = TimeSpan.FromMinutes(2);

    private readonly IServiceProvider _services;
    private readonly ILogger<DvrWatchdogService> _logger;

    // Tracks last-observed file size per recordingId so we can detect
    // a stalled writer between ticks. Cleared when a recording leaves
    // the Recording state.
    private readonly Dictionary<int, (long Size, DateTime At)> _lastSize = new();

    // A streak of loop verdicts ends when the ticks stop for longer than
    // this, for example during a slow tick or a mode that skipped mode 4.
    private static readonly TimeSpan MaxLoopTickGap = TickInterval * 3;

    // A loop stop must reach the exit handler this long before the last
    // part of the window starts.
    internal static readonly TimeSpan LoopStopMargin = TimeSpan.FromMinutes(1);

    // The current streak of loop verdicts for each recording.
    private readonly Dictionary<int, LoopStreak> _loopStreaks = new();

    // Recordings that loop and cannot rotate. The warning comes once for
    // each recording.
    private readonly HashSet<int> _loopWarned = new();

    /// <summary>
    /// A run of ticks that all gave a loop verdict. <see cref="Since"/> is the
    /// first verdict. <see cref="LastAt"/> is the most recent one.
    /// </summary>
    internal sealed record LoopStreak(DateTime Since, DateTime LastAt);

    /// <summary>What mode 4 does with one tick's loop verdict.</summary>
    internal enum LoopResponse
    {
        /// <summary>No loop, or a loop that has not lasted long enough.</summary>
        Wait,
        /// <summary>Stop the recorder, so the exit handler rotates to a fallback.</summary>
        Stop,
        /// <summary>Keep recording. No fallback channel can take over.</summary>
        KeepRecordingNoFallback,
        /// <summary>Keep recording. The exit handler would complete the recording and not rotate.</summary>
        KeepRecordingNearEnd,
    }

    public DvrWatchdogService(IServiceProvider services, ILogger<DvrWatchdogService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[DVR Watchdog] Started; tick interval {Interval}", TickInterval);

        // First tick after a small delay so the rest of startup
        // settles before we touch the recorder service.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DVR Watchdog] Tick failed");
            }

            try { await Task.Delay(TickInterval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var recorder = scope.ServiceProvider.GetRequiredService<FFmpegRecorderService>();
        // Shares this scope's DbContext with `db`, so rows loaded below
        // are tracked by the same context its fallback rotation saves.
        var dvrService = scope.ServiceProvider.GetRequiredService<DvrRecordingService>();
        var loopMonitor = scope.ServiceProvider.GetRequiredService<StreamLoopMonitor>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();

        var now = DateTime.UtcNow;
        // A config read that fails skips the loop check for this tick. The
        // loop state stays, and modes 0 to 3 still run. Null means the
        // setting is unknown.
        bool? loopDetectionEnabled;
        try
        {
            loopDetectionEnabled = (await configService.GetConfigAsync()).DvrLoopDetectionEnabled;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[DVR Watchdog] Could not read the loop-detection setting; the looping-stream check skips this tick");
            loopDetectionEnabled = null;
        }
        if (loopDetectionEnabled == false)
        {
            // A later enable then waits a whole window again.
            ForgetLoops(loopMonitor);
        }

        // Mode 0 - missed-schedule recovery. A Scheduled row whose
        // entire window (ScheduledEnd + PostPadding) is now in the
        // past was missed entirely - probably because the app was
        // down during its start time, or no IPTV slot was available
        // and the conflict policy was Refuse. Mark it Failed so the
        // user can see what happened and decide whether to reschedule.
        //
        // Catchup rows are exempt: a closed window is their NORMAL
        // trigger condition (the archive download only starts after the
        // event ends), so this rule would instantly fail every one of
        // them. CatchupDownloadService owns their lifecycle, including
        // failing rows whose window falls out of archive retention.
        var missed = await db.DvrRecordings
            .Where(r => r.Status == DvrRecordingStatus.Scheduled)
            .Where(r => r.Method == DvrRecordingMethod.Live)
            .Where(r => r.ScheduledEnd.AddMinutes(r.PostPadding) < now)
            .ToListAsync(ct);
        foreach (var row in missed)
        {
            _logger.LogWarning(
                "[DVR Watchdog] Missed recording {Id} ('{Title}'): scheduled window closed at {End} (+{Pad}m), still in Scheduled state. Marking Failed.",
                row.Id, row.Title, row.ScheduledEnd, row.PostPadding);
            row.Status = DvrRecordingStatus.Failed;
            row.ActualEnd = now;
            row.ErrorMessage = (row.ErrorMessage ?? "") +
                "Watchdog: missed - the recording window closed before any recorder picked it up (app downtime, no available source slot, or scheduling conflict).";
            await dvrService.NotifyRecordingFailedAsync(row,
                "The recording window closed before any recorder picked it up (app downtime, no available source slot, or scheduling conflict).");
        }

        // Catchup downloads sit in Recording state with no recorder
        // process handle (CatchupDownloadService runs its own ffmpeg and
        // enforces its own timeout), a window that's always in the past
        // (overrun rule), and a .part work file (stall rule can't see
        // growth at OutputPath). The stall and loop checks would misfire on them.
        var inFlight = await db.DvrRecordings
            .Where(r => r.Status == DvrRecordingStatus.Recording)
            .Where(r => r.Method == DvrRecordingMethod.Live)
            .ToListAsync(ct);

        if (inFlight.Count == 0)
        {
            // Garbage-collect stale tracker entries.
            if (_lastSize.Count > 0) _lastSize.Clear();
            ForgetLoops(loopMonitor);
            // Persist any missed-schedule transitions before returning.
            if (missed.Count > 0) await db.SaveChangesAsync(ct);
            return;
        }

        var seen = new HashSet<int>();

        foreach (var row in inFlight)
        {
            seen.Add(row.Id);

            // A recording being finished off has no process by design: it is
            // past its capture and into revealing, remuxing and measuring,
            // which for a large file runs for minutes. Reconciling it there
            // stamped a failure on a recording that was ending normally.
            if (recorder.IsFinalizing(row.Id))
            {
                _lastSize.Remove(row.Id);
                continue;
            }

            var processAlive = recorder.IsRecordingActive(row.Id);

            // Mode 1 - process is dead but row says Recording. Either
            // the recorder crashed or the app restarted and lost the
            // handle. Either way: mark Failed so the user can retry.
            if (!processAlive)
            {
                // The recorder's exit callback may have finalized (or
                // rotated) this row between our snapshot query and now;
                // re-read before overwriting a terminal state.
                await db.Entry(row).ReloadAsync(ct);
                if (row.Status != DvrRecordingStatus.Recording)
                {
                    _lastSize.Remove(row.Id);
                    continue;
                }

                // A recorder this app does not track can still be writing
                // this file. An app restart leaves the previous instance's
                // FFmpeg running and reparented, and finalising or rotating the
                // row here would put a second recorder on the same output path.
                // Growth is the only evidence available without a stored pid.
                if (IsOutputStillGrowing(row, now, out var growingSize))
                {
                    _logger.LogWarning(
                        "[DVR Watchdog] Recording {Id} has no tracked FFmpeg process but its output is still being written ({Size} bytes). Leaving it alone; a recorder from an earlier run is probably still writing it.",
                        row.Id, growingSize);
                    continue;
                }

                _logger.LogWarning(
                    "[DVR Watchdog] Recording {Id} is in Recording state but no live FFmpeg process is tracked; reconciling",
                    row.Id);
                _lastSize.Remove(row.Id);

                // Delegate to the recorder-exit handler instead of
                // hard-failing: it finalizes as Completed when the exit
                // landed at the natural end of the window with data on
                // disk (the common case when the exit callback's status
                // write was lost to a busy database or an app restart),
                // and otherwise marks Failed with fallback rotation -
                // exactly what this branch used to duplicate by hand,
                // minus the completed-recording-wearing-an-error outcome.
                try
                {
                    await dvrService.HandleRecorderExitAsync(row.Id, 0,
                        "watchdog: recorder process was not tracked");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[DVR Watchdog] Reconciliation failed for recording {Id}", row.Id);
                }
                continue;
            }

            // Mode 2 - run overran by more than the grace window.
            // Force a graceful stop. The recorder's StopRecordingAsync
            // sends 'q' to FFmpeg stdin and falls back to kill on
            // timeout; status will land Completed via that path.
            var stopBy = row.ScheduledEnd.AddMinutes(row.PostPadding).Add(OverrunGrace);
            if (now > stopBy)
            {
                _logger.LogWarning(
                    "[DVR Watchdog] Recording {Id} overran (scheduled end {End} + {Pad}m + {Grace}m grace); forcing stop",
                    row.Id, row.ScheduledEnd, row.PostPadding, (int)OverrunGrace.TotalMinutes);
                _ = recorder.StopRecordingAsync(row.Id);
                _lastSize.Remove(row.Id);
                continue;
            }

            // Mode 3 - process is alive but output isn't growing.
            // We need at least one prior observation to compare
            // against, so on the first tick we just sample.
            long? currentSize = null;
            if (!string.IsNullOrEmpty(row.OutputPath) && File.Exists(row.OutputPath))
            {
                try { currentSize = new FileInfo(row.OutputPath).Length; }
                catch { /* ignore transient FS errors */ }
            }

            if (currentSize.HasValue)
            {
                if (_lastSize.TryGetValue(row.Id, out var prev))
                {
                    var grew = currentSize.Value - prev.Size;
                    var elapsed = now - prev.At;
                    if (elapsed >= StalledNoGrowthWindow && grew < StalledMinBytesGrowth)
                    {
                        _logger.LogWarning(
                            "[DVR Watchdog] Recording {Id} output stalled: {Grew} bytes in {Elapsed}; killing FFmpeg and reconciling",
                            row.Id, grew, elapsed);
                        await StopAndReconcileAsync(recorder, dvrService, loopMonitor, row.Id,
                            $"watchdog: output stalled (only {grew} bytes written in {(int)elapsed.TotalSeconds}s)");
                        continue;
                    }
                    // Still growing - refresh the sample so the next
                    // tick measures from "now" rather than from start.
                    if (grew >= StalledMinBytesGrowth)
                    {
                        _lastSize[row.Id] = (currentSize.Value, now);
                    }
                }
                else
                {
                    _lastSize[row.Id] = (currentSize.Value, now);
                }
            }

            // Mode 4 - output grows but repeats itself. A stop goes through
            // the same exit handler as a stall. With the setting unknown,
            // mode 4 does nothing and keeps its state.
            if (loopDetectionEnabled is bool enabled)
            {
                if (WatchesForLoop(enabled, now, row.ScheduledStart))
                {
                    // A failure here must not stop modes 0 to 3 or the save below.
                    try
                    {
                        await CheckForLoopAsync(db, row, now, recorder, dvrService, loopMonitor, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[DVR Watchdog] Looping-stream check failed for recording {Id}", row.Id);
                    }
                }
                else
                {
                    // Pre-event footage is no evidence. The watch starts at the
                    // scheduled start.
                    loopMonitor.Forget(row.Id);
                    _loopStreaks.Remove(row.Id);
                }
            }
        }

        // Clean tracker entries for rows that are no longer in flight
        // (Completed, Failed, Cancelled) so the dictionary doesn't
        // grow forever.
        var toForget = _lastSize.Keys.Where(k => !seen.Contains(k)).ToList();
        foreach (var id in toForget) _lastSize.Remove(id);
        loopMonitor.ForgetAllExcept(seen);
        foreach (var id in _loopStreaks.Keys.Where(id => !seen.Contains(id)).ToList())
            _loopStreaks.Remove(id);
        _loopWarned.IntersectWith(seen);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DVR Watchdog] Failed to persist watchdog status updates");
        }
    }

    /// <summary>
    /// Mode 4 for one recording. A stop needs a loop that lasts and a
    /// fallback channel to rotate to.
    /// </summary>
    private async Task CheckForLoopAsync(
        SportarrDbContext db, DvrRecording row, DateTime now, FFmpegRecorderService recorder,
        DvrRecordingService dvrService, StreamLoopMonitor loopMonitor, CancellationToken ct)
    {
        var looping = await loopMonitor.IsLoopingAsync(row.Id, row.OutputPath, now, ct);
        var streak = NextLoopStreak(_loopStreaks.GetValueOrDefault(row.Id), looping, now);
        if (streak == null)
        {
            // A later loop is a new event, so it warns again.
            _loopStreaks.Remove(row.Id);
            _loopWarned.Remove(row.Id);
        }
        else
        {
            _loopStreaks[row.Id] = streak;
        }

        // The tick time can be old by now. The look and earlier rows take
        // time, so the decision uses the current clock.
        var response = await DecideOnLoopAsync(streak, now, DateTime.UtcNow, row.ScheduledStart, row.ScheduledEnd,
            row.PostPadding, () => dvrService.HasUsableFallbackAsync(row));

        var minutes = (int)(now - (streak?.Since ?? now) + StreamLoopMonitor.Window).TotalMinutes;
        switch (response)
        {
            case LoopResponse.Stop:
                _logger.LogWarning(
                    "[DVR Watchdog] Recording {Id} has repeated the same short clip for about {Minutes} minutes. Stopping FFmpeg to rotate to a fallback channel",
                    row.Id, minutes);
                await StopForLoopAsync(db, row, recorder, dvrService, loopMonitor, ct,
                    $"watchdog: stream is looping (the same short clip for about {minutes} minutes)");
                break;

            case LoopResponse.KeepRecordingNoFallback:
            case LoopResponse.KeepRecordingNearEnd:
                var reason = response == LoopResponse.KeepRecordingNoFallback
                    ? "no usable fallback channel can take over"
                    : "the recording is in the last part of its window, where a stop completes it instead of rotating";
                if (_loopWarned.Add(row.Id))
                    _logger.LogWarning(
                        "[DVR Watchdog] Recording {Id} has repeated the same short clip for about {Minutes} minutes. Recording continues, because {Reason}",
                        row.Id, minutes, reason);
                else
                    _logger.LogDebug("[DVR Watchdog] Recording {Id} still repeats the same clip. Recording continues, because {Reason}", row.Id, reason);
                break;
        }
    }

    /// <summary>
    /// Stops a looping recorder and hands the row to the exit handler. The
    /// look can take seconds. In that time the recorder can exit and its own
    /// callback can rotate the row. So the method checks again first. After
    /// the stop it calls the exit handler only when no process is left. The
    /// handler skips a row that is no longer Recording or already being
    /// finalized, so a second call does nothing.
    /// </summary>
    private async Task StopForLoopAsync(
        SportarrDbContext db, DvrRecording row, FFmpegRecorderService recorder, DvrRecordingService dvrService,
        StreamLoopMonitor loopMonitor, CancellationToken ct, string reason)
    {
        var id = row.Id;
        _loopStreaks.Remove(id);
        _loopWarned.Remove(id);
        loopMonitor.Forget(id);

        if (!recorder.IsRecordingActive(id) || recorder.IsFinalizing(id))
        {
            _logger.LogDebug("[DVR Watchdog] Recording {Id} stopped while it was checked for a loop; leaving it to its exit handler", id);
            return;
        }

        await db.Entry(row).ReloadAsync(ct);
        if (db.Entry(row).State == EntityState.Detached || row.Status != DvrRecordingStatus.Recording)
        {
            _logger.LogDebug("[DVR Watchdog] Recording {Id} left the Recording state while it was checked for a loop", id);
            return;
        }

        RecordingResult stopped;
        try
        {
            stopped = await recorder.StopRecordingAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DVR Watchdog] Could not stop looping recording {Id}", id);
            return;
        }
        _lastSize.Remove(id);

        // The stop can fail and leave FFmpeg running. Mode 1 or a later
        // tick handles that recording.
        if (recorder.IsRecordingActive(id))
        {
            _logger.LogWarning("[DVR Watchdog] Recording {Id} is still running after the stop ({Error})", id, stopped.Error);
            return;
        }

        try
        {
            await dvrService.HandleRecorderExitAsync(id, 0, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DVR Watchdog] Reconciliation failed for recording {Id} ({Reason})", id, reason);
        }
    }

    /// <summary>
    /// The streak after this tick's verdict. A tick without a loop ends the
    /// streak. A long pause between ticks starts a new streak.
    /// </summary>
    internal static LoopStreak? NextLoopStreak(LoopStreak? streak, bool looping, DateTime now)
    {
        if (!looping)
            return null;
        if (streak == null || now - streak.LastAt > MaxLoopTickGap)
            return new LoopStreak(now, now);
        return streak with { LastAt = now };
    }

    /// <summary>
    /// What mode 4 does with the current streak. The streak must cover one
    /// whole window after its first verdict. The first and the last verdict
    /// then judge two windows that do not overlap, six minutes of footage in
    /// total. A short loop in an ad break or a promo does not last that long.
    /// The method asks for a fallback only when a stop is otherwise due.
    /// <paramref name="tickTime"/> is the time of this tick's verdict.
    /// <paramref name="decidedAt"/> is the current clock.
    /// </summary>
    internal static async Task<LoopResponse> DecideOnLoopAsync(
        LoopStreak? streak, DateTime tickTime, DateTime decidedAt, DateTime scheduledStart, DateTime scheduledEnd,
        int postPaddingMinutes, Func<Task<bool>> hasUsableFallback)
    {
        if (streak == null || tickTime - streak.Since < StreamLoopMonitor.Window)
            return LoopResponse.Wait;

        // Near the end the exit handler completes the recording and does
        // not rotate. A stop there only cuts off the event. The stop itself
        // takes time, up to a 10-second graceful wait, so the margin keeps
        // the exit handler out of that part of the window. A positive size
        // stands for the capture on disk, so only the time rules apply.
        if (DvrRecordingService.ExitLooksLikeANormalEnd(decidedAt + LoopStopMargin, scheduledStart, scheduledEnd, postPaddingMinutes, fileSize: 1))
            return LoopResponse.KeepRecordingNearEnd;

        // The loop can be filler, such as a rain delay. Without a fallback a
        // stop loses the rest of the event.
        if (!await hasUsableFallback())
            return LoopResponse.KeepRecordingNoFallback;

        return LoopResponse.Stop;
    }

    /// <summary>
    /// Whether mode 4 looks at a recording on this tick. Footage before the
    /// scheduled start is often a promo or a holding loop, so it does not
    /// count.
    /// </summary>
    internal static bool WatchesForLoop(bool enabled, DateTime now, DateTime scheduledStart) =>
        enabled && now >= scheduledStart;

    private void ForgetLoops(StreamLoopMonitor loopMonitor)
    {
        loopMonitor.ForgetAll();
        _loopStreaks.Clear();
        _loopWarned.Clear();
    }

    /// <summary>
    /// True while the output file still looks like something is writing it.
    ///
    /// A single quiet tick is not evidence. An upstream stream can pause and a
    /// write can sit in a buffer for longer than one tick, and treating that as
    /// a dead writer would reconcile and rotate the row while the earlier
    /// run's FFmpeg is still going, putting two recorders on one file. Only a
    /// whole window with no growth counts, the same threshold the tracked
    /// recordings use.
    ///
    /// No earlier sample counts as growth on purpose, because the tracker is
    /// empty right after the restart that strands a recorder in the first
    /// place.
    /// </summary>
    private bool IsOutputStillGrowing(DvrRecording row, DateTime now, out long size)
    {
        size = 0;
        if (string.IsNullOrEmpty(row.OutputPath) || !File.Exists(row.OutputPath))
        {
            return false;
        }

        try
        {
            size = new FileInfo(row.OutputPath).Length;
        }
        catch
        {
            return false;
        }

        if (!_lastSize.TryGetValue(row.Id, out var prev))
        {
            _lastSize[row.Id] = (size, now);
            return true;
        }

        if (size > prev.Size)
        {
            _lastSize[row.Id] = (size, now);
            return true;
        }

        // The timestamp is when the file last grew, not when it was last
        // looked at, so this measures a real quiet stretch.
        return now - prev.At < StalledNoGrowthWindow;
    }

    /// <summary>
    /// Stops a recorder that the watchdog gave up on. The row then goes to
    /// the exit handler that the recorder's own exit callback uses. That
    /// handler decides between Completed and Failed.
    ///
    /// A stop close to the scheduled end counts as a live event that ended
    /// early, because the feed drops when the broadcast stops. The handler
    /// then finalizes the recording, so the importer picks it up. Without
    /// that, the capture stays under its hidden file name. A stop earlier in
    /// the window fails the recording and rotates it to a fallback channel.
    /// </summary>
    private async Task StopAndReconcileAsync(
        FFmpegRecorderService recorder, DvrRecordingService dvrService, StreamLoopMonitor loopMonitor,
        int recordingId, string reason)
    {
        try { await recorder.StopRecordingAsync(recordingId); } catch { /* fallthrough to reconcile */ }
        _lastSize.Remove(recordingId);
        loopMonitor.Forget(recordingId);
        _loopStreaks.Remove(recordingId);
        _loopWarned.Remove(recordingId);

        try
        {
            await dvrService.HandleRecorderExitAsync(recordingId, 0, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DVR Watchdog] Reconciliation failed for recording {Id} ({Reason})", recordingId, reason);
        }
    }
}
