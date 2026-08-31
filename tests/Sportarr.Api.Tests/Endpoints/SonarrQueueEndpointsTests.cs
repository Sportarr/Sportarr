using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using FluentAssertions;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Tests for the wire contract the v3/queue compat endpoint serves. Archive
/// extractors read two fields off it: status, to know the download finished,
/// and outputPath, to find the folder on disk.
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
        // A finished download Sportarr has not imported yet (still extracting,
        // path not ready). Dashboards and queue cleaners read this field.
        var result = SonarrQueueEndpoints.MapStatus(input);

        result.TrackedDownloadState.Should().Be("importPending");
    }

    [Theory]
    [InlineData(DownloadStatus.Completed)]
    [InlineData(DownloadStatus.Importing)]
    [InlineData(DownloadStatus.ImportPending)]
    [InlineData(DownloadStatus.ImportWarning)]
    public void MapStatus_DownloadFinished_ReportsCompletedStatus(DownloadStatus input)
    {
        // Unpackerr keys on the plain status field, not trackedDownloadState:
        // a record is ready to unpack when status is "completed" and the
        // protocol is one it was told to handle.
        var result = SonarrQueueEndpoints.MapStatus(input);

        result.Status.Should().Be("completed");
    }

    [Fact]
    public void ToQueueRecord_ReportsTheClientPathAsOutputPath()
    {
        // An extractor looks for <its configured path>/<title> first, then
        // falls back to outputPath. Without this field a download whose folder
        // on disk is not named after the release is never unpacked.
        var record = SonarrQueueEndpoints.ToQueueRecord(new DownloadQueueItem
        {
            Title = "Some.Race.2026.1080p.WEB-DL.x264",
            DownloadId = "hash",
            Status = DownloadStatus.ImportPending,
            OutputPath = "/downloads/some-other-folder-name"
        });

        Prop(record, "outputPath").Should().Be("/downloads/some-other-folder-name");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToQueueRecord_WithNoUsablePath_ReportsNullOutputPath(string? path)
    {
        // Rows created before the field existed, and clients that never report
        // a path, must send null rather than an empty string. An extractor
        // treats an empty string as a real path and stats it.
        var record = SonarrQueueEndpoints.ToQueueRecord(new DownloadQueueItem
        {
            Title = "Some.Race.2026.1080p.WEB-DL.x264",
            DownloadId = "hash",
            Status = DownloadStatus.Completed,
            OutputPath = path
        });

        Prop(record, "outputPath").Should().BeNull();
    }

    private static object? Prop(object record, string name) =>
        record.GetType().GetProperty(name)!.GetValue(record);
}
