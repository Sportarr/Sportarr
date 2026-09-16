using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

public class FFmpegStreamServiceTests
{
    [Fact]
    public void Hls_playlist_keeps_enough_segments_to_absorb_live_source_jitter()
    {
        using var service = new FFmpegStreamService(NullLogger<FFmpegStreamService>.Instance);
        var builder = typeof(FFmpegStreamService).GetMethod(
            "BuildHlsArguments",
            BindingFlags.Instance | BindingFlags.NonPublic);

        builder.Should().NotBeNull();
        var arguments = (List<string>)builder!.Invoke(
            service,
            ["https://provider.example/live", "/tmp/session/playlist.m3u8", null, false])!;

        arguments.Should().ContainInOrder("-hls_list_size", "10");
    }

    [Fact]
    public void Hls_output_normalizes_live_video_with_short_fixed_keyframe_intervals()
    {
        using var service = new FFmpegStreamService(NullLogger<FFmpegStreamService>.Instance);
        var builder = typeof(FFmpegStreamService).GetMethod(
            "BuildHlsArguments",
            BindingFlags.Instance | BindingFlags.NonPublic);

        builder.Should().NotBeNull();
        var arguments = (List<string>)builder!.Invoke(
            service,
            ["https://provider.example/live", "/tmp/session/playlist.m3u8", null, true])!;

        arguments.Should().ContainInOrder(
            "-map", "0:v:0",
            "-map", "0:a:0?",
            "-c:v", "libx264",
            "-preset", "ultrafast",
            "-tune", "zerolatency",
            "-g", "120",
            "-keyint_min", "1",
            "-sc_threshold", "0",
            "-force_key_frames", "expr:gte(t,n_forced*2)",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac");
    }

    [Fact]
    public void Hls_output_uses_stream_copy_without_explicit_normalization()
    {
        using var service = new FFmpegStreamService(NullLogger<FFmpegStreamService>.Instance);
        var builder = typeof(FFmpegStreamService).GetMethod(
            "BuildHlsArguments",
            BindingFlags.Instance | BindingFlags.NonPublic);

        builder.Should().NotBeNull();
        var arguments = (List<string>)builder!.Invoke(
            service,
            ["https://provider.example/live", "/tmp/session/playlist.m3u8", null, false])!;

        arguments.Should().ContainInOrder("-c", "copy");
        arguments.Should().NotContain("libx264");
        arguments.Should().NotContain("aac");
    }

    [Fact]
    public void H264_encoder_probe_requires_the_libx264_encoder()
    {
        var probe = typeof(FFmpegStreamService).GetMethod(
            "HasH264Encoder",
            BindingFlags.Static | BindingFlags.NonPublic);

        probe.Should().NotBeNull();
        probe!.Invoke(null, [" V..... libx264            libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (codec h264) "])
            .Should().Be(true);
        probe.Invoke(null, [" V..... h264_nvenc          NVIDIA NVENC H.264 encoder "])
            .Should().Be(false);
    }

    [Fact]
    public async Task Starts_ffmpeg_produces_a_playlist_and_stops_only_the_requested_session()
    {
        var ffmpegPath = FindFfmpeg();
        if (ffmpegPath == null)
        {
            // FFmpeg is an optional test dependency. Run the full integration
            // path in environments that provide it, without failing unrelated
            // test runs on machines that only exercise argument construction.
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"sportarr-ffmpeg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "source.ts");
        const string channelId = "integration-session-test";

        try
        {
            await RunProcessAsync(ffmpegPath, [
                "-hide_banner", "-loglevel", "error", "-y",
                "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=10",
                "-t", "3", "-c:v", "mpeg2video", "-f", "mpegts", sourcePath
            ]);

            await using var sourceServer = new MpegTsServer(sourcePath);
            using var service = new FFmpegStreamService(NullLogger<FFmpegStreamService>.Instance);

            var first = await service.StartStreamAsync(channelId, sourceServer.Url.ToString());
            first.Success.Should().BeTrue(first.Error);
            first.SessionId.Should().NotBeNullOrWhiteSpace();
            var firstSessionId = first.SessionId!;
            service.GetHlsFilePath(firstSessionId, "playlist.m3u8").Should().NotBeNull();

            await service.StopStreamAsync(channelId, "not-the-active-session");
            service.IsSessionActive(firstSessionId).Should().BeTrue();

            await service.StopStreamAsync(channelId, firstSessionId);
            service.IsSessionActive(firstSessionId).Should().BeFalse();

            var second = await service.StartStreamAsync(channelId, sourceServer.Url.ToString());
            second.Success.Should().BeTrue(second.Error);
            second.SessionId.Should().NotBe(firstSessionId);

            await service.StopStreamAsync(channelId, firstSessionId);
            service.IsSessionActive(second.SessionId!).Should().BeTrue();

            await service.StopStreamAsync(channelId, second.SessionId);
            service.IsSessionActive(second.SessionId!).Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static string? FindFfmpeg()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                ArgumentList = { "-version" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process == null) return null;
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? "ffmpeg" : null;
        }
        catch
        {
            return null;
        }
    }


    private static async Task RunProcessAsync(string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stderr = await stderrTask;
        process.ExitCode.Should().Be(0, stderr);
    }

    private sealed class MpegTsServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly byte[] _payload;
        private readonly Task _serverTask;

        public MpegTsServer(string sourcePath)
        {
            _payload = File.ReadAllBytes(sourcePath);
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            Url = new Uri($"http://127.0.0.1:{endpoint.Port}/stream.ts");
            _serverTask = ServeAsync();
        }

        public Uri Url { get; }

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    try
                    {
                        await ReadRequestHeadersAsync(stream, _stop.Token);
                        var headers = Encoding.ASCII.GetBytes(
                            "HTTP/1.1 200 OK\r\nContent-Type: video/mp2t\r\nConnection: keep-alive\r\n\r\n");
                        await stream.WriteAsync(headers, _stop.Token);

                        while (!_stop.IsCancellationRequested)
                        {
                            for (var offset = 0; offset < _payload.Length; offset += 188 * 7)
                            {
                                var count = Math.Min(188 * 7, _payload.Length - offset);
                                await stream.WriteAsync(_payload.AsMemory(offset, count), _stop.Token);
                                await Task.Delay(100, _stop.Token);
                            }
                        }
                    }
                    catch (IOException) when (!_stop.IsCancellationRequested) { }
                    catch (SocketException) when (!_stop.IsCancellationRequested) { }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (IOException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }

        private static async Task ReadRequestHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            var request = new StringBuilder();
            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) return;
                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (request.Length > 32 * 1024) throw new InvalidOperationException("HTTP request headers are too large.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try { await _serverTask; } catch (SocketException) { }
            _stop.Dispose();
        }
    }
}
