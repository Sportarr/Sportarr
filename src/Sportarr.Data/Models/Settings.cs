using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sportarr.Api.Models;

// Root Settings Container
public class AppSettings
{
    [Key]
    public int Id { get; set; } = 1; // Single row for app settings

    // Serialized JSON for each settings category
    public string HostSettings { get; set; } = "{}";
    public string SecuritySettings { get; set; } = "{}";
    public string ProxySettings { get; set; } = "{}";
    public string LoggingSettings { get; set; } = "{}";
    public string AnalyticsSettings { get; set; } = "{}";
    public string BackupSettings { get; set; } = "{}";
    public string UpdateSettings { get; set; } = "{}";
    public string UISettings { get; set; } = "{}";
    public string MediaManagementSettings { get; set; } = "{}";
    public string TrashSyncSettings { get; set; } = "{}";
    public string DevelopmentSettings { get; set; } = "{}";

    // Download handling settings (stored directly for frontend compatibility)
    public bool EnableCompletedDownloadHandling { get; set; } = true;
    // Note: RemoveCompletedDownloads and RemoveFailedDownloads are now per-client settings (per-client)
    // Configure in each Download Client's settings instead of globally here
    public int CheckForFinishedDownloadInterval { get; set; } = 1;
    public bool RedownloadFailedDownloads { get; set; } = true;
    public bool RedownloadFailedFromInteractiveSearch { get; set; } = true;

    // Stored in config.xml (Config.StalledDownloadTimeoutMinutes), not in
    // this table - NotMapped keeps it on the JSON wire contract without a
    // schema change.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int StalledDownloadTimeoutMinutes { get; set; } = 60;

    // Search Queue Management (Huntarr-style queue threshold pause)
    public int MaxDownloadQueueSize { get; set; } = -1; // -1 = no limit
    public int SearchSleepDuration { get; set; } = 900; // seconds between search cycles

    // Hub changes feed cursor. The HubChangesPollerService stores the last
    // consumed feed sequence here so polling resumes where it left off
    // across restarts. 0 = never polled (feed answers with resync + head).
    public long HubChangesCursor { get; set; } = 0;

    // Indexer Options (advanced settings)
    public int IndexerRetention { get; set; } = 0; // days - releases older than this won't be grabbed (0 = disabled)
    public int RssSyncInterval { get; set; } = 60; // minutes between RSS sync cycles
    public bool PreferIndexerFlags { get; set; } = true; // prefer releases with special indexer flags
    public int SearchCacheDuration { get; set; } = 120; // seconds to cache search results
    public int IndexerMinimumAgeMinutes { get; set; } = 0; // minutes to wait after a release is posted before grabbing it

    // IPTV / EPG auto-refresh intervals in hours (0 = disabled).
    // Stored in config.xml (Config.*); NotMapped keeps them on the settings
    // JSON contract without a schema change.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int IptvPlaylistRefreshHours { get; set; } = 168;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int EpgRefreshHours { get; set; } = 48;

    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}

// Host Configuration
public class HostSettings
{
    public string BindAddress { get; set; } = "*";
    public int Port { get; set; } = 1867; // Sportarr's default port
    public string UrlBase { get; set; } = "";
    public string InstanceName { get; set; } = "Sportarr";
    public bool EnableSsl { get; set; } = false;
    public int SslPort { get; set; } = 1868; // Sportarr's default SSL port
    public string SslCertPath { get; set; } = "";
    public string SslCertPassword { get; set; } = "";
}

// Security Configuration
public class SecuritySettings
{
    public string AuthenticationMethod { get; set; } = "none";
    public string AuthenticationRequired { get; set; } = "disabledForLocalAddresses";
    public string ApiKey { get; set; } = "";
    public string CertificateValidation { get; set; } = "enabled";

    // Stored credentials (hashed)
    public string Username { get; set; } = "";
    public string Password { get; set; } = ""; // Plaintext when setting, cleared after hashing
    public string PasswordHash { get; set; } = ""; // PBKDF2 hash
    public string PasswordSalt { get; set; } = ""; // Base64 encoded salt
    public int PasswordIterations { get; set; } = 10000; // PBKDF2 iterations
}

// Proxy Configuration
public class ProxySettings
{
    public bool UseProxy { get; set; } = false;
    public string ProxyType { get; set; } = "http";
    public string ProxyHostname { get; set; } = "";
    public int ProxyPort { get; set; } = 8080;
    public string ProxyUsername { get; set; } = "";
    public string ProxyPassword { get; set; } = "";
    public string ProxyBypassFilter { get; set; } = "";
    public bool ProxyBypassLocalAddresses { get; set; } = true;
}

// Logging Configuration
public class LoggingSettings
{
    public string LogLevel { get; set; } = "info";
}

// Analytics Configuration
public class AnalyticsSettings
{
    public bool SendAnonymousUsageData { get; set; } = true;
}

// Backup Configuration
public class BackupSettings
{
    public string BackupFolder { get; set; } = "";
    public int BackupInterval { get; set; } = 7;
    public int BackupRetention { get; set; } = 28;
}

// Update Configuration
public class UpdateSettings
{
    public string Branch { get; set; } = "main";
    public bool Automatic { get; set; } = false;
    public string Mechanism { get; set; } = "docker";
    public string ScriptPath { get; set; } = "";
}

// Development Configuration (hidden settings for testing)
public class DevelopmentSettings
{
    /// <summary>
    /// Custom metadata API URL for development/testing purposes.
    /// When set, Sportarr will use this URL instead of the default sportarr.net API.
    /// Empty string uses the default API.
    /// </summary>
    public string CustomMetadataApiUrl { get; set; } = "";
}

// UI Configuration
public class UISettings
{
    // Calendar
    public string FirstDayOfWeek { get; set; } = "sunday";
    public string CalendarWeekColumnHeader { get; set; } = "ddd M/D";

    // Dates
    public string ShortDateFormat { get; set; } = "MMM D YYYY";
    public string LongDateFormat { get; set; } = "dddd, MMMM D YYYY";
    public string TimeFormat { get; set; } = "h:mm A";
    public bool ShowRelativeDates { get; set; } = true;

    // Style
    public string Theme { get; set; } = "auto";
    public bool EnableColorImpairedMode { get; set; } = false;

    // Language
    public string UILanguage { get; set; } = "en";

    // Display
    public string EventViewMode { get; set; } = "auto";
    public bool ShowUnknownLeagueItems { get; set; } = false;
    public bool ShowEventPath { get; set; } = false;

    // Timezone
    public string TimeZone { get; set; } = ""; // Empty = use system timezone, otherwise IANA timezone ID (e.g., "America/New_York")
}

// Media Management Configuration
public class MediaManagementSettings
{
    public int Id { get; set; }

    // Root folders are NOT stored here — they live in their own RootFolders
    // table (the single source of truth the UI writes to). Load them via
    // RootFolderLoader where needed.

    // File Management
    public bool RenameEvents { get; set; } = false;
    public bool RenameFiles { get; set; } = true;

    // Stored in config.xml (Config.DownloadPropersAndRepacks); NotMapped
    // keeps it on the media-management JSON contract without a schema change.
    // Values: preferAndUpgrade | doNotUpgrade | doNotPrefer.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string DownloadPropersAndRepacks { get; set; } = "preferAndUpgrade";
    public bool ReplaceIllegalCharacters { get; set; } = true;
    public bool EnableMultiPartEpisodes { get; set; } = true; // Detect and name multi-part episodes for Fighting sports
    // {Sportarr Id} stamps the canonical event id ({sportarr-ev-XXXXXXX})
    // into every filename so imports, rescans, and manual moves match
    // exactly forever (docs/RELEASE_NAMING.md). Empty for legacy rows.
    public string StandardFileFormat { get; set; } = "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full} {Sportarr Id}";

    // Folders - Cascading options for granular control
    // CreateLeagueFolders: Creates folders like /UFC/, /Premier League/
    // CreateSeasonFolders: Creates folders like /UFC/Season 2024/ (requires CreateLeagueFolders)
    // CreateEventFolders: Creates folders like /UFC/Season 2024/UFC 310/ (requires CreateSeasonFolders)
    public bool CreateLeagueFolders { get; set; } = true;
    public bool CreateSeasonFolders { get; set; } = true;
    public bool CreateEventFolders { get; set; } = false; // Default false - events go in season folder
    public string LeagueFolderFormat { get; set; } = "{Series}";
    public string SeasonFolderFormat { get; set; } = "Season {Season}";
    public string EventFolderFormat { get; set; } = "{Event Title} ({Year}-{Month}-{Day}) E{Episode}";
    public bool DeleteEmptyFolders { get; set; } = false;

    /// <summary>
    /// Unmonitor events whose files were deleted from disk by something
    /// other than Sportarr (cron cleanup, media server delete-after-watch,
    /// manual deletion), so deliberately removed games aren't re-downloaded.
    /// Sportarr's own upgrades and renames are unaffected: their replacement
    /// files are tracked before the old file's deletion is observed.
    /// </summary>
    public bool UnmonitorDeletedEvents { get; set; } = false;
    // ReorganizeFolders: When true, file rename operations will also move files to match current folder settings
    // When false, rename only changes filenames without moving files to different folders
    public bool ReorganizeFolders { get; set; } = false;

    // Legacy property for backward compatibility - maps to CreateLeagueFolders && CreateSeasonFolders
    [Obsolete("Use CreateLeagueFolders, CreateSeasonFolders, and CreateEventFolders instead")]
    [JsonIgnore] // Exclude from JSON serialization to avoid confusion with new granular properties
    public bool CreateEventFolder
    {
        get => CreateLeagueFolders && CreateSeasonFolders;
        set
        {
            CreateLeagueFolders = value;
            CreateSeasonFolders = value;
        }
    }

    // Importing
    public bool CopyFiles { get; set; } = false;
    public bool SkipFreeSpaceCheck { get; set; } = false;
    public long MinimumFreeSpace { get; set; } = 100;
    public bool UseHardlinks { get; set; } = true;
    public bool ImportExtraFiles { get; set; } = false;
    public string ExtraFileExtensions { get; set; } = "srt,nfo";

    /// <summary>
    /// Comma-separated list of file extensions the user wants to count
    /// against an indexer's FailDownloads=UserDefinedExtensions policy.
    /// e.g. ".nfo, .url, txt". Leave blank to disable the user-defined
    /// category (the Executables / PotentiallyDangerous categories are
    /// hardcoded and unaffected by this).
    /// </summary>
    public string? UserRejectedExtensions { get; set; }

    // Permissions
    public bool SetPermissions { get; set; } = false;
    public string FileChmod { get; set; } = "644";
    public string ChmodFolder { get; set; } = "755";
    public string ChownUser { get; set; } = string.Empty;
    public string ChownGroup { get; set; } = "";

    // Note: RemoveCompletedDownloads and RemoveFailedDownloads are now per-client settings (per-client)
    // Configure in each Download Client's settings instead of in Media Management

    // Advanced
    public string ChangeFileDate { get; set; } = "None";
    public string RecycleBin { get; set; } = "";
    public int RecycleBinCleanup { get; set; } = 7;

    // Stored in config.xml (Config.WatchFolders); NotMapped keeps it on the
    // media-management JSON contract without a schema change.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<string> WatchFolders { get; set; } = new();

    // RETIRED: event retention moved to League.RetentionDays (per-league).
    // The columns stay (dropping them would churn the migration snapshot for
    // nothing) but nothing reads or writes them anymore.
    public bool EnableEventRetention { get; set; } = false;
    public int EventRetentionDays { get; set; } = 30;

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}

// Root Folder Model. Only Id, Path, Created, and the optional
// DefaultQualityProfileId / DefaultDownloadClientCategory are persisted —
// Accessible / FreeSpace / TotalSpace are recomputed live every time
// the API is read so the UI can't show stale "200 GiB free" while the
// disk is actually full. NotMapped on those keeps the JSON response
// shape unchanged for callers but tells EF to ignore them on read /
// write so we can't accidentally trust the persisted value.
//
// The two Default* columns are a Sportarr-specific extension: users
// often dedicate fast disks to current sports and archive disks to old
// replays, so each root can hint a Quality Profile and a Download
// Client category to apply to leagues bound under it. Both are
// optional — leagues fall back to their explicit setting (or the
// global default) when the root has nothing pinned.
public class RootFolder
{
    public int Id { get; set; }
    public required string Path { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional default Quality Profile applied to leagues bound to this
    /// root folder when the user doesn't pick one explicitly at add time.
    /// </summary>
    public int? DefaultQualityProfileId { get; set; }

    /// <summary>
    /// Optional download-client category override applied at grab time
    /// for any event in a league bound to this root. When set, this
    /// replaces the download client's configured Category for that grab.
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? DefaultDownloadClientCategory { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool Accessible { get; set; } = true;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public long FreeSpace { get; set; } = 0;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public long TotalSpace { get; set; } = 0;
}

// Import History
public class ImportHistory
{
    public int Id { get; set; }
    public int? EventId { get; set; }  // Nullable to allow history to persist after event deletion
    public Event? Event { get; set; }
    public int? DownloadQueueItemId { get; set; }
    public DownloadQueueItem? DownloadQueueItem { get; set; }
    public required string SourcePath { get; set; }
    public required string DestinationPath { get; set; }
    public required string Quality { get; set; }
    public long Size { get; set; }
    public ImportDecision Decision { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The part of the event (for multi-part events like fight cards: "Early Prelims", "Prelims", "Main Card")
    /// Null if not a multi-part event or applies to the whole event
    /// </summary>
    public string? Part { get; set; }
}

// Import decision for a file
public enum ImportDecision
{
    Approved,
    Rejected,
    AlreadyImported,
    Upgraded
}

// Parsed media file information
public class ParsedFileInfo
{
    public required string EventTitle { get; set; }
    public string? Quality { get; set; }
    public string? ReleaseGroup { get; set; }
    public string? Resolution { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public string? Source { get; set; }
    public DateTime? AirDate { get; set; }
    public string? Edition { get; set; }
    public string? Language { get; set; }
    public bool IsProperOrRepack { get; set; }
    /// <summary>Audio-stream language tags read from ffprobe (e.g. ["eng", "spa"]).
    /// Populated by MediaFileInspector when filename parsing comes up short on
    /// the Resolution / Source fields. Empty list if ffprobe wasn't run or
    /// the file had no language tags.</summary>
    public List<string> DetectedLanguages { get; set; } = new();

    /// <summary>Canonical event id ("ev-XXXXXXX") from an id token in the
    /// name, or from the file's embedded SPORTARR tag when the name carries
    /// none (docs/RELEASE_NAMING.md). Authoritative for matching.</summary>
    public string? SportarrEventId { get; set; }

    /// <summary>Canonical league id ("lg-XXXXXX"), the pack-release
    /// equivalent of <see cref="SportarrEventId"/>.</summary>
    public string? SportarrLeagueId { get; set; }
}

// File naming tokens and their replacements
public class FileNamingTokens
{
    public string EventTitle { get; set; } = string.Empty;
    public string EventTitleThe { get; set; } = string.Empty;
    public DateTime? AirDate { get; set; }
    public string Quality { get; set; } = string.Empty;
    public string QualityFull { get; set; } = string.Empty;
    public string ReleaseGroup { get; set; } = string.Empty;
    public string OriginalTitle { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;

    // Plex TV show structure tokens
    public string Series { get; set; } = string.Empty;  // League name
    public string Season { get; set; } = string.Empty;  // Season year (2024)
    public string Episode { get; set; } = string.Empty; // Episode number (01, 02, etc.)
    public string Part { get; set; } = string.Empty;    // Multi-part suffix (pt1, pt2, pt3) for fight card segments
    public string PartName { get; set; } = string.Empty; // Human part label suffix (" - Prelims", " - Main Card"); empty for single-part files. Separator embedded, same convention as Part.
    public string CustomFormats { get; set; } = string.Empty; // Space-joined names of matched formats flagged IncludeCustomFormatWhenRenaming
    public string SportarrId { get; set; } = string.Empty; // Canonical event id (ev-XXXXXXX); {Sportarr Id} renders it as {sportarr-ev-XXXXXXX} per docs/RELEASE_NAMING.md
}

// Notification Model (stored separately with Tags)
public class Notification
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Implementation { get; set; } = "";
    public bool Enabled { get; set; } = true;

    // Serialized configuration as JSON
    public string ConfigJson { get; set; } = "{}";

    // Tags for scoping to specific leagues
    public List<int> Tags { get; set; } = new();

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}
