using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using FluentAssertions;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Tests for the status-vocabulary mapping the v3/queue compat endpoint uses. This is
/// the part Unpackerr actually depends on: it watches trackedDownloadState for
/// "importPending" to know a download finished but Sportarr couldn't import it yet
/// (e.g. packed archives still extracting).
/// </summary>
public class SonarrQueueEndpointsTests
{
    [Theory]
    [InlineData(DownloadStatus.Queued, "queued", "downloading", "ok")]
    [InlineData(DownloadStatus.Downloading, "downloading", "downloading", "ok")]
    [InlineData(DownloadStatus.Paused, "paused", "downloading", "ok")]
    [InlineData(DownloadStatus.Warning, "warning", "downloading", "warning")]
    [InlineData(DownloadStatus.Importing, "completed", "importing", "ok")]
    [InlineData(DownloadStatus.Failed, "failed", "failedPending", "error")]
    public void MapStatus_ReturnsExpectedVocabulary(DownloadStatus input, string status, string trackedDownloadState, string trackedDownloadStatus)
    {
        var result = SonarrQueueEndpoints.MapStatus(input);

        result.Status.Should().Be(status);
        result.TrackedDownloadState.Should().Be(trackedDownloadState);
        result.TrackedDownloadStatus.Should().Be(trackedDownloadStatus);
    }

    [Theory]
    [InlineData(DownloadStatus.Completed)]
    [InlineData(DownloadStatus.ImportPending)]
    [InlineData(DownloadStatus.ImportWarning)]
    public void MapStatus_DownloadFinishedButNotImported_ReportsImportPending(DownloadStatus input)
    {
        // This is the state Unpackerr actually watches for: the download is done, but
        // Sportarr hasn't imported it yet (still extracting, path not ready, etc).
        var result = SonarrQueueEndpoints.MapStatus(input);

        result.TrackedDownloadState.Should().Be("importPending");
    }
}
