using System.ComponentModel.DataAnnotations;

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

    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}

// Host Configuration
public class HostSettings
{
    public string BindAddress { get; set; } = "*";
    public int Port { get; set; } = 7878;
    public string UrlBase { get; set; } = "";
    public string InstanceName { get; set; } = "Sportarr";
    public bool EnableSsl { get; set; } = false;
    public int SslPort { get; set; } = 9898;
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
    public bool SendAnonymousUsageData { get; set; } = false;
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
    public bool ShowUnknownLeagueItems { get; set; } = false;
    public bool ShowEventPath { get; set; } = false;
}

// Media Management Configuration
public class MediaManagementSettings
{
    public int Id { get; set; }

    // Root folders
    public List<RootFolder> RootFolders { get; set; } = new();

    // File Management
    public bool RenameEvents { get; set; } = false;
    public bool RenameFiles { get; set; } = true;
    public bool ReplaceIllegalCharacters { get; set; } = true;
    public string StandardEventFormat { get; set; } = "{Event Title} - {Event Date} - {League}";
    public string StandardFileFormat { get; set; } = "{Event Title} - {Air Date} - {Quality Full}";

    // Folders
    public bool CreateEventFolders { get; set; } = true;
    public bool CreateEventFolder { get; set; } = true;
    public string EventFolderFormat { get; set; } = "{Event Title}";
    public bool DeleteEmptyFolders { get; set; } = false;

    // Importing
    public bool CopyFiles { get; set; } = false;
    public bool SkipFreeSpaceCheck { get; set; } = false;
    public long MinimumFreeSpace { get; set; } = 100;
    public bool UseHardlinks { get; set; } = true;
    public bool ImportExtraFiles { get; set; } = false;
    public string ExtraFileExtensions { get; set; } = "srt,nfo";

    // Permissions
    public bool SetPermissions { get; set; } = false;
    public string FileChmod { get; set; } = "644";
    public string ChmodFolder { get; set; } = "755";
    public string ChownUser { get; set; } = string.Empty;
    public string ChownGroup { get; set; } = "";

    // Download client interaction
    public bool RemoveCompletedDownloads { get; set; } = true;
    public bool RemoveFailedDownloads { get; set; } = true;

    // Advanced
    public string ChangeFileDate { get; set; } = "None";
    public string RecycleBin { get; set; } = "";
    public int RecycleBinCleanup { get; set; } = 7;

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}

// Root Folder Model
public class RootFolder
{
    public int Id { get; set; }
    public required string Path { get; set; }
    public bool Accessible { get; set; } = true;
    public long FreeSpace { get; set; } = 0;
    public long TotalSpace { get; set; } = 0;
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime LastChecked { get; set; } = DateTime.UtcNow;
}

// Import History
public class ImportHistory
{
    public int Id { get; set; }
    public int EventId { get; set; }
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

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}
