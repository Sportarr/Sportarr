using Sportarr.Api.Helpers;

namespace Sportarr.Api.Services;

/// <summary>
/// Watches live captures for a provider that loops a short clip in place of
/// a lost channel. <see cref="FrameRecurrence"/> gives the verdict.
///
/// Each look fingerprints the keyframes that the recorder added to the
/// capture since the previous look. The monitor never opens the stream, so a
/// provider with one connection per account never gets a second request.
///
/// The monitor watches only transport-stream captures. A part cut from the
/// middle of a .ts file still decodes. A part of a matroska or mp4 file that
/// is still being written does not. Stream-copy recordings capture as .ts,
/// and that is the default. Re-encoded recordings write their final
/// container directly, so the monitor does not watch them.
///
/// The class is not thread-safe. The DVR watchdog is its only caller, and it
/// runs one tick at a time.
/// </summary>
public class StreamLoopMonitor
{
    // A verdict looks at this much footage. The first pass of a clip has no
    // earlier match, and 80% of the window must repeat. So a clip of up to
    // about 36 seconds is caught. A longer loop is not.
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(3);

    // This cap limits the work of one look. 64 MB covers 30 seconds of a
    // stream up to about 17 Mbps. A look above the cap reads only the most
    // recent 64 MB and records a gap in front of it.
    internal const long MaxBytesPerLook = 64L * 1024 * 1024;

    // Each look stops this far before the end of the file. The recorder can
    // still be writing the last keyframe there. The next look starts at the
    // same position, so a keyframe that spans that position can be lost.
    // A lost keyframe also shifts the lags across it by one. The period band
    // absorbs that for periods of five keyframes or more. Periods of three
    // and four have no band, so there the lost keyframe lowers the score and
    // can delay a verdict.
    internal const long TailMargin = 512L * 1024;

    // Ranges start and end on transport-stream packet boundaries.
    private const long PacketSize = 188;

    // A capture with no video gets this many tries before the monitor stops
    // sampling it. One short range can fail to show the video stream.
    private const int NoVideoLooksBeforeSkip = 2;

    private readonly IKeyframeSampler _sampler;
    private readonly ILogger<StreamLoopMonitor> _logger;
    private readonly Dictionary<int, Capture> _captures = new();

    public StreamLoopMonitor(IKeyframeSampler sampler, ILogger<StreamLoopMonitor> logger)
    {
        _sampler = sampler;
        _logger = logger;
    }

    /// <summary>
    /// Reads what the recorder wrote to <paramref name="outputPath"/> since the
    /// previous call for this recording. Returns whether the last
    /// <see cref="Window"/> of it is a loop. The first look only notes the
    /// file position, so the monitor never judges footage from before the
    /// watch started. A true answer comes only after a whole window.
    /// </summary>
    public async Task<bool> IsLoopingAsync(int recordingId, string? outputPath, DateTime now, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(outputPath) ||
            !outputPath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(outputPath))
            return false;

        if (!_captures.TryGetValue(recordingId, out var capture) || capture.Path != outputPath)
        {
            capture = new Capture(outputPath, now);
            _captures[recordingId] = capture;
        }

        if (capture.Skipped)
            return false;

        long size;
        try { size = new FileInfo(capture.Path).Length; }
        catch (IOException) { return false; }

        if (capture.Offset is long offset && size < offset)
        {
            // The file got shorter, so it is a different capture now. Start
            // the watch again.
            _logger.LogDebug("[DVR Loop] {Path} got shorter; watching it again from its end", capture.Path);
            capture = new Capture(outputPath, now);
            _captures[recordingId] = capture;
        }

        // A failed look gives no fresh evidence, so it gives no verdict. The
        // watchdog then restarts its count of loop verdicts.
        if (!await ReadNewKeyframesAsync(capture, size, now, ct))
            return false;
        if (capture.WatchedSince > now - Window)
            return false;

        var (keyframes, gaps) = capture.Snapshot();
        return FrameRecurrence.LooksLooped(keyframes, gaps);
    }

    /// <summary>Drops what was gathered for recordings that are no longer in flight.</summary>
    public void ForgetAllExcept(IReadOnlySet<int> recordingIds)
    {
        foreach (var id in _captures.Keys.Where(id => !recordingIds.Contains(id)).ToList())
            _captures.Remove(id);
    }

    public void ForgetAll() => _captures.Clear();

    public void Forget(int recordingId) => _captures.Remove(recordingId);

    /// <summary>False when the look failed.</summary>
    private async Task<bool> ReadNewKeyframesAsync(Capture capture, long size, DateTime now, CancellationToken ct)
    {
        var end = AlignDown(Math.Max(0, size - TailMargin));

        if (capture.Offset is not long from)
        {
            capture.Offset = end;
            return true;
        }

        if (end <= from)
        {
            capture.Expire(now, Window);
            return true;
        }

        // A large append loses its start. The gap stops the verdict from
        // comparing keyframes across the missing part.
        if (end - from > MaxBytesPerLook)
        {
            from = end - AlignDown(MaxBytesPerLook);
            capture.GapPending = true;
        }
        capture.Offset = end;

        try
        {
            var keyframes = await _sampler.SampleAsync(capture.Path, from, end - from, ct);
            capture.NoVideoLooks = 0;
            capture.Record(keyframes, now, Window);
            return true;
        }
        catch (NoVideoStreamException)
        {
            capture.GapPending = true;
            capture.Expire(now, Window);
            if (++capture.NoVideoLooks >= NoVideoLooksBeforeSkip)
            {
                capture.Skipped = true;
                _logger.LogDebug("[DVR Loop] {Path} has no video; the looping-stream check skips it", capture.Path);
            }
            return false;
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // A shutdown can break the pipe before FFmpeg sees the cancel.
            // That is no sampling failure.
            throw new OperationCanceledException(ct);
        }
        catch (Exception ex)
        {
            // A missed look only delays a verdict. It must never disturb the
            // recording. The warning comes once, so that a check that can
            // never run is still visible. Examples are a missing FFmpeg or
            // an FFmpeg that is too old.
            capture.GapPending = true;
            capture.Expire(now, Window);
            if (capture.SamplingHasFailed)
                _logger.LogDebug(ex, "[DVR Loop] Could not sample {Path}", capture.Path);
            else
                _logger.LogWarning(ex, "[DVR Loop] Could not sample {Path}; the looping-stream check is not running for this recording", capture.Path);
            capture.SamplingHasFailed = true;
            return false;
        }
    }

    private static long AlignDown(long position) => position - position % PacketSize;

    /// <summary>One capture's read position and its keyframes inside the window.</summary>
    private sealed class Capture
    {
        private readonly Queue<(DateTime At, ulong Fingerprint, bool AfterGap)> _keyframes = new();

        public Capture(string path, DateTime watchedSince)
        {
            Path = path;
            WatchedSince = watchedSince;
        }

        public string Path { get; }
        public DateTime WatchedSince { get; }

        // Null until the first look notes the position.
        public long? Offset { get; set; }
        public bool SamplingHasFailed { get; set; }
        public bool Skipped { get; set; }
        public int NoVideoLooks { get; set; }

        // True when footage before the next keyframe was not sampled.
        public bool GapPending { get; set; }

        public (List<ulong> Keyframes, HashSet<int> Gaps) Snapshot()
        {
            var keyframes = new List<ulong>(_keyframes.Count);
            var gaps = new HashSet<int>();
            foreach (var (_, fingerprint, afterGap) in _keyframes)
            {
                if (afterGap)
                    gaps.Add(keyframes.Count);
                keyframes.Add(fingerprint);
            }
            return (keyframes, gaps);
        }

        public void Record(IEnumerable<ulong> keyframes, DateTime at, TimeSpan window)
        {
            foreach (var fingerprint in keyframes)
            {
                _keyframes.Enqueue((at, fingerprint, GapPending));
                GapPending = false;
            }
            Expire(at, window);
        }

        public void Expire(DateTime at, TimeSpan window)
        {
            while (_keyframes.Count > 0 && _keyframes.Peek().At < at - window)
                _keyframes.Dequeue();
        }
    }
}
