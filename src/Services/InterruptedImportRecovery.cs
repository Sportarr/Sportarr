using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

public static class InterruptedImportRecovery
{
    public static bool HasCompletedImport(DownloadQueueItem item, IEnumerable<EventFile> files,
        IEnumerable<ImportHistory> histories)
    {
        var destinations = histories
            .Where(history => history.DownloadQueueItemId == item.Id &&
                history.Decision == ImportDecision.Approved &&
                (!item.LastUpdate.HasValue || history.ImportedAt >= item.LastUpdate.Value))
            .Select(history => history.DestinationPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return files.Any(file => destinations.Contains(file.FilePath) && File.Exists(file.FilePath));
    }
}
