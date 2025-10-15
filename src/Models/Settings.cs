using System.ComponentModel.DataAnnotations;

namespace Fightarr.Api.Models;

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
    public string InstanceName { get; set; } = "Fightarr";
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

    // DEPRECATED: Username and Password are now stored in Users table with proper hashing
    // These fields are kept temporarily for migration purposes only
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
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
    public bool ShowUnknownOrganizationItems { get; set; } = false;
    public bool ShowEventPath { get; set; } = false;
}

// Media Management Configuration
public class MediaManagementSettings
{
    // File Management
    public bool RenameEvents { get; set; } = false;
    public bool ReplaceIllegalCharacters { get; set; } = true;
    public string StandardEventFormat { get; set; } = "{Event Title} - {Event Date} - {Organization}";

    // Folders
    public bool CreateEventFolders { get; set; } = true;
    public bool DeleteEmptyFolders { get; set; } = false;

    // Importing
    public bool SkipFreeSpaceCheck { get; set; } = false;
    public int MinimumFreeSpace { get; set; } = 100;
    public bool UseHardlinks { get; set; } = true;
    public bool ImportExtraFiles { get; set; } = false;
    public string ExtraFileExtensions { get; set; } = "srt,nfo";

    // Advanced
    public string ChangeFileDate { get; set; } = "None";
    public string RecycleBin { get; set; } = "";
    public int RecycleBinCleanup { get; set; } = 7;
    public bool SetPermissions { get; set; } = false;
    public string ChmodFolder { get; set; } = "755";
    public string ChownGroup { get; set; } = "";
}

// Root Folder Model
public class RootFolder
{
    public int Id { get; set; }
    public string Path { get; set; } = "";
    public bool Accessible { get; set; } = true;
    public long FreeSpace { get; set; } = 0;
    public DateTime LastChecked { get; set; } = DateTime.UtcNow;
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
