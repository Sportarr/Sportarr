using FluentAssertions;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// Locks the two safety-critical rules behind #184: keep retrying while an external
/// extractor may still be running, and never delete a successfully-downloaded torrent's
/// data on an import failure.
/// </summary>
public class DownloadFailurePolicyTests
{
    private static readonly DateTime Now = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(30);

    [Fact]
    public void IsWithinExtractionGrace_JustCompleted_KeepsWaiting()
    {
        var completedAt = Now.AddMinutes(-5); // 5 min ago, well inside the window
        DownloadFailurePolicy.IsWithinExtractionGrace(completedAt, Now.AddHours(-1), Now, Grace)
            .Should().BeTrue();
    }

    [Fact]
    public void IsWithinExtractionGrace_PastWindow_GivesUp()
    {
        var completedAt = Now.AddMinutes(-31); // just past the 30 min window
        DownloadFailurePolicy.IsWithinExtractionGrace(completedAt, Now.AddHours(-1), Now, Grace)
            .Should().BeFalse();
    }

    [Fact]
    public void IsWithinExtractionGrace_NoCompletedAt_FallsBackToAdded()
    {
        // CompletedAt unknown: measure from Added instead so the clock still advances.
        DownloadFailurePolicy.IsWithinExtractionGrace(null, Now.AddMinutes(-10), Now, Grace)
            .Should().BeTrue();
        DownloadFailurePolicy.IsWithinExtractionGrace(null, Now.AddMinutes(-45), Now, Grace)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldRemoveDataOnFailure_ImportFailureOfCompletedDownload_NeverRemoves()
    {
        // Data downloaded fine (import failure). Must never remove/delete, even when the
        // client's RemoveFailedDownloads setting is on - this is the HnR data-loss guard.
        DownloadFailurePolicy.ShouldRemoveDataOnFailure(downloadCompleted: true, removeFailedDownloadsSetting: true)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldRemoveDataOnFailure_GenuineDownloadFailure_RespectsSetting()
    {
        // Never completed = garbage/partial data. Removal follows the client setting.
        DownloadFailurePolicy.ShouldRemoveDataOnFailure(downloadCompleted: false, removeFailedDownloadsSetting: true)
            .Should().BeTrue();
        DownloadFailurePolicy.ShouldRemoveDataOnFailure(downloadCompleted: false, removeFailedDownloadsSetting: false)
            .Should().BeFalse();
    }
}
