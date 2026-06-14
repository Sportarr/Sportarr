using System.Xml.Serialization;

namespace Sportarr.Api.Models;

/// <summary>
/// Main configuration file (config.xml).
/// </summary>
[XmlRoot("Config")]
public class Config
{
    // Security
    public string ApiKey { get; set; } = Guid.NewGuid().ToString("N");
    public string AuthenticationMethod { get; set; } = "None"; // None, Basic, Forms
    public string AuthenticationRequired { get; set; } = "DisabledForLocalAddresses";
    public bool AuthenticationEnabled { get; set; } = false;
    public string Username { get; set; } = "";
    public string Password { get; set; } = ""; // Stored hashed in PasswordHash; this field exists for clients that POST plaintext
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public int PasswordIterations { get; set; } = 10000;
    public string CertificateValidation { get; set; } = "Enabled";
    public string SslCertHash { get; set; } = "";

    // Host
    public string BindAddress { get; set; } = "*";
    public int Port { get; set; } = 1867; // Sportarr's default port
    public string UrlBase { get; set; } = "";
    public string InstanceName { get; set; } = "Sportarr";
    public bool EnableSsl { get; set; } = false;
    public int SslPort { get; set; } = 1868; // Sportarr's default SSL port
    public string SslCertPath { get; set; } = "";
    public string SslCertPassword { get; set; } = "";
    public bool LaunchBrowser { get; set; } = false; // Open the web UI in the default browser on startup

    // Proxy
    public bool UseProxy { get; set; } = false;
    public string ProxyType { get; set; } = "Http";
    public string ProxyHostname { get; set; } = "";
    public int ProxyPort { get; set; } = 8080;
    public string ProxyUsername { get; set; } = "";
    public string ProxyPassword { get; set; } = "";
    public string ProxyBypassFilter { get; set; } = "";
    public bool ProxyBypassLocalAddresses { get; set; } = true;

    // Logging
    public string LogLevel { get; set; } = "Info"; // Trace, Debug, Info, Warn, Error, Fatal
    public string ConsoleLogLevel { get; set; } = ""; // Separate log level for console (Docker). Empty = use LogLevel
    public int LogSizeLimit { get; set; } = 1; // Maximum log file size in MB before rotation

    // Analytics
    public bool SendAnonymousUsageData { get; set; } = false;
    public bool AnalyticsEnabled { get; set; } = false;

    // Backup
    public string BackupFolder { get; set; } = "";
    public int BackupInterval { get; set; } = 7;
    public int BackupRetention { get; set; } = 28;

    // Update
    public string Branch { get; set; } = "main";
    public bool UpdateAutomatically { get; set; } = false;
    public string UpdateMechanism { get; set; } = "Docker"; // BuiltIn, Script, External, Docker, Apt
    public string UpdateScriptPath { get; set; } = ""; // Path to custom update script

    // UI
    public string FirstDayOfWeek { get; set; } = "Sunday";
    public string CalendarWeekColumnHeader { get; set; } = "ddd M/D";
    public string ShortDateFormat { get; set; } = "MMM D YYYY";
    public string LongDateFormat { get; set; } = "dddd, MMMM D YYYY";
    public string TimeFormat { get; set; } = "h:mm A";
    public bool ShowRelativeDates { get; set; } = true;
    public string Theme { get; set; } = "Auto";
    public bool EnableColorImpairedMode { get; set; } = false;
    public string UILanguage { get; set; } = "en";
    public string EventViewMode { get; set; } = "auto";
    public bool ShowUnknownLeagueItems { get; set; } = false;
    public bool ShowEventPath { get; set; } = false;
    public string TimeZone { get; set; } = ""; // Empty = use system timezone, otherwise IANA timezone ID (e.g., "America/New_York")

    // Media Management
    public bool RenameEvents { get; set; } = false;
    public bool ReplaceIllegalCharacters { get; set; } = true;
    public bool EnableMultiPartEpisodes { get; set; } = true; // Detect and name multi-part episodes (Early Prelims, Prelims, Main Card) for Fighting sports
    public string SeriesFolderFormat { get; set; } = "{Series}";
    public string SeasonFolderFormat { get; set; } = "Season {Season}";
    public bool CreateEventFolders { get; set; } = true;
    public bool DeleteEmptyFolders { get; set; } = false;
    public bool SkipFreeSpaceCheck { get; set; } = false;
    public int MinimumFreeSpace { get; set; } = 100;
    public bool UseHardlinks { get; set; } = true;
    public bool ImportExtraFiles { get; set; } = false;
    public string ExtraFileExtensions { get; set; } = "srt,nfo";
    public string ChangeFileDate { get; set; } = "None";
    public string RecycleBin { get; set; } = "";
    public int RecycleBinCleanup { get; set; } = 7;
    public bool SetPermissions { get; set; } = false;
    public string ChmodFolder { get; set; } = "755";
    public string ChownGroup { get; set; } = "";

    // Download Client Settings
    public string DownloadClientWorkingFolders { get; set; } = "_UNPACK_,_FAILED_";
    public bool EnableCompletedDownloadHandling { get; set; } = true;
    // Note: RemoveCompletedDownloads and RemoveFailedDownloads are now per-client settings
    // See DownloadClient.RemoveCompletedDownloads and DownloadClient.RemoveFailedDownloads
    public int CheckForFinishedDownloadInterval { get; set; } = 1; // minutes
    public bool RedownloadFailedDownloads { get; set; } = true;
    public bool RedownloadFailedFromInteractiveSearch { get; set; } = true;

    // Search Settings
    public int SearchCacheDuration { get; set; } = 300; // seconds (5 min) - cache raw indexer results (prevents duplicate API calls for multi-part events, same-year searches, and different sessions at same location)

    // Indexer Settings
    public int IndexerRetention { get; set; } = 0; // days - releases older than this won't be grabbed (0 = disabled)

    /// <summary>
    /// Grace period in days before the disk scanner hard-deletes an EventFile
    /// row whose path has been continuously missing. Protects restore-to-new-
    /// server flows, transient NAS unmounts, and similar scenarios. Default 30
    /// days. Set to 0 to never auto-delete (user prunes manually).
    /// </summary>
    public int EventFileMissingDeleteAfterDays { get; set; } = 30;
    public bool PreferIndexerFlags { get; set; } = true; // prefer releases with special indexer flags (Freeleech, Scene, etc.)

    // Queue Threshold Settings (Huntarr-style)
    // Pause searching when download queue exceeds threshold to prevent overloading
    public int MaxDownloadQueueSize { get; set; } = -1; // -1 = no limit, otherwise pause when queue exceeds this
    // OBSOLETE: never wired to a service. The "automatic search cycle" cadence is
    // now BacklogSearchIntervalMinutes (hardcoded default 6h). Field retained so
    // existing config.xml files don't blow up on deserialization; do not read.
    [Obsolete("Use BacklogSearchIntervalMinutes. Field exists only for backwards compatibility.")]
    public int SearchSleepDuration { get; set; } = 900; // seconds between search cycles (default 15 minutes, Huntarr pattern)

    // RSS Sync Settings.
    // RSS Sync pulls the latest releases from indexer RSS feeds and matches against monitored events locally.
    // This is MUCH more efficient than searching per-event:
    // - Old approach: N queries per sync (one per monitored event) = thousands of queries/day
    // - New approach: M queries per sync (one per RSS-enabled indexer) = 24-100 queries/day
    public int RssSyncInterval { get; set; } = 15; // minutes between RSS sync cycles (default 15, min 10, max 120)
    public int MaxRssReleasesPerIndexer { get; set; } = 500; // max releases to fetch per indexer RSS feed (increased from 100 to avoid missing releases)
    public int RssReleaseAgeLimit { get; set; } = 14; // days - only consider releases posted within this window (sports releases are time-sensitive)

    // Backlog Search Settings — scheduled missing/cutoff-unmet search.
    // RSS only catches recent releases. The backlog service walks past-aired monitored
    // events that are missing (or below cutoff) and runs targeted indexer searches for
    // them. Honors League.SearchForMissingEvents and League.SearchForCutoffUnmetEvents.
    public int BacklogSearchIntervalMinutes { get; set; } = 360; // 6 hours between backlog passes
    public int BacklogSearchMaxConcurrent { get; set; } = 3; // SemaphoreSlim cap so backlog doesn't hammer indexers
    public int BacklogSearchMaxAgeDays { get; set; } = 365; // skip events older than this on backlog pass (1y by default; 0 = no cap)
    public bool BacklogSearchEnabled { get; set; } = true;

    // Indexer minimum age.
    // Wait this many minutes after a release was posted to the indexer before
    // grabbing it. Useful in slow Usenet groups where posts can be partial or
    // get pulled shortly after upload, and on torrent indexers where letting
    // a few seeders attach first improves grab reliability.
    public int IndexerMinimumAgeMinutes { get; set; } = 0;

    // Auto-search retry backoff schedule (minutes), one entry per retry attempt.
    // Default is an exponential pattern. Comma-separated string for easy editing.
    public string AutoSearchRetryBackoffMinutes { get; set; } = "30,60,120,240,480"; // 30m, 1h, 2h, 4h, 8h

    // DVR Settings
    public int DvrDefaultProfileId { get; set; } = 1; // Default quality profile ID (1 = Copy/No Transcoding) - DEPRECATED, use encoding settings below
    public string DvrRecordingPath { get; set; } = ""; // Root path for DVR recordings (empty = use root folder)
    public string DvrFileNamingPattern { get; set; } = "{Title} - {Date}"; // File naming pattern
    public int DvrPrePaddingMinutes { get; set; } = 5; // Minutes to start recording before scheduled event
    public int DvrPostPaddingMinutes { get; set; } = 30; // Minutes to continue recording after scheduled end
    public int DvrMaxConcurrentRecordings { get; set; } = 0; // Maximum concurrent recordings (0 = unlimited)
    public bool DvrDeleteAfterImport { get; set; } = false; // Delete recordings after successful import
    public int DvrRecordingRetentionDays { get; set; } = 0; // Days to keep recordings (0 = never delete)
    public int DvrHardwareAcceleration { get; set; } = 99; // HardwareAcceleration enum (99 = Auto)
    public string DvrFfmpegPath { get; set; } = ""; // Custom FFmpeg path (empty = use system PATH)
    public bool DvrEnableReconnect { get; set; } = true; // Enable stream reconnection on failures
    public int DvrMaxReconnectAttempts { get; set; } = 5; // Maximum reconnection attempts
    public int DvrReconnectDelaySeconds { get; set; } = 5; // Delay between reconnection attempts

    /// <summary>
    /// What happens when scheduling a new recording would push an
    /// IPTV source past its MaxStreams cap or push the global
    /// DvrMaxConcurrentRecordings cap. One of: "Refuse", "Queue",
    /// "Preempt". Default Refuse - safest behavior; the user gets
    /// an explicit error and can resolve the conflict manually.
    /// Queue keeps the row in Scheduled state past its start time
    /// until a slot opens. Preempt cancels the lowest-priority
    /// active recording to make room (never preempts a recording
    /// of higher or equal priority).
    /// </summary>
    public string DvrConflictPolicy { get; set; } = "Refuse";

    // DVR Catchup Settings
    //
    // Catchup downloads pull the already-aired recording window from the
    // provider's timeshift archive after the event finishes, instead of
    // capturing the stream live. No start/end guessing, survives app
    // downtime, retryable while the archive retains the window. Method
    // ported from timeshifter by scottrobertson
    // (github.com/scottrobertson/timeshifter).

    /// <summary>
    /// When true (default), events whose resolved channel has a catchup
    /// archive (Xtream tv_archive) are downloaded from the archive after
    /// they finish airing instead of being recorded live. Channels
    /// without an archive always fall back to live recording.
    /// </summary>
    public bool DvrUseCatchupWhenAvailable { get; set; } = true;

    /// <summary>
    /// Extra minutes to wait after an event's window closes before
    /// downloading it from the archive. Providers can lag in making the
    /// most recent footage available; a grace period avoids pulling a
    /// truncated tail and having to re-download.
    /// </summary>
    public int DvrCatchupReadyGraceMinutes { get; set; } = 15;

    /// <summary>
    /// Timeshift URL style for catchup downloads. "auto" (default) tries
    /// the path style (/timeshift/user/pass/duration/start/streamId.ts,
    /// which most Xtream panels expect), falls back to the "php" style
    /// (streaming/timeshift.php query parameters) on failure, and
    /// remembers per provider which one worked. "path" / "php" force a
    /// single style for unusual panels.
    /// </summary>
    public string DvrCatchupTimeshiftMode { get; set; } = "auto";

    /// <summary>
    /// How many hours back the catchup auto-scheduler looks for finished
    /// monitored events that never got a recording (missed because the
    /// app was down, the event was added late, or it pre-dates catchup).
    /// Bounded by each channel's own archive retention.
    /// </summary>
    public int DvrCatchupBackfillHours { get; set; } = 48;

    // Development Settings (hidden - only serialized to XML when set)
    public string CustomMetadataApiUrl { get; set; } = ""; // Custom metadata API URL for development/testing (empty = use default sportarr.net)

    /// <summary>
    /// Only serialize CustomMetadataApiUrl to XML if it has a value (keeps config.xml clean)
    /// </summary>
    public bool ShouldSerializeCustomMetadataApiUrl() => !string.IsNullOrEmpty(CustomMetadataApiUrl);

    // DVR Encoding Settings (direct settings instead of profile-based)
    public string DvrVideoCodec { get; set; } = "copy"; // Video codec (copy, h264, hevc, av1, etc.)
    public string DvrAudioCodec { get; set; } = "copy"; // Audio codec (copy, aac, ac3, eac3)
    public string DvrAudioChannels { get; set; } = "original"; // Audio channels (original, stereo, 5.1)
    public int DvrAudioBitrate { get; set; } = 192; // Audio bitrate in kbps (0 = auto)
    public int DvrVideoBitrate { get; set; } = 0; // Video bitrate in kbps (0 = auto/VBR, only used when not copy)
    public string DvrContainer { get; set; } = "mp4"; // Output container format (mp4, mkv, ts)

    // Performance Settings (advanced tuning)
    // These values have sensible defaults but can be adjusted for specific environments

    /// <summary>
    /// Maximum concurrent event searches (prevents overwhelming indexers)
    /// </summary>
    public int MaxConcurrentEventSearches { get; set; } = 3;

    /// <summary>
    /// Delay in milliseconds between starting event searches
    /// </summary>
    public int EventSearchDelayMs { get; set; } = 3000;

    /// <summary>
    /// Maximum concurrent indexer queries per search
    /// </summary>
    public int MaxConcurrentIndexerQueries { get; set; } = 5;

    /// <summary>
    /// Default runtime in minutes for sports events (used for size estimation)
    /// </summary>
    public int DefaultSportsRuntimeMinutes { get; set; } = 180;

    /// <summary>
    /// Size comparison chunk in MB for quality matching
    /// </summary>
    public double SizeComparisonChunkMB { get; set; } = 200.0;

    /// <summary>
    /// Minimum match score for auto-grabbing releases (0-100)
    /// </summary>
    public int AutoGrabMinMatchScore { get; set; } = 50;

    /// <summary>
    /// Minimum match confidence for release matching (0-100)
    /// </summary>
    public int MinimumMatchConfidence { get; set; } = 60;

    /// <summary>
    /// Download client cache sliding expiration in minutes
    /// </summary>
    public int DownloadClientCacheSlidingExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// Download client cache absolute expiration in hours
    /// </summary>
    public int DownloadClientCacheAbsoluteExpirationHours { get; set; } = 2;

    /// <summary>
    /// Default HTTP client timeout in seconds
    /// </summary>
    public int HttpClientTimeoutSeconds { get; set; } = 100;
}
