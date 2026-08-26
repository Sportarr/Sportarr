using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class FileImportServiceTerminalStateTests
{
    [Fact]
    public void MarkDownloadImported_NormalizesStaleTransferState()
    {
        var importedAt = new DateTime(2026, 8, 19, 12, 30, 0, DateTimeKind.Utc);
        var download = NewDownload(size: 1_000, downloaded: 350);
        download.Status = DownloadStatus.Importing;
        download.Progress = 35;
        download.TimeRemaining = TimeSpan.FromMinutes(20);
        download.ErrorMessage = "stale downloader error";
        download.LastUpdate = importedAt.AddMinutes(-5);

        FileImportService.MarkDownloadImported(download, importedAt);

        download.Status.Should().Be(DownloadStatus.Imported);
        download.Progress.Should().Be(100);
        download.Downloaded.Should().Be(1_000);
        download.TimeRemaining.Should().BeNull();
        download.CompletedAt.Should().Be(importedAt);
        download.ImportedAt.Should().Be(importedAt);
        download.LastUpdate.Should().Be(importedAt);
        download.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void MarkDownloadImported_PreservesExistingCompletedAt()
    {
        var completedAt = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        var importedAt = completedAt.AddMinutes(30);
        var download = NewDownload(size: 1_000, downloaded: 1_000);
        download.CompletedAt = completedAt;

        FileImportService.MarkDownloadImported(download, importedAt);

        download.CompletedAt.Should().Be(completedAt);
        download.ImportedAt.Should().Be(importedAt);
    }

    [Fact]
    public void MarkDownloadImported_DoesNotReduceDownloadedBytes()
    {
        var download = NewDownload(size: 1_000, downloaded: 1_050);

        FileImportService.MarkDownloadImported(download, DateTime.UtcNow);

        download.Downloaded.Should().Be(1_050);
    }

    [Fact]
    public void MarkDownloadImported_DoesNotInventBytesWhenSizeIsUnknown()
    {
        var download = NewDownload(size: 0, downloaded: 0);

        FileImportService.MarkDownloadImported(download, DateTime.UtcNow);

        download.Downloaded.Should().Be(0);
        download.Progress.Should().Be(100);
    }

    private static DownloadQueueItem NewDownload(long size, long downloaded) => new()
    {
        Title = "Example release",
        DownloadId = "example-download-id",
        Size = size,
        Downloaded = downloaded,
    };
}
