using System.Text.Json.Serialization;

namespace Sportarr.Api.Models;

/// <summary>
/// Top-level JSON document embedded as `manifest.json` inside every backup
/// zip produced from sportarr v4.0.1002+. Older backups omit this file; the
/// restore flow falls back to a "blind" restore in that case.
///
/// The manifest captures everything the restore preview screen needs to
/// answer "what am I about to commit to?" before the .db file gets
/// overwritten:
///   * Source host + version: spot machine-to-machine restores at a glance
///   * Configured root folders on the source: detect path drift up front
///   * File-level inventory: total count + a representative sample so the
///     preview can show "12,481 files in the backup, you have N matching
///     paths"
///
/// Kept intentionally small: full per-file listings would inflate backup
/// size on libraries with tens of thousands of events. The first
/// `SampleFiles` rows are enough for the path-remap heuristic to find a
/// common prefix without serializing every row.
/// </summary>
public class BackupManifest
{
    [JsonPropertyName("manifestVersion")]
    public int ManifestVersion { get; set; } = 1;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("sportarrVersion")]
    public string? SportarrVersion { get; set; }

    /// <summary>
    /// Identifier of the host that produced the backup. Useful for the
    /// admin to spot a machine-to-machine restore at a glance. We do NOT
    /// require this to match the restoring host -- it's purely informational.
    /// </summary>
    [JsonPropertyName("sourceHost")]
    public string? SourceHost { get; set; }

    /// <summary>
    /// Database provider that produced this backup: "sqlite" or "postgres". Null on
    /// backups made before Postgres support existed - those are implicitly sqlite.
    /// Restoring a backup onto a differently-provisioned install is not supported (the
    /// data formats are incompatible - a raw SQLite file copy vs a pg_dump archive), so
    /// the restore flow fails fast on a mismatch rather than corrupting anything.
    /// </summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>
    /// Optional free-text note provided by the admin when creating the
    /// backup (already exists in BackupInfo + the create-backup endpoint).
    /// </summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>
    /// Configured root folders on the source host. The restore preview UI
    /// uses these to detect path drift: if the source had `/mnt/user/data`
    /// as a root and none of those paths exist on the target, that's the
    /// signal to surface the remap wizard.
    /// </summary>
    [JsonPropertyName("rootFolders")]
    public List<string> RootFolders { get; set; } = new();

    /// <summary>
    /// Total EventFile rows in the source database -- gives the preview
    /// screen an at-a-glance "12,481 files in this backup" number without
    /// having to inspect the .db.
    /// </summary>
    [JsonPropertyName("totalEventFiles")]
    public int TotalEventFiles { get; set; }

    /// <summary>
    /// Total Events rows. Same purpose as TotalEventFiles -- a top-level
    /// scope summary the preview UI can show before the restore commits.
    /// </summary>
    [JsonPropertyName("totalEvents")]
    public int TotalEvents { get; set; }

    /// <summary>
    /// Total Leagues rows.
    /// </summary>
    [JsonPropertyName("totalLeagues")]
    public int TotalLeagues { get; set; }

    /// <summary>
    /// A representative sample of EventFile paths -- enough for the
    /// path-remap heuristic to compute the longest common prefix and
    /// suggest a remap without forcing every backup to carry an inventory
    /// of tens of thousands of rows. 200 rows is sufficient signal for
    /// every real-world prefix-drift case observed.
    /// </summary>
    [JsonPropertyName("sampleFiles")]
    public List<string> SampleFiles { get; set; } = new();
}
