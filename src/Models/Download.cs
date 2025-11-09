namespace Fightarr.Api.Models;

/// <summary>
/// Download client types supported by Fightarr
/// </summary>
public enum DownloadClientType
{
    QBittorrent,
    Transmission,
    Deluge,
    RTorrent,
    UTorrent,
    Sabnzbd,
    NzbGet
}

/// <summary>
/// Download client configuration
/// </summary>
public class DownloadClient
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DownloadClientType Type { get; set; }
    public required string Host { get; set; }
    public int Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ApiKey { get; set; }
    public string Category { get; set; } = "fightarr";
    public string? PostImportCategory { get; set; } // Category to move downloads to after import (Sonarr feature)
    public bool UseSsl { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 1;
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// Download queue item status
/// </summary>
public enum DownloadStatus
{
    Queued,
    Downloading,
    Paused,
    Completed,
    Failed,
    Warning,
    Importing,
    Imported
}

/// <summary>
/// Download queue item
/// </summary>
public class DownloadQueueItem
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }
    public required string Title { get; set; }
    public required string DownloadId { get; set; } // ID from download client
    public int? DownloadClientId { get; set; }
    public DownloadClient? DownloadClient { get; set; }
    public DownloadStatus Status { get; set; }
    public string? Quality { get; set; }
    public long Size { get; set; }
    public long Downloaded { get; set; }
    public double Progress { get; set; } // 0-100
    public TimeSpan? TimeRemaining { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Added { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? ImportedAt { get; set; }

    // Enhanced download monitoring fields
    public int? RetryCount { get; set; } = 0;
    public DateTime? LastUpdate { get; set; }
    public string? TorrentInfoHash { get; set; } // For blocklist tracking
    public string? Indexer { get; set; } // Which indexer this came from
    public string? Protocol { get; set; } // "Usenet" or "Torrent"

    // Universal event tracking (no subdivisions - all sports use Event.Monitored)
    // Event association is handled via EventId in DownloadQueueItem
}

/// <summary>
/// Indexer types for searching releases
/// </summary>
public enum IndexerType
{
    Torznab,
    Newznab,
    Rss,
    Torrent
}

/// <summary>
/// Indexer configuration for searching releases
/// </summary>
public class Indexer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public IndexerType Type { get; set; }
    public required string Url { get; set; }
    public string? ApiKey { get; set; }
    public string ApiPath { get; set; } = "/api";

    // Enable/Disable controls (matching Sonarr)
    public bool Enabled { get; set; } = true;
    public bool EnableRss { get; set; } = true;
    public bool EnableAutomaticSearch { get; set; } = true;
    public bool EnableInteractiveSearch { get; set; } = true;

    // Categories
    public List<string> Categories { get; set; } = new();
    public List<string>? AnimeCategories { get; set; }

    // Priority and seeding
    public int Priority { get; set; } = 25;
    public int MinimumSeeders { get; set; } = 1;
    public double? SeedRatio { get; set; }
    public int? SeedTime { get; set; } // in minutes
    public int? SeasonPackSeedTime { get; set; } // in minutes

    // Advanced settings
    public string? AdditionalParameters { get; set; }
    public List<string>? MultiLanguages { get; set; }
    public bool RejectBlocklistedTorrentHashes { get; set; } = true;
    public int? EarlyReleaseLimit { get; set; }

    // Download client association
    public int? DownloadClientId { get; set; }

    // Tags for filtering
    public List<int> Tags { get; set; } = new();

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// Search result from indexer with quality evaluation
/// </summary>
public class ReleaseSearchResult
{
    public required string Title { get; set; }
    public required string Guid { get; set; }
    public required string DownloadUrl { get; set; }
    public string? InfoUrl { get; set; }
    public required string Indexer { get; set; }
    public string? TorrentInfoHash { get; set; } // For blocklist tracking
    public string Protocol { get; set; } = "Unknown"; // "Usenet" or "Torrent"
    public long Size { get; set; }
    public string? Quality { get; set; }
    public string? Source { get; set; } // WEB-DL, BluRay, HDTV, etc.
    public string? Codec { get; set; } // H.264, HEVC, AV1, etc.
    public int? Seeders { get; set; }
    public int? Leechers { get; set; }
    public DateTime PublishDate { get; set; }

    /// <summary>
    /// Total calculated score (quality + custom formats)
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Whether this release meets profile requirements
    /// </summary>
    public bool Approved { get; set; } = true;

    /// <summary>
    /// Reasons why this release was rejected (empty if approved)
    /// </summary>
    public List<string> Rejections { get; set; } = new();

    /// <summary>
    /// Custom formats that matched this release
    /// </summary>
    public List<MatchedFormat> MatchedFormats { get; set; } = new();

    /// <summary>
    /// Base quality score before custom formats
    /// </summary>
    public int QualityScore { get; set; }

    /// <summary>
    /// Score from custom formats
    /// </summary>
    public int CustomFormatScore { get; set; }

    // NOTE: CardType removed - all sports use universal Event monitoring
    // No subdivisions like "Prelims" vs "Main Card" - that's scene naming, not TheSportsDB API concept
}

/// <summary>
/// Request model for release search
/// </summary>
public class ReleaseSearchRequest
{
    public required string Query { get; set; }
    public int? QualityProfileId { get; set; }
    public int MaxResultsPerIndexer { get; set; } = 100;
}

/// <summary>
/// Blocklist item for failed or rejected releases
/// </summary>
public class BlocklistItem
{
    public int Id { get; set; }
    public int? EventId { get; set; }
    public Event? Event { get; set; }
    public required string Title { get; set; }
    public required string TorrentInfoHash { get; set; } // For torrent blocking
    public string? Indexer { get; set; }
    public BlocklistReason Reason { get; set; }
    public string? Message { get; set; }
    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Reasons why a release was blocklisted
/// </summary>
public enum BlocklistReason
{
    FailedDownload,
    MissingFiles,
    CorruptedFiles,
    QualityMismatch,
    ManualBlock,
    ImportFailed
}
