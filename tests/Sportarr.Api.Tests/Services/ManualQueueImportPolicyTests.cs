using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class ManualQueueImportPolicyTests
{
    [Theory]
    [InlineData("completed", 100, true)]
    [InlineData("paused", 100, true)]
    [InlineData("paused", 99, false)]
    [InlineData("downloading", 100, false)]
    [InlineData("failed", 100, false)]
    public void ClientStatusMustRepresentACompleteDownload(string status, double progress, bool allowed)
    {
        ManualQueueImportPolicy.IsCompletedClientStatus(new DownloadClientStatus { Status = status, Progress = progress })
            .Should().Be(allowed);
    }

    [Theory]
    [InlineData("Not an upgrade for the existing file. Existing quality: WEBDL-2160p. New quality: HDTV-1080p.")]
    [InlineData("Not a revision upgrade for the existing file.")]
    [InlineData("Not a custom format upgrade for the existing file. New score 500 does not improve on 1000.")]
    public void CompletedPreferenceWarningCanBeImportedByChoice(string reason)
    {
        ManualQueueImportPolicy.CanImportAnyway(Row(DownloadStatus.ImportWarning, 100, reason)).Should().BeTrue();
    }

    [Theory]
    [InlineData(DownloadStatus.Downloading, 100, "Not an upgrade for the existing file.")]
    [InlineData(DownloadStatus.ImportWarning, 99, "Not an upgrade for the existing file.")]
    [InlineData(DownloadStatus.ImportWarning, 100, "Pack member unresolved: No member uniquely identifies this event.")]
    [InlineData(DownloadStatus.ImportWarning, 100, "The part destination matches another existing file.")]
    [InlineData(DownloadStatus.Imported, 100, "Not an upgrade for the existing file.")]
    public void OtherQueueStatesCannotBypassPreferenceGate(DownloadStatus status, double progress, string reason)
    {
        ManualQueueImportPolicy.CanImportAnyway(Row(status, progress, reason)).Should().BeFalse();
    }

    [Fact]
    public void MissingDownloadClientDoesNotOfferAnUnusableImportAction()
    {
        var row = Row(DownloadStatus.ImportWarning, 100, "Not an upgrade for the existing file.");
        row.DownloadClient = null;

        ManualQueueImportPolicy.CanImportAnyway(row).Should().BeFalse();
    }

    [Fact]
    public void RealUpgradeRejectionOffersTheManualChoice()
    {
        var decision = ImportUpgradeRule.Evaluate(
            "WEBDL-2160p", 560, "existing.2160p.WEB-DL",
            "HDTV-1080p", 2500, "preferred.1080p.HDTV", "preferAndUpgrade");
        decision.IsUpgrade.Should().BeFalse();

        ManualQueueImportPolicy.CanImportAnyway(Row(DownloadStatus.ImportWarning, 100, decision.Rejection!))
            .Should().BeTrue();
    }

    [Fact]
    public void NullImportResultDoesNotLeaveAClaimedRowImporting()
    {
        var row = Row(DownloadStatus.Importing, 100, "Not an upgrade for the existing file.");

        ManualQueueImportPolicy.MarkIncompleteImport(row);

        row.Status.Should().Be(DownloadStatus.Failed);
        row.ErrorMessage.Should().Be("Import did not complete. Retry the import.");
    }

    private static DownloadQueueItem Row(DownloadStatus status, double progress, string reason) => new()
    {
        EventId = 1,
        Title = "Spanish Grand Prix - Qualifying",
        DownloadId = "completed-release",
        Status = status,
        Progress = progress,
        DownloadClient = new DownloadClient { Name = "Test client", Host = "localhost" },
        ErrorMessage = reason
    };
}
