using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

public sealed record ImportSelectedRequest(string RelativePath);

public static class ManualQueueImportPolicy
{
    public const string AmbiguousVideoWarning = "Multiple videos could match this event. Choose the file to import.";

    public static bool IsCompletedClientStatus(DownloadClientStatus status) =>
        string.Equals(status.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(status.Status, "paused", StringComparison.OrdinalIgnoreCase) && status.Progress >= 99.9);

    public static bool CanImportAnyway(DownloadQueueItem item)
    {
        if (item.Status != DownloadStatus.ImportWarning || item.Progress < 100 || item.DownloadClient is null)
            return false;

        var reason = item.ErrorMessage;
        return reason != null &&
            (reason.StartsWith(ImportUpgradeRule.LowerQualityRejection, StringComparison.Ordinal) ||
             reason.StartsWith(ImportUpgradeRule.RevisionRejection, StringComparison.Ordinal) ||
             reason.StartsWith(ImportUpgradeRule.CustomFormatRejection, StringComparison.Ordinal));
    }

    public static bool CanChooseVideo(DownloadQueueItem item) =>
        !item.IsPack && item.Status == DownloadStatus.ImportWarning && item.Progress >= 100 &&
        item.DownloadClient is not null &&
        item.ErrorMessage?.StartsWith(AmbiguousVideoWarning, StringComparison.Ordinal) == true;

    public static void RestoreVideoChoice(DownloadQueueItem item)
    {
        item.Status = DownloadStatus.ImportWarning;
        item.ErrorMessage = AmbiguousVideoWarning;
        item.LastUpdate = DateTime.UtcNow;
    }

    public static async Task<bool> TryClaimAsync(SportarrDbContext db, int id)
    {
        var claimed = await db.DownloadQueue
            .Where(item => item.Id == id && item.Status == DownloadStatus.ImportWarning && item.Progress >= 100)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.Status, DownloadStatus.Importing)
                .SetProperty(item => item.LastUpdate, DateTime.UtcNow));
        return claimed == 1;
    }

    public static void MarkIncompleteImport(DownloadQueueItem item)
    {
        if (item.Status != DownloadStatus.Importing) return;
        item.Status = DownloadStatus.Failed;
        item.ErrorMessage = "Import did not complete. Retry the import.";
        item.LastUpdate = DateTime.UtcNow;
    }
}
