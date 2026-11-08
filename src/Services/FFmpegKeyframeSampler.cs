using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Services;

/// <summary>Fingerprints the keyframes in a byte range of a transport-stream capture.</summary>
public interface IKeyframeSampler
{
    /// <summary>
    /// Returns one fingerprint per keyframe in <paramref name="length"/> bytes
    /// of <paramref name="path"/> from <paramref name="from"/>, oldest first.
    /// A keyframe that the range cuts in two can be missing. Throws
    /// <see cref="NoVideoStreamException"/> when the range holds no video.
    /// Throws a different exception when the range could not be sampled, for
    /// example when FFmpeg is missing, fails or times out.
    /// </summary>
    Task<IReadOnlyList<ulong>> SampleAsync(string path, long from, long length, CancellationToken ct);
}

/// <summary>The sampled range holds no video stream, as in a radio channel.</summary>
public class NoVideoStreamException : Exception
{
    public NoVideoStreamException(string message) : base(message) { }
}

/// <summary>
/// Sends a byte range of the capture through a local FFmpeg. FFmpeg decodes
/// only keyframes and scales each one to a 9x8 grayscale thumbnail. A look
/// costs a fraction of a second of CPU. The sampler reads the capture from
/// disk while the recorder writes it. It never contacts the provider.
/// </summary>
public class FFmpegKeyframeSampler : IKeyframeSampler
{
    // The timeout is well inside the watchdog's 30-second tick. One hung
    // sampler then cannot delay the checks on other recordings for long.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    // A failed search for FFmpeg runs again after this time. A user can then
    // install FFmpeg without a restart.
    private static readonly TimeSpan MissRetryInterval = TimeSpan.FromMinutes(10);

    private const int StderrTailChars = 2000;

    private readonly Func<Task<string?>> _configuredPath;
    private readonly Func<string?> _resolveFfmpeg;
    private readonly object _pathLock = new();
    private (string Configured, string? Resolved, DateTime At)? _cachedPath;

    public FFmpegKeyframeSampler(FFmpegRecorderService recorder, ConfigService config)
        : this(async () => (await config.GetConfigAsync()).DvrFfmpegPath, recorder.GetFFmpegPath)
    {
    }

    internal FFmpegKeyframeSampler(Func<Task<string?>> configuredPath, Func<string?> resolveFfmpeg)
    {
        _configuredPath = configuredPath;
        _resolveFfmpeg = resolveFfmpeg;
    }

    public async Task<IReadOnlyList<ulong>> SampleAsync(string path, long from, long length, CancellationToken ct)
    {
        var ffmpegPath = await FfmpegPathAsync();
        if (string.IsNullOrEmpty(ffmpegPath))
            throw new InvalidOperationException("FFmpeg was not found");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        using var process = Process.Start(StartInfo(ffmpegPath)) ?? throw new InvalidOperationException("FFmpeg did not start");

        // A pipe write can ignore cancellation. A kill on cancel ends every
        // wait on the process. This callback runs on a timer thread, so it
        // must not throw.
        using var killOnCancel = timeout.Token.Register(() => Kill(process));

        using var output = new MemoryStream();
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
        var stderr = ReadTailAsync(process.StandardError, timeout.Token);
        try
        {
            try
            {
                await using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    source.Seek(from, SeekOrigin.Begin);
                    await CopyRangeAsync(source, process.StandardInput.BaseStream, length, timeout.Token);
                }
            }
            catch (IOException) when (!timeout.IsCancellationRequested)
            {
                // FFmpeg closes its input when it stops early, for example
                // when the range holds no video. The exit code below tells
                // what happened.
            }
            try { process.StandardInput.Close(); } catch (IOException) { /* FFmpeg already closed its input */ }

            await Task.WhenAll(stdout, stderr);
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (Exception ex) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // The kill on timeout can surface as a cancel or as a broken pipe.
            throw new TimeoutException($"FFmpeg did not sample the capture within {Timeout.TotalSeconds:0} seconds", ex);
        }
        finally
        {
            if (!process.HasExited)
                Kill(process);
            // The copies fail after the process stops. Observe them, so that
            // a failure cannot appear later as an unobserved exception.
            try { await Task.WhenAll(stdout, stderr); } catch { /* the result is already decided */ }
        }

        if (process.ExitCode != 0)
        {
            var errors = (await stderr).Trim();
            if (MeansNoVideo(errors))
                throw new NoVideoStreamException("The capture holds no video stream");
            throw new InvalidOperationException(
                $"FFmpeg exited with code {process.ExitCode}" + (errors.Length > 0 ? $": {errors}" : ""));
        }

        return FrameRecurrence.Fingerprints(output.ToArray());
    }

    /// <summary>
    /// The FFmpeg path, cached for each configured value. A search for FFmpeg
    /// can start <c>ffmpeg -version</c> and wait for it. A look must not do
    /// that on every tick.
    /// </summary>
    private async Task<string?> FfmpegPathAsync()
    {
        var configured = (await _configuredPath())?.Trim() ?? "";
        var now = DateTime.UtcNow;
        lock (_pathLock)
        {
            if (_cachedPath is { } cached && cached.Configured == configured &&
                (cached.Resolved != null || now - cached.At < MissRetryInterval))
                return cached.Resolved;
        }

        var resolved = _resolveFfmpeg();
        lock (_pathLock)
            _cachedPath = (configured, resolved, now);
        return resolved;
    }

    /// <summary>
    /// True when FFmpeg failed because the input has no video stream. The
    /// map finds no stream. FFmpeg 6.1 fails earlier, because no video
    /// decoder used the keyframe option.
    /// </summary>
    internal static bool MeansNoVideo(string errors) =>
        errors.Contains("matches no streams", StringComparison.OrdinalIgnoreCase) ||
        (errors.Contains("skip_frame", StringComparison.Ordinal) &&
         errors.Contains("is not a decoding option", StringComparison.OrdinalIgnoreCase));

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
            // The process is already gone, or the system refused the kill.
        }
    }

    /// <summary>Reads FFmpeg's error output and keeps only its end.</summary>
    private static async Task<string> ReadTailAsync(StreamReader reader, CancellationToken ct)
    {
        var tail = new StringBuilder();
        var buffer = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            tail.Append(buffer, 0, read);
            if (tail.Length > StderrTailChars)
                tail.Remove(0, tail.Length - StderrTailChars);
        }
        return tail.ToString();
    }

    // Passthrough timing stops FFmpeg from copying thumbnails up to the frame
    // rate. It needs FFmpeg 5.1 or later. An older FFmpeg rejects the option
    // and the exit code reports it.
    private static ProcessStartInfo StartInfo(string ffmpegPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[]
        {
            "-hide_banner", "-nostdin", "-loglevel", "error",
            "-f", "mpegts", "-skip_frame", "nokey", "-i", "pipe:0",
            "-map", "0:v:0",
            "-vf", $"scale={FrameRecurrence.ThumbnailWidth}:{FrameRecurrence.ThumbnailHeight}:flags=area,format=gray",
            "-fps_mode", "passthrough",
            "-f", "rawvideo", "pipe:1"
        })
            psi.ArgumentList.Add(arg);
        return psi;
    }

    private static async Task CopyRangeAsync(Stream source, Stream destination, long length, CancellationToken ct)
    {
        var buffer = new byte[81920];
        while (length > 0)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length)), ct);
            if (read == 0)
                return;
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            length -= read;
        }
    }
}
