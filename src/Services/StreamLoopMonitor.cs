using Sportarr.Api.Helpers;

namespace Sportarr.Api.Services;

/// <summary>
/// Watches live captures for a provider that loops a short clip in place of
/// the channel it lost (see <see cref="FrameRecurrence"/>).
///
/// It fingerprints the keyframes of what the recorder appended to the capture
/// since the last look. Nothing here opens the stream, so a provider that
/// allows one connection per account is never asked for a second one. Only
/// transport-stream captures are watched: a stretch cut from the middle of one
/// still decodes, which is not true of a matroska or mp4 file being written.
/// Stream-copy recordings, the default, capture as .ts; re-encoded ones write
/// their final container directly and are not watched.
///
/// Not thread-safe: the DVR watchdog is its only caller and runs one tick at
/// a time.
/// </summary>
public class StreamLoopMonitor
{
    // How much footage a verdict judges, and how far back before it a repeat
    // may find the picture it repeats. A loop is caught once most of the
    // window repeats, so clips up to the difference, seven minutes, are
    // caught; a longer one is not. Short loops are still reported a window
    // after they start.
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan Lookback = TimeSpan.FromMinutes(10);

    // Reading is capped so the first look at a long-running capture (after an
    // app restart) does not decode the whole file. 64 MB is minutes of a
    // typical 1080p channel, more than the gap between two watchdog ticks.
    // Those keyframes all count as seen at that first look, which is harmless:
    // a verdict still waits a whole window from then.
    internal const long MaxBytesPerLook = 64L * 1024 * 1024;

    private readonly IKeyframeSampler _sampler;
    private readonly ILogger<StreamLoopMonitor> _logger;
    private readonly Dictionary<int, Capture> _captures = new();

    public StreamLoopMonitor(IKeyframeSampler sampler, ILogger<StreamLoopMonitor> logger)
    {
        _sampler = sampler;
        _logger = logger;
    }

    /// <summary>
    /// Takes in what was written to <paramref name="outputPath"/> since the
    /// previous call for this recording and answers whether the last
    /// <see cref="Window"/> of it is a loop. The first answer can be true
    /// only once a whole window has been watched.
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

        capture.Record(await ReadNewKeyframesAsync(capture, ct), now, Lookback);
        return capture.WatchedSince <= now - Window &&
               FrameRecurrence.LooksLooped(capture.KeyframesBefore(now - Window), capture.KeyframesSince(now - Window));
    }

    /// <summary>Drops what was gathered for recordings no longer in flight.</summary>
    public void ForgetAllExcept(IReadOnlySet<int> recordingIds)
    {
        foreach (var id in _captures.Keys.Where(id => !recordingIds.Contains(id)).ToList())
            _captures.Remove(id);
    }

    public void ForgetAll() => _captures.Clear();

    public void Forget(int recordingId) => _captures.Remove(recordingId);

    private async Task<IReadOnlyList<ulong>> ReadNewKeyframesAsync(Capture capture, CancellationToken ct)
    {
        long size;
        try { size = new FileInfo(capture.Path).Length; }
        catch (IOException) { return Array.Empty<ulong>(); }

        if (size <= capture.Offset)
            return Array.Empty<ulong>();

        // A range rarely starts or ends on a packet boundary, so a keyframe
        // cut in two at either end is lost. That only thins the evidence.
        var from = Math.Max(capture.Offset, size - MaxBytesPerLook);
        capture.Offset = size;

        try
        {
            return await _sampler.SampleAsync(capture.Path, from, size - from, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A missed look only delays a verdict; it must never disturb the
            // recording itself. Warn once, so a check that can never run
            // (FFmpeg missing or too old) does not go unnoticed.
            if (capture.SamplingHasFailed)
                _logger.LogDebug(ex, "[DVR Loop] Could not sample {Path}", capture.Path);
            else
                _logger.LogWarning(ex, "[DVR Loop] Could not sample {Path}; the looping-stream check is not running for this recording", capture.Path);
            capture.SamplingHasFailed = true;
            return Array.Empty<ulong>();
        }
    }

    /// <summary>One capture's read position and its keyframes inside the lookback.</summary>
    private sealed class Capture
    {
        private readonly Queue<(DateTime At, ulong Fingerprint)> _keyframes = new();

        public Capture(string path, DateTime watchedSince)
        {
            Path = path;
            WatchedSince = watchedSince;
        }

        public string Path { get; }
        public DateTime WatchedSince { get; }
        public long Offset { get; set; }
        public bool SamplingHasFailed { get; set; }

        public IReadOnlyList<ulong> KeyframesBefore(DateTime moment) =>
            _keyframes.Where(k => k.At < moment).Select(k => k.Fingerprint).ToList();

        public IReadOnlyList<ulong> KeyframesSince(DateTime moment) =>
            _keyframes.Where(k => k.At >= moment).Select(k => k.Fingerprint).ToList();

        public void Record(IEnumerable<ulong> keyframes, DateTime at, TimeSpan kept)
        {
            foreach (var fingerprint in keyframes)
                _keyframes.Enqueue((at, fingerprint));
            while (_keyframes.Count > 0 && _keyframes.Peek().At < at - kept)
                _keyframes.Dequeue();
        }
    }
}
