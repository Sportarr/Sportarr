using System;
using System.Collections.Generic;
using System.IO;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Removes the file behind a pending import that was found on disk, when
/// the user removes the row. A row from a download client is cleared
/// through the client instead. The file goes to the recycle bin when one
/// is set, else it is deleted. Only a file inside a root folder is
/// touched: the row's path is data the scan wrote, and a delete must never
/// reach outside the library.
/// </summary>
public static class PendingImportFiles
{
    public sealed record Outcome(bool Removed, bool Recycled, string Detail);

    /// <summary>
    /// Whether removing a pending import removes its file: only a row the
    /// scan made from disk (no download client), still pending, and not
    /// told otherwise. A row from a download client is cleared through the
    /// client; a row already accepted points at a file the library holds.
    /// </summary>
    public static bool ShouldRemove(int? downloadClientId, bool? deleteFile, Models.PendingImportStatus status)
        => downloadClientId == null && deleteFile != false && status == Models.PendingImportStatus.Pending;

    public static Outcome RemoveFromDisk(string? filePath, string? recycleBin, IEnumerable<string> rootFolders, bool trackedByLibrary = false)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new Outcome(false, false, "no file path on the row");
        }
        if (trackedByLibrary)
        {
            // The row went stale: the library imported this very file since.
            return new Outcome(false, false, "the library tracks this file, left alone");
        }
        if (!File.Exists(filePath))
        {
            return new Outcome(false, false, "the file is already gone");
        }
        if (!PathResolution.IsInsideAny(filePath, rootFolders))
        {
            return new Outcome(false, false, "the file is outside every root folder, left alone");
        }

        if (!string.IsNullOrEmpty(recycleBin) && Directory.Exists(recycleBin))
        {
            var target = RecyclePaths.FindFree(recycleBin, Path.GetFileName(filePath));
            File.Move(filePath, target);
            return new Outcome(true, true, $"moved to the recycle bin as {Path.GetFileName(target)}");
        }

        File.Delete(filePath);
        return new Outcome(true, false, "deleted");
    }
}
