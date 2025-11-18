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
    public int Port { get; set; } = 7878;
    public string UrlBase { get; set; } = "";
    public string InstanceName { get; set; } = "Sportarr";
    public bool EnableSsl { get; set; } = false;
    public int SslPort { get; set; } = 9898;
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

    // Media Management
    public bool RenameEvents { get; set; } = false;
    public bool ReplaceIllegalCharacters { get; set; } = true;
    public string StandardEventFormat { get; set; } = "{Series} - s{Season}e{Episode} - {Event Title}";
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
    public string DownloadClientWorkingFolders { get; set; } = "_UNPACK_,_FAILED_"; // Sonarr compatibility
    public string RemotePathMappingHost { get; set; } = ""; // For remote path mapping
    public string RemotePathMappingRemotePath { get; set; } = "";
    public string RemotePathMappingLocalPath { get; set; } = "";
    public string DownloadPath { get; set; } = "/downloads/sportarr"; // Default download path for clients
    public bool EnableCompletedDownloadHandling { get; set; } = true;
    public bool RemoveCompletedDownloads { get; set; } = true;
    public int CheckForFinishedDownloadInterval { get; set; } = 1; // minutes
    public bool EnableFailedDownloadHandling { get; set; } = true;
    public bool RedownloadFailedDownloads { get; set; } = true;
    public bool RemoveFailedDownloads { get; set; } = true;

    // Indexer Settings
    public int IndexerRetention { get; set; } = 0; // days (0 = disabled)
    public int RssSyncInterval { get; set; } = 60; // minutes (minimum 10)
    public bool PreferIndexerFlags { get; set; } = true;
}
