using System.Diagnostics;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Services;

/// <summary>Fingerprints the keyframes in a byte range of a transport-stream capture.</summary>
public interface IKeyframeSampler
{
    /// <summary>
    /// One fingerprint per keyframe found in <paramref name="length"/> bytes
    /// of <paramref name="path"/> from <paramref name="from"/>, oldest first.
    /// Throws when the range could not be sampled.
    /// </summary>
    Task<IReadOnlyList<ulong>> SampleAsync(string path, long from, long length, CancellationToken ct);
}

/// <summary>
/// Pipes a byte range of the capture through a local FFmpeg that decodes only
/// keyframes, down to one 9x8 grayscale thumbnail each. That keeps a look to a
/// fraction of a second of CPU. The capture is read from disk while the
/// recorder keeps writing it; the provider is never contacted.
/// </summary>
public class FFmpegKeyframeSampler : IKeyframeSampler
{
    // Well inside the watchdog's 30-second tick, so one hung sampler cannot
    // hold up the checks on the other recordings for long.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly FFmpegRecorderService _recorder;

    public FFmpegKeyframeSampler(FFmpegRecorderService recorder) => _recorder = recorder;

    public async Task<IReadOnlyList<ulong>> SampleAsync(string path, long from, long length, CancellationToken ct)
    {
        var ffmpegPath = _recorder.GetFFmpegPath();
        if (string.IsNullOrEmpty(ffmpegPath))
            throw new InvalidOperationException("FFmpeg was not found");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        using var process = Process.Start(StartInfo(ffmpegPath)) ?? throw new InvalidOperationException("FFmpeg did not start");
        using var output = new MemoryStream();
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        try
        {
            await using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                source.Seek(from, SeekOrigin.Begin);
                await CopyRangeAsync(source, process.StandardInput.BaseStream, length, timeout.Token);
            }
            process.StandardInput.Close();

            await Task.WhenAll(stdout, stderr);
            await process.WaitForExitAsync(timeout.Token);
            return FrameRecurrence.Fingerprints(output.ToArray());
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already gone */ }
            }
            // The copies fail once the process is gone; observe them so a
            // failure here cannot surface later as an unobserved exception.
            try { await Task.WhenAll(stdout, stderr); } catch { /* the outcome was decided above */ }
        }
    }

    // Passthrough timing stops FFmpeg from duplicating thumbnails up to the
    // frame rate (it needs FFmpeg 5.1 or later).
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
            "-hide_banner", "-nostdin", "-loglevel", "quiet",
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
