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

        var now = DateTime.UtcNow;

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
        // growth at OutputPath). All three modes would misfire on them.
        var inFlight = await db.DvrRecordings
            .Where(r => r.Status == DvrRecordingStatus.Recording)
            .Where(r => r.Method == DvrRecordingMethod.Live)
            .ToListAsync(ct);

        if (inFlight.Count == 0)
        {
            // Garbage-collect stale tracker entries.
            if (_lastSize.Count > 0) _lastSize.Clear();
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
                        try { await recorder.StopRecordingAsync(row.Id); } catch { /* fallthrough to reconcile */ }
                        _lastSize.Remove(row.Id);

                        // Delegate the Completed-vs-Failed call to the same
                        // logic the recorder's own exit callback uses: a
                        // stall this close to the recording's scheduled end
                        // is treated as the live event ending early (the
                        // upstream feed drops the instant the broadcast
                        // stops) rather than a genuinely dead stream, so it
                        // gets finalized, revealed and picked up by the
                        // importer instead of abandoned under its hidden
                        // dot-prefixed filename with an unqueryable status.
                        try
                        {
                            await dvrService.HandleRecorderExitAsync(row.Id, 0,
                                $"watchdog: output stalled (only {grew} bytes written in {(int)elapsed.TotalSeconds}s)");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[DVR Watchdog] Reconciliation failed for stalled recording {Id}", row.Id);
                        }
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
        }

        // Clean tracker entries for rows that are no longer in flight
        // (Completed, Failed, Cancelled) so the dictionary doesn't
        // grow forever.
        var toForget = _lastSize.Keys.Where(k => !seen.Contains(k)).ToList();
        foreach (var id in toForget) _lastSize.Remove(id);

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
}
