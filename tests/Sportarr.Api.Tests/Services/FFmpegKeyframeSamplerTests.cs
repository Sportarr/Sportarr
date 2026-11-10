using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The keyframe sampler against a real FFmpeg, end to end through the loop
/// monitor. The tests make small transport streams with FFmpeg's own test
/// sources. They skip when FFmpeg is not installed.
/// </summary>
public class FFmpegKeyframeSamplerTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 7, 16, 0, 0, DateTimeKind.Utc);

    // The pattern of 9x8 black and white blocks changes twice a second, at
    // random. Each block fills one thumbnail pixel, so the fingerprint
    // survives encoding.
    private const string Blocks =
        "color=c=black:s=144x128:r=10,format=gray," +
        "geq=lum='255*gt(mod(abs(sin(floor(T*2)*12.9898+floor(X/16)*78.233+floor(Y/16)*37.719)*43758.5453),1),0.5)'";

    private readonly string _dir = Directory.CreateTempSubdirectory("keyframe-sampler-").FullName;
    private readonly FFmpegKeyframeSampler _sampler = new(() => Task.FromResult<string?>(null), () => "ffmpeg");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [FfmpegFact]
    public async Task A_stream_that_loops_a_13_second_clip_is_a_loop()
    {
        var clip = Path.Combine(_dir, "clip.ts");
        var loop = Path.Combine(_dir, "loop.ts");
        Ffmpeg(["-f", "lavfi", "-i", Blocks, "-t", "13", .. Encode(gopSeconds: 1), clip]);
        Ffmpeg(["-stream_loop", "-1", "-i", clip, "-t", "240", .. Encode(gopSeconds: 1), loop]);

        (await WatchAsync(loop)).Should().Contain(true);
    }

    [FfmpegFact]
    public async Task A_stream_that_loops_a_13_second_clip_with_2_second_keyframes_is_a_loop()
    {
        var clip = Path.Combine(_dir, "clip.ts");
        var loop = Path.Combine(_dir, "loop.ts");
        Ffmpeg(["-f", "lavfi", "-i", Blocks, "-t", "13", .. Encode(gopSeconds: 2), clip]);
        Ffmpeg(["-stream_loop", "-1", "-i", clip, "-t", "240", .. Encode(gopSeconds: 2), loop]);

        (await WatchAsync(loop)).Should().Contain(true);
    }

    [FfmpegFact]
    public async Task Footage_that_never_repeats_is_not_a_loop()
    {
        var live = Path.Combine(_dir, "live.ts");
        Ffmpeg(["-f", "lavfi", "-i", Blocks, "-t", "240", .. Encode(gopSeconds: 1), live]);

        var keyframes = await _sampler.SampleAsync(live, 0, new FileInfo(live).Length, default);
        keyframes.Should().HaveCountGreaterThan(200, "one keyframe per second of footage");

        (await WatchAsync(live)).Should().OnlyContain(v => !v);
    }

    [FfmpegFact]
    public async Task A_capture_without_video_says_so()
    {
        var radio = Path.Combine(_dir, "radio.ts");
        Ffmpeg("-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "10", "-c:a", "mp2", "-f", "mpegts", radio);

        var act = () => _sampler.SampleAsync(radio, 0, new FileInfo(radio).Length, default);

        await act.Should().ThrowAsync<NoVideoStreamException>();
    }

    [Fact]
    public async Task A_failing_ffmpeg_reports_its_exit_code_and_error_output()
    {
        // A stand-in for an FFmpeg that rejects an option, as FFmpeg before
        // 5.1 does with -fps_mode. It exits at once without reading input.
        if (OperatingSystem.IsWindows())
            return;
        var fake = FakeFfmpeg("echo \"Unrecognized option 'fps_mode'.\" >&2; exit 8");
        var capture = Path.Combine(_dir, "event.ts");
        File.WriteAllBytes(capture, new byte[1024 * 1024]);
        var sampler = new FFmpegKeyframeSampler(() => Task.FromResult<string?>(null), () => fake);

        var act = () => sampler.SampleAsync(capture, 0, 1024 * 1024, default);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("code 8").And.Contain("fps_mode");
    }

    [Theory]
    [InlineData("Stream map '0:v:0' matches no streams.\nTo ignore this, add a trailing '?' to the map.", true)]
    [InlineData("[in#0/mpegts @ 0x55] Codec AVOption skip_frame (Allow frame skipping) is not a decoding option.\nError opening input file pipe:0.", true)]
    [InlineData("Unrecognized option 'fps_mode'.", false)]
    [InlineData("", false)]
    public void Only_the_errors_of_an_input_without_video_mean_no_video(string errors, bool noVideo)
    {
        // These are the messages of FFmpeg 6.1 for an audio-only capture.
        FFmpegKeyframeSampler.MeansNoVideo(errors).Should().Be(noVideo);
    }

    [Fact]
    public async Task The_ffmpeg_path_is_resolved_once_for_each_configured_value()
    {
        if (OperatingSystem.IsWindows())
            return;
        var fake = FakeFfmpeg("exit 1");
        var capture = Path.Combine(_dir, "event.ts");
        File.WriteAllBytes(capture, new byte[188]);
        var configured = "/opt/ffmpeg";
        var resolves = 0;
        var sampler = new FFmpegKeyframeSampler(() => Task.FromResult<string?>(configured), () => { resolves++; return fake; });

        for (var i = 0; i < 3; i++)
            await FluentActions.Awaiting(() => sampler.SampleAsync(capture, 0, 188, default)).Should().ThrowAsync<Exception>();
        resolves.Should().Be(1);

        configured = "/usr/local/bin/ffmpeg";
        await FluentActions.Awaiting(() => sampler.SampleAsync(capture, 0, 188, default)).Should().ThrowAsync<Exception>();
        resolves.Should().Be(2);
    }

    private string FakeFfmpeg(string body)
    {
        var path = Path.Combine(_dir, "fake-ffmpeg.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    /// <summary>
    /// Copies the file into a growing capture, 30 seconds of footage for each
    /// watchdog tick, and returns the monitor's verdict on each tick.
    /// </summary>
    private async Task<List<bool>> WatchAsync(string source)
    {
        var monitor = new StreamLoopMonitor(_sampler, NullLogger<StreamLoopMonitor>.Instance);
        var capture = Path.Combine(_dir, "capture-" + Path.GetFileName(source));
        var bytes = File.ReadAllBytes(source);
        const int Ticks = 8;
        var verdicts = new List<bool>();
        await using (var file = new FileStream(capture, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            for (var tick = 0; tick <= Ticks + 2; tick++)
            {
                var upTo = (int)Math.Min(bytes.Length, (long)bytes.Length * tick / Ticks);
                await file.WriteAsync(bytes.AsMemory((int)file.Length, upTo - (int)file.Length));
                await file.FlushAsync();
                verdicts.Add(await monitor.IsLoopingAsync(1, capture, Start + TimeSpan.FromSeconds(30 * tick), default));
            }
        }
        return verdicts;
    }

    // A tiny, fast encode with a fixed keyframe grid and no extra keyframes
    // at scene cuts. Random blocks look like a scene cut on every change.
    private static string[] Encode(int gopSeconds) => new[]
    {
        "-an", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
        "-g", (10 * gopSeconds).ToString(), "-keyint_min", (10 * gopSeconds).ToString(), "-sc_threshold", "0",
        "-f", "mpegts", "-y",
    };

    private static void Ffmpeg(params string[] args)
    {
        var psi = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin" }.Concat(args))
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var errors = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, errors.Result);
    }
}

/// <summary>A test that needs FFmpeg on the PATH. It skips without FFmpeg.</summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process == null) return false;
            process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    });

    public FfmpegFactAttribute()
    {
        if (!Available.Value)
            Skip = "FFmpeg is not installed";
    }
}
