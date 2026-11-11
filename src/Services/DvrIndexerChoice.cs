using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// The choice between a finished recording and the downloaded file that
/// already holds its part. The DVR import, the pending import endpoints and
/// recording retention use these rules.
/// </summary>
public static class DvrIndexerChoice
{
    public const string AskPolicy = "Ask";
    public const string KeepBetterPolicy = "KeepBetter";

    /// <summary>
    /// Only one caller at a time creates, starts or resolves a choice.
    /// </summary>
    public static readonly SemaphoreSlim Gate = new(1, 1);

    private static int _gateWaiters;

    /// <summary>
    /// How many callers wait in <see cref="EnterGateAsync"/> now. Tests read
    /// it to know that a request waits for the gate.
    /// </summary>
    internal static int GateWaiters => Volatile.Read(ref _gateWaiters);

    /// <summary>
    /// Wait for <see cref="Gate"/>. Release it with Gate.Release().
    /// </summary>
    public static async Task EnterGateAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _gateWaiters);
        try
        {
            await Gate.WaitAsync(ct);
        }
        finally
        {
            Interlocked.Decrement(ref _gateWaiters);
        }
    }

    /// <summary>
    /// The choices an accept is importing now. A stored Importing status can
    /// outlive a restart, so only this set says that an import runs.
    /// Add only while holding <see cref="Gate"/>.
    /// </summary>
    private static readonly ConcurrentDictionary<int, byte> InFlight = new();

    /// <summary>
    /// The recordings a KeepBetter import or the retention prune works on now.
    /// The import runs without the gate, so this set keeps the two apart.
    /// </summary>
    private static readonly ConcurrentDictionary<int, byte> RecordingsInUse = new();

    public enum Outcome
    {
        /// <summary>The downloaded file stays and the choice row is gone.</summary>
        Resolved,
        /// <summary>The downloaded file is not on disk, so the choice stays open.</summary>
        DownloadedFileMissing,
        /// <summary>The choice row no longer exists.</summary>
        Gone,
        /// <summary>An accept is importing the recording now.</summary>
        Busy,
        /// <summary>The choice is already resolved.</summary>
        Closed,
    }

    /// <summary>
    /// Mark a choice as being imported. The caller holds <see cref="Gate"/>.
    /// Dispose the result when the import ends.
    /// </summary>
    public static IDisposable? TryStartImport(int pendingImportId) =>
        InFlight.TryAdd(pendingImportId, 0) ? new Claim(InFlight, pendingImportId) : null;

    /// <summary>
    /// Claim a recording for one caller. Null when another caller has it.
    /// Dispose the result when the work ends.
    /// </summary>
    public static IDisposable? TryClaimRecording(int recordingId) =>
        RecordingsInUse.TryAdd(recordingId, 0) ? new Claim(RecordingsInUse, recordingId) : null;

    public static bool IsImporting(int pendingImportId) => InFlight.ContainsKey(pendingImportId);

    /// <summary>
    /// Set choices left Importing by a stopped process back to Pending, so
    /// that the user sees them again.
    /// </summary>
    public static async Task<int> ReopenInterruptedChoicesAsync(SportarrDbContext db, CancellationToken ct = default)
    {
        var stuck = await db.PendingImports
            .Where(p => p.DownloadId.StartsWith("dvr-") && p.Status == PendingImportStatus.Importing)
            .ToListAsync(ct);
        var reopened = stuck.Where(choice => !IsImporting(choice.Id)).ToList();
        foreach (var choice in reopened)
            choice.Status = PendingImportStatus.Pending;
        await db.SaveChangesAsync(ct);
        return reopened.Count;
    }

    private sealed class Claim(ConcurrentDictionary<int, byte> set, int id) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                set.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// The stored policy, or Ask for an empty or unknown value.
    /// </summary>
    public static string NormalizePolicy(string? value) =>
        string.Equals(value?.Trim(), KeepBetterPolicy, StringComparison.OrdinalIgnoreCase)
            ? KeepBetterPolicy
            : AskPolicy;

    public static bool KeepsBetterFile(Config config) =>
        NormalizePolicy(config.DvrIndexerConflictPolicy) == KeepBetterPolicy;

    /// <summary>
    /// Whether a downloaded file for the part is tracked and on disk.
    /// </summary>
    public static async Task<bool> DownloadedFileOnDiskAsync(SportarrDbContext db, int? eventId, string? partName,
        CancellationToken ct = default)
    {
        if (eventId == null)
            return false;
        var files = await db.EventFiles.AsNoTracking()
            .Where(file => file.EventId == eventId && file.Exists)
            .ToListAsync(ct);
        return files.Any(file => SourcePrecedence.RecordingConflictsWithIndexerFile([file], partName) &&
            File.Exists(file.FilePath));
    }

    /// <summary>
    /// Whether the recording can be the only copy of its part. A tracked file
    /// for the part is gone from disk, and no tracked file for the part is on
    /// disk. The recording's own path does not count.
    /// </summary>
    public static async Task<bool> RecordingMayBeOnlyCopyAsync(SportarrDbContext db, int eventId, string? partName,
        string? recordingPath, CancellationToken ct = default)
    {
        var files = (await db.EventFiles.AsNoTracking()
                .Where(file => file.EventId == eventId)
                .ToListAsync(ct))
            .Where(file =>
                string.Equals(SourcePrecedence.NormalizePart(file.PartName), SourcePrecedence.NormalizePart(partName),
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(file.FilePath, recordingPath, StringComparison.Ordinal))
            .ToList();
        var onDisk = files.Where(file => file.Exists && File.Exists(file.FilePath)).ToList();
        return onDisk.Count == 0 && files.Count > onDisk.Count;
    }

    /// <summary>
    /// The blocklist row for a pending import the user rejects.
    /// </summary>
    public static BlocklistItem RejectedImportBlock(PendingImport import) => new()
    {
        EventId = import.SuggestedEventId,
        Title = import.Title,
        TorrentInfoHash = import.TorrentInfoHash,
        Protocol = import.Protocol,
        Part = import.SuggestedPart,
        FilePath = import.FilePath,
        Reason = BlocklistReason.ManualBlock,
        Message = "User rejected pending import",
        BlockedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Keep the downloaded file for an open recording choice. The recording
    /// becomes Ignored and the choice row goes. The recording file stays on
    /// disk. The caller holds <see cref="Gate"/>.
    /// </summary>
    public static async Task<Outcome> KeepDownloadAsync(SportarrDbContext db, PendingImport import, bool blocklist,
        CancellationToken ct = default)
    {
        // Check the claim first. An accept starts only under the gate, which
        // the caller holds, so the row cannot change after this check. Read
        // the other way round, an accept could finish between the two reads.
        if (IsImporting(import.Id))
            return Outcome.Busy;
        var current = await db.PendingImports.AsNoTracking()
            .FirstOrDefaultAsync(pending => pending.Id == import.Id, ct);
        if (current == null)
            return Outcome.Gone;
        // A stored Importing status without a running import is left from a stop.
        if (current.Status is not (PendingImportStatus.Pending or PendingImportStatus.Importing))
            return Outcome.Closed;

        if (import.DownloadId.StartsWith("dvr-", StringComparison.Ordinal) &&
            int.TryParse(import.DownloadId.AsSpan(4), out var recordingId))
        {
            var recording = await db.DvrRecordings.FirstOrDefaultAsync(r => r.Id == recordingId &&
                r.OutputPath == import.FilePath && r.Status == DvrRecordingStatus.Completed, ct);
            if (recording != null)
            {
                // The recording is the only copy while the downloaded file is missing.
                if (!await DownloadedFileOnDiskAsync(db, recording.EventId, recording.PartName, ct))
                    return Outcome.DownloadedFileMissing;
                recording.Status = DvrRecordingStatus.Ignored;
                recording.LastUpdated = DateTime.UtcNow;
            }
        }

        if (blocklist)
            db.Blocklist.Add(RejectedImportBlock(import));
        db.PendingImports.Remove(import);
        await db.SaveChangesAsync(ct);
        return Outcome.Resolved;
    }
}
