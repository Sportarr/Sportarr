using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Muxing a live feed's ADTS AAC straight into mp4 or matroska runs ffmpeg's
/// aac_adtstoasc filter, and one corrupt frame ends the recording with exit
/// 183. A stream-copy capture therefore goes to a transport stream, which
/// shrugs corruption off, and is remuxed to the configured container when it
/// finishes.
/// </summary>
public class DvrCaptureContainerTests
{
    [Theory]
    [InlineData("mp4")]
    [InlineData("mkv")]
    public void StreamCopyCapturesToATransportStream(string container)
    {
        DvrRecordingService.CaptureContainer(container, "copy").Should().Be("ts",
            "a single corrupt AAC frame would otherwise abort the recording mid-event");
    }

    [Fact]
    public void ReencodingWritesTheConfiguredContainerDirectly()
    {
        DvrRecordingService.CaptureContainer("mkv", "libx264").Should().Be("mkv");
    }

    [Fact]
    public void ATransportStreamIsCapturedAsItself()
    {
        DvrRecordingService.CaptureContainer("ts", "copy").Should().Be("ts");
    }

    [Fact]
    public void OnlyAnMp4RemuxAsksForFaststart()
    {
        FFmpegRecorderService.OutputContainerArguments("/data/sports/race.mp4")
            .Should().Equal("-movflags", "+faststart");
        FFmpegRecorderService.OutputContainerArguments("/data/sports/race.mkv")
            .Should().BeEmpty("matroska has no faststart option");
    }
}
