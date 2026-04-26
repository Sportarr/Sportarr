namespace Sportarr.Api.Models;

/// <summary>
/// A release held in delay-profile purgatory.
///
/// When RSS sync (or a future targeted-search caller) finds a matching release
/// for an event, the delay profile may say "hold this for N minutes so a
/// better release can show up before we commit." Instead of grabbing
/// immediately, we persist the release here. The PendingReleaseReaperService
/// walks expired entries and picks the best release per event.
///
/// Mirrors Sonarr's PendingReleases table.
/// </summary>
public class PendingRelease
{
    public int Id { get; set; }

    public int EventId { get; set; }
    public Event? Event { get; set; }

    // Release identity (enough to grab later via DownloadClientService)
    public string Title { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string? InfoUrl { get; set; }
    public string Indexer { get; set; } = string.Empty;
    public int? IndexerId { get; set; }
    public string? TorrentInfoHash { get; set; }
    public string Protocol { get; set; } = string.Empty;

    // Selection metadata
    public long Size { get; set; }
    public string? Quality { get; set; }
    public string? Source { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? ReleaseGroup { get; set; }
    public int QualityScore { get; set; }
    public int CustomFormatScore { get; set; }
    public int Score { get; set; }
    public int MatchScore { get; set; }
    public string? Part { get; set; }
    public int? Seeders { get; set; }
    public int? Leechers { get; set; }
    public DateTime PublishDate { get; set; }

    // Delay window state
    public DateTime AddedToPendingAt { get; set; } = DateTime.UtcNow;
    public DateTime ReleasableAt { get; set; }
    public string Reason { get; set; } = "DelayProfile";
    public PendingReleaseStatus Status { get; set; } = PendingReleaseStatus.Pending;
}

public enum PendingReleaseStatus
{
    Pending = 0,    // Waiting for ReleasableAt
    Released = 1,   // Best-of-window winner; promoted to DownloadQueue
    Cancelled = 2,  // Superseded by a better pending release for the same event
    Failed = 3      // Grab attempt failed (logged for retry/inspection)
}
