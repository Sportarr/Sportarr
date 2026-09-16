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
            ["https://provider.example/live", "/tmp/session/playlist.m3u8", null])!;

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
            ["https://provider.example/live", "/tmp/session/playlist.m3u8", null])!;

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
}
