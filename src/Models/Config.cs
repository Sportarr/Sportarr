using System.Xml.Serialization;

namespace Sportarr.Api.Models;

/// <summary>
/// Main configuration file (config.xml) - matches Sonarr/Radarr pattern
/// </summary>
[XmlRoot("Config")]
public class Config
{
    // Security
    public string ApiKey { get; set; } = Guid.NewGuid().ToString("N");
    public string AuthenticationMethod { get; set; } = "None"; // None, Basic, Forms
    public string AuthenticationRequired { get; set; } = "DisabledForLocalAddresses";
    public bool AuthenticationEnabled { get; set; } = false; // Sonarr compatibility
    public string Username { get; set; } = "";
    public string Password { get; set; } = ""; // Sonarr stores hashed, but has this field
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public int PasswordIterations { get; set; } = 10000;
    public string CertificateValidation { get; set; } = "Enabled";
    public string SslCertHash { get; set; } = ""; // Sonarr field for SSL cert

    // Host
    public string BindAddress { get; set; } = "*";
    public int Port { get; set; } = 1867; // Sportarr's default port
    public string UrlBase { get; set; } = "";
    public string InstanceName { get; set; } = "Sportarr";
    public bool EnableSsl { get; set; } = false;
    public int SslPort { get; set; } = 1868; // Sportarr's default SSL port
    public string SslCertPath { get; set; } = "";
    public string SslCertPassword { get; set; } = "";
    public bool LaunchBrowser { get; set; } = false; // Sonarr opens browser on startup

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
    public bool AnalyticsEnabled { get; set; } = false; // Sonarr field name

    // Backup
    public string BackupFolder { get; set; } = "";
    public int BackupInterval { get; set; } = 7;
    public int BackupRetention { get; set; } = 28;

    // Update
    public string Branch { get; set; } = "main";
    public bool UpdateAutomatically { get; set; } = false; // Sonarr field name
    public string UpdateMechanism { get; set; } = "Docker"; // Sonarr field name (BuiltIn, Script, External, Docker, Apt)
    public string UpdateScriptPath { get; set; } = ""; // Sonarr field name for custom update script

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
    public bool RemoveCompletedDownloads { get; set; } = true;
    public int CheckForFinishedDownloadInterval { get; set; } = 1; // minutes
    public bool EnableFailedDownloadHandling { get; set; } = true;
    public bool RedownloadFailedDownloads { get; set; } = true;
    public bool RemoveFailedDownloads { get; set; } = true;

    // Search Settings
    public int SearchCacheDuration { get; set; } = 120; // seconds - cache raw indexer results (prevents duplicate API calls for multi-part events and same-year searches)

    // Queue Threshold Settings (Huntarr-style)
    // Pause searching when download queue exceeds threshold to prevent overloading
    public int MaxDownloadQueueSize { get; set; } = -1; // -1 = no limit, otherwise pause when queue exceeds this
    public int SearchSleepDuration { get; set; } = 900; // seconds between search cycles (default 15 minutes, Huntarr pattern)

    // RSS Sync Settings (Sonarr-style)
    // RSS Sync pulls the latest releases from indexer RSS feeds and matches against monitored events locally
    // This is MUCH more efficient than searching per-event:
    // - Old approach: N queries per sync (one per monitored event) = thousands of queries/day
    // - New approach: M queries per sync (one per RSS-enabled indexer) = 24-100 queries/day
    public int RssSyncInterval { get; set; } = 15; // minutes between RSS sync cycles (Sonarr default: 15, min: 10, max: 120)
    public int MaxRssReleasesPerIndexer { get; set; } = 100; // max releases to fetch per indexer RSS feed
    public int RssReleaseAgeLimit { get; set; } = 14; // days - only consider releases posted within this window (sports releases are time-sensitive)

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

    // DVR Encoding Settings (direct settings instead of profile-based)
    public string DvrVideoCodec { get; set; } = "copy"; // Video codec (copy, h264, hevc, av1, etc.)
    public string DvrAudioCodec { get; set; } = "copy"; // Audio codec (copy, aac, ac3, eac3)
    public string DvrAudioChannels { get; set; } = "original"; // Audio channels (original, stereo, 5.1)
    public int DvrAudioBitrate { get; set; } = 192; // Audio bitrate in kbps (0 = auto)
    public int DvrVideoBitrate { get; set; } = 0; // Video bitrate in kbps (0 = auto/VBR, only used when not copy)
    public string DvrContainer { get; set; } = "mp4"; // Output container format (mp4, mkv, ts)
}
