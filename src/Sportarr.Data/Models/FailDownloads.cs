namespace Sportarr.Api.Models;

/// <summary>
/// Per-indexer policy for what kinds of files-in-the-download-folder
/// should escalate a completed grab from "warn the user" to "fail the
/// download outright" (which blocklists the release and triggers a
/// re-search on the next cycle). Stored on Indexer.FailDownloads as a
/// nullable list of int values; empty / null means warn-only behavior
/// (existing import logic, no escalation).
///
/// Mirrors the upstream FailDownloads enum so the rules are familiar
/// and the values transfer directly across the family.
/// </summary>
public enum FailDownloads
{
    /// <summary>
    /// The download folder contains a file with an executable extension
    /// (.exe, .bat, .cmd, .sh). When present, treat the grab as failed
    /// rather than letting the import pick the largest video file and
    /// leave the executable sitting in the staging folder.
    /// </summary>
    Executables = 0,

    /// <summary>
    /// The download folder contains a file with a "potentially dangerous"
    /// extension (.lnk, .ps1, .vbs, .scr, .arj, .lzh, .zipx). Same
    /// escalation as Executables — fail the grab rather than warn.
    /// </summary>
    PotentiallyDangerous = 1,

    /// <summary>
    /// The download folder contains a file whose extension is in the
    /// user's MediaManagementSettings.UserRejectedExtensions
    /// (comma-separated list — e.g. ".nfo, .url"). Fully user-defined
    /// escape hatch for site-specific junk that this list doesn't cover.
    /// </summary>
    UserDefinedExtensions = 2,
}
