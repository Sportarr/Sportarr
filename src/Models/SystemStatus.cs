namespace Fightarr.Api.Models;

public class SystemStatus
{
    public string AppName { get; set; } = "Fightarr";
    public string Version { get; set; } = "1.0.0";
    public string BuildTime { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
    public bool IsDebug { get; set; }
    public bool IsProduction { get; set; }
    public bool IsDocker { get; set; }
    public string RuntimeVersion { get; set; } = Environment.Version.ToString();
    public string DatabaseType { get; set; } = "SQLite";
    public string DatabaseVersion { get; set; } = "3.x";
    public string Authentication { get; set; } = "none";
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public string AppData { get; set; } = string.Empty;
    public string OsName { get; set; } = Environment.OSVersion.Platform.ToString();
    public string OsVersion { get; set; } = Environment.OSVersion.Version.ToString();
    public string Branch { get; set; } = "main";
    public int MigrationVersion { get; set; }
    public string UrlBase { get; set; } = string.Empty;
}
