using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Models.Metadata;
using Sportarr.Api.Models.Requests;
using Sportarr.Api.Services;
using Sportarr.Api.Middleware;
using Sportarr.Api.Helpers;
using Sportarr.Api.Health;
using Sportarr.Api.Startup;
using Serilog;
using Serilog.Events;
using System.Text.Json;
using Polly;
using Polly.Extensions.Http;
using System.Runtime.InteropServices;
#if WINDOWS
using Sportarr.Windows;
using System.Windows.Forms;
#endif

// Use system SQLite library instead of bundled e_sqlite3 (avoids "invalid opcode" on older CPUs)
SQLitePCL.Batteries_V2.Init();

// Set default environment variables (same as Docker sets, for consistency outside Docker)
// These can still be overridden by the user if needed
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT",
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production");
Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT",
    Environment.GetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT") ?? "1");

// Parse command-line arguments (Sonarr/Radarr style)
var runInTray = args.Contains("--tray") || args.Contains("-t");
var showHelp = args.Contains("--help") || args.Contains("-h") || args.Contains("-?");

// Parse -data argument (Sonarr/Radarr compatible)
// Supports: -data=path, -data path, --data=path, --data path
string? dataArgPath = null;
for (int i = 0; i < args.Length; i++)
{
    var arg = args[i];
    if (arg.StartsWith("-data=", StringComparison.OrdinalIgnoreCase) ||
        arg.StartsWith("--data=", StringComparison.OrdinalIgnoreCase))
    {
        dataArgPath = arg.Substring(arg.IndexOf('=') + 1);
        break;
    }
    else if ((arg.Equals("-data", StringComparison.OrdinalIgnoreCase) ||
              arg.Equals("--data", StringComparison.OrdinalIgnoreCase)) &&
             i + 1 < args.Length)
    {
        dataArgPath = args[i + 1];
        break;
    }
}

if (showHelp)
{
    Console.WriteLine("Sportarr - Universal Sports PVR");
    Console.WriteLine();
    Console.WriteLine("Usage: Sportarr [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -data <path>  Path to store application data (config, database, logs)");
    Console.WriteLine("  --tray, -t    Start minimized to system tray (Windows only)");
    Console.WriteLine("  --help, -h    Show this help message");
    Console.WriteLine();
    Console.WriteLine("Environment Variables:");
    Console.WriteLine("  Sportarr__DataPath    Path to store data files (default: ./data)");
    Console.WriteLine("  Sportarr__ApiKey      API key for external access");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  Sportarr -data C:\\ProgramData\\Sportarr");
    Console.WriteLine("  Sportarr -data=/config");
    Console.WriteLine();
    return;
}

// Pre-configure builder to read configuration before setting up Serilog
var preBuilder = WebApplication.CreateBuilder(args);

// Configuration - get data path first so logs go in the right place
// Priority: 1) -data argument, 2) Sportarr__DataPath env var, 3) Platform default
//
// Windows:
//   If the current working directory is inside a protected location (Program Files,
//   Program Files (x86), or the Windows directory), unconditionally use
//   %ProgramData%\Sportarr. Windows does not auto-elevate items launched from the
//   Startup folder, so the app cannot rely on admin rights and must not write to
//   protected directories. Any residual ./data folder in such a location is
//   migrated to %ProgramData%\Sportarr on first run so existing users do not lose
//   data. For non-protected install locations, keep using ./data if it exists
//   (backwards compat), otherwise default to %ProgramData%\Sportarr.
//
// Non-Windows:
//   ./data relative to the current working directory (unchanged).
var apiKey = preBuilder.Configuration["Sportarr:ApiKey"] ?? Guid.NewGuid().ToString("N");
var dataPath = dataArgPath ?? preBuilder.Configuration["Sportarr:DataPath"];
var isWindowsPlatform = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
    System.Runtime.InteropServices.OSPlatform.Windows);

if (string.IsNullOrEmpty(dataPath))
{
    var cwd = Directory.GetCurrentDirectory();
    var cwdData = Path.Combine(cwd, "data");

    if (isWindowsPlatform)
    {
        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Sportarr");

        // Is the CWD under a Windows protected directory?
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        bool CwdStartsWith(string prefix) =>
            !string.IsNullOrEmpty(prefix) &&
            cwd.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        var cwdIsProtected = CwdStartsWith(programFiles)
            || CwdStartsWith(programFilesX86)
            || CwdStartsWith(windowsDir);

        if (cwdIsProtected)
        {
            // Always use ProgramData — never write inside Program Files or system32
            dataPath = programData;
        }
        else
        {
            // Non-protected install: prefer existing ./data for backwards compat,
            // otherwise default to ProgramData.
            dataPath = Directory.Exists(cwdData) ? cwdData : programData;
        }
    }
    else
    {
        dataPath = cwdData;
    }
}

Directory.CreateDirectory(dataPath);

// WINDOWS DATA RECOVERY + ACL FIX
// Users upgrading from broken versions can have data scattered across multiple
// legacy locations (install folder, system32, prior CWDs). Search the known
// candidates for a sportarr.db and recover the best one into the current
// dataPath. Also fix ACLs because admin-created files in ProgramData do NOT
// inherit Users-write by default (ProgramData only grants Users Create, not
// Modify, and new files inherit CREATOR OWNER which becomes the creating admin).
if (isWindowsPlatform)
{
    try
    {
        var cwdNow = Directory.GetCurrentDirectory();
        var legacyCandidates = new List<string>
        {
            Path.Combine(cwdNow, "data"),
            Path.Combine(AppContext.BaseDirectory, "data"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "system32", "data"),
        };

        // Also scan Program Files roots for any Sportarr*\data folder — handles
        // custom install folder names like "Sportarr-Sports".
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        })
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root, "Sportarr*", SearchOption.TopDirectoryOnly))
                {
                    legacyCandidates.Add(Path.Combine(dir, "data"));
                }
            }
            catch { /* best effort */ }
        }

        // Exclude the current dataPath from legacy candidates and dedupe.
        var dataPathFull = Path.GetFullPath(dataPath);
        legacyCandidates = legacyCandidates
            .Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))
            .Where(p => !string.Equals(Path.GetFullPath(p), dataPathFull, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Pick the legacy candidate with the largest sportarr.db.
        (string Path, long DbSize)? bestLegacy = null;
        foreach (var p in legacyCandidates)
        {
            var size = GetSportarrDbSizeBytes(p);
            if (size > 0 && (bestLegacy == null || size > bestLegacy.Value.DbSize))
            {
                bestLegacy = (p, size);
            }
        }

        var currentDbSize = GetSportarrDbSizeBytes(dataPath);

        if (bestLegacy != null)
        {
            // Auto-recover if current is empty OR legacy db is more than 2x larger.
            // The 2x heuristic catches this case: user had a real database, then a
            // broken launch created a near-empty schema-only db at the new location,
            // and we need to restore the old one. Refuses to overwrite if both dbs
            // have similar sizes (both have real data — too risky to pick).
            var shouldRecover = currentDbSize == 0 || bestLegacy.Value.DbSize > currentDbSize * 2;

            if (shouldRecover)
            {
                try
                {
                    // Back up anything currently in dataPath before overwriting.
                    if (currentDbSize > 0)
                    {
                        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        var currentDb = Path.Combine(dataPath, "sportarr.db");
                        var backup = Path.Combine(dataPath, $"sportarr.db.before-recovery-{timestamp}");
                        File.Copy(currentDb, backup);
                        Console.WriteLine($"[Sportarr] Backed up current database to {backup}");
                    }

                    Console.WriteLine($"[Sportarr] Recovering data from {bestLegacy.Value.Path} " +
                        $"(sportarr.db {bestLegacy.Value.DbSize / 1024} KB) to {dataPath} " +
                        $"(previous db {currentDbSize / 1024} KB)");

                    var filesCopied = CopyDirectoryRecursive(bestLegacy.Value.Path, dataPath, overwrite: true);
                    Console.WriteLine($"[Sportarr] Recovered {filesCopied} file(s). " +
                        $"The old folder at {bestLegacy.Value.Path} can be deleted manually.");
                }
                catch (Exception recEx)
                {
                    Console.WriteLine($"[Sportarr] ERROR: data recovery failed: {recEx.Message}");
                }
            }
            else
            {
                // Don't auto-overwrite a substantial existing db. Log guidance.
                Console.WriteLine($"[Sportarr] NOTE: Found legacy data at {bestLegacy.Value.Path} " +
                    $"(sportarr.db {bestLegacy.Value.DbSize / 1024} KB). " +
                    $"Current data at {dataPath} has sportarr.db {currentDbSize / 1024} KB.");
                Console.WriteLine("[Sportarr] If your imports/indexers appear missing, stop Sportarr, " +
                    "copy the old sportarr.db over the new one, and restart.");
            }
        }

        // Warn about any other stale legacy folders so the user knows they are safe to delete.
        foreach (var p in legacyCandidates)
        {
            if (bestLegacy != null && string.Equals(p, bestLegacy.Value.Path, StringComparison.OrdinalIgnoreCase))
                continue;
            Console.WriteLine($"[Sportarr] NOTE: Stale data folder at {p} is no longer used and can be deleted manually.");
        }
    }
    catch (Exception migEx)
    {
        Console.WriteLine($"[Sportarr] Warning: legacy data check failed: {migEx.Message}");
    }

    // Fix ACLs so non-admin users can write to files created by a previous
    // admin launch. Best-effort: non-admin processes cannot modify ACLs, but
    // when any admin launch happens the permissions get fixed once and stay
    // correct for future non-admin launches.
    try
    {
        if (IsRunningAsWindowsAdministrator())
        {
            Console.WriteLine("[Sportarr] Running with administrator privileges — applying ACL fixup to data directory.");
            EnsureWindowsUsersCanWrite(dataPath);
            Console.WriteLine("[Sportarr] ACL fixup complete. Future non-admin launches should work correctly.");
        }
        // Non-admin launches skip the ACL fix silently (cannot modify ACLs on
        // files owned by others). If the write test below fails because of
        // stale admin-only ACLs, the fallback to %LocalAppData% will kick in
        // and log guidance telling the user to run once as administrator.
    }
    catch (Exception aclEx)
    {
        Console.WriteLine($"[Sportarr] Note: could not adjust ACLs on {dataPath}: {aclEx.Message}");
    }
}

// Defense in depth: verify the chosen directory is actually writable. If the
// primary target is not writable on Windows (typically because an earlier
// admin run created files with admin-only ACLs), fall back to
// %LocalAppData%\Sportarr which is always writable by the current user.
try
{
    var probePath = Path.Combine(dataPath, ".sportarr-write-test");
    File.WriteAllText(probePath, "ok");
    File.Delete(probePath);
}
catch (Exception writeEx)
{
    if (isWindowsPlatform)
    {
        var localAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sportarr");
        Console.WriteLine($"[Sportarr] WARNING: {dataPath} is not writable ({writeEx.Message}).");
        Console.WriteLine($"[Sportarr] Falling back to {localAppData} (per-user data directory).");
        Console.WriteLine("[Sportarr] This usually means the directory was created by an admin process. " +
            "Launch Sportarr once as administrator to fix permissions, or use -data to specify a different path.");
        dataPath = localAppData;
        Directory.CreateDirectory(dataPath);
    }
    else
    {
        Console.WriteLine($"[Sportarr] ERROR: data directory {dataPath} is not writable: {writeEx.Message}");
        Console.WriteLine("[Sportarr] Use -data <path> or set Sportarr__DataPath to a writable directory.");
        throw;
    }
}

Console.WriteLine($"[Sportarr] Data directory: {dataPath}");

// Configure Serilog with logs inside the data directory (like Sonarr)
// This ensures logs are accessible in Docker when user maps their config volume
var logsPath = Path.Combine(dataPath, "logs");
Directory.CreateDirectory(logsPath);
Console.WriteLine($"[Sportarr] Logs directory: {logsPath}");

// Read settings from config.xml if it exists (like Sonarr)
// This includes log level, port, and bind address - needed before web host is built
var configuredLogLevel = LogEventLevel.Information; // Default to Info
int port = 1867; // Default port
string bindAddress = "*"; // Default bind address
var configPath = Path.Combine(dataPath, "config.xml");
if (File.Exists(configPath))
{
    try
    {
        var configXml = System.Xml.Linq.XDocument.Load(configPath);

        // Read log level
        var logLevelElement = configXml.Root?.Element("LogLevel");
        if (logLevelElement != null)
        {
            var logLevelStr = logLevelElement.Value?.ToLower() ?? "info";
            configuredLogLevel = logLevelStr switch
            {
                "trace" => LogEventLevel.Verbose,  // Serilog uses Verbose for Trace
                "debug" => LogEventLevel.Debug,
                "info" or "information" => LogEventLevel.Information,
                "warn" or "warning" => LogEventLevel.Warning,
                "error" => LogEventLevel.Error,
                "fatal" => LogEventLevel.Fatal,
                _ => LogEventLevel.Information
            };
            Console.WriteLine($"[Sportarr] Log level from config: {logLevelStr} -> {configuredLogLevel}");
        }

        // Read port setting
        var portElement = configXml.Root?.Element("Port");
        if (portElement != null && int.TryParse(portElement.Value, out var configPort) && configPort > 0)
        {
            port = configPort;
        }

        // Read bind address setting
        var bindAddressElement = configXml.Root?.Element("BindAddress");
        if (bindAddressElement != null && !string.IsNullOrWhiteSpace(bindAddressElement.Value))
        {
            bindAddress = bindAddressElement.Value;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Sportarr] Warning: Could not read config.xml: {ex.Message}");
    }
}

Console.WriteLine($"[Sportarr] Configured to listen on {bindAddress}:{port}");

// Output template for logs (shared between console and file)
var outputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";

// Create sanitizing formatter to protect sensitive data
var sanitizingFormatter = new SanitizingTextFormatter(outputTemplate);

// Configure Serilog like Sonarr:
// - Main log file: sportarr.txt with rolling by size and day
// - Retained file count: 10 files (manageable storage)
// - File size: 10MB per file (reduces number of files created)
// - When file reaches size limit, rolls to sportarr_001.txt, sportarr_002.txt, etc.
// - Oldest files are automatically deleted when limit is reached
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(configuredLogLevel)      // Use configured log level
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatter: sanitizingFormatter)
    .WriteTo.File(
        formatter: sanitizingFormatter,
        path: Path.Combine(logsPath, "sportarr.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 10,           // Keep only 10 files for storage management
        fileSizeLimitBytes: 10485760,         // 10MB per file (reduces file count)
        rollOnFileSizeLimit: true,            // Roll when size limit reached
        shared: true,                         // Allow multiple processes to write
        flushToDiskInterval: TimeSpan.FromSeconds(1))
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on configured port from config.xml
// Use configured bind address (same pattern as Sonarr/Radarr)
builder.WebHost.UseUrls($"http://{bindAddress}:{port}");

// Use Serilog for all logging
builder.Host.UseSerilog();

builder.Configuration["Sportarr:ApiKey"] = apiKey;
// Propagate the resolved data path into the DI configuration so that services
// like ConfigService (which loads/saves config.xml) use the same directory as
// the database and logs. Without this, ConfigService falls back to a
// CWD-relative "./data" path, which on Windows ends up in C:\WINDOWS\system32
// or C:\Program Files depending on how the process was launched — causing a
// split-brain where sportarr.db lives in %ProgramData%\Sportarr but config.xml
// is read/written somewhere else.
builder.Configuration["Sportarr:DataPath"] = dataPath;

builder.Services
    .AddSportarrSwagger()
    .AddSportarrCoreServices()
    .AddSportarrHttpClients()
    .AddSportarrIndexing()
    .AddSportarrFileServices()
    .AddSportarrIptv()
    .AddSportarrBackgroundServices()
    .AddSportarrDatabase(System.IO.Path.Combine(dataPath, "sportarr.db"))
    .AddSportarrCors(builder.Environment);

Sportarr.Api.Authentication.AuthenticationBuilderExtensions.AddAppAuthentication(builder.Services);

builder.Services.AddHealthChecks().AddSportarrHealthChecks();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

AgentInstaller.Install(dataPath, isWindowsPlatform);

// Configure middleware pipeline

// URL Base support for reverse proxy setups (e.g., /sportarr)
// Must be configured early in the pipeline, before routing
string configuredUrlBase = "";
{
    var configService = app.Services.GetRequiredService<Sportarr.Api.Services.ConfigService>();
    var config = configService.GetConfigAsync().GetAwaiter().GetResult();
    configuredUrlBase = config.UrlBase?.Trim() ?? "";
    if (!string.IsNullOrEmpty(configuredUrlBase))
    {
        // Ensure proper formatting: starts with /, no trailing /
        if (!configuredUrlBase.StartsWith("/"))
            configuredUrlBase = "/" + configuredUrlBase;
        configuredUrlBase = configuredUrlBase.TrimEnd('/');

        Log.Information("[URL Base] Configured URL base: {UrlBase}", configuredUrlBase);

        // UsePathBase strips the URL base from incoming request paths
        // e.g., /sportarr/api/leagues becomes /api/leagues
        app.UsePathBase(configuredUrlBase);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// Global exception handling - must be early in pipeline
app.UseExceptionHandling();

// Add X-Application-Version header to all API responses (required for Prowlarr)
app.UseVersionHeader();

// ASP.NET Core Authentication & Authorization (Sonarr/Radarr pattern)
app.UseAuthentication();
app.UseAuthorization();
app.UseDynamicAuthentication(); // Dynamic scheme selection based on settings

// Map controller routes (for AuthenticationController)
app.MapControllers();

// Map built-in health checks endpoint (provides detailed health status)
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                data = e.Value.Data.Count > 0 ? e.Value.Data : null,
                exception = e.Value.Exception?.Message
            })
        };
        await context.Response.WriteAsJsonAsync(result);
    }
});

// Configure static files (UI from wwwroot)
// For URL base support, we need to inject the urlBase into index.html
// and rewrite asset paths to include the base
app.Use(async (context, next) =>
{
    // Serve index.html with urlBase injection for SPA routes
    var path = context.Request.Path.Value ?? "";

    // Check if this is a request that should serve index.html (SPA fallback)
    // Skip API routes, static assets, and other special endpoints
    var isApiOrAsset = path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/assets", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/initialize.json", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/ping", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
                       path.Contains(".");  // Has file extension (e.g., .js, .css, .svg)

    if (!isApiOrAsset)
    {
        // Serve index.html with urlBase injected
        var webRootPath = app.Environment.WebRootPath;
        var indexPath = Path.Combine(webRootPath, "index.html");

        if (File.Exists(indexPath))
        {
            var html = await File.ReadAllTextAsync(indexPath);

            // Get the configured URL base
            var configService = context.RequestServices.GetRequiredService<Sportarr.Api.Services.ConfigService>();
            var config = await configService.GetConfigAsync();
            var urlBase = config.UrlBase?.Trim() ?? "";
            if (!string.IsNullOrEmpty(urlBase))
            {
                if (!urlBase.StartsWith("/"))
                    urlBase = "/" + urlBase;
                urlBase = urlBase.TrimEnd('/');

                // Inject urlBase script before the first script tag
                // This sets window.Sportarr.urlBase BEFORE main.tsx runs
                var urlBaseScript = $@"<script>window.Sportarr = window.Sportarr || {{}}; window.Sportarr.urlBase = '{urlBase}';</script>";
                html = html.Replace("<script", urlBaseScript + "<script");

                // Rewrite asset paths to include urlBase
                // /assets/ -> /sportarr/assets/
                // /logo.svg -> /sportarr/logo.svg
                html = html.Replace("href=\"/", $"href=\"{urlBase}/");
                html = html.Replace("src=\"/", $"src=\"{urlBase}/");
            }

            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(html);
            return;
        }
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

// Initialize endpoint (for frontend) - keep for SPA compatibility
app.MapGet("/initialize.json", async (Sportarr.Api.Services.ConfigService configService) =>
{
    // Get API key from config.xml (same source that authentication uses)
    var config = await configService.GetConfigAsync();
    // Ensure urlBase is properly formatted (starts with / if not empty, no trailing /)
    var urlBase = config.UrlBase?.Trim() ?? "";
    if (!string.IsNullOrEmpty(urlBase))
    {
        if (!urlBase.StartsWith("/"))
            urlBase = "/" + urlBase;
        urlBase = urlBase.TrimEnd('/');
    }
    return Results.Json(new
    {
        apiRoot = "", // Empty since all API routes already start with /api
        apiKey = config.ApiKey,
        release = Sportarr.Api.Version.GetFullVersion(),
        version = Sportarr.Api.Version.GetFullVersion(),
        instanceName = "Sportarr",
        theme = "auto",
        branch = "main",
        analytics = false,
        userHash = Guid.NewGuid().ToString("N")[..8],
        urlBase = urlBase,
        isProduction = !app.Environment.IsDevelopment()
    });
});

// Health check
app.MapGet("/ping", () => Results.Ok("pong"));

app.MapAuthEndpoints();

// API: System Status
app.MapGet("/api/system/status", async (Sportarr.Api.Services.ConfigService configService) =>
{
    var config = await configService.GetConfigAsync();
    var status = new SystemStatus
    {
        AppName = "Sportarr",
        Version = Sportarr.Api.Version.GetFullVersion(),  // Use full 4-part version (e.g., 4.0.81.140)
        Branch = Environment.GetEnvironmentVariable("SPORTARR_BRANCH") ?? config.Branch,
        IsDebug = app.Environment.IsDevelopment(),
        IsProduction = app.Environment.IsProduction(),
        IsDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true",
        DatabaseType = "SQLite",
        Authentication = "apikey",
        AppData = dataPath,
        StartTime = DateTime.UtcNow,
        TimeZone = string.IsNullOrEmpty(config.TimeZone) ? TimeZoneInfo.Local.Id : config.TimeZone
    };
    return Results.Ok(status);
});

app.MapSystemStatusEndpoints();

app.MapSystemBackupEndpoints();

app.MapSystemAgentEndpoints();

app.MapSystemUpdatesEndpoint();

app.MapSystemEventEndpoints();

app.MapLibraryEndpoints();

app.MapLogEndpoints(logsPath);

app.MapTaskEndpoints();

app.MapEventEndpoints();

// REMOVED: FightCard endpoints (obsolete - universal approach uses Event.Monitored)
// REMOVED: Organization endpoints (obsolete - replaced with League-based API)
// - /api/organizations (GET) - Replaced with /api/leagues
// - /api/organizations/{name}/events (GET) - Replaced with /api/leagues/{id}/events

app.MapTagAndQualityProfileEndpoints();

app.MapCustomFormatEndpoints();

app.MapTrashGuidesEndpoints();
// ==================== End TRaSH Guides API ====================

// API: Get all delay profiles
app.MapProfileAndListEndpoints();

app.MapTagsManagementEndpoints();

app.MapRootFolderAndNotificationEndpoints();

// API: Config (lightweight endpoint for specific config values)
// Note: Does not require authorization as it only returns non-sensitive feature flags
app.MapSettingsEndpoints();

app.MapDownloadClientEndpoints();

app.MapQueueAndImportEndpoints();

app.MapHistoryEndpoints();

app.MapBlocklistAndWantedEndpoints();

app.MapIndexerEndpoints();

app.MapIptvEndpoints();

// ============================================================================
// EPG (Electronic Program Guide) Endpoints
// ============================================================================

// Get all EPG sources
app.MapEpgEndpoints();

// ============================================================================
// DVR Recording Endpoints
// ============================================================================

app.MapDvrEndpoints();

// API: Manual search for specific event (Universal: supports all sports)
app.MapManualEventSearchEndpoints();

app.MapLeagueEndpoints();

// ====================================================================================
// FOLLOWED TEAMS API - Cross-League Team Monitoring
// ====================================================================================

app.MapFollowedTeamsAndTeamsEndpoints();

// ========================================
// EVENT SEARCH ENDPOINTS (Sportarr API)
// ========================================

app.MapEventSearchAndGrabEndpoints();

app.MapSearchAndCalendarEndpoints();

// ========================================
// PROWLARR INTEGRATION - Sonarr/Radarr-Compatible Application API
// ========================================

// Prowlarr uses /api/v1/indexer to sync indexers to applications
// These endpoints allow Prowlarr to automatically push indexers to Sportarr

app.MapV1ProwlarrEndpoints(dataPath);

app.MapSonarrSystemEndpoints(dataPath);

app.MapSonarrCommandEndpoints();

app.MapSonarrSeriesEndpoints();

app.MapSonarrCalendarEndpoint();

app.MapSonarrEpisodeFileEndpoints();

// ============================================================================
// END DECYPHARR COMPATIBILITY ENDPOINTS
// ============================================================================

// ============================================================================
// MAINTAINERR COMPATIBILITY ENDPOINTS (Sonarr v3 API)
// These endpoints enable Maintainerr to manage Sportarr content
// ============================================================================

app.MapSonarrConfigEndpoints();

// ============================================================================
// END MAINTAINERR COMPATIBILITY ENDPOINTS
// ============================================================================

app.MapSonarrIndexerEndpoints();
app.MapSonarrDownloadClientEndpoint();

// ===========================================================================
// EVENT MAPPING API - For release name matching
// ===========================================================================

app.MapEventMappingEndpoints();

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

Log.Information("========================================");
Log.Information("Sportarr is starting...");
Log.Information("App Version: {AppVersion}", Sportarr.Api.Version.GetFullVersion());
Log.Information("API Version: {ApiVersion}", Sportarr.Api.Version.ApiVersion);
Log.Information("Environment: {Environment}", app.Environment.EnvironmentName);
Log.Information("URL: http://localhost:1867");
Log.Information("Logs Directory: {LogsPath}", logsPath);
Log.Information("========================================");

try
{
    Log.Information("[Sportarr] Starting web host");

#if WINDOWS
    // Windows: Support system tray mode
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        // Create shutdown token that tray icon can use to signal exit
        using var appShutdown = new CancellationTokenSource();

        // If --tray flag is set, hide console and show tray icon
        if (runInTray)
        {
            WindowsTrayIcon.HideConsole();
            Log.Information("[Sportarr] Running in tray mode - console hidden");
        }

        // Always show tray icon on Windows
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        using var trayIcon = new WindowsTrayIcon(1867, appShutdown);

        // Run web host in background, tray icon on UI thread.
        // If the host fails to start (e.g. port already in use), capture the
        // exception, trigger app shutdown so the tray loop exits, and rethrow
        // it to the outer catch so the user sees a clean error instead of a
        // zombie tray icon with no web UI behind it.
        Exception? webHostFailure = null;
        var webHostTask = Task.Run(async () =>
        {
            try
            {
                await app.RunAsync(appShutdown.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when shutting down
            }
            catch (Exception ex)
            {
                webHostFailure = ex;
                appShutdown.Cancel();
            }
        });

        // Show startup notification
        trayIcon.ShowBalloon("Sportarr", "Sportarr is running on port 1867", System.Windows.Forms.ToolTipIcon.Info);

        // Run Windows Forms message loop until shutdown requested
        while (!appShutdown.Token.IsCancellationRequested)
        {
            Application.DoEvents();
            Thread.Sleep(100);
        }

        // Wait for web host to finish
        webHostTask.Wait(TimeSpan.FromSeconds(5));

        // If the web host died on startup, rethrow so the outer catch can
        // translate it into a user-friendly error (e.g. port in use).
        if (webHostFailure != null)
        {
            throw webHostFailure;
        }
    }
    else
    {
        // Non-Windows: just run normally
        app.Run();
    }
#else
    // Non-Windows build: just run normally
    app.Run();
#endif
}
// Detect the common "port already in use" crash and surface a user-friendly
// message instead of a wall of stack traces. This usually means another
// Sportarr instance is already running — e.g. the user has Sportarr set to
// launch from both a Task Scheduler entry and a Startup shortcut, and the
// second instance loses the race for the port.
catch (Exception ex) when (IsAddressInUseException(ex))
{
    var friendly =
        $"Port {port} is already in use. Another Sportarr instance is probably already running. " +
        $"Check the system tray, or Task Manager for Sportarr.exe, or any services/scheduled tasks " +
        $"that may auto-start Sportarr. You can also change the port in config.xml.";
    Console.WriteLine($"[Sportarr] ERROR: {friendly}");
    Log.Fatal("[Sportarr] {Message}", friendly);
    Log.CloseAndFlush();
    Environment.Exit(1);
}
catch (Exception ex)
{
    Log.Fatal(ex, "[Sportarr] Application terminated unexpectedly");
    throw;
}
finally
{
    Log.Information("[Sportarr] Shutting down...");
    Log.CloseAndFlush();
}

// Walks the exception chain looking for the SocketException with
// AddressAlreadyInUse, regardless of how deeply Kestrel's host pipeline wraps it.
static bool IsAddressInUseException(Exception? ex)
{
    while (ex != null)
    {
        if (ex is System.Net.Sockets.SocketException sx &&
            sx.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
        {
            return true;
        }
        ex = ex.InnerException;
    }
    return false;
}

// Returns the size of sportarr.db in the given data directory, or 0 if the file
// does not exist or cannot be read. Used for choosing the best legacy data
// location to recover from.
static long GetSportarrDbSizeBytes(string dataDirectory)
{
    try
    {
        var dbPath = Path.Combine(dataDirectory, "sportarr.db");
        if (!File.Exists(dbPath)) return 0;
        return new FileInfo(dbPath).Length;
    }
    catch
    {
        return 0;
    }
}

// Recursively copies a directory tree. Returns the number of files copied.
// Skips files that cannot be read/written (best effort).
static int CopyDirectoryRecursive(string source, string destination, bool overwrite)
{
    var copied = 0;
    Directory.CreateDirectory(destination);
    foreach (var srcFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        try
        {
            var relative = Path.GetRelativePath(source, srcFile);
            var dstFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
            File.Copy(srcFile, dstFile, overwrite);
            copied++;
        }
        catch
        {
            // Skip individual files that fail — best-effort recovery
        }
    }
    return copied;
}

#if WINDOWS
// Returns true if the current Windows process is running with administrator
// privileges (elevated). Used to decide whether to attempt ACL fixups that
// require admin rights.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static bool IsRunningAsWindowsAdministrator()
{
    try
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
    catch
    {
        return false;
    }
}
#else
static bool IsRunningAsWindowsAdministrator() => false;
#endif

#if WINDOWS
// Ensures that BUILTIN\Users has Modify permissions on the given directory and
// all files inside it. This fixes the common scenario where an admin-launched
// Sportarr created sportarr.db in %ProgramData%\Sportarr with admin-only ACLs,
// blocking subsequent non-admin launches with "readonly database" errors.
// Only attempts to modify ACLs when running as administrator — non-admin
// processes cannot change ACLs on files they do not own.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static void EnsureWindowsUsersCanWrite(string directoryPath)
{
    if (!Directory.Exists(directoryPath)) return;

    // ACL modification requires ownership or WRITE_DAC on each object.
    // Non-admin processes cannot change ACLs on files owned by another user,
    // so skip the entire operation when not elevated — it would just fail
    // silently on every file and waste startup time.
    if (!IsRunningAsWindowsAdministrator()) return;

    var usersSid = new System.Security.Principal.SecurityIdentifier(
        System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null);

    // Directory rule: inherit Modify down to all children, so new files created
    // here (by any user, including admin) will grant Users write access.
    var dirRule = new System.Security.AccessControl.FileSystemAccessRule(
        usersSid,
        System.Security.AccessControl.FileSystemRights.Modify |
            System.Security.AccessControl.FileSystemRights.Synchronize,
        System.Security.AccessControl.InheritanceFlags.ContainerInherit |
            System.Security.AccessControl.InheritanceFlags.ObjectInherit,
        System.Security.AccessControl.PropagationFlags.None,
        System.Security.AccessControl.AccessControlType.Allow);

    var dirInfo = new DirectoryInfo(directoryPath);
    var dirSecurity = dirInfo.GetAccessControl();
    dirSecurity.AddAccessRule(dirRule);
    dirInfo.SetAccessControl(dirSecurity);

    // Existing files don't retroactively inherit — walk them and add an explicit
    // Modify ACE to each. Best-effort per file.
    var fileRule = new System.Security.AccessControl.FileSystemAccessRule(
        usersSid,
        System.Security.AccessControl.FileSystemRights.Modify |
            System.Security.AccessControl.FileSystemRights.Synchronize,
        System.Security.AccessControl.InheritanceFlags.None,
        System.Security.AccessControl.PropagationFlags.None,
        System.Security.AccessControl.AccessControlType.Allow);

    foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
    {
        try
        {
            var fi = new FileInfo(file);
            var fs = fi.GetAccessControl();
            fs.AddAccessRule(fileRule);
            fi.SetAccessControl(fs);
        }
        catch
        {
            // Best effort — skip files we can't modify
        }
    }
}
#else
// Non-Windows builds stub this out so the top-level code can call it
// unconditionally under the runtime isWindowsPlatform guard.
static void EnsureWindowsUsersCanWrite(string directoryPath)
{
    // No-op on non-Windows
}
#endif

// Make Program class accessible to integration tests
public partial class Program { }
