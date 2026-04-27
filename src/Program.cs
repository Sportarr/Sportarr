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

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Default named HttpClient used by services that don't configure their own.
// PooledConnectionLifetime keeps DNS fresh (Docker container names rotate),
// timeout prevents hung calls from pinning thread pool threads.
builder.Services.AddHttpClient(string.Empty)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
    })
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
    });

// EPG/XMLTV downloads can be very large gzipped feeds, so allow a longer timeout.
builder.Services.AddHttpClient("EpgClient")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
    })
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
    });

// Add memory cache for download client caching with expiration
builder.Services.AddMemoryCache();

// Configure named HttpClient for download clients with proper DNS refresh for Docker container names
// PooledConnectionLifetime ensures DNS is re-resolved periodically (important for Docker container name resolution)
builder.Services.AddHttpClient("DownloadClient")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // Re-resolve DNS every 2 minutes (matches Sonarr behavior for Docker container names)
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        // Don't cache connections indefinitely
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
        // Allow redirect following
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5
    })
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(100);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
    });

// Configure named HttpClient for download clients that need SSL certificate validation bypass
// Used for self-signed certificates on qBittorrent/other clients behind reverse proxies
builder.Services.AddHttpClient("DownloadClientSkipSsl")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // Re-resolve DNS every 2 minutes (matches Sonarr behavior)
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        // Don't cache connections indefinitely
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
        // Allow redirect following
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        // Bypass SSL certificate validation for self-signed certs
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
        }
    })
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(100);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
    });

// Register DownloadClientService - uses IHttpClientFactory for proper HttpClient lifecycle management
builder.Services.AddScoped<Sportarr.Api.Services.DownloadClientService>();

// Configure HttpClient for TRaSH Guides GitHub API with proper User-Agent
builder.Services.AddHttpClient("TrashGuides")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0 (https://github.com/Sportarr/Sportarr)");
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    });

// Register rate limiting service (Sonarr-style HTTP-level rate limiting)
builder.Services.AddSingleton<IRateLimitService, RateLimitService>();

// Register RateLimitHandler as transient (one per HttpClient)
builder.Services.AddTransient<RateLimitHandler>();

// Configure HttpClient for indexer searches with rate limiting and Polly retry policy
// Rate limiting is now handled at the HTTP layer via RateLimitHandler, matching Sonarr/Radarr
builder.Services.AddHttpClient("IndexerClient")
    .AddHttpMessageHandler<RateLimitHandler>()  // Rate limiting at HTTP layer
    .AddTransientHttpErrorPolicy(policyBuilder =>
        policyBuilder.WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryCount, context) =>
            {
                Console.WriteLine($"[Indexer] Retry {retryCount} after {timespan.TotalSeconds}s due to {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
            }))
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
    });

// Configure HttpClient for IPTV stream proxying (avoids CORS issues in browser)
builder.Services.AddHttpClient("StreamProxy")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // Allow redirects for stream URLs
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10, // Increased for IPTV providers that chain redirects
        // Disable connection pooling for streaming to avoid stale connections
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30)
    })
    .ConfigureHttpClient(client =>
    {
        // Longer timeout for stream connections
        client.Timeout = TimeSpan.FromMinutes(5);
    });

// Configure HttpClient for IPTV services (source syncing, channel testing, API calls)
// This client properly follows HTTP 302 redirects which many IPTV providers use
builder.Services.AddHttpClient("IptvClient")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // CRITICAL: Allow redirects - many IPTV providers (especially Xtream Codes) use 302 redirects
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10,
        // DNS refresh for dynamic IPTV server IPs
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
    })
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VLC/3.0.18 LibVLC/3.0.18");
    });

builder.Services.AddControllers(); // Add MVC controllers for AuthenticationController
// Configure minimal API JSON options - serialize enums as integers for frontend compatibility
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Use camelCase for JSON property names to match frontend expectations
    // Frontend sends: { externalId: "...", name: "..." }
    // Backend has: { ExternalId, Name } with PascalCase properties
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;

    // Enable case-insensitive property name matching for JSON deserialization
    // This allows Sportarr API responses (idLeague, strLeague) to map to our League model
    options.SerializerOptions.PropertyNameCaseInsensitive = true;

    // Handle circular references (e.g., Event -> League -> Events -> Event)
    // This prevents serialization errors when navigation properties create cycles
    options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;

    // DO NOT add JsonStringEnumConverter - we need numeric enum values for frontend
    // The frontend expects type: 5 (number), not type: "Sabnzbd" (string)
});
builder.Services.AddSingleton<Sportarr.Api.Services.ConfigService>();
builder.Services.AddScoped<Sportarr.Api.Services.UserService>();
builder.Services.AddScoped<Sportarr.Api.Services.AuthenticationService>();
builder.Services.AddScoped<Sportarr.Api.Services.SimpleAuthService>();
builder.Services.AddScoped<Sportarr.Api.Services.SessionService>();
// Note: DownloadClientService is registered above as Scoped with IHttpClientFactory pattern
builder.Services.AddScoped<Sportarr.Api.Services.IndexerStatusService>(); // Sonarr-style indexer health tracking and backoff
builder.Services.AddScoped<Sportarr.Api.Services.IndexerSearchService>();
builder.Services.AddScoped<Sportarr.Api.Services.ReleaseMatchingService>(); // Sonarr-style release validation to prevent downloading wrong content
builder.Services.AddSingleton<Sportarr.Api.Services.ReleaseMatchScorer>(); // Match scoring for event-to-release matching
builder.Services.AddScoped<Sportarr.Api.Services.ReleaseCacheService>(); // Local release cache for RSS-first search strategy
builder.Services.AddSingleton<Sportarr.Api.Services.SearchQueueService>(); // Queue for parallel search execution
builder.Services.AddSingleton<Sportarr.Api.Services.SearchResultCache>(); // In-memory cache for raw indexer results (reduces API calls)
builder.Services.AddSingleton<Sportarr.Api.Services.CustomFormatMatchCache>(); // In-memory cache for CF match results (avoids repeated regex evaluation)
builder.Services.AddScoped<Sportarr.Api.Services.AutomaticSearchService>();
builder.Services.AddScoped<Sportarr.Api.Services.DelayProfileService>();
builder.Services.AddScoped<Sportarr.Api.Services.QualityDetectionService>();
builder.Services.AddScoped<Sportarr.Api.Services.ReleaseEvaluator>();
builder.Services.AddScoped<Sportarr.Api.Services.ReleaseProfileService>(); // Release profile keyword filtering (Sonarr-style)
builder.Services.AddScoped<Sportarr.Api.Services.MediaFileParser>();
builder.Services.AddScoped<Sportarr.Api.Services.SportsFileNameParser>(); // Sports-specific filename parsing (UFC, WWE, NFL, etc.)
builder.Services.AddScoped<Sportarr.Api.Services.FileNamingService>();
builder.Services.AddScoped<Sportarr.Api.Services.FileRenameService>(); // Auto-renames files when event metadata changes
builder.Services.AddScoped<Sportarr.Api.Services.EventPartDetector>(); // Multi-part episode detection for Fighting sports
builder.Services.AddScoped<Sportarr.Api.Services.FileFormatManager>(); // Auto-manages {Part} token in file format
builder.Services.AddScoped<Sportarr.Api.Services.FileImportService>();
builder.Services.AddScoped<Sportarr.Api.Services.ImportMatchingService>(); // Matches external downloads to events
builder.Services.AddScoped<Sportarr.Api.Services.CustomFormatService>();
builder.Services.AddScoped<Sportarr.Api.Services.TrashGuideSyncService>(); // TRaSH Guides sync for custom formats and scores
builder.Services.AddHostedService<Sportarr.Api.Services.TrashSyncBackgroundService>(); // TRaSH Guides auto-sync background service
builder.Services.AddSingleton<Sportarr.Api.Services.DiskSpaceService>(); // Disk space detection (handles Docker volumes correctly)
builder.Services.AddScoped<Sportarr.Api.Services.HealthCheckService>();
builder.Services.AddScoped<Sportarr.Api.Services.BackupService>();
builder.Services.AddScoped<Sportarr.Api.Services.LibraryImportService>();
builder.Services.AddScoped<Sportarr.Api.Services.NotificationService>(); // Multi-provider notifications (Discord, Telegram, Pushover, Plex, Jellyfin, Emby, etc.)
builder.Services.AddScoped<Sportarr.Api.Services.ImportListService>();
// ImportService removed - CompletedDownloadHandlingService now uses FileImportService which has proper folder structure with episode numbers
builder.Services.AddScoped<Sportarr.Api.Services.ProvideImportItemService>(); // Provides import items with path translation
builder.Services.AddScoped<Sportarr.Api.Services.EventQueryService>(); // Universal: Sport-aware query builder for all sports
builder.Services.AddScoped<Sportarr.Api.Services.LeagueEventSyncService>(); // Syncs events from Sportarr API to populate leagues
builder.Services.AddScoped<Sportarr.Api.Services.TeamLeagueDiscoveryService>(); // Discovers leagues for followed teams (cross-league team monitoring)
builder.Services.AddScoped<Sportarr.Api.Services.SeasonSearchService>(); // Season-level search for manual season pack discovery
builder.Services.AddScoped<Sportarr.Api.Services.EventMappingService>(); // Event mapping sync and lookup for release name matching
builder.Services.AddScoped<Sportarr.Api.Services.PackImportService>(); // Multi-file pack import (e.g., NFL-2025-Week15 containing all games)
builder.Services.AddHostedService<Sportarr.Api.Services.EventMappingSyncBackgroundService>(); // Automatic event mapping sync every 12 hours (like Sonarr XEM)
builder.Services.AddHostedService<Sportarr.Api.Services.LeagueEventAutoSyncService>(); // Background service for automatic periodic event sync

// Sportarr API client for sports metadata (sportarr.net).
// 30s timeout matches Sonarr/Radarr; without it, .NET defaults to 100s and a
// hung sportarr.net request would pin a thread-pool thread for 100s × 3 retries
// = 5+ minutes. PooledConnectionLifetime ensures DNS is re-resolved periodically.
builder.Services.AddHttpClient<Sportarr.Api.Services.SportarrApiClient>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
    })
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
    })
    .AddTransientHttpErrorPolicy(policyBuilder =>
        policyBuilder.WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                Console.WriteLine($"[SportarrAPI] Retry {retryAttempt} after {timespan.TotalSeconds}s delay");
            }
        ));

builder.Services.AddSingleton<Sportarr.Api.Services.TaskService>();
builder.Services.AddHostedService<Sportarr.Api.Services.EnhancedDownloadMonitorService>(); // Unified download monitoring with retry, blocklist, and auto-import
builder.Services.AddHostedService<Sportarr.Api.Services.RssSyncService>(); // Automatic RSS sync for new releases
builder.Services.AddHostedService<Sportarr.Api.Services.BacklogSearchService>(); // Sonarr-style scheduled search for missing + cutoff-unmet events
builder.Services.AddHostedService<Sportarr.Api.Services.PendingReleaseReaperService>(); // Promotes best-of-delay-window release per event
builder.Services.AddHostedService<Sportarr.Api.Services.TvScheduleSyncService>(); // TV schedule sync for automatic search timing
builder.Services.AddSingleton<Sportarr.Api.Services.DiskScanService>(); // Periodic file existence verification (Sonarr-style disk scan)
builder.Services.AddHostedService<Sportarr.Api.Services.DiskScanService>(sp => sp.GetRequiredService<Sportarr.Api.Services.DiskScanService>());
builder.Services.AddHostedService<Sportarr.Api.Services.FileWatcherService>(); // Real-time file monitoring via FileSystemWatcher

// IPTV/DVR services for recording live streams
builder.Services.AddScoped<Sportarr.Api.Services.M3uParserService>();
builder.Services.AddScoped<Sportarr.Api.Services.XtreamCodesClient>();
builder.Services.AddScoped<Sportarr.Api.Services.IptvSourceService>();
builder.Services.AddScoped<Sportarr.Api.Services.ChannelAutoMappingService>();
builder.Services.AddSingleton<Sportarr.Api.Services.FFmpegRecorderService>();
builder.Services.AddSingleton<Sportarr.Api.Services.FFmpegStreamService>(); // Live stream transcoding service
builder.Services.AddScoped<Sportarr.Api.Services.DvrRecordingService>();
builder.Services.AddScoped<Sportarr.Api.Services.EventDvrService>();
builder.Services.AddHostedService<Sportarr.Api.Services.DvrSchedulerService>();
builder.Services.AddSingleton<Sportarr.Api.Services.DvrAutoSchedulerService>(); // DVR auto-scheduling service (singleton for background + manual trigger)
builder.Services.AddHostedService(sp => sp.GetRequiredService<Sportarr.Api.Services.DvrAutoSchedulerService>()); // Run as hosted service
builder.Services.AddScoped<Sportarr.Api.Services.DvrQualityScoreCalculator>(); // DVR quality score estimation
builder.Services.AddScoped<Sportarr.Api.Services.XmltvParserService>(); // XMLTV EPG parser
builder.Services.AddScoped<Sportarr.Api.Services.EpgService>(); // EPG management service
builder.Services.AddScoped<Sportarr.Api.Services.EpgSchedulingService>(); // EPG-based DVR scheduling optimization
builder.Services.AddScoped<Sportarr.Api.Services.FilteredExportService>(); // Filtered M3U/EPG export service

// Add ASP.NET Core Authentication (Sonarr/Radarr pattern)
Sportarr.Api.Authentication.AuthenticationBuilderExtensions.AddAppAuthentication(builder.Services);

// Configure database
var dbPath = Path.Combine(dataPath, "sportarr.db");
Console.WriteLine($"[Sportarr] Database path: {dbPath}");
builder.Services.AddDbContext<SportarrDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}")
           .ConfigureWarnings(w => w
               .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning)
               .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)
               .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.FirstWithoutOrderByAndFilterWarning)));
// Add DbContextFactory for concurrent database access (used by IndexerStatusService for parallel indexer searches)
builder.Services.AddDbContextFactory<SportarrDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}")
           .ConfigureWarnings(w => w
               .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning)
               .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)
               .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.FirstWithoutOrderByAndFilterWarning)), ServiceLifetime.Scoped);

// Add CORS - more restrictive in production, permissive in development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Development: allow any origin for ease of testing
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            // Production: restrict to same-origin and common local development URLs
            // Users can configure additional origins via config if needed
            policy.WithOrigins(
                    "http://localhost:5000",
                    "http://localhost:5001",
                    "https://localhost:5000",
                    "https://localhost:5001",
                    "http://127.0.0.1:5000",
                    "http://127.0.0.1:5001")
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

// Add comprehensive health checks
builder.Services.AddHealthChecks()
    .AddSportarrHealthChecks();

var app = builder.Build();

// Apply database migrations automatically on startup
try
{
    Console.WriteLine("[Sportarr] Applying database migrations...");
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        // Check if database exists and has tables but no migration history
        // This happens when database was created with EnsureCreated() instead of Migrate()
        var canConnect = await db.Database.CanConnectAsync();
        var hasMigrationHistory = canConnect && (await db.Database.GetAppliedMigrationsAsync()).Any();

        // Check if AppSettings table exists (core table that should always be present)
        bool hasTables = false;
        if (canConnect)
        {
            using var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AppSettings'";
            var result = await command.ExecuteScalarAsync();
            hasTables = Convert.ToInt32(result) > 0;
        }

        if (canConnect && hasTables && !hasMigrationHistory)
        {
            // Database was created with EnsureCreated() - we need to seed the migration history
            // to prevent migrations from trying to recreate existing tables
            Console.WriteLine("[Sportarr] Detected database created without migrations. Seeding migration history...");

            // Get all migrations that exist in the codebase
            var allMigrations = db.Database.GetMigrations().ToList();

            // Mark all existing migrations as applied (since tables already exist)
            // We'll use a raw SQL approach since the history table doesn't exist yet
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                    ""MigrationId"" TEXT NOT NULL,
                    ""ProductVersion"" TEXT NOT NULL,
                    CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (""MigrationId"")
                )");

            // Insert all migrations as applied (using parameterized query to prevent SQL injection)
            foreach (var migration in allMigrations)
            {
                try
                {
                    db.Database.ExecuteSqlInterpolated(
                        $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({migration}, '8.0.0')");
                    Console.WriteLine($"[Sportarr] Marked migration as applied: {migration}");
                }
                catch
                {
                    // Migration might already be in history, skip
                }
            }

            Console.WriteLine("[Sportarr] Migration history seeded successfully");
        }

        // Now apply any new migrations
        db.Database.Migrate();

        // Ensure MonitoredParts column exists in Leagues table (backwards compatibility fix)
        // This handles cases where migrations were applied but column wasn't created
        try
        {
            var checkColumnSql = "SELECT COUNT(*) FROM pragma_table_info('Leagues') WHERE name='MonitoredParts'";
            var columnExists = db.Database.SqlQueryRaw<int>(checkColumnSql).AsEnumerable().FirstOrDefault();

            if (columnExists == 0)
            {
                Console.WriteLine("[Sportarr] Leagues.MonitoredParts column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE Leagues ADD COLUMN MonitoredParts TEXT");
                Console.WriteLine("[Sportarr] Leagues.MonitoredParts column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify Leagues.MonitoredParts column: {ex.Message}");
        }

        // Ensure MonitoredParts column exists in Events table (backwards compatibility fix)
        try
        {
            var checkEventColumnSql = "SELECT COUNT(*) FROM pragma_table_info('Events') WHERE name='MonitoredParts'";
            var eventColumnExists = db.Database.SqlQueryRaw<int>(checkEventColumnSql).AsEnumerable().FirstOrDefault();

            if (eventColumnExists == 0)
            {
                Console.WriteLine("[Sportarr] Events.MonitoredParts column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE Events ADD COLUMN MonitoredParts TEXT");
                Console.WriteLine("[Sportarr] Events.MonitoredParts column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify Events.MonitoredParts column: {ex.Message}");
        }

        // Ensure DisableSslCertificateValidation column exists in DownloadClients table (backwards compatibility fix)
        try
        {
            var checkSslColumnSql = "SELECT COUNT(*) FROM pragma_table_info('DownloadClients') WHERE name='DisableSslCertificateValidation'";
            var sslColumnExists = db.Database.SqlQueryRaw<int>(checkSslColumnSql).AsEnumerable().FirstOrDefault();

            if (sslColumnExists == 0)
            {
                Console.WriteLine("[Sportarr] DownloadClients.DisableSslCertificateValidation column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE DownloadClients ADD COLUMN DisableSslCertificateValidation INTEGER NOT NULL DEFAULT 0");
                Console.WriteLine("[Sportarr] DownloadClients.DisableSslCertificateValidation column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify DownloadClients.DisableSslCertificateValidation column: {ex.Message}");
        }

        // Ensure SequentialDownload and FirstAndLastFirst columns exist in DownloadClients table (debrid service support)
        try
        {
            var checkSeqColumnSql = "SELECT COUNT(*) FROM pragma_table_info('DownloadClients') WHERE name='SequentialDownload'";
            var seqColumnExists = db.Database.SqlQueryRaw<int>(checkSeqColumnSql).AsEnumerable().FirstOrDefault();

            if (seqColumnExists == 0)
            {
                Console.WriteLine("[Sportarr] DownloadClients.SequentialDownload column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE DownloadClients ADD COLUMN SequentialDownload INTEGER NOT NULL DEFAULT 0");
                Console.WriteLine("[Sportarr] DownloadClients.SequentialDownload column added successfully");
            }

            var checkFirstLastColumnSql = "SELECT COUNT(*) FROM pragma_table_info('DownloadClients') WHERE name='FirstAndLastFirst'";
            var firstLastColumnExists = db.Database.SqlQueryRaw<int>(checkFirstLastColumnSql).AsEnumerable().FirstOrDefault();

            if (firstLastColumnExists == 0)
            {
                Console.WriteLine("[Sportarr] DownloadClients.FirstAndLastFirst column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE DownloadClients ADD COLUMN FirstAndLastFirst INTEGER NOT NULL DEFAULT 0");
                Console.WriteLine("[Sportarr] DownloadClients.FirstAndLastFirst column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify DownloadClients sequential download columns: {ex.Message}");
        }

        // Ensure Directory column exists in DownloadClients table (download directory override feature)
        try
        {
            var checkDirColumnSql = "SELECT COUNT(*) FROM pragma_table_info('DownloadClients') WHERE name='Directory'";
            var dirColumnExists = db.Database.SqlQueryRaw<int>(checkDirColumnSql).AsEnumerable().FirstOrDefault();

            if (dirColumnExists == 0)
            {
                Console.WriteLine("[Sportarr] DownloadClients.Directory column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE DownloadClients ADD COLUMN Directory TEXT NULL");
                Console.WriteLine("[Sportarr] DownloadClients.Directory column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify DownloadClients.Directory column: {ex.Message}");
        }

        // Ensure ImportRetryCount column exists in DownloadQueue table (backwards compatibility fix)
        // This column was added but EF Core migrations may not have run properly on some databases
        try
        {
            var checkImportRetryColumnSql = "SELECT COUNT(*) FROM pragma_table_info('DownloadQueue') WHERE name='ImportRetryCount'";
            var importRetryColumnExists = db.Database.SqlQueryRaw<int>(checkImportRetryColumnSql).AsEnumerable().FirstOrDefault();

            if (importRetryColumnExists == 0)
            {
                Console.WriteLine("[Sportarr] DownloadQueue.ImportRetryCount column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE DownloadQueue ADD COLUMN ImportRetryCount INTEGER NOT NULL DEFAULT 0");
                Console.WriteLine("[Sportarr] DownloadQueue.ImportRetryCount column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify DownloadQueue.ImportRetryCount column: {ex.Message}");
        }

        // Ensure IndexerId column exists in DownloadQueue table (backwards compatibility fix)
        // This column was added for seed config lookup but may be missing on older databases
        try
        {
            var checkIndexerIdColumnSql = "SELECT COUNT(*) FROM pragma_table_info('DownloadQueue') WHERE name='IndexerId'";
            var indexerIdColumnExists = db.Database.SqlQueryRaw<int>(checkIndexerIdColumnSql).AsEnumerable().FirstOrDefault();

            if (indexerIdColumnExists == 0)
            {
                Console.WriteLine("[Sportarr] DownloadQueue.IndexerId column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE DownloadQueue ADD COLUMN IndexerId INTEGER NULL");
                Console.WriteLine("[Sportarr] DownloadQueue.IndexerId column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify DownloadQueue.IndexerId column: {ex.Message}");
        }

        // Remove deprecated UseSymlinks column from MediaManagementSettings if it exists
        // (Decypharr handles symlinks itself, Sportarr doesn't need this setting)
        try
        {
            var checkSymlinkColumnSql = "SELECT COUNT(*) FROM pragma_table_info('MediaManagementSettings') WHERE name='UseSymlinks'";
            var symlinkColumnExists = db.Database.SqlQueryRaw<int>(checkSymlinkColumnSql).AsEnumerable().FirstOrDefault();

            if (symlinkColumnExists > 0)
            {
                Console.WriteLine("[Sportarr] Removing deprecated UseSymlinks column from MediaManagementSettings...");
                // SQLite doesn't support DROP COLUMN directly before 3.35.0, so we need to recreate the table
                // However, EF Core will simply ignore the extra column, so we can leave it for now
                // The column won't be used and will be cleaned up on next migration
                Console.WriteLine("[Sportarr] UseSymlinks column will be ignored (deprecated setting removed)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not check for deprecated UseSymlinks column: {ex.Message}");
        }

        // Ensure EventFiles table exists (backwards compatibility fix for file tracking)
        // This handles cases where migration history was seeded before EventFiles migration existed
        try
        {
            var checkTableSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='EventFiles'";
            var tableExists = db.Database.SqlQueryRaw<int>(checkTableSql).AsEnumerable().FirstOrDefault();

            if (tableExists == 0)
            {
                Console.WriteLine("[Sportarr] EventFiles table missing - creating it now...");

                // Create EventFiles table with all columns and indexes
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE ""EventFiles"" (
                        ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""EventId"" INTEGER NOT NULL,
                        ""FilePath"" TEXT NOT NULL,
                        ""Size"" INTEGER NOT NULL,
                        ""Quality"" TEXT NULL,
                        ""PartName"" TEXT NULL,
                        ""PartNumber"" INTEGER NULL,
                        ""Added"" TEXT NOT NULL,
                        ""LastVerified"" TEXT NULL,
                        ""Exists"" INTEGER NOT NULL DEFAULT 1,
                        CONSTRAINT ""FK_EventFiles_Events_EventId"" FOREIGN KEY (""EventId"") REFERENCES ""Events"" (""Id"") ON DELETE CASCADE
                    )");

                // Create indexes
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_EventFiles_EventId"" ON ""EventFiles"" (""EventId"")");
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_EventFiles_PartNumber"" ON ""EventFiles"" (""PartNumber"")");
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_EventFiles_Exists"" ON ""EventFiles"" (""Exists"")");

                Console.WriteLine("[Sportarr] EventFiles table created successfully");
                Console.WriteLine("[Sportarr] File tracking is now enabled for all sports");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify EventFiles table: {ex.Message}");
        }

        // Ensure PendingImports table exists (for external download detection feature)
        try
        {
            var checkTableSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PendingImports'";
            var tableExists = db.Database.SqlQueryRaw<int>(checkTableSql).AsEnumerable().FirstOrDefault();

            if (tableExists == 0)
            {
                Console.WriteLine("[Sportarr] PendingImports table missing - creating it now...");

                // Create PendingImports table with all columns
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE ""PendingImports"" (
                        ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""DownloadClientId"" INTEGER NULL,
                        ""DownloadId"" TEXT NOT NULL,
                        ""Title"" TEXT NOT NULL,
                        ""FilePath"" TEXT NOT NULL,
                        ""Size"" INTEGER NOT NULL DEFAULT 0,
                        ""Quality"" TEXT NULL,
                        ""QualityScore"" INTEGER NOT NULL DEFAULT 0,
                        ""Status"" INTEGER NOT NULL DEFAULT 0,
                        ""ErrorMessage"" TEXT NULL,
                        ""SuggestedEventId"" INTEGER NULL,
                        ""SuggestedPart"" TEXT NULL,
                        ""SuggestionConfidence"" INTEGER NOT NULL DEFAULT 0,
                        ""Detected"" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        ""ResolvedAt"" TEXT NULL,
                        ""Protocol"" TEXT NULL,
                        ""TorrentInfoHash"" TEXT NULL,
                        CONSTRAINT ""FK_PendingImports_DownloadClients_DownloadClientId"" FOREIGN KEY (""DownloadClientId"") REFERENCES ""DownloadClients"" (""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_PendingImports_Events_SuggestedEventId"" FOREIGN KEY (""SuggestedEventId"") REFERENCES ""Events"" (""Id"") ON DELETE SET NULL
                    )");

                // Create indexes
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_PendingImports_DownloadClientId"" ON ""PendingImports"" (""DownloadClientId"")");
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_PendingImports_SuggestedEventId"" ON ""PendingImports"" (""SuggestedEventId"")");
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_PendingImports_Status"" ON ""PendingImports"" (""Status"")");

                Console.WriteLine("[Sportarr] PendingImports table created successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify PendingImports table: {ex.Message}");
        }

        // Ensure PendingImports has IsPack, FileCount, MatchedEventsCount columns (added for pack import support)
        try
        {
            var checkTableSql2 = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PendingImports'";
            var table2Exists = db.Database.SqlQueryRaw<int>(checkTableSql2).AsEnumerable().FirstOrDefault();

            if (table2Exists > 0)
            {
                var checkIsPack = "SELECT COUNT(*) FROM pragma_table_info('PendingImports') WHERE name='IsPack'";
                var isPackExists = db.Database.SqlQueryRaw<int>(checkIsPack).AsEnumerable().FirstOrDefault();

                if (isPackExists == 0)
                {
                    Console.WriteLine("[Sportarr] Adding IsPack/FileCount/MatchedEventsCount columns to PendingImports...");
                    db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PendingImports"" ADD COLUMN ""IsPack"" INTEGER NOT NULL DEFAULT 0");
                    db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PendingImports"" ADD COLUMN ""FileCount"" INTEGER NOT NULL DEFAULT 0");
                    db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PendingImports"" ADD COLUMN ""MatchedEventsCount"" INTEGER NOT NULL DEFAULT 0");
                    Console.WriteLine("[Sportarr] PendingImports columns added successfully");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not add PendingImports columns: {ex.Message}");
        }

        // Ensure PendingImports.DownloadClientId is nullable (for disk-discovered files with no download client)
        // SQLite doesn't support ALTER COLUMN, so we rebuild the table if needed
        try
        {
            var checkNullableSql = "SELECT COUNT(*) FROM pragma_table_info('PendingImports') WHERE name='DownloadClientId' AND \"notnull\" = 1";
            var isNotNull = db.Database.SqlQueryRaw<int>(checkNullableSql).AsEnumerable().FirstOrDefault();

            if (isNotNull > 0) // Column is NOT NULL, needs to be nullable
            {
                Console.WriteLine("[Sportarr] Rebuilding PendingImports table to make DownloadClientId nullable...");

                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE ""PendingImports_new"" (
                        ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""DownloadClientId"" INTEGER NULL,
                        ""DownloadId"" TEXT NOT NULL,
                        ""Title"" TEXT NOT NULL,
                        ""FilePath"" TEXT NOT NULL,
                        ""Size"" INTEGER NOT NULL DEFAULT 0,
                        ""Quality"" TEXT NULL,
                        ""QualityScore"" INTEGER NOT NULL DEFAULT 0,
                        ""Status"" INTEGER NOT NULL DEFAULT 0,
                        ""ErrorMessage"" TEXT NULL,
                        ""SuggestedEventId"" INTEGER NULL,
                        ""SuggestedPart"" TEXT NULL,
                        ""SuggestionConfidence"" INTEGER NOT NULL DEFAULT 0,
                        ""Detected"" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        ""ResolvedAt"" TEXT NULL,
                        ""Protocol"" TEXT NULL,
                        ""TorrentInfoHash"" TEXT NULL,
                        ""IsPack"" INTEGER NOT NULL DEFAULT 0,
                        ""FileCount"" INTEGER NOT NULL DEFAULT 0,
                        ""MatchedEventsCount"" INTEGER NOT NULL DEFAULT 0,
                        CONSTRAINT ""FK_PendingImports_DownloadClients_DownloadClientId"" FOREIGN KEY (""DownloadClientId"") REFERENCES ""DownloadClients"" (""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_PendingImports_Events_SuggestedEventId"" FOREIGN KEY (""SuggestedEventId"") REFERENCES ""Events"" (""Id"") ON DELETE SET NULL
                    )");

                db.Database.ExecuteSqlRaw(@"
                    INSERT INTO ""PendingImports_new"" (""Id"", ""DownloadClientId"", ""DownloadId"", ""Title"", ""FilePath"", ""Size"", ""Quality"", ""QualityScore"", ""Status"", ""ErrorMessage"", ""SuggestedEventId"", ""SuggestedPart"", ""SuggestionConfidence"", ""Detected"", ""ResolvedAt"", ""Protocol"", ""TorrentInfoHash"", ""IsPack"", ""FileCount"", ""MatchedEventsCount"")
                    SELECT ""Id"", CASE WHEN ""DownloadClientId"" = 0 THEN NULL ELSE ""DownloadClientId"" END, ""DownloadId"", ""Title"", ""FilePath"", ""Size"", ""Quality"", ""QualityScore"", ""Status"", ""ErrorMessage"", ""SuggestedEventId"", ""SuggestedPart"", ""SuggestionConfidence"", ""Detected"", ""ResolvedAt"", ""Protocol"", ""TorrentInfoHash"", ""IsPack"", ""FileCount"", ""MatchedEventsCount""
                    FROM ""PendingImports""");

                db.Database.ExecuteSqlRaw(@"DROP TABLE ""PendingImports""");
                db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PendingImports_new"" RENAME TO ""PendingImports""");
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_PendingImports_DownloadClientId"" ON ""PendingImports"" (""DownloadClientId"")");
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_PendingImports_SuggestedEventId"" ON ""PendingImports"" (""SuggestedEventId"")");
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_PendingImports_Status"" ON ""PendingImports"" (""Status"")");
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_PendingImports_DownloadId"" ON ""PendingImports"" (""DownloadId"")");

                Console.WriteLine("[Sportarr] PendingImports table rebuilt successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify PendingImports.DownloadClientId nullability: {ex.Message}");
        }

        // Ensure EnableMultiPartEpisodes column exists in MediaManagementSettings (backwards compatibility fix)
        // This handles cases where migration history was seeded before the column was added
        try
        {
            var checkColumnSql = "SELECT COUNT(*) FROM pragma_table_info('MediaManagementSettings') WHERE name='EnableMultiPartEpisodes'";
            var columnExists = db.Database.SqlQueryRaw<int>(checkColumnSql).AsEnumerable().FirstOrDefault();

            if (columnExists == 0)
            {
                Console.WriteLine("[Sportarr] EnableMultiPartEpisodes column missing - adding it now...");
                db.Database.ExecuteSqlRaw(@"ALTER TABLE ""MediaManagementSettings"" ADD COLUMN ""EnableMultiPartEpisodes"" INTEGER NOT NULL DEFAULT 1");
                Console.WriteLine("[Sportarr] EnableMultiPartEpisodes column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify EnableMultiPartEpisodes column: {ex.Message}");
        }

        // Ensure Events.BroadcastDate column exists.
        // The seeding code above marks every migration in the assembly as
        // applied for legacy EnsureCreated() databases - including newer
        // migrations whose columns don't actually exist yet. This safety net
        // catches that case by checking the column directly.
        try
        {
            var checkSql = "SELECT COUNT(*) FROM pragma_table_info('Events') WHERE name='BroadcastDate'";
            var exists = db.Database.SqlQueryRaw<int>(checkSql).AsEnumerable().FirstOrDefault();
            if (exists == 0)
            {
                Console.WriteLine("[Sportarr] Events.BroadcastDate column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE \"Events\" ADD COLUMN \"BroadcastDate\" TEXT NULL");
                Console.WriteLine("[Sportarr] Events.BroadcastDate column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify Events.BroadcastDate column: {ex.Message}");
        }

        // Ensure AppSettings.IndexerMinimumAgeMinutes column exists.
        // Same EnsureCreated() seeding edge case as above.
        try
        {
            var checkSql = "SELECT COUNT(*) FROM pragma_table_info('AppSettings') WHERE name='IndexerMinimumAgeMinutes'";
            var exists = db.Database.SqlQueryRaw<int>(checkSql).AsEnumerable().FirstOrDefault();
            if (exists == 0)
            {
                Console.WriteLine("[Sportarr] AppSettings.IndexerMinimumAgeMinutes column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE \"AppSettings\" ADD COLUMN \"IndexerMinimumAgeMinutes\" INTEGER NOT NULL DEFAULT 0");
                Console.WriteLine("[Sportarr] AppSettings.IndexerMinimumAgeMinutes column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify AppSettings.IndexerMinimumAgeMinutes column: {ex.Message}");
        }

        // Ensure PendingReleases table exists (delay-profile feature).
        // Required by RssSyncService and PendingReleaseReaperService - without
        // this table both services would crash on first run for legacy DBs.
        try
        {
            var checkTableSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PendingReleases'";
            var tableExists = db.Database.SqlQueryRaw<int>(checkTableSql).AsEnumerable().FirstOrDefault();

            if (tableExists == 0)
            {
                Console.WriteLine("[Sportarr] PendingReleases table missing - creating it now...");

                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE ""PendingReleases"" (
                        ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""EventId"" INTEGER NOT NULL,
                        ""Title"" TEXT NOT NULL,
                        ""Guid"" TEXT NOT NULL,
                        ""DownloadUrl"" TEXT NOT NULL,
                        ""InfoUrl"" TEXT NULL,
                        ""Indexer"" TEXT NOT NULL,
                        ""IndexerId"" INTEGER NULL,
                        ""TorrentInfoHash"" TEXT NULL,
                        ""Protocol"" TEXT NOT NULL,
                        ""Size"" INTEGER NOT NULL,
                        ""Quality"" TEXT NULL,
                        ""Source"" TEXT NULL,
                        ""Codec"" TEXT NULL,
                        ""Language"" TEXT NULL,
                        ""ReleaseGroup"" TEXT NULL,
                        ""QualityScore"" INTEGER NOT NULL,
                        ""CustomFormatScore"" INTEGER NOT NULL,
                        ""Score"" INTEGER NOT NULL,
                        ""MatchScore"" INTEGER NOT NULL,
                        ""Part"" TEXT NULL,
                        ""Seeders"" INTEGER NULL,
                        ""Leechers"" INTEGER NULL,
                        ""PublishDate"" TEXT NOT NULL,
                        ""AddedToPendingAt"" TEXT NOT NULL,
                        ""ReleasableAt"" TEXT NOT NULL,
                        ""Reason"" TEXT NOT NULL,
                        ""Status"" INTEGER NOT NULL,
                        CONSTRAINT ""FK_PendingReleases_Events_EventId"" FOREIGN KEY (""EventId"") REFERENCES ""Events"" (""Id"") ON DELETE CASCADE
                    )");

                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_PendingReleases_EventId"" ON ""PendingReleases"" (""EventId"")");
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_PendingReleases_Status_ReleasableAt"" ON ""PendingReleases"" (""Status"", ""ReleasableAt"")");

                Console.WriteLine("[Sportarr] PendingReleases table created successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify PendingReleases table: {ex.Message}");
        }

        // Ensure granular folder format/creation columns exist in MediaManagementSettings
        // These were added after some installs and may be missing from older databases
        try
        {
            var columnsToAdd = new[]
            {
                ("LeagueFolderFormat", "TEXT NOT NULL DEFAULT '{Series}'"),
                ("SeasonFolderFormat", "TEXT NOT NULL DEFAULT 'Season {Season}'"),
                ("CreateLeagueFolders", "INTEGER NOT NULL DEFAULT 1"),
                ("CreateSeasonFolders", "INTEGER NOT NULL DEFAULT 1"),
                ("ReorganizeFolders", "INTEGER NOT NULL DEFAULT 0"),
            };

            foreach (var (columnName, columnDef) in columnsToAdd)
            {
                var checkSql = $"SELECT COUNT(*) FROM pragma_table_info('MediaManagementSettings') WHERE name='{columnName}'";
                var exists = db.Database.SqlQueryRaw<int>(checkSql).AsEnumerable().FirstOrDefault();
                if (exists == 0)
                {
                    Console.WriteLine($"[Sportarr] Adding missing column {columnName} to MediaManagementSettings...");
                    db.Database.ExecuteSqlRaw("ALTER TABLE \"MediaManagementSettings\" ADD COLUMN \"" + columnName + "\" " + columnDef);
                    Console.WriteLine($"[Sportarr] Column {columnName} added successfully");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not add missing MediaManagementSettings columns: {ex.Message}");
        }

        // Remove deprecated StandardEventFormat column if it exists (backwards compatibility fix)
        // This column was removed but migration may not have run properly on some databases
        try
        {
            var checkColumnSql = "SELECT COUNT(*) FROM pragma_table_info('MediaManagementSettings') WHERE name='StandardEventFormat'";
            var columnExists = db.Database.SqlQueryRaw<int>(checkColumnSql).AsEnumerable().FirstOrDefault();

            if (columnExists > 0)
            {
                Console.WriteLine("[Sportarr] Removing deprecated StandardEventFormat column from MediaManagementSettings...");
                // SQLite doesn't support DROP COLUMN directly, so we need to recreate the table
                // Note: Using single quotes for SQL string literals (not C# interpolation)
                var createTableSql = @"
                    CREATE TABLE IF NOT EXISTS MediaManagementSettings_new (
                        Id INTEGER PRIMARY KEY,
                        RenameFiles INTEGER NOT NULL DEFAULT 1,
                        StandardFileFormat TEXT NOT NULL DEFAULT '{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}',
                        EventFolderFormat TEXT NOT NULL DEFAULT '{Event Title}',
                        LeagueFolderFormat TEXT NOT NULL DEFAULT '{Series}',
                        SeasonFolderFormat TEXT NOT NULL DEFAULT 'Season {Season}',
                        CreateEventFolder INTEGER NOT NULL DEFAULT 1,
                        RenameEvents INTEGER NOT NULL DEFAULT 0,
                        ReplaceIllegalCharacters INTEGER NOT NULL DEFAULT 1,
                        CreateLeagueFolders INTEGER NOT NULL DEFAULT 1,
                        CreateSeasonFolders INTEGER NOT NULL DEFAULT 1,
                        CreateEventFolders INTEGER NOT NULL DEFAULT 1,
                        ReorganizeFolders INTEGER NOT NULL DEFAULT 0,
                        DeleteEmptyFolders INTEGER NOT NULL DEFAULT 0,
                        SkipFreeSpaceCheck INTEGER NOT NULL DEFAULT 0,
                        MinimumFreeSpace INTEGER NOT NULL DEFAULT 100,
                        UseHardlinks INTEGER NOT NULL DEFAULT 1,
                        ImportExtraFiles INTEGER NOT NULL DEFAULT 0,
                        ExtraFileExtensions TEXT NOT NULL DEFAULT 'srt,nfo',
                        ChangeFileDate TEXT NOT NULL DEFAULT 'None',
                        RecycleBin TEXT NOT NULL DEFAULT '',
                        RecycleBinCleanup INTEGER NOT NULL DEFAULT 7,
                        SetPermissions INTEGER NOT NULL DEFAULT 0,
                        FileChmod TEXT NOT NULL DEFAULT '644',
                        ChmodFolder TEXT NOT NULL DEFAULT '755',
                        ChownUser TEXT NOT NULL DEFAULT '',
                        ChownGroup TEXT NOT NULL DEFAULT '',
                        CopyFiles INTEGER NOT NULL DEFAULT 0,
                        Created TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        LastModified TEXT,
                        EnableMultiPartEpisodes INTEGER NOT NULL DEFAULT 1,
                        RootFolders TEXT NOT NULL DEFAULT '[]'
                    )";

                using var connection = db.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                    await connection.OpenAsync();

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = createTableSql;
                    await cmd.ExecuteNonQueryAsync();
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO MediaManagementSettings_new (
                            Id, RenameFiles, StandardFileFormat, EventFolderFormat,
                            LeagueFolderFormat, SeasonFolderFormat,
                            CreateEventFolder, RenameEvents, ReplaceIllegalCharacters,
                            CreateLeagueFolders, CreateSeasonFolders, CreateEventFolders, ReorganizeFolders,
                            DeleteEmptyFolders, SkipFreeSpaceCheck, MinimumFreeSpace, UseHardlinks,
                            ImportExtraFiles, ExtraFileExtensions, ChangeFileDate, RecycleBin, RecycleBinCleanup,
                            SetPermissions, FileChmod, ChmodFolder, ChownUser, ChownGroup,
                            CopyFiles, Created, LastModified,
                            EnableMultiPartEpisodes, RootFolders
                        )
                        SELECT
                            Id, RenameFiles, StandardFileFormat, EventFolderFormat,
                            COALESCE(LeagueFolderFormat, '{Series}'), COALESCE(SeasonFolderFormat, 'Season {Season}'),
                            CreateEventFolder, RenameEvents, ReplaceIllegalCharacters,
                            COALESCE(CreateLeagueFolders, 1), COALESCE(CreateSeasonFolders, 1), CreateEventFolders, COALESCE(ReorganizeFolders, 0),
                            DeleteEmptyFolders, SkipFreeSpaceCheck, MinimumFreeSpace, UseHardlinks,
                            ImportExtraFiles, ExtraFileExtensions, ChangeFileDate, RecycleBin, RecycleBinCleanup,
                            SetPermissions, FileChmod, ChmodFolder, ChownUser, ChownGroup,
                            CopyFiles, Created, LastModified,
                            COALESCE(EnableMultiPartEpisodes, 1), COALESCE(RootFolders, '[]')
                        FROM MediaManagementSettings";
                    await cmd.ExecuteNonQueryAsync();
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "DROP TABLE MediaManagementSettings";
                    await cmd.ExecuteNonQueryAsync();
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "ALTER TABLE MediaManagementSettings_new RENAME TO MediaManagementSettings";
                    await cmd.ExecuteNonQueryAsync();
                }

                Console.WriteLine("[Sportarr] StandardEventFormat column removed successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not remove StandardEventFormat column: {ex.Message}");
        }

        // Remove deprecated RemoveCompletedDownloads/RemoveFailedDownloads from MediaManagementSettings
        // These were moved to per-client settings but initial migration created them as NOT NULL without DEFAULT
        // The StandardEventFormat migration above handles this for fresh installs, but users who updated
        // through intermediate versions may have had StandardEventFormat removed while these columns remained
        try
        {
            var checkRemoveCol = "SELECT COUNT(*) FROM pragma_table_info('MediaManagementSettings') WHERE name='RemoveCompletedDownloads'";
            var removeColExists = db.Database.SqlQueryRaw<int>(checkRemoveCol).AsEnumerable().FirstOrDefault();

            if (removeColExists > 0)
            {
                Console.WriteLine("[Sportarr] Removing deprecated RemoveCompletedDownloads/RemoveFailedDownloads columns from MediaManagementSettings...");

                var createTableSql = @"
                    CREATE TABLE IF NOT EXISTS MediaManagementSettings_new (
                        Id INTEGER PRIMARY KEY,
                        RenameFiles INTEGER NOT NULL DEFAULT 1,
                        StandardFileFormat TEXT NOT NULL DEFAULT '{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}',
                        EventFolderFormat TEXT NOT NULL DEFAULT '{Event Title}',
                        LeagueFolderFormat TEXT NOT NULL DEFAULT '{Series}',
                        SeasonFolderFormat TEXT NOT NULL DEFAULT 'Season {Season}',
                        CreateEventFolder INTEGER NOT NULL DEFAULT 1,
                        RenameEvents INTEGER NOT NULL DEFAULT 0,
                        ReplaceIllegalCharacters INTEGER NOT NULL DEFAULT 1,
                        CreateLeagueFolders INTEGER NOT NULL DEFAULT 1,
                        CreateSeasonFolders INTEGER NOT NULL DEFAULT 1,
                        CreateEventFolders INTEGER NOT NULL DEFAULT 1,
                        ReorganizeFolders INTEGER NOT NULL DEFAULT 0,
                        DeleteEmptyFolders INTEGER NOT NULL DEFAULT 0,
                        SkipFreeSpaceCheck INTEGER NOT NULL DEFAULT 0,
                        MinimumFreeSpace INTEGER NOT NULL DEFAULT 100,
                        UseHardlinks INTEGER NOT NULL DEFAULT 1,
                        ImportExtraFiles INTEGER NOT NULL DEFAULT 0,
                        ExtraFileExtensions TEXT NOT NULL DEFAULT 'srt,nfo',
                        ChangeFileDate TEXT NOT NULL DEFAULT 'None',
                        RecycleBin TEXT NOT NULL DEFAULT '',
                        RecycleBinCleanup INTEGER NOT NULL DEFAULT 7,
                        SetPermissions INTEGER NOT NULL DEFAULT 0,
                        FileChmod TEXT NOT NULL DEFAULT '644',
                        ChmodFolder TEXT NOT NULL DEFAULT '755',
                        ChownUser TEXT NOT NULL DEFAULT '',
                        ChownGroup TEXT NOT NULL DEFAULT '',
                        CopyFiles INTEGER NOT NULL DEFAULT 0,
                        Created TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        LastModified TEXT,
                        EnableMultiPartEpisodes INTEGER NOT NULL DEFAULT 1,
                        RootFolders TEXT NOT NULL DEFAULT '[]'
                    )";

                using var connection2 = db.Database.GetDbConnection();
                if (connection2.State != System.Data.ConnectionState.Open)
                    await connection2.OpenAsync();

                using (var cmd = connection2.CreateCommand())
                {
                    cmd.CommandText = createTableSql;
                    await cmd.ExecuteNonQueryAsync();
                }

                using (var cmd = connection2.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO MediaManagementSettings_new (
                            Id, RenameFiles, StandardFileFormat, EventFolderFormat,
                            LeagueFolderFormat, SeasonFolderFormat,
                            CreateEventFolder, RenameEvents, ReplaceIllegalCharacters,
                            CreateLeagueFolders, CreateSeasonFolders, CreateEventFolders, ReorganizeFolders,
                            DeleteEmptyFolders, SkipFreeSpaceCheck, MinimumFreeSpace, UseHardlinks,
                            ImportExtraFiles, ExtraFileExtensions, ChangeFileDate, RecycleBin, RecycleBinCleanup,
                            SetPermissions, FileChmod, ChmodFolder, ChownUser, ChownGroup,
                            CopyFiles, Created, LastModified,
                            EnableMultiPartEpisodes, RootFolders
                        )
                        SELECT
                            Id, RenameFiles, StandardFileFormat, EventFolderFormat,
                            COALESCE(LeagueFolderFormat, '{Series}'), COALESCE(SeasonFolderFormat, 'Season {Season}'),
                            CreateEventFolder, RenameEvents, ReplaceIllegalCharacters,
                            COALESCE(CreateLeagueFolders, 1), COALESCE(CreateSeasonFolders, 1), CreateEventFolders, COALESCE(ReorganizeFolders, 0),
                            DeleteEmptyFolders, SkipFreeSpaceCheck, MinimumFreeSpace, UseHardlinks,
                            ImportExtraFiles, ExtraFileExtensions, ChangeFileDate, RecycleBin, RecycleBinCleanup,
                            SetPermissions, FileChmod, ChmodFolder, ChownUser, ChownGroup,
                            CopyFiles, Created, LastModified,
                            COALESCE(EnableMultiPartEpisodes, 1), COALESCE(RootFolders, '[]')
                        FROM MediaManagementSettings";
                    await cmd.ExecuteNonQueryAsync();
                }

                using (var cmd = connection2.CreateCommand())
                {
                    cmd.CommandText = "DROP TABLE MediaManagementSettings";
                    await cmd.ExecuteNonQueryAsync();
                }

                using (var cmd = connection2.CreateCommand())
                {
                    cmd.CommandText = "ALTER TABLE MediaManagementSettings_new RENAME TO MediaManagementSettings";
                    await cmd.ExecuteNonQueryAsync();
                }

                Console.WriteLine("[Sportarr] Deprecated download columns removed successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not remove deprecated download columns: {ex.Message}");
        }

        // Ensure RedownloadFailedFromInteractiveSearch column exists in AppSettings (added in download settings rework)
        try
        {
            var checkRedownloadInteractiveCol = "SELECT COUNT(*) FROM pragma_table_info('AppSettings') WHERE name='RedownloadFailedFromInteractiveSearch'";
            var redownloadInteractiveExists = db.Database.SqlQueryRaw<int>(checkRedownloadInteractiveCol).AsEnumerable().FirstOrDefault();

            if (redownloadInteractiveExists == 0)
            {
                Console.WriteLine("[Sportarr] AppSettings.RedownloadFailedFromInteractiveSearch column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE AppSettings ADD COLUMN RedownloadFailedFromInteractiveSearch INTEGER NOT NULL DEFAULT 1");
                Console.WriteLine("[Sportarr] AppSettings.RedownloadFailedFromInteractiveSearch column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify AppSettings.RedownloadFailedFromInteractiveSearch column: {ex.Message}");
        }

        // Ensure IsManualSearch column exists in DownloadQueue (added in download settings rework)
        try
        {
            var checkIsManualSearchCol = "SELECT COUNT(*) FROM pragma_table_info('DownloadQueue') WHERE name='IsManualSearch'";
            var isManualSearchExists = db.Database.SqlQueryRaw<int>(checkIsManualSearchCol).AsEnumerable().FirstOrDefault();

            if (isManualSearchExists == 0)
            {
                Console.WriteLine("[Sportarr] DownloadQueue.IsManualSearch column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE DownloadQueue ADD COLUMN IsManualSearch INTEGER NOT NULL DEFAULT 0");
                Console.WriteLine("[Sportarr] DownloadQueue.IsManualSearch column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify DownloadQueue.IsManualSearch column: {ex.Message}");
        }

        // Ensure ReleaseGroup column exists in EventFiles table (for file renaming with {Release Group} token)
        try
        {
            var checkRgColumnSql = "SELECT COUNT(*) FROM pragma_table_info('EventFiles') WHERE name='ReleaseGroup'";
            var rgColumnExists = db.Database.SqlQueryRaw<int>(checkRgColumnSql).AsEnumerable().FirstOrDefault();

            if (rgColumnExists == 0)
            {
                Console.WriteLine("[Sportarr] EventFiles.ReleaseGroup column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE EventFiles ADD COLUMN ReleaseGroup TEXT");
                Console.WriteLine("[Sportarr] EventFiles.ReleaseGroup column added successfully");

                // Backfill release groups from existing OriginalTitle values
                var filesWithOriginalTitle = await db.EventFiles
                    .Where(ef => ef.OriginalTitle != null && ef.OriginalTitle != "")
                    .ToListAsync();

                int backfilled = 0;
                foreach (var ef in filesWithOriginalTitle)
                {
                    var rgMatch = System.Text.RegularExpressions.Regex.Match(
                        ef.OriginalTitle!, @"-([A-Za-z0-9]+)(?:\.[a-z]{2,4})?$");
                    if (rgMatch.Success)
                    {
                        var group = rgMatch.Groups[1].Value;
                        var excluded = new[] { "DL", "WEB", "HD", "SD", "UHD" };
                        if (!excluded.Contains(group.ToUpper()))
                        {
                            ef.ReleaseGroup = group;
                            backfilled++;
                        }
                    }
                }

                if (backfilled > 0)
                {
                    await db.SaveChangesAsync();
                    Console.WriteLine($"[Sportarr] Backfilled ReleaseGroup for {backfilled} existing files");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify EventFiles.ReleaseGroup column: {ex.Message}");
        }

        // Ensure DownloadId column exists in GrabHistory table (for external download detection)
        try
        {
            var checkGhColumnSql = "SELECT COUNT(*) FROM pragma_table_info('GrabHistory') WHERE name='DownloadId'";
            var ghColumnExists = db.Database.SqlQueryRaw<int>(checkGhColumnSql).AsEnumerable().FirstOrDefault();

            if (ghColumnExists == 0)
            {
                Console.WriteLine("[Sportarr] GrabHistory.DownloadId column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE GrabHistory ADD COLUMN DownloadId TEXT");

                // Backfill for torrents: qBittorrent uses TorrentInfoHash as DownloadId
                db.Database.ExecuteSqlRaw(
                    "UPDATE GrabHistory SET DownloadId = TorrentInfoHash WHERE TorrentInfoHash IS NOT NULL AND DownloadId IS NULL");

                var backfilledCount = db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) FROM GrabHistory WHERE DownloadId IS NOT NULL").AsEnumerable().FirstOrDefault();
                Console.WriteLine($"[Sportarr] GrabHistory.DownloadId column added (backfilled {backfilledCount} torrent grabs)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify GrabHistory.DownloadId column: {ex.Message}");
        }

        // Recalculate QualityScore for all EventFiles and DownloadQueueItems
        // Previous scoring used inverted profile-index logic (SDTV scored higher than 1080p)
        // Now uses deterministic resolution + source scoring
        try
        {
            var filesToFix = await db.EventFiles.Where(f => f.Quality != null).ToListAsync();
            var fixedFiles = 0;
            foreach (var file in filesToFix)
            {
                var correctScore = ReleaseEvaluator.CalculateQualityScoreFromName(file.Quality);
                if (file.QualityScore != correctScore)
                {
                    file.QualityScore = correctScore;
                    fixedFiles++;
                }
            }

            var queueToFix = await db.DownloadQueue.Where(d => d.Quality != null).ToListAsync();
            var fixedQueue = 0;
            foreach (var item in queueToFix)
            {
                var correctScore = ReleaseEvaluator.CalculateQualityScoreFromName(item.Quality);
                if (item.QualityScore != correctScore)
                {
                    item.QualityScore = correctScore;
                    fixedQueue++;
                }
            }

            if (fixedFiles > 0 || fixedQueue > 0)
            {
                await db.SaveChangesAsync();
                Console.WriteLine($"[Sportarr] Recalculated QualityScore: {fixedFiles} files, {fixedQueue} queue items updated");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not recalculate QualityScore: {ex.Message}");
        }

        // Ensure Tags columns exist for tag-based filtering support
        try
        {
            var tagsTables = new[] { ("Leagues", "Tags"), ("DownloadClients", "Tags"), ("Notifications", "Tags"), ("Indexers", "Tags") };
            foreach (var (table, column) in tagsTables)
            {
                var checkSql = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
                var exists = db.Database.SqlQueryRaw<int>(checkSql).AsEnumerable().FirstOrDefault();
                if (exists == 0)
                {
                    Console.WriteLine($"[Sportarr] {table}.{column} column missing - adding it now...");
                    db.Database.ExecuteSqlRaw($"ALTER TABLE {table} ADD COLUMN {column} TEXT NOT NULL DEFAULT '[]'");
                    Console.WriteLine($"[Sportarr] {table}.{column} column added successfully");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify Tags columns: {ex.Message}");
        }

        // Clean up orphaned events (events whose leagues no longer exist)
        try
        {
            var orphanedEvents = await db.Events
                .Where(e => e.LeagueId == null || !db.Leagues.Any(l => l.Id == e.LeagueId))
                .ToListAsync();

            if (orphanedEvents.Count > 0)
            {
                Console.WriteLine($"[Sportarr] Found {orphanedEvents.Count} orphaned events (no league) - cleaning up...");
                db.Events.RemoveRange(orphanedEvents);
                await db.SaveChangesAsync();
                Console.WriteLine($"[Sportarr] Successfully removed {orphanedEvents.Count} orphaned events");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not clean up orphaned events: {ex.Message}");
        }

        // Clean up incomplete tasks on startup (Sonarr-style behavior)
        // Tasks that were Queued or Running when the app shut down should be cleared
        // This prevents old queued searches from unexpectedly executing after restart
        try
        {
            var incompleteTasks = await db.Tasks
                .Where(t => t.Status == Sportarr.Api.Models.TaskStatus.Queued ||
                           t.Status == Sportarr.Api.Models.TaskStatus.Running ||
                           t.Status == Sportarr.Api.Models.TaskStatus.Aborting)
                .ToListAsync();

            if (incompleteTasks.Count > 0)
            {
                Console.WriteLine($"[Sportarr] Found {incompleteTasks.Count} incomplete tasks from previous session - cleaning up...");
                foreach (var task in incompleteTasks)
                {
                    task.Status = Sportarr.Api.Models.TaskStatus.Cancelled;
                    task.Ended = DateTime.UtcNow;
                    task.Message = "Cancelled: Application was restarted";
                }
                await db.SaveChangesAsync();
                Console.WriteLine($"[Sportarr] Marked {incompleteTasks.Count} tasks as cancelled");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not clean up incomplete tasks: {ex.Message}");
        }
    }
    Console.WriteLine("[Sportarr] Database migrations applied successfully");

    // Ensure StandardFileFormat is updated to new default format (backwards compatibility fix)
    // This runs AFTER migrations so EnableMultiPartEpisodes column exists
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        try
        {
            var mediaSettings = await db.MediaManagementSettings.FirstOrDefaultAsync();
            if (mediaSettings != null)
            {
                const string correctFormat = "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}";
                const string correctFormatNoPart = "{Series} - {Season}{Episode} - {Event Title} - {Quality Full}";

                // Check if StandardFileFormat needs to be updated
                var currentFormat = mediaSettings.StandardFileFormat ?? "";

                // Only update if it's NOT already in the correct format
                if (!currentFormat.Equals(correctFormat, StringComparison.OrdinalIgnoreCase) &&
                    !currentFormat.Equals(correctFormatNoPart, StringComparison.OrdinalIgnoreCase))
                {
                    // Check if this is an old format that should be replaced
                    var oldFormats = new[]
                    {
                        "{Event Title} - {Event Date} - {League}",
                        "{Event Title} - {Air Date} - {Quality Full}",
                        "{League}/{Event Title}",
                        "{Event Title}",
                        ""
                    };

                    if (oldFormats.Any(f => f.Equals(currentFormat, StringComparison.OrdinalIgnoreCase)) ||
                        string.IsNullOrWhiteSpace(currentFormat))
                    {
                        Console.WriteLine($"[Sportarr] Updating StandardFileFormat from '{currentFormat}' to new Plex-style format...");
                        mediaSettings.StandardFileFormat = correctFormat;
                        await db.SaveChangesAsync();
                        Console.WriteLine("[Sportarr] StandardFileFormat updated successfully");
                    }
                    else
                    {
                        // User has a custom format - log but don't update
                        Console.WriteLine($"[Sportarr] StandardFileFormat is custom: '{currentFormat}' - not updating automatically");
                    }
                }
                else
                {
                    Console.WriteLine($"[Sportarr] StandardFileFormat is already correct: '{currentFormat}'");
                }
            }
            else
            {
                Console.WriteLine("[Sportarr] Warning: MediaManagementSettings not found - will be created on first use");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not update StandardFileFormat: {ex.Message}");
        }
    }

    // Ensure file format matches EnableMultiPartEpisodes setting
    using (var scope = app.Services.CreateScope())
    {
        var fileFormatManager = scope.ServiceProvider.GetRequiredService<Sportarr.Api.Services.FileFormatManager>();
        var configService = scope.ServiceProvider.GetRequiredService<Sportarr.Api.Services.ConfigService>();
        var config = await configService.GetConfigAsync();
        await fileFormatManager.EnsureFileFormatMatchesMultiPartSetting(config.EnableMultiPartEpisodes);
        Console.WriteLine($"[Sportarr] File format verified (EnableMultiPartEpisodes={config.EnableMultiPartEpisodes})");
    }

    // CRITICAL: Sync SecuritySettings from config.xml to database on startup
    // This ensures the DynamicAuthenticationMiddleware has the correct auth settings
    // Previously, settings were only saved to config.xml but middleware reads from database
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<Sportarr.Api.Services.ConfigService>();
        var config = await configService.GetConfigAsync();

        Console.WriteLine($"[Sportarr] Syncing SecuritySettings to database (AuthMethod={config.AuthenticationMethod}, AuthRequired={config.AuthenticationRequired})");

        var appSettings = await db.AppSettings.FirstOrDefaultAsync();
        if (appSettings == null)
        {
            appSettings = new AppSettings { Id = 1 };
            db.AppSettings.Add(appSettings);
        }

        // Check if we have a plaintext password but no hash - need to hash it
        var passwordHash = config.PasswordHash ?? "";
        var passwordSalt = config.PasswordSalt ?? "";
        var passwordIterations = config.PasswordIterations > 0 ? config.PasswordIterations : 10000;

        if (!string.IsNullOrWhiteSpace(config.Password) && string.IsNullOrWhiteSpace(passwordHash))
        {
            Console.WriteLine("[Sportarr] Found plaintext password without hash - hashing now...");

            // Generate salt and hash the password
            var salt = new byte[128 / 8];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            var hashedBytes = Microsoft.AspNetCore.Cryptography.KeyDerivation.KeyDerivation.Pbkdf2(
                password: config.Password,
                salt: salt,
                prf: Microsoft.AspNetCore.Cryptography.KeyDerivation.KeyDerivationPrf.HMACSHA512,
                iterationCount: passwordIterations,
                numBytesRequested: 256 / 8);

            passwordHash = Convert.ToBase64String(hashedBytes);
            passwordSalt = Convert.ToBase64String(salt);

            // Save hashed credentials back to config.xml (clear plaintext)
            await configService.UpdateConfigAsync(c =>
            {
                c.Password = ""; // Clear plaintext
                c.PasswordHash = passwordHash;
                c.PasswordSalt = passwordSalt;
                c.PasswordIterations = passwordIterations;
            });

            Console.WriteLine("[Sportarr] Password hashed and saved to config.xml");
        }

        // Create SecuritySettings JSON for database
        var dbSecuritySettings = new SecuritySettings
        {
            AuthenticationMethod = config.AuthenticationMethod?.ToLower() ?? "none",
            AuthenticationRequired = config.AuthenticationRequired?.ToLower() ?? "disabledforlocaladdresses",
            Username = config.Username ?? "",
            Password = "", // Never store plaintext
            ApiKey = config.ApiKey ?? "",
            CertificateValidation = config.CertificateValidation?.ToLower() ?? "enabled",
            PasswordHash = passwordHash,
            PasswordSalt = passwordSalt,
            PasswordIterations = passwordIterations
        };

        appSettings.SecuritySettings = System.Text.Json.JsonSerializer.Serialize(dbSecuritySettings);
        await db.SaveChangesAsync();

        Console.WriteLine("[Sportarr] SecuritySettings synced to database successfully");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Sportarr] ERROR: Database migration failed: {ex.Message}");
    Console.WriteLine($"[Sportarr] Stack trace: {ex.StackTrace}");
    throw;
}

// Copy media server agents to config directory for easy Docker access
try
{
    var agentsSourcePath = Path.Combine(AppContext.BaseDirectory, "agents");
    var agentsDestPath = Path.Combine(dataPath, "agents");

    Console.WriteLine($"[Sportarr] Looking for agents at: {agentsSourcePath}");

    // Check if source exists in app directory
    if (Directory.Exists(agentsSourcePath))
    {
        Console.WriteLine($"[Sportarr] Found agents source directory");

        var needsCopy = !Directory.Exists(agentsDestPath);

        // Check if we need to update (source is newer)
        if (!needsCopy && Directory.Exists(agentsDestPath))
        {
            var sourceInfo = new DirectoryInfo(agentsSourcePath);
            var destInfo = new DirectoryInfo(agentsDestPath);
            needsCopy = sourceInfo.LastWriteTimeUtc > destInfo.LastWriteTimeUtc;
        }

        if (needsCopy)
        {
            Console.WriteLine($"[Sportarr] Copying media server agents to {agentsDestPath}...");
            var agentAccessDenied = new List<string>();
            CopyDirectory(agentsSourcePath, agentsDestPath, agentAccessDenied);

            if (agentAccessDenied.Count == 0)
            {
                Console.WriteLine("[Sportarr] Media server agents copied successfully");
            }
            else
            {
                Console.WriteLine($"[Sportarr] Media server agents partially updated ({agentAccessDenied.Count} file(s) could not be overwritten):");
                foreach (var f in agentAccessDenied.Take(5))
                {
                    Console.WriteLine($"[Sportarr]   - {f}");
                }
                if (agentAccessDenied.Count > 5)
                {
                    Console.WriteLine($"[Sportarr]   ... and {agentAccessDenied.Count - 5} more");
                }
                if (isWindowsPlatform && !IsRunningAsWindowsAdministrator())
                {
                    Console.WriteLine("[Sportarr] These files were created by a previous elevated run and the current user cannot modify them.");
                    Console.WriteLine("[Sportarr] Launch Sportarr once as administrator to fix these permissions permanently.");
                }
            }
            Console.WriteLine("[Sportarr] Plex agent available at: {0}", Path.Combine(agentsDestPath, "plex", "Sportarr.bundle"));
        }
        else
        {
            Console.WriteLine($"[Sportarr] Media server agents already available at {agentsDestPath}");
        }
    }
    else
    {
        // Agents not in build output - create them dynamically
        Console.WriteLine($"[Sportarr] Agents not found in build output, checking config directory...");

        var plexAgentFile = Path.Combine(agentsDestPath, "plex", "Sportarr.bundle", "Contents", "Code", "__init__.py");
        var needsUpdate = !Directory.Exists(agentsDestPath) || !File.Exists(plexAgentFile);

        // Check if existing agent has the broken code (import os, CRLF line endings)
        if (!needsUpdate && File.Exists(plexAgentFile))
        {
            var existingCode = File.ReadAllText(plexAgentFile);
            // Detect old broken agent: has "import os" or "os.environ" or CRLF line endings
            if (existingCode.Contains("import os") || existingCode.Contains("os.environ") || existingCode.Contains("\r\n"))
            {
                Console.WriteLine("[Sportarr] Detected outdated Plex agent with CRLF or import issues, updating...");
                needsUpdate = true;
            }
        }

        if (needsUpdate)
        {
            Console.WriteLine($"[Sportarr] Creating/updating agents in {agentsDestPath}...");
            CreateDefaultAgents(agentsDestPath);
            Console.WriteLine("[Sportarr] Agents created/updated successfully");
            Console.WriteLine("[Sportarr] Plex agent available at: {0}", Path.Combine(agentsDestPath, "plex", "Sportarr.bundle"));
        }
        else
        {
            Console.WriteLine($"[Sportarr] Media server agents already available at {agentsDestPath}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Sportarr] Warning: Could not setup agents directory: {ex.Message}");
}

// Helper function to recursively copy directories.
// Resilient to per-file failures: tracks successes and access-denied failures
// separately so the caller can log a useful summary. Used by the agents copy
// code which can hit admin-owned files left over from a prior elevated run.
static void CopyDirectory(string sourceDir, string destDir,
    List<string>? accessDeniedFiles = null)
{
    try
    {
        Directory.CreateDirectory(destDir);
    }
    catch (UnauthorizedAccessException)
    {
        accessDeniedFiles?.Add(destDir);
        return;
    }

    foreach (var file in Directory.GetFiles(sourceDir))
    {
        var destFile = Path.Combine(destDir, Path.GetFileName(file));
        try
        {
            File.Copy(file, destFile, true);
        }
        catch (UnauthorizedAccessException)
        {
            accessDeniedFiles?.Add(destFile);
        }
        catch (IOException)
        {
            // File may be locked by another process; skip it
            accessDeniedFiles?.Add(destFile);
        }
    }

    foreach (var dir in Directory.GetDirectories(sourceDir))
    {
        var dirName = Path.GetFileName(dir);
        // Skip obj and bin directories (build artifacts)
        if (dirName == "obj" || dirName == "bin")
            continue;
        CopyDirectory(dir, Path.Combine(destDir, dirName), accessDeniedFiles);
    }
}

// Create default agents when not available in build output
static void CreateDefaultAgents(string agentsDestPath)
{
    // Create Plex agent
    var plexPath = Path.Combine(agentsDestPath, "plex", "Sportarr.bundle", "Contents", "Code");
    Directory.CreateDirectory(plexPath);

    // Fixed Plex agent code - no imports, hardcoded URL, uses LF line endings
    var plexAgentCode = "# -*- coding: utf-8 -*-\n\nSPORTARR_API_URL = 'https://sportarr.net'\n\n\ndef Start():\n    Log.Info(\"[Sportarr] Agent starting...\")\n    Log.Info(\"[Sportarr] API URL: %s\" % SPORTARR_API_URL)\n    HTTP.CacheTime = 3600\n\n\nclass SportarrAgent(Agent.TV_Shows):\n    name = 'Sportarr'\n    languages = ['en']\n    primary_provider = True\n    fallback_agent = False\n    accepts_from = ['com.plexapp.agents.localmedia']\n\n    def search(self, results, media, lang, manual):\n        Log.Info(\"[Sportarr] Searching for: %s\" % media.show)\n\n        try:\n            search_url = \"%s/api/metadata/plex/search?title=%s\" % (\n                SPORTARR_API_URL,\n                String.Quote(media.show, usePlus=True)\n            )\n\n            if media.year:\n                search_url = search_url + \"&year=%s\" % media.year\n\n            Log.Debug(\"[Sportarr] Search URL: %s\" % search_url)\n            response = JSON.ObjectFromURL(search_url)\n\n            if 'results' in response:\n                for idx, series in enumerate(response['results'][:10]):\n                    score = 100 - (idx * 5)\n\n                    if series.get('title', '').lower() == media.show.lower():\n                        score = 100\n\n                    results.Append(MetadataSearchResult(\n                        id=str(series.get('id')),\n                        name=series.get('title'),\n                        year=series.get('year'),\n                        score=score,\n                        lang=lang\n                    ))\n\n                    Log.Info(\"[Sportarr] Found: %s (ID: %s, Score: %d)\" % (\n                        series.get('title'), series.get('id'), score\n                    ))\n\n        except Exception as e:\n            Log.Error(\"[Sportarr] Search error: %s\" % str(e))\n\n    def update(self, metadata, media, lang, force):\n        Log.Info(\"[Sportarr] Updating metadata for ID: %s\" % metadata.id)\n\n        try:\n            series_url = \"%s/api/metadata/plex/series/%s\" % (SPORTARR_API_URL, metadata.id)\n            Log.Debug(\"[Sportarr] Series URL: %s\" % series_url)\n            series = JSON.ObjectFromURL(series_url)\n\n            if series:\n                metadata.title = series.get('title')\n                metadata.summary = series.get('summary')\n                metadata.originally_available_at = None\n\n                if series.get('year'):\n                    try:\n                        metadata.originally_available_at = Datetime.ParseDate(\"%s-01-01\" % series.get('year'))\n                    except:\n                        pass\n\n                metadata.studio = series.get('studio')\n                metadata.content_rating = series.get('content_rating')\n\n                metadata.genres.clear()\n                for genre in series.get('genres', []):\n                    metadata.genres.add(genre)\n\n                if series.get('poster_url'):\n                    try:\n                        metadata.posters[series['poster_url']] = Proxy.Media(\n                            HTTP.Request(series['poster_url']).content\n                        )\n                    except Exception as e:\n                        Log.Warn(\"[Sportarr] Failed to fetch poster: %s\" % e)\n\n                if series.get('banner_url'):\n                    try:\n                        metadata.banners[series['banner_url']] = Proxy.Media(\n                            HTTP.Request(series['banner_url']).content\n                        )\n                    except Exception as e:\n                        Log.Warn(\"[Sportarr] Failed to fetch banner: %s\" % e)\n\n                if series.get('fanart_url'):\n                    try:\n                        metadata.art[series['fanart_url']] = Proxy.Media(\n                            HTTP.Request(series['fanart_url']).content\n                        )\n                    except Exception as e:\n                        Log.Warn(\"[Sportarr] Failed to fetch fanart: %s\" % e)\n\n            seasons_url = \"%s/api/metadata/plex/series/%s/seasons\" % (SPORTARR_API_URL, metadata.id)\n            Log.Debug(\"[Sportarr] Seasons URL: %s\" % seasons_url)\n            seasons_response = JSON.ObjectFromURL(seasons_url)\n\n            if 'seasons' in seasons_response:\n                for season_data in seasons_response['seasons']:\n                    season_num = season_data.get('season_number')\n                    if season_num in media.seasons:\n                        season = metadata.seasons[season_num]\n                        season.title = season_data.get('title', \"Season %s\" % season_num)\n                        season.summary = season_data.get('summary', '')\n\n                        if season_data.get('poster_url'):\n                            try:\n                                season.posters[season_data['poster_url']] = Proxy.Media(\n                                    HTTP.Request(season_data['poster_url']).content\n                                )\n                            except Exception as e:\n                                Log.Warn(\"[Sportarr] Failed to fetch season poster: %s\" % e)\n\n                        self.update_episodes(metadata, media, season_num)\n\n        except Exception as e:\n            Log.Error(\"[Sportarr] Update error: %s\" % str(e))\n\n    def update_episodes(self, metadata, media, season_num):\n        Log.Debug(\"[Sportarr] Updating episodes for season %s\" % season_num)\n\n        try:\n            episodes_url = \"%s/api/metadata/plex/series/%s/season/%s/episodes\" % (\n                SPORTARR_API_URL, metadata.id, season_num\n            )\n            Log.Debug(\"[Sportarr] Episodes URL: %s\" % episodes_url)\n            episodes_response = JSON.ObjectFromURL(episodes_url)\n\n            if 'episodes' in episodes_response:\n                for ep_data in episodes_response['episodes']:\n                    ep_num = ep_data.get('episode_number')\n\n                    if ep_num in media.seasons[season_num].episodes:\n                        episode = metadata.seasons[season_num].episodes[ep_num]\n\n                        title = ep_data.get('title', \"Episode %s\" % ep_num)\n                        if ep_data.get('part_name'):\n                            title = \"%s - %s\" % (title, ep_data['part_name'])\n\n                        episode.title = title\n                        episode.summary = ep_data.get('summary', '')\n\n                        if ep_data.get('air_date'):\n                            try:\n                                episode.originally_available_at = Datetime.ParseDate(ep_data['air_date'])\n                            except:\n                                pass\n\n                        if ep_data.get('duration_minutes'):\n                            episode.duration = ep_data['duration_minutes'] * 60 * 1000\n\n                        if ep_data.get('thumb_url'):\n                            try:\n                                episode.thumbs[ep_data['thumb_url']] = Proxy.Media(\n                                    HTTP.Request(ep_data['thumb_url']).content\n                                )\n                            except Exception as e:\n                                Log.Warn(\"[Sportarr] Failed to fetch episode thumb: %s\" % e)\n\n                        Log.Debug(\"[Sportarr] Updated S%sE%s: %s\" % (season_num, ep_num, title))\n\n        except Exception as e:\n            Log.Error(\"[Sportarr] Episodes update error: %s\" % str(e))\n";

    File.WriteAllText(Path.Combine(plexPath, "__init__.py"), plexAgentCode);

    // Create Info.plist for Plex (using LF line endings)
    var infoPlistPath = Path.Combine(agentsDestPath, "plex", "Sportarr.bundle", "Contents");
    var infoPlist = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<plist version=\"1.0\">\n<dict>\n    <key>CFBundleIdentifier</key>\n    <string>com.sportarr.agents.sportarr</string>\n\n    <key>PlexPluginClass</key>\n    <string>Agent</string>\n\n    <key>PlexClientPlatforms</key>\n    <string>*</string>\n\n    <key>PlexClientPlatformExclusions</key>\n    <string></string>\n\n    <key>PlexFrameworkVersion</key>\n    <string>2</string>\n\n    <key>PlexPluginCodePolicy</key>\n    <string>Elevated</string>\n\n    <key>PlexBundleVersion</key>\n    <string>1</string>\n\n    <key>CFBundleVersion</key>\n    <string>1.0.0</string>\n\n    <key>PlexAgentAttributionText</key>\n    <string>Metadata provided by Sportarr</string>\n</dict>\n</plist>\n";
    File.WriteAllText(Path.Combine(infoPlistPath, "Info.plist"), infoPlist);

    // Create Jellyfin agent placeholder
    var jellyfinPath = Path.Combine(agentsDestPath, "jellyfin");
    Directory.CreateDirectory(jellyfinPath);
    var jellyfinReadme = @"# Sportarr Jellyfin Plugin

The Jellyfin plugin needs to be built from source or downloaded from releases.

## Building from Source

```bash
cd agents/jellyfin/Sportarr
dotnet build -c Release
```

## Installation

Copy the built DLL to your Jellyfin plugins directory:
- Docker: /config/plugins/Sportarr/
- Windows: %APPDATA%\Jellyfin\Server\plugins\Sportarr\
- Linux: ~/.local/share/jellyfin/plugins/Sportarr/

Then restart Jellyfin.
";
    File.WriteAllText(Path.Combine(jellyfinPath, "README.md"), jellyfinReadme);

    // Create a README for the agents folder
    var agentsReadme = @"# Sportarr Media Server Agents

This folder contains metadata agents for media servers.

## Plex

The `plex/Sportarr.bundle` folder is a Plex metadata agent.
Copy it to your Plex plugins directory and restart Plex.

## Jellyfin

See `jellyfin/README.md` for Jellyfin plugin instructions.
";
    File.WriteAllText(Path.Combine(agentsDestPath, "README.md"), agentsReadme);
}

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
