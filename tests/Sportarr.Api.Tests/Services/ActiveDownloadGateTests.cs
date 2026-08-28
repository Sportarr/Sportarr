using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #194: an event that is already downloading was still a missing
/// candidate for the scheduled searches, so a large event grabbed a second
/// release while the first was still transferring. The reporter's pair was an
/// F1 race in 4K, two different releases from two indexers, two and a half
/// hours apart, both 2160p.
/// </summary>
public class ActiveDownloadGateTests
{
    private static DownloadQueueItem Row(
        int eventId,
        DownloadStatus status,
        string? part = null,
        string title = "Formula1.2026.Dutch.Grand.Prix.2160p") => new()
    {
        EventId = eventId,
        Title = title,
        DownloadId = Guid.NewGuid().ToString(),
        Status = status,
        Part = part,
        LastUpdate = DateTime.UtcNow,
    };

    [Theory]
    [InlineData(DownloadStatus.Queued)]
    [InlineData(DownloadStatus.Downloading)]
    [InlineData(DownloadStatus.Completed)]
    [InlineData(DownloadStatus.Importing)]
    [InlineData(DownloadStatus.ImportPending)]
    public void BlocksWhileTheGrabHasNotLanded(DownloadStatus status)
    {
        var blocking = ActiveDownloadGate.FindBlocking(new[] { Row(7, status) }, 7, null);

        blocking.Should().NotBeNull();
    }

    [Theory]
    [InlineData(DownloadStatus.Failed)]
    [InlineData(DownloadStatus.Imported)]
    [InlineData(DownloadStatus.Paused)]
    [InlineData(DownloadStatus.ImportWarning)]
    public void AllowsOnceTheGrabIsFinishedWithOneWayOrAnother(DownloadStatus status)
    {
        var blocking = ActiveDownloadGate.FindBlocking(new[] { Row(7, status) }, 7, null);

        blocking.Should().BeNull();
    }

    [Fact]
    public void OnlyBlocksTheEventThatIsDownloading()
    {
        var queue = new[] { Row(7, DownloadStatus.Downloading) };

        ActiveDownloadGate.FindBlocking(queue, 8, null).Should().BeNull();
    }

    [Fact]
    public void PartlessSearchIsNotBlockedByAPartDownload()
    {
        // The parts of a multi-part event still search on their own, the same
        // way RSS sync treats them.
        var queue = new[] { Row(7, DownloadStatus.Downloading, part: "Part 2") };

        ActiveDownloadGate.FindBlocking(queue, 7, null).Should().BeNull();
        ActiveDownloadGate.FindBlocking(queue, 7, "Part 2").Should().NotBeNull();
        ActiveDownloadGate.FindBlocking(queue, 7, "Part 1").Should().BeNull();
    }

    [Fact]
    public void ReportsTheReleaseThatIsAlreadyOnItsWay()
    {
        var queue = new[]
        {
            Row(7, DownloadStatus.Failed, title: "Formula1.2026.Dutch.Grand.Prix.HLG.2160p.WEB.h265-OLD"),
            Row(7, DownloadStatus.Downloading, title: "Formula1.2026.Dutch.Grand.Prix.HLG.2160p.WEB.h265-BILLIE"),
        };

        ActiveDownloadGate.FindBlocking(queue, 7, null)!.Title
            .Should().Be("Formula1.2026.Dutch.Grand.Prix.HLG.2160p.WEB.h265-BILLIE");
    }
}
