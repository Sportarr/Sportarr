namespace Sportarr.Api.Models;

/// <summary>
/// Download client types supported by Sportarr
/// </summary>
public enum DownloadClientType
{
    QBittorrent,
    Transmission,
    Deluge,
    RTorrent,
    UTorrent,
    Sabnzbd,
    NzbGet,
    Decypharr,
    DecypharrUsenet,
    NZBdav
}

/// <summary>
/// Initial state for torrents when added to the download client
/// Matches Sonarr/Radarr behavior for testing automation before going live
/// </summary>
public enum TorrentInitialState
{
    /// <summary>
    /// Start downloading immediately (default, normal behavior)
    /// </summary>
    Started = 0,

    /// <summary>
    /// Force start the torrent (ignore queue limits)
    /// </summary>
    ForceStarted = 1,

    /// <summary>
    /// Add torrent in stopped/paused state (useful for testing automation)
    /// </summary>
    Stopped = 2
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
    public string? UrlBase { get; set; } // URL base path (e.g., "/sabnzbd" for SABnzbd, empty for root)
    public string Category { get; set; } = "sportarr";
    public string? PostImportCategory { get; set; } // Category to move downloads to after import (Sonarr feature)
    public bool UseSsl { get; set; }
    public bool DisableSslCertificateValidation { get; set; } = false; // Allow self-signed certificates (for local networks)
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 1;
    public bool SequentialDownload { get; set; } = false; // Download pieces in order (useful for debrid services like Decypharr)
    public bool FirstAndLastFirst { get; set; } = false; // Prioritize first and last pieces (for quick video preview)
    public TorrentInitialState InitialState { get; set; } = TorrentInitialState.Started; // Initial state when torrent is added (Started, ForceStarted, Stopped)
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
    Imported,
    ImportPending // Download complete but waiting for path to become accessible
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
    public List<string> StatusMessages { get; set; } = new(); // Sonarr-style status messages (warnings, errors)
    public DateTime Added { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? ImportedAt { get; set; }

    // Enhanced download monitoring fields
    public int? RetryCount { get; set; } = 0;
    public int? ImportRetryCount { get; set; } = 0; // Separate counter for import retries (path accessibility)
    public DateTime? LastUpdate { get; set; }
    public string? TorrentInfoHash { get; set; } // For blocklist tracking
    public string? Indexer { get; set; } // Which indexer this came from
    public string? Protocol { get; set; } // "Usenet" or "Torrent"

    /// <summary>
    /// Counter for tracking consecutive "not found" polls from download client.
    /// When download is removed from client externally (not through Sportarr),
    /// this counter increments. After 3 consecutive "not found" checks,
    /// the queue item is auto-removed (Sonarr behavior).
    /// </summary>
    public int? MissingFromClientCount { get; set; } = 0;

    // Quality scores from the grabbed release
    public int QualityScore { get; set; }
    public int CustomFormatScore { get; set; }

    // Video codec and source for multi-part consistency checks
    public string? Codec { get; set; }  // H.264, HEVC, AV1, etc.
    public string? Source { get; set; } // WEB-DL, BluRay, HDTV, etc.

    /// <summary>
    /// The part of the event (for multi-part events like fight cards: "Early Prelims", "Prelims", "Main Card")
    /// Null if not a multi-part event or applies to the whole event
    /// </summary>
    public string? Part { get; set; }

    /// <summary>
    /// For pack downloads (like weekly packs), groups related queue items together.
    /// All events in the same pack share the same PackGroupId.
    /// This enables Sonarr-style season pack behavior where all episodes appear in queue.
    /// </summary>
    public Guid? PackGroupId { get; set; }

    /// <summary>
    /// Whether this queue item is part of a pack download
    /// </summary>
    public bool IsPack { get; set; } = false;

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

    // Rate limiting settings (Sonarr-style)
    public int? QueryLimit { get; set; } // Max queries per hour (null = unlimited)
    public int? GrabLimit { get; set; } // Max grabs per hour (null = unlimited)
    public int RequestDelayMs { get; set; } = 0; // Delay between requests in milliseconds

    // Navigation property
    public IndexerStatus? Status { get; set; }

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// Indexer status for tracking health and rate limiting (Sonarr-style)
/// Implements separate query/grab backoffs as proposed in Sonarr issue #3132
/// </summary>
public class IndexerStatus
{
    public int Id { get; set; }

    // Foreign key to Indexer
    public int IndexerId { get; set; }
    public Indexer? Indexer { get; set; }

    // Query failure tracking (search/RSS failures)
    public int QueryFailures { get; set; } = 0;
    public DateTime? QueryDisabledUntil { get; set; } // Backoff for query failures
    public DateTime? LastQueryFailure { get; set; }
    public string? LastQueryFailureReason { get; set; }

    // Grab failure tracking (download failures) - separate from query failures
    // Per Sonarr #3132: grab failures shouldn't prevent searching
    public int GrabFailures { get; set; } = 0;
    public DateTime? GrabDisabledUntil { get; set; } // Backoff for grab failures
    public DateTime? LastGrabFailure { get; set; }
    public string? LastGrabFailureReason { get; set; }

    // Legacy field for backward compatibility (will be migrated to QueryFailures)
    public int ConsecutiveFailures { get; set; } = 0;
    public DateTime? LastFailure { get; set; }
    public string? LastFailureReason { get; set; }
    public DateTime? DisabledUntil { get; set; } // Legacy - use QueryDisabledUntil/GrabDisabledUntil

    // Rate limiting tracking (per-hour counters)
    public int QueriesThisHour { get; set; } = 0;
    public int GrabsThisHour { get; set; } = 0;
    public DateTime? HourResetTime { get; set; } // When to reset hourly counters

    // Success tracking
    public DateTime? LastSuccess { get; set; }
    public DateTime? LastRssSyncAttempt { get; set; }

    // HTTP 429 handling - respects Retry-After without adding exponential backoff
    public DateTime? RateLimitedUntil { get; set; } // Retry-After from 429 response

    // Connection error tracking - DNS/network errors don't escalate (Sonarr pattern)
    // These are likely user network issues, not indexer problems
    public int ConnectionErrors { get; set; } = 0;
    public DateTime? LastConnectionError { get; set; }
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
    public int? IndexerId { get; set; } // For release profile filtering
    public string? TorrentInfoHash { get; set; } // For blocklist tracking
    public string Protocol { get; set; } = "Unknown"; // "Usenet" or "Torrent"
    public long Size { get; set; }
    public string? Quality { get; set; }
    public string? Source { get; set; } // WEB-DL, BluRay, HDTV, etc.
    public string? Codec { get; set; } // H.264, HEVC, AV1, etc.
    public string? Language { get; set; } // Detected language from title (English, German, French, etc.)
    public int? Seeders { get; set; }
    public int? Leechers { get; set; }
    public DateTime PublishDate { get; set; }

    /// <summary>
    /// Indexer flags (e.g., "freeleech", "internal", "scene")
    /// Used by custom format IndexerFlag specifications
    /// </summary>
    public string? IndexerFlags { get; set; }

    /// <summary>
    /// Total calculated score (quality + custom formats)
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Match score for event-to-release matching (0-100)
    /// Higher score = better match to the event being searched for.
    /// Based on: team names, location, date, round number, etc.
    /// </summary>
    public int MatchScore { get; set; }

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

    /// <summary>
    /// Size-based score for tiebreaking (Sonarr-style)
    /// Higher score = closer to preferred size OR larger file when no preferred set
    /// Uses 200MB rounding chunks to prevent minor differences affecting selection
    /// </summary>
    public long SizeScore { get; set; }

    /// <summary>
    /// Whether this release is on the blocklist (still shown but marked)
    /// </summary>
    public bool IsBlocklisted { get; set; } = false;

    /// <summary>
    /// Reason for blocklisting (if blocklisted)
    /// </summary>
    public string? BlocklistReason { get; set; }

    /// <summary>
    /// The part of the event this release was searched for (for multi-part events like fight cards)
    /// E.g., "Prelims", "Main Card", "Early Prelims"
    /// Null if search was for the whole event
    /// </summary>
    public string? Part { get; set; }

    /// <summary>
    /// Whether this is a pack release (e.g., NFL-2025-Week15 containing multiple games)
    /// Pack releases require special handling - they contain files for multiple events
    /// </summary>
    public bool IsPack { get; set; } = false;
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
    public string? TorrentInfoHash { get; set; } // For torrent blocking (optional for Usenet)
    public string? Indexer { get; set; }
    public string? Protocol { get; set; } // "Usenet" or "Torrent"
    public BlocklistReason Reason { get; set; }
    public string? Message { get; set; }
    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The part of the event (for multi-part events like fight cards)
    /// Null if applies to the whole event
    /// </summary>
    public string? Part { get; set; }
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

/// <summary>
/// Status of pending import (external download needing manual intervention)
/// </summary>
public enum PendingImportStatus
{
    Pending,        // Awaiting user action
    Importing,      // Currently being imported
    Completed,      // Successfully imported
    Rejected        // User rejected this import
}

/// <summary>
/// Pending import - external download from download client that needs manual mapping
/// Similar to Sonarr's "Manual Import" queue for unrecognized downloads
/// </summary>
public class PendingImport
{
    public int Id { get; set; }

    /// <summary>
    /// Download client that reported this file
    /// </summary>
    public int DownloadClientId { get; set; }
    public DownloadClient? DownloadClient { get; set; }

    /// <summary>
    /// Download ID from client (for tracking/removal)
    /// </summary>
    public required string DownloadId { get; set; }

    /// <summary>
    /// Original filename/title from download client
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// File path on disk (from download client)
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Quality detected from filename/file
    /// </summary>
    public string? Quality { get; set; }

    /// <summary>
    /// Quality score calculated from detected quality
    /// </summary>
    public int QualityScore { get; set; }

    /// <summary>
    /// Current status of this import
    /// </summary>
    public PendingImportStatus Status { get; set; } = PendingImportStatus.Pending;

    /// <summary>
    /// Error message if import failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// User-selected or AI-suggested event ID for mapping
    /// </summary>
    public int? SuggestedEventId { get; set; }
    public Event? SuggestedEvent { get; set; }

    /// <summary>
    /// User-selected or AI-suggested part for multi-part episodes (Fighting sports)
    /// </summary>
    public string? SuggestedPart { get; set; }

    /// <summary>
    /// Confidence score for the suggestion (0-100)
    /// </summary>
    public int SuggestionConfidence { get; set; }

    /// <summary>
    /// When this was detected/added
    /// </summary>
    public DateTime Detected { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When user took action (completed or rejected)
    /// </summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// Protocol (Torrent or Usenet)
    /// </summary>
    public string? Protocol { get; set; }

    /// <summary>
    /// Torrent info hash for tracking
    /// </summary>
    public string? TorrentInfoHash { get; set; }

    /// <summary>
    /// True if this is a multi-file pack (e.g., NFL-2025-Week15)
    /// Packs require special handling to import multiple events from one download
    /// </summary>
    public bool IsPack { get; set; }

    /// <summary>
    /// Number of video files in this download (for packs)
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Number of events this pack matches (populated after pack scan)
    /// </summary>
    public int MatchedEventsCount { get; set; }
}

/// <summary>
/// External download information from download client
/// Used for detecting downloads added outside of Sportarr
/// </summary>
public class ExternalDownloadInfo
{
    /// <summary>
    /// Download client's ID for this download (hash for torrents, nzo_id for usenet)
    /// </summary>
    public required string DownloadId { get; set; }

    /// <summary>
    /// Download title/name
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Category assigned in download client
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// Full path where download is saved
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// Download size in bytes
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Is download completed?
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Protocol (Torrent or Usenet)
    /// </summary>
    public string? Protocol { get; set; }

    /// <summary>
    /// Torrent info hash (torrent only)
    /// </summary>
    public string? TorrentInfoHash { get; set; }

    /// <summary>
    /// When download was completed
    /// </summary>
    public DateTime? CompletedDate { get; set; }
}

/// <summary>
/// Result of adding a download to a download client
/// </summary>
public class AddDownloadResult
{
    public bool Success { get; set; }
    public string? DownloadId { get; set; }
    public string? ErrorMessage { get; set; }
    public AddDownloadErrorType ErrorType { get; set; } = AddDownloadErrorType.None;

    public static AddDownloadResult Succeeded(string downloadId) => new()
    {
        Success = true,
        DownloadId = downloadId
    };

    public static AddDownloadResult Failed(string errorMessage, AddDownloadErrorType errorType = AddDownloadErrorType.Unknown) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        ErrorType = errorType
    };
}

/// <summary>
/// Type of error when adding a download fails
/// </summary>
public enum AddDownloadErrorType
{
    None,
    Unknown,
    LoginFailed,
    InvalidTorrent,
    TorrentRejected,
    ConnectionFailed,
    Timeout,
    RateLimited
}

/// <summary>
/// History of grabbed releases - stores original release info for re-grabbing
/// This is a Sportarr-exclusive feature not available in Sonarr/Radarr.
/// When users lose their media files but keep their database, they can
/// re-grab the exact same releases they originally downloaded.
/// </summary>
public class GrabHistory
{
    public int Id { get; set; }

    /// <summary>
    /// Event this grab was for
    /// </summary>
    public int EventId { get; set; }
    public Event? Event { get; set; }

    /// <summary>
    /// Original release title from the indexer
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// The indexer name that provided this release
    /// </summary>
    public required string Indexer { get; set; }

    /// <summary>
    /// Indexer ID for re-grabbing (may be null if indexer was deleted)
    /// </summary>
    public int? IndexerId { get; set; }

    /// <summary>
    /// Download URL (torrent file URL, magnet link, or NZB URL)
    /// </summary>
    public required string DownloadUrl { get; set; }

    /// <summary>
    /// GUID from the indexer (for deduplication)
    /// </summary>
    public required string Guid { get; set; }

    /// <summary>
    /// Protocol: "Torrent" or "Usenet"
    /// </summary>
    public required string Protocol { get; set; }

    /// <summary>
    /// Torrent info hash (for torrents - allows magnet link fallback)
    /// </summary>
    public string? TorrentInfoHash { get; set; }

    /// <summary>
    /// Release size in bytes
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Quality string (e.g., "1080p WEB-DL")
    /// </summary>
    public string? Quality { get; set; }

    /// <summary>
    /// Video codec (H.264, HEVC, etc.)
    /// </summary>
    public string? Codec { get; set; }

    /// <summary>
    /// Video source (WEB-DL, BluRay, etc.)
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Quality score at time of grab
    /// </summary>
    public int QualityScore { get; set; }

    /// <summary>
    /// Custom format score at time of grab
    /// </summary>
    public int CustomFormatScore { get; set; }

    /// <summary>
    /// Part name for multi-part events (e.g., "Prelims", "Main Card")
    /// Null if not a multi-part grab
    /// </summary>
    public string? PartName { get; set; }

    /// <summary>
    /// When this release was grabbed
    /// </summary>
    public DateTime GrabbedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the download was successfully imported
    /// </summary>
    public bool WasImported { get; set; } = false;

    /// <summary>
    /// When the file was imported (if applicable)
    /// </summary>
    public DateTime? ImportedAt { get; set; }

    /// <summary>
    /// Whether the media file currently exists on disk
    /// Updated by disk scan service
    /// </summary>
    public bool FileExists { get; set; } = false;

    /// <summary>
    /// Last time we attempted to re-grab this release
    /// Used to prevent spam re-grabs
    /// </summary>
    public DateTime? LastRegrabAttempt { get; set; }

    /// <summary>
    /// Number of times this has been re-grabbed
    /// </summary>
    public int RegrabCount { get; set; } = 0;

    /// <summary>
    /// Download client ID that was used for the original grab
    /// </summary>
    public int? DownloadClientId { get; set; }

    /// <summary>
    /// Whether this grab has been superseded by a newer grab for the same event+part.
    /// When a new grab is made (upgrade or replacement), older grabs are marked as superseded
    /// so users don't accidentally re-grab an old/replaced file.
    /// </summary>
    public bool Superseded { get; set; } = false;
}

/// <summary>
/// Cached release from RSS sync or search results.
/// This is the core of the RSS-first search strategy:
/// - RSS feeds are polled periodically and releases cached here
/// - When searching for an event, we query the local cache first (instant, no API calls)
/// - Active indexer search only happens as a fallback
///
/// Benefits:
/// - 1 API call per indexer per RSS sync vs N calls per event search
/// - All fuzzy matching happens locally with no rate limits
/// - Releases are discovered as they appear, not when you search
/// </summary>
public class ReleaseCache
{
    public int Id { get; set; }

    /// <summary>
    /// Original release title from the indexer (exactly as returned)
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Normalized title for searching (lowercase, periods/dashes removed, diacritics stripped)
    /// </summary>
    public required string NormalizedTitle { get; set; }

    /// <summary>
    /// Space-separated searchable terms extracted from title
    /// Includes: original terms, location aliases, demonyms, common substitutions
    /// Example: "formula1 2025 round19 united states usa american cota austin gp"
    /// This enables efficient LIKE queries for fuzzy matching
    /// </summary>
    public required string SearchTerms { get; set; }

    /// <summary>
    /// GUID from the indexer (for deduplication)
    /// </summary>
    public required string Guid { get; set; }

    /// <summary>
    /// Download URL (torrent file URL, magnet link, or NZB URL)
    /// </summary>
    public required string DownloadUrl { get; set; }

    /// <summary>
    /// Info/details page URL (optional)
    /// </summary>
    public string? InfoUrl { get; set; }

    /// <summary>
    /// Name of the indexer that provided this release
    /// </summary>
    public required string Indexer { get; set; }

    /// <summary>
    /// Indexer ID (for faster joins)
    /// </summary>
    public int? IndexerId { get; set; }

    /// <summary>
    /// Protocol: "Torrent" or "Usenet"
    /// </summary>
    public required string Protocol { get; set; }

    /// <summary>
    /// Torrent info hash (for blocklist checking and magnet fallback)
    /// </summary>
    public string? TorrentInfoHash { get; set; }

    /// <summary>
    /// Release size in bytes
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Detected quality (e.g., "WEBDL-1080p", "HDTV-720p")
    /// </summary>
    public string? Quality { get; set; }

    /// <summary>
    /// Video source (WEB-DL, BluRay, HDTV, etc.)
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Video codec (H.264, HEVC, AV1, etc.)
    /// </summary>
    public string? Codec { get; set; }

    /// <summary>
    /// Detected language from title
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Number of seeders (for torrents)
    /// </summary>
    public int? Seeders { get; set; }

    /// <summary>
    /// Number of leechers (for torrents)
    /// </summary>
    public int? Leechers { get; set; }

    /// <summary>
    /// When the release was published by the indexer
    /// </summary>
    public DateTime PublishDate { get; set; }

    /// <summary>
    /// Indexer flags (e.g., "freeleech", "internal", "scene")
    /// </summary>
    public string? IndexerFlags { get; set; }

    /// <summary>
    /// When this release was added to the cache
    /// </summary>
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this cache entry expires (for cleanup)
    /// Default: 7 days from cache time for sports content
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Whether this release was from RSS sync (true) or active search (false)
    /// RSS releases are trusted more for timing
    /// </summary>
    public bool FromRss { get; set; } = true;

    /// <summary>
    /// Detected year from the release title (for date-based matching)
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Detected month from the release title (for date-based matching)
    /// </summary>
    public int? Month { get; set; }

    /// <summary>
    /// Detected day from the release title (for date-based matching)
    /// </summary>
    public int? Day { get; set; }

    /// <summary>
    /// Detected round/week number from the release title (for motorsport/weekly sports)
    /// </summary>
    public int? RoundNumber { get; set; }

    /// <summary>
    /// Detected sport/league prefix (e.g., "Formula1", "UFC", "NFL", "NBA")
    /// </summary>
    public string? SportPrefix { get; set; }

    /// <summary>
    /// Whether this is a pack release (e.g., NFL-2025-Week15 containing multiple games)
    /// </summary>
    public bool IsPack { get; set; } = false;
}
