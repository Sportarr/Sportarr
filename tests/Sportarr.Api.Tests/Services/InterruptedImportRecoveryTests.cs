using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class InterruptedImportRecoveryTests
{
    [Fact]
    public void ExistingOldFileDoesNotCompleteAnInterruptedReplacement()
    {
        var file = Path.GetTempFileName();
        try
        {
            var row = new DownloadQueueItem { Id = 41, EventId = 7, Title = "new-release", DownloadId = "job", Status = DownloadStatus.Importing,
                LastUpdate = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc) };
            var oldFile = new EventFile { EventId = 7, FilePath = file, OriginalTitle = "old-release" };

            InterruptedImportRecovery.HasCompletedImport(row, [oldFile], []).Should().BeFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void CompletedImportNeedsItsOwnHistoryAndPresentDestination()
    {
        var file = Path.GetTempFileName();
        try
        {
            var row = new DownloadQueueItem { Id = 41, EventId = 7, Title = "new-release", DownloadId = "job", Status = DownloadStatus.Importing,
                LastUpdate = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc) };
            var importedFile = new EventFile { EventId = 7, FilePath = file, OriginalTitle = "new-release" };
            var history = new ImportHistory { EventId = 7, DownloadQueueItemId = 41, SourcePath = "source", DestinationPath = file,
                Quality = "HDTV-1080p", Decision = ImportDecision.Approved, ImportedAt = row.LastUpdate.Value.AddMinutes(1) };

            InterruptedImportRecovery.HasCompletedImport(row, [importedFile], [history]).Should().BeTrue();
            history.DownloadQueueItemId = 42;
            InterruptedImportRecovery.HasCompletedImport(row, [importedFile], [history]).Should().BeFalse();
            history.DownloadQueueItemId = 41;
            history.ImportedAt = row.LastUpdate.Value.AddMinutes(-1);
            InterruptedImportRecovery.HasCompletedImport(row, [importedFile], [history]).Should().BeFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }
}
