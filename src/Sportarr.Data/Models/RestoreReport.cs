namespace Sportarr.Api.Models;

/// <summary>
/// Captures one backup-restore operation and the reconciliation pass that
/// followed it. The admin UI reads this table to answer "I restored a
/// backup -- what happened to my files?" without forcing the user to dig
/// through disk-scan logs.
///
/// Lifecycle:
///   * Created when RestoreBackupAsync starts (Status = "pending")
///   * Reconciliation runs (DiskScanService.TriggerScanNow + a wait for
///     completion); counts are filled in and Status flips to "completed"
///   * If anything threw, Status = "failed" and Notes carries the exception
///
/// Path remaps applied during this restore session are recorded as a JSON
/// array on PathRemapsJson so the admin can see exactly what was rewritten.
/// </summary>
public class RestoreReport
{
    public int Id { get; set; }

    /// <summary>
    /// The backup zip's filename (e.g. "sportarr_backup_20260517_143205.zip").
    /// Stored as a name not a full path so the report is portable across
    /// installs and the backup folder can move.
    /// </summary>
    public required string BackupFileName { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Total EventFile rows present in the restored database. Should equal
    /// FilesFound + FilesMissing + FilesSkippedUnreachableRoot once the
    /// reconciliation pass completes.
    /// </summary>
    public int TotalEventFiles { get; set; }

    /// <summary>
    /// Files whose stored FilePath exists on disk after restore. These need
    /// no further action.
    /// </summary>
    public int FilesFound { get; set; }

    /// <summary>
    /// Files whose stored FilePath does NOT exist on disk after restore. The
    /// admin can either remap (Phase 2) or accept that they're gone.
    /// </summary>
    public int FilesMissing { get; set; }

    /// <summary>
    /// Files under root folders that are currently unreachable (mount not
    /// attached, NAS down, restored backup before paths were remapped). The
    /// disk scanner deliberately does not mark these missing because we
    /// can't tell the difference between "file is gone" and "mount went
    /// away"; surfacing the count here tells the admin "fix your mounts and
    /// rescan."
    /// </summary>
    public int FilesSkippedUnreachableRoot { get; set; }

    /// <summary>
    /// Verbatim contents of the backup zip's manifest.json file (if the
    /// backup was produced by a sportarr that wrote one). Backups produced
    /// by older versions land with null here and the reconciliation
    /// proceeds without a source-of-truth comparison.
    /// </summary>
    public string? ManifestJson { get; set; }

    /// <summary>
    /// Path remaps applied during this restore. JSON array of
    /// { "from": "...", "to": "...", "affected": int } so the admin
    /// can audit + revert via the UI.
    /// </summary>
    public string? PathRemapsJson { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Host the backup was produced on, sourced from the manifest. Most
    /// useful for diagnosing path drift: backups from a different machine
    /// almost always need a remap.
    /// </summary>
    public string? SourceHost { get; set; }

    /// <summary>
    /// Sportarr version the backup was produced under, again from the
    /// manifest. Surfaced in the UI so the admin can spot a version skew
    /// before they commit to the restore.
    /// </summary>
    public string? SourceSportarrVersion { get; set; }

    /// <summary>
    /// Lifecycle marker: "pending" | "completed" | "failed".
    /// </summary>
    public string Status { get; set; } = "pending";
}
