using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #253. An event-specific IPTV feed is torn down the moment the
/// broadcast ends, so the capture stops growing there. That is indis-
/// tinguishable from a dead stream by byte growth alone, and the recording
/// was marked Failed, which left a full capture on disk under its hidden
/// dot-prefixed name where the importer never looks.
///
/// The reporter's own case: a 00:10 to 03:40 window whose feed dropped at
/// 03:25 after capturing 7.9GB, marked Failed.
///
/// The grace is a fraction of the scheduled duration rather than a flat
/// number of minutes, so a short capture and a long one get proportionate
/// leeway. Confirmed on a live recorder as well: a feed dropped 50 seconds
/// before the end finalized as Completed with the file revealed, and one
/// killed five minutes into a 60-minute window still failed.
/// </summary>
public class DvrEarlyStreamEndTests
{
    private const long SomeData = 8_000_000_000;

    private static bool NormalEnd(DateTime start, DateTime end, DateTime exitAt, long fileSize = SomeData, int postPadding = 0)
        => DvrRecordingService.ExitLooksLikeANormalEnd(exitAt, start, end, postPadding, fileSize);

    [Fact]
    public void TheReportersOwnRecordingReadsAsAFinishedBroadcast()
    {
        var start = new DateTime(2026, 8, 20, 0, 10, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 8, 20, 3, 40, 0, DateTimeKind.Utc);

        NormalEnd(start, end, exitAt: new DateTime(2026, 8, 20, 3, 25, 0, DateTimeKind.Utc))
            .Should().BeTrue("the feed dropped 15 minutes early on a 3.5 hour window");
    }

    [Fact]
    public void AStreamThatRunsToItsScheduledEndStillReadsAsNormal()
    {
        var start = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(2);

        NormalEnd(start, end, exitAt: end).Should().BeTrue();
        NormalEnd(start, end, exitAt: end.AddSeconds(-20)).Should().BeTrue("the old 30 second tolerance still applies");
    }

    [Fact]
    public void ADeathEarlyInTheWindowIsStillAFailure()
    {
        var start = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(1);

        NormalEnd(start, end, exitAt: start.AddMinutes(5))
            .Should().BeFalse("five minutes into an hour is a dead stream, not a finished event");
    }

    [Fact]
    public void TheGraceIsProportionalToTheWindow()
    {
        var start = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var longWindow = start.AddHours(4);
        var shortWindow = start.AddMinutes(5);

        NormalEnd(start, longWindow, exitAt: longWindow.AddMinutes(-10))
            .Should().BeTrue("ten minutes early on a four hour window is nothing");
        NormalEnd(start, shortWindow, exitAt: shortWindow.AddMinutes(-2))
            .Should().BeFalse("two minutes early on a five minute window is most of it");
    }

    [Fact]
    public void NothingOnDiskIsNeverANormalEnd()
    {
        var start = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(2);

        NormalEnd(start, end, exitAt: end, fileSize: 0)
            .Should().BeFalse("an empty capture is not a completed recording, whenever it stopped");
    }

    [Fact]
    public void PostPaddingExtendsTheNaturalEnd()
    {
        var start = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(2);

        NormalEnd(start, end, exitAt: end.AddMinutes(9), postPadding: 10)
            .Should().BeTrue("the recording runs to the end of its padding");
    }

    [Fact]
    public void TheEdgeOfTheGraceIsIncluded()
    {
        var start = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(1);
        var threshold = end.AddMinutes(-12);

        NormalEnd(start, end, exitAt: threshold).Should().BeTrue("the boundary counts as a normal end");
        NormalEnd(start, end, exitAt: threshold.AddSeconds(-1)).Should().BeFalse("a second earlier does not");
    }
}
