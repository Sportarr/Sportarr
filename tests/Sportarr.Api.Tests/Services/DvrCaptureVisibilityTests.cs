using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A partial transport stream still plays, so a media server scan picks up a
/// half-recorded event and adds it to the library. Plex, Jellyfin and Emby all
/// skip a file whose name starts with a dot, so a capture stays hidden while it
/// is written and takes its real name when it finishes. The extension is never
/// touched, because the catchup downloader reads the container from it.
/// </summary>
public class DvrCaptureVisibilityTests
{
    [Fact]
    public void HidesTheFileNameButKeepsTheFolderAndExtension()
    {
        var hidden = DvrRecordingService.HideWhileWriting(
            Path.Combine("/data", "sports", "UFC 320 - Main Card.ts"));

        Path.GetFileName(hidden).Should().Be(".UFC 320 - Main Card.ts");
        Path.GetDirectoryName(hidden).Should().Be(Path.Combine("/data", "sports"));
        Path.GetExtension(hidden).Should().Be(".ts", "the catchup downloader reads the container from the extension");
    }

    [Fact]
    public void HidingAnAlreadyHiddenCaptureChangesNothing()
    {
        var once = DvrRecordingService.HideWhileWriting("/data/sports/game.ts");
        var twice = DvrRecordingService.HideWhileWriting(once);

        twice.Should().Be(once, "a rescheduled recording must not stack dots");
    }
}
