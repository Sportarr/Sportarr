using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Models.Metadata;
using Sportarr.Api.Services;
using Sportarr.Api.Middleware;
using Sportarr.Api.Helpers;
using Serilog;
using Serilog.Events;
using System.Text.Json;
using Polly;
using Polly.Extensions.Http;

// Configure Serilog
var logsPath = Path.Combine(Directory.GetCurrentDirectory(), "logs");
Directory.CreateDirectory(logsPath);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(logsPath, "sportarr.txt"),
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        fileSizeLimitBytes: 10485760, // 10MB
        rollOnFileSizeLimit: true,
        shared: true,
        flushToDiskInterval: TimeSpan.FromSeconds(1))
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for all logging
builder.Host.UseSerilog();

// Configuration
var apiKey = builder.Configuration["Sportarr:ApiKey"] ?? Guid.NewGuid().ToString("N");
var dataPath = builder.Configuration["Sportarr:DataPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "data");

try
{
    Directory.CreateDirectory(dataPath);
    Console.WriteLine($"[Sportarr] Data directory: {dataPath}");
}
catch (Exception ex)
{
    Console.WriteLine($"[Sportarr] ERROR: Failed to create data directory: {ex.Message}");
    throw;
}

builder.Configuration["Sportarr:ApiKey"] = apiKey;

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient(); // For calling Sportarr-API

// Configure HttpClient for indexer searches with Polly retry policy
builder.Services.AddHttpClient("IndexerClient")
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
    // This allows TheSportsDB API responses (idLeague, strLeague) to map to our League model
    options.SerializerOptions.PropertyNameCaseInsensitive = true;

    // DO NOT add JsonStringEnumConverter - we need numeric enum values for frontend
    // The frontend expects type: 5 (number), not type: "Sabnzbd" (string)
});
builder.Services.AddSingleton<Sportarr.Api.Services.ConfigService>();
builder.Services.AddScoped<Sportarr.Api.Services.UserService>();
builder.Services.AddScoped<Sportarr.Api.Services.AuthenticationService>();
builder.Services.AddScoped<Sportarr.Api.Services.SimpleAuthService>();
builder.Services.AddScoped<Sportarr.Api.Services.SessionService>();
builder.Services.AddScoped<Sportarr.Api.Services.DownloadClientService>();
builder.Services.AddScoped<Sportarr.Api.Services.IndexerSearchService>();
builder.Services.AddScoped<Sportarr.Api.Services.AutomaticSearchService>();
builder.Services.AddScoped<Sportarr.Api.Services.DelayProfileService>();
builder.Services.AddScoped<Sportarr.Api.Services.QualityDetectionService>();
builder.Services.AddScoped<Sportarr.Api.Services.ReleaseEvaluator>();
builder.Services.AddScoped<Sportarr.Api.Services.MediaFileParser>();
builder.Services.AddScoped<Sportarr.Api.Services.FileNamingService>();
builder.Services.AddScoped<Sportarr.Api.Services.FileImportService>();
builder.Services.AddScoped<Sportarr.Api.Services.CustomFormatService>();
builder.Services.AddScoped<Sportarr.Api.Services.HealthCheckService>();
builder.Services.AddScoped<Sportarr.Api.Services.BackupService>();
builder.Services.AddScoped<Sportarr.Api.Services.LibraryImportService>();
builder.Services.AddScoped<Sportarr.Api.Services.ImportListService>();
builder.Services.AddScoped<Sportarr.Api.Services.ImportService>(); // Handles completed download imports
builder.Services.AddScoped<Sportarr.Api.Services.EventQueryService>(); // Universal: Sport-aware query builder for all sports
builder.Services.AddScoped<Sportarr.Api.Services.LeagueEventSyncService>(); // Syncs events from TheSportsDB to populate leagues
builder.Services.AddHostedService<Sportarr.Api.Services.LeagueEventAutoSyncService>(); // Background service for automatic periodic event sync

// TheSportsDB client for universal sports metadata (via Sportarr-API)
builder.Services.AddHttpClient<Sportarr.Api.Services.TheSportsDBClient>()
    .AddTransientHttpErrorPolicy(policyBuilder =>
        policyBuilder.WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                Console.WriteLine($"[TheSportsDB] Retry {retryAttempt} after {timespan.TotalSeconds}s delay");
            }
        ));

builder.Services.AddSingleton<Sportarr.Api.Services.TaskService>();
builder.Services.AddHostedService<Sportarr.Api.Services.EnhancedDownloadMonitorService>(); // Unified download monitoring with retry, blocklist, and auto-import
builder.Services.AddHostedService<Sportarr.Api.Services.RssSyncService>(); // Automatic RSS sync for new releases
builder.Services.AddHostedService<Sportarr.Api.Services.TvScheduleSyncService>(); // TV schedule sync for automatic search timing
builder.Services.AddHostedService<Sportarr.Api.Services.EventMonitoringService>(); // Sonarr/Radarr-style automatic search timing for Live events


// Add ASP.NET Core Authentication (Sonarr/Radarr pattern)
Sportarr.Api.Authentication.AuthenticationBuilderExtensions.AddAppAuthentication(builder.Services);

// Configure database
var dbPath = Path.Combine(dataPath, "sportarr.db");
Console.WriteLine($"[Sportarr] Database path: {dbPath}");
builder.Services.AddDbContext<SportarrDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

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
        var canConnect = db.Database.CanConnect();
        var hasMigrationHistory = canConnect && db.Database.GetAppliedMigrations().Any();

        // Check if AppSettings table exists (core table that should always be present)
        bool hasTables = false;
        if (canConnect)
        {
            using var connection = db.Database.GetDbConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AppSettings'";
            hasTables = Convert.ToInt32(command.ExecuteScalar()) > 0;
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

            // Insert all migrations as applied
            foreach (var migration in allMigrations)
            {
                try
                {
                    db.Database.ExecuteSqlRaw(
                        $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('{migration}', '8.0.0')");
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
    }
    Console.WriteLine("[Sportarr] Database migrations applied successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"[Sportarr] ERROR: Database migration failed: {ex.Message}");
    Console.WriteLine($"[Sportarr] Stack trace: {ex.StackTrace}");
    throw;
}

// Configure middleware pipeline
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

// Configure static files (UI from wwwroot)
app.UseDefaultFiles();
app.UseStaticFiles();

// Initialize endpoint (for frontend) - keep for SPA compatibility
app.MapGet("/initialize.json", () =>
{
    return Results.Json(new
    {
        apiRoot = "", // Empty since all API routes already start with /api
        apiKey,
        release = Sportarr.Api.Version.GetFullVersion(),
        version = Sportarr.Api.Version.GetFullVersion(),
        instanceName = "Sportarr",
        theme = "auto",
        branch = "main",
        analytics = false,
        userHash = Guid.NewGuid().ToString("N")[..8],
        urlBase = "",
        isProduction = !app.Environment.IsDevelopment()
    });
});

// Health check
app.MapGet("/ping", () => Results.Ok("pong"));

// Authentication endpoints
app.MapPost("/api/login", async (
    LoginRequest request,
    Sportarr.Api.Services.SimpleAuthService authService,
    Sportarr.Api.Services.SessionService sessionService,
    HttpContext context,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[AUTH LOGIN] Login attempt for user: {Username}", request.Username);

    var isValid = await authService.ValidateCredentialsAsync(request.Username, request.Password);

    if (isValid)
    {
        logger.LogInformation("[AUTH LOGIN] Login successful for user: {Username}", request.Username);

        // Get client IP and User-Agent for session tracking
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = context.Request.Headers["User-Agent"].ToString();

        // Create secure session with cryptographic session ID
        var sessionId = await sessionService.CreateSessionAsync(request.Username, ipAddress, userAgent, request.RememberMe);

        // Set secure authentication cookie with session ID
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true, // Prevents JavaScript access (XSS protection)
            Secure = false,  // Set to true in production with HTTPS
            SameSite = SameSiteMode.Strict, // CSRF protection
            Expires = request.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddDays(7),
            Path = "/"
        };
        context.Response.Cookies.Append("SportarrAuth", sessionId, cookieOptions);

        logger.LogInformation("[AUTH LOGIN] Session created from IP: {IP}", ipAddress);

        return Results.Ok(new LoginResponse { Success = true, Token = sessionId, Message = "Login successful" });
    }

    logger.LogWarning("[AUTH LOGIN] Login failed for user: {Username}", request.Username);
    return Results.Unauthorized();
});

app.MapPost("/api/logout", async (
    Sportarr.Api.Services.SessionService sessionService,
    HttpContext context,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[AUTH LOGOUT] Logout requested");

    // Get session ID from cookie
    var sessionId = context.Request.Cookies["SportarrAuth"];
    if (!string.IsNullOrEmpty(sessionId))
    {
        // Delete session from database
        await sessionService.DeleteSessionAsync(sessionId);
    }

    // Delete cookie
    context.Response.Cookies.Delete("SportarrAuth");
    return Results.Ok(new { message = "Logged out successfully" });
});

// NEW SIMPLE FLOW: Check if initial setup is complete
app.MapGet("/api/auth/check", async (
    Sportarr.Api.Services.SimpleAuthService authService,
    Sportarr.Api.Services.SessionService sessionService,
    HttpContext context,
    ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[AUTH CHECK] Starting auth check");

        // Step 1: Check if setup is complete (credentials exist)
        var isSetupComplete = await authService.IsSetupCompleteAsync();
        logger.LogInformation("[AUTH CHECK] IsSetupComplete={IsSetupComplete}", isSetupComplete);

        if (!isSetupComplete)
        {
            // No credentials exist - need initial setup
            logger.LogInformation("[AUTH CHECK] Setup not complete, redirecting to setup");
            return Results.Ok(new { setupComplete = false, authenticated = false });
        }

        // Step 2: Setup is complete, validate session with security checks
        var sessionId = context.Request.Cookies["SportarrAuth"];
        if (string.IsNullOrEmpty(sessionId))
        {
            logger.LogInformation("[AUTH CHECK] No session cookie found");
            return Results.Ok(new { setupComplete = true, authenticated = false });
        }

        // Get client IP and User-Agent for validation
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = context.Request.Headers["User-Agent"].ToString();

        // Validate session with IP and User-Agent checks
        var (isValid, username) = await sessionService.ValidateSessionAsync(
            sessionId,
            ipAddress,
            userAgent,
            strictIpCheck: true,      // Reject if IP doesn't match
            strictUserAgentCheck: true // Reject if User-Agent doesn't match
        );

        if (isValid)
        {
            logger.LogInformation("[AUTH CHECK] Valid session for user {Username} from IP {IP}", username, ipAddress);
            return Results.Ok(new { setupComplete = true, authenticated = true, username });
        }
        else
        {
            logger.LogWarning("[AUTH CHECK] Invalid session - IP or User-Agent mismatch");
            // Delete invalid cookie
            context.Response.Cookies.Delete("SportarrAuth");
            return Results.Ok(new { setupComplete = true, authenticated = false });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[AUTH CHECK] CRITICAL ERROR: {Message}", ex.Message);
        logger.LogError(ex, "[AUTH CHECK] Stack trace: {StackTrace}", ex.StackTrace);
        // On error, assume setup incomplete to force setup page
        return Results.Ok(new { setupComplete = false, authenticated = false });
    }
});

// NEW: Initial setup endpoint - creates first user credentials
app.MapPost("/api/setup", async (SetupRequest request, Sportarr.Api.Services.SimpleAuthService authService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[SETUP] Initial setup requested for username: {Username}", request.Username);

        // Check if setup is already complete
        var isSetupComplete = await authService.IsSetupCompleteAsync();
        logger.LogInformation("[SETUP] IsSetupComplete check result: {Result}", isSetupComplete);

        if (isSetupComplete)
        {
            logger.LogWarning("[SETUP] Setup already complete, rejecting request");
            return Results.BadRequest(new { error = "Setup is already complete" });
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            logger.LogWarning("[SETUP] Validation failed: Username or password is empty");
            return Results.BadRequest(new { error = "Username and password are required" });
        }

        if (request.Password.Length < 6)
        {
            logger.LogWarning("[SETUP] Validation failed: Password too short ({Length} chars)", request.Password.Length);
            return Results.BadRequest(new { error = "Password must be at least 6 characters" });
        }

        // Create initial credentials
        logger.LogInformation("[SETUP] Creating credentials for user: {Username}", request.Username);
        await authService.SetCredentialsAsync(request.Username, request.Password);
        logger.LogInformation("[SETUP] Initial setup complete for user: {Username}", request.Username);

        return Results.Ok(new { message = "Setup complete. Please login with your credentials." });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[SETUP] CRITICAL ERROR during setup: {Message}", ex.Message);
        logger.LogError(ex, "[SETUP] Stack trace: {StackTrace}", ex.StackTrace);
        return Results.Problem(detail: ex.Message, title: "Setup failed");
    }
});

// API: System Status
app.MapGet("/api/system/status", (HttpContext context) =>
{
    var status = new SystemStatus
    {
        AppName = "Sportarr",
        Version = Sportarr.Api.Version.GetFullVersion(),  // Use full 4-part version (e.g., 4.0.81.140)
        IsDebug = app.Environment.IsDevelopment(),
        IsProduction = app.Environment.IsProduction(),
        IsDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true",
        DatabaseType = "SQLite",
        Authentication = "apikey",
        AppData = dataPath,
        StartTime = DateTime.UtcNow
    };
    return Results.Ok(status);
});

// API: System Health Checks
app.MapGet("/api/system/health", async (Sportarr.Api.Services.HealthCheckService healthCheckService) =>
{
    var healthResults = await healthCheckService.PerformAllChecksAsync();
    return Results.Ok(healthResults);
});

// API: System Backup Management
app.MapGet("/api/system/backup", async (Sportarr.Api.Services.BackupService backupService) =>
{
    var backups = await backupService.GetBackupsAsync();
    return Results.Ok(backups);
});

app.MapPost("/api/system/backup", async (Sportarr.Api.Services.BackupService backupService, string? note) =>
{
    try
    {
        var backup = await backupService.CreateBackupAsync(note);
        return Results.Ok(backup);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/api/system/backup/restore/{backupName}", async (string backupName, Sportarr.Api.Services.BackupService backupService) =>
{
    try
    {
        await backupService.RestoreBackupAsync(backupName);
        return Results.Ok(new { message = "Backup restored successfully. Please restart Sportarr for changes to take effect." });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapDelete("/api/system/backup/{backupName}", async (string backupName, Sportarr.Api.Services.BackupService backupService) =>
{
    try
    {
        await backupService.DeleteBackupAsync(backupName);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/api/system/backup/cleanup", async (Sportarr.Api.Services.BackupService backupService) =>
{
    try
    {
        await backupService.CleanupOldBackupsAsync();
        return Results.Ok(new { message = "Old backups cleaned up successfully" });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

// API: System Updates - Check for new versions from GitHub
app.MapGet("/api/system/updates", async (ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[UPDATES] Checking for updates from GitHub");

        // Get current version using the centralized version helper
        var currentVersion = Sportarr.Api.Version.GetFullVersion();

        logger.LogInformation("[UPDATES] Current version: {Version}", currentVersion);

        // Fetch releases from GitHub API
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", $"Sportarr/{currentVersion}");

        var response = await httpClient.GetAsync("https://api.github.com/repos/Sportarr/Sportarr/releases");

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[UPDATES] Failed to fetch releases from GitHub: {StatusCode}", response.StatusCode);
            return Results.Problem("Failed to fetch updates from GitHub");
        }

        var json = await response.Content.ReadAsStringAsync();
        var releases = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        var releaseList = new List<object>();
        string? latestVersion = null;

        foreach (var release in releases.EnumerateArray())
        {
            var tagName = release.GetProperty("tag_name").GetString() ?? "";
            var version = tagName.TrimStart('v'); // Remove 'v' prefix if present
            var publishedAt = release.GetProperty("published_at").GetString() ?? DateTime.UtcNow.ToString();
            var body = release.GetProperty("body").GetString() ?? "";
            var htmlUrl = release.GetProperty("html_url").GetString() ?? "";
            var isDraft = release.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean();
            var isPrerelease = release.TryGetProperty("prerelease", out var prereleaseProp) && prereleaseProp.GetBoolean();

            // Skip drafts and prereleases
            if (isDraft || isPrerelease)
            {
                continue;
            }

            // Track latest version
            if (latestVersion == null)
            {
                latestVersion = version;
            }

            // Parse changelog from release body
            var changes = new List<string>();
            if (!string.IsNullOrEmpty(body))
            {
                var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    // Skip headers and empty lines
                    if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                    {
                        continue;
                    }
                    // Add bullet points
                    if (trimmed.StartsWith("-") || trimmed.StartsWith("*"))
                    {
                        changes.Add(trimmed.TrimStart('-', '*').Trim());
                    }
                    else if (changes.Count < 10) // Limit to 10 changes
                    {
                        changes.Add(trimmed);
                    }
                }
            }

            // Check if this release is installed (compare 3-part base version)
            var currentParts = currentVersion.Split('.');
            var currentBase = currentParts.Length >= 3 ? $"{currentParts[0]}.{currentParts[1]}.{currentParts[2]}" : currentVersion;
            var isInstalled = version == currentBase || version == currentVersion;

            releaseList.Add(new
            {
                version,
                releaseDate = publishedAt,
                branch = "main",
                changes = changes.Take(10).ToList(), // Limit to 10 changes per release
                downloadUrl = htmlUrl,
                isInstalled,
                isLatest = version == latestVersion
            });

            // Only show last 10 releases
            if (releaseList.Count >= 10)
            {
                break;
            }
        }

        // Compare versions properly - currentVersion is 4-part (4.0.81.140), latestVersion is 3-part (4.0.82)
        var updateAvailable = false;
        if (latestVersion != null)
        {
            // Extract first 3 parts of current version for comparison (4.0.81.140 -> 4.0.81)
            var currentParts = currentVersion.Split('.');
            var currentBase = currentParts.Length >= 3 ? $"{currentParts[0]}.{currentParts[1]}.{currentParts[2]}" : currentVersion;

            // latestVersion is already 3-part from GitHub tags (v4.0.82 -> 4.0.82)
            updateAvailable = latestVersion != currentBase && latestVersion != currentVersion;
        }

        logger.LogInformation("[UPDATES] Current: {Current}, Latest: {Latest}, Available: {Available}",
            currentVersion, latestVersion ?? "unknown", updateAvailable);

        return Results.Ok(new
        {
            updateAvailable,
            currentVersion,
            latestVersion = latestVersion ?? currentVersion,
            releases = releaseList
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[UPDATES] Error checking for updates");
        return Results.Problem("Error checking for updates: " + ex.Message);
    }
});

// API: System Events (Audit Log)
app.MapGet("/api/system/event", async (SportarrDbContext db, int page = 1, int pageSize = 50, string? type = null, string? category = null) =>
{
    var query = db.SystemEvents.AsQueryable();

    if (!string.IsNullOrEmpty(type) && Enum.TryParse<EventType>(type, true, out var eventType))
    {
        query = query.Where(e => e.Type == eventType);
    }

    if (!string.IsNullOrEmpty(category) && Enum.TryParse<EventCategory>(category, true, out var eventCategory))
    {
        query = query.Where(e => e.Category == eventCategory);
    }

    var totalCount = await query.CountAsync();
    var events = await query
        .OrderByDescending(e => e.Timestamp)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    return Results.Ok(new
    {
        events,
        page,
        pageSize,
        totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
        totalRecords = totalCount
    });
});

app.MapDelete("/api/system/event/{id:int}", async (int id, SportarrDbContext db) =>
{
    var systemEvent = await db.SystemEvents.FindAsync(id);
    if (systemEvent is null) return Results.NotFound();

    db.SystemEvents.Remove(systemEvent);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapPost("/api/system/event/cleanup", async (SportarrDbContext db, int days = 30) =>
{
    var cutoffDate = DateTime.UtcNow.AddDays(-days);
    var oldEvents = db.SystemEvents.Where(e => e.Timestamp < cutoffDate);
    db.SystemEvents.RemoveRange(oldEvents);
    var deleted = await db.SaveChangesAsync();
    return Results.Ok(new { message = $"Deleted {deleted} old system events", deletedCount = deleted });
});

// API: Library Import - Scan filesystem for existing event files
app.MapPost("/api/library/scan", async (Sportarr.Api.Services.LibraryImportService service, string folderPath, bool includeSubfolders = true) =>
{
    try
    {
        var result = await service.ScanFolderAsync(folderPath, includeSubfolders);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to scan folder: {ex.Message}");
    }
});

app.MapPost("/api/library/import", async (Sportarr.Api.Services.LibraryImportService service, List<Sportarr.Api.Services.FileImportRequest> requests) =>
{
    try
    {
        var result = await service.ImportFilesAsync(requests);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to import files: {ex.Message}");
    }
});

// API: Get log files list
app.MapGet("/api/log/file", (ILogger<Program> logger) =>
{
    try
    {
        var logFiles = Directory.GetFiles(logsPath, "*.txt")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new
            {
                filename = Path.GetFileName(f.FullName),
                lastWriteTime = f.LastWriteTime,
                size = f.Length
            })
            .ToList();

        logger.LogInformation("[LOG FILES] Listing {Count} log files", logFiles.Count);
        return Results.Ok(logFiles);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LOG FILES] Error listing log files");
        return Results.Problem("Error listing log files");
    }
});

// API: Get specific log file content
app.MapGet("/api/log/file/{filename}", (string filename, ILogger<Program> logger) =>
{
    try
    {
        // Sanitize filename to prevent directory traversal
        filename = Path.GetFileName(filename);
        var logFilePath = Path.Combine(logsPath, filename);

        if (!File.Exists(logFilePath))
        {
            logger.LogWarning("[LOG FILES] File not found: {Filename}", filename);
            return Results.NotFound(new { message = "Log file not found" });
        }

        logger.LogInformation("[LOG FILES] Reading log file: {Filename}", filename);

        // Read with FileShare.ReadWrite to allow reading while Serilog is writing
        string content;
        using (var fileStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(fileStream))
        {
            content = reader.ReadToEnd();
        }

        return Results.Ok(new
        {
            filename = filename,
            content = content,
            lastWriteTime = File.GetLastWriteTime(logFilePath),
            size = new FileInfo(logFilePath).Length
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LOG FILES] Error reading log file: {Filename}", filename);
        return Results.Problem("Error reading log file");
    }
});

// API: Download log file
app.MapGet("/api/log/file/{filename}/download", (string filename, ILogger<Program> logger) =>
{
    try
    {
        // Sanitize filename to prevent directory traversal
        filename = Path.GetFileName(filename);
        var logFilePath = Path.Combine(logsPath, filename);

        if (!File.Exists(logFilePath))
        {
            logger.LogWarning("[LOG FILES] File not found for download: {Filename}", filename);
            return Results.NotFound(new { message = "Log file not found" });
        }

        logger.LogInformation("[LOG FILES] Downloading log file: {Filename}", filename);

        // Read with FileShare.ReadWrite to allow reading while Serilog is writing
        byte[] fileBytes;
        using (var fileStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var memoryStream = new MemoryStream())
        {
            fileStream.CopyTo(memoryStream);
            fileBytes = memoryStream.ToArray();
        }

        return Results.File(fileBytes, "text/plain", filename);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LOG FILES] Error downloading log file: {Filename}", filename);
        return Results.Problem("Error downloading log file");
    }
});

// API: Get all tasks (with optional limit)
app.MapGet("/api/task", async (Sportarr.Api.Services.TaskService taskService, int? pageSize) =>
{
    try
    {
        var tasks = await taskService.GetAllTasksAsync(pageSize);
        return Results.Ok(tasks);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "[TASK API] Error getting tasks");
        return Results.Problem("Error getting tasks");
    }
});

// API: Get specific task
app.MapGet("/api/task/{id:int}", async (int id, Sportarr.Api.Services.TaskService taskService) =>
{
    try
    {
        var task = await taskService.GetTaskAsync(id);
        return task is null ? Results.NotFound() : Results.Ok(task);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "[TASK API] Error getting task {TaskId}", id);
        return Results.Problem("Error getting task");
    }
});

// API: Queue a new task (for testing)
app.MapPost("/api/task", async (Sportarr.Api.Services.TaskService taskService, TaskRequest request) =>
{
    try
    {
        var task = await taskService.QueueTaskAsync(
            request.Name,
            request.CommandName,
            request.Priority ?? 0,
            request.Body
        );
        return Results.Created($"/api/task/{task.Id}", task);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "[TASK API] Error queueing task");
        return Results.Problem("Error queueing task");
    }
});

// API: Cancel a task
app.MapDelete("/api/task/{id:int}", async (int id, Sportarr.Api.Services.TaskService taskService) =>
{
    try
    {
        var success = await taskService.CancelTaskAsync(id);
        if (!success)
        {
            return Results.NotFound(new { message = "Task not found or cannot be cancelled" });
        }
        return Results.Ok(new { message = "Task cancelled successfully" });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "[TASK API] Error cancelling task {TaskId}", id);
        return Results.Problem("Error cancelling task");
    }
});

// API: Clean up old tasks
app.MapPost("/api/task/cleanup", async (Sportarr.Api.Services.TaskService taskService, int? keepCount) =>
{
    try
    {
        await taskService.CleanupOldTasksAsync(keepCount ?? 100);
        return Results.Ok(new { message = "Old tasks cleaned up successfully" });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "[TASK API] Error cleaning up tasks");
        return Results.Problem("Error cleaning up tasks");
    }
});

// API: Get all events (universal for all sports)
app.MapGet("/api/events", async (SportarrDbContext db) =>
{
    var events = await db.Events
        .Include(e => e.League)        // Universal (UFC, Premier League, NBA, etc.)
        .Include(e => e.HomeTeam)      // Universal (team sports and combat sports)
        .Include(e => e.AwayTeam)      // Universal (team sports and combat sports)
        .OrderByDescending(e => e.EventDate)
        .ToListAsync();

    // Convert to DTOs to avoid JsonPropertyName serialization issues
    var response = events.Select(EventResponse.FromEvent).ToList();
    return Results.Ok(response);
});

// API: Get single event (universal for all sports)
app.MapGet("/api/events/{id:int}", async (int id, SportarrDbContext db) =>
{
    var evt = await db.Events
        .Include(e => e.League)        // Universal (UFC, Premier League, NBA, etc.)
        .Include(e => e.HomeTeam)      // Universal (team sports and combat sports)
        .Include(e => e.AwayTeam)      // Universal (team sports and combat sports)
        .FirstOrDefaultAsync(e => e.Id == id);

    if (evt is null) return Results.NotFound();

    // Return DTO to avoid JsonPropertyName serialization issues
    return Results.Ok(EventResponse.FromEvent(evt));
});

// API: Create event (universal for all sports)
app.MapPost("/api/events", async (CreateEventRequest request, SportarrDbContext db) =>
{
    var evt = new Event
    {
        ExternalId = request.ExternalId,
        Title = request.Title,
        Sport = request.Sport,           // Universal: Fighting, Soccer, Basketball, etc.
        LeagueId = request.LeagueId,     // Universal: UFC, Premier League, NBA
        HomeTeamId = request.HomeTeamId, // Team sports and combat sports
        AwayTeamId = request.AwayTeamId, // Team sports and combat sports
        Season = request.Season,
        Round = request.Round,
        EventDate = request.EventDate,
        Venue = request.Venue,
        Location = request.Location,
        Broadcast = request.Broadcast,
        Status = request.Status,
        Monitored = request.Monitored,
        QualityProfileId = request.QualityProfileId,
        Images = request.Images ?? new List<string>()
    };

    // Check if event already exists (by ExternalId OR by Title + EventDate)
    var existingEvent = await db.Events
        .Include(e => e.League)
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .FirstOrDefaultAsync(e =>
            (e.ExternalId != null && e.ExternalId == evt.ExternalId) ||
            (e.Title == evt.Title && e.EventDate.Date == evt.EventDate.Date));

    if (existingEvent != null)
    {
        // Event already exists - return it with AlreadyAdded flag
        return Results.Ok(new { Event = existingEvent, AlreadyAdded = true });
    }

    db.Events.Add(evt);
    await db.SaveChangesAsync();

    // Reload event with related entities
    var createdEvent = await db.Events
        .Include(e => e.League)
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .FirstOrDefaultAsync(e => e.Id == evt.Id);

    if (createdEvent is null) return Results.Problem("Failed to create event");

    return Results.Created($"/api/events/{evt.Id}", createdEvent);
});

// API: Update event (universal for all sports)
app.MapPut("/api/events/{id:int}", async (int id, JsonElement body, SportarrDbContext db) =>
{
    var evt = await db.Events.FindAsync(id);
    if (evt is null) return Results.NotFound();

    // Extract fields from request body (only update fields that are present)
    if (body.TryGetProperty("title", out var titleValue))
        evt.Title = titleValue.GetString() ?? evt.Title;

    if (body.TryGetProperty("sport", out var sportValue))
        evt.Sport = sportValue.GetString() ?? evt.Sport;

    if (body.TryGetProperty("leagueId", out var leagueIdValue))
    {
        if (leagueIdValue.ValueKind == JsonValueKind.Null)
            evt.LeagueId = null;
        else if (leagueIdValue.ValueKind == JsonValueKind.Number)
            evt.LeagueId = leagueIdValue.GetInt32();
    }

    if (body.TryGetProperty("eventDate", out var dateValue))
        evt.EventDate = dateValue.GetDateTime();

    if (body.TryGetProperty("venue", out var venueValue))
        evt.Venue = venueValue.GetString();

    if (body.TryGetProperty("location", out var locationValue))
        evt.Location = locationValue.GetString();

    if (body.TryGetProperty("monitored", out var monitoredValue))
        evt.Monitored = monitoredValue.GetBoolean();

    if (body.TryGetProperty("qualityProfileId", out var qualityProfileIdValue))
    {
        if (qualityProfileIdValue.ValueKind == JsonValueKind.Null)
            evt.QualityProfileId = null;
        else if (qualityProfileIdValue.ValueKind == JsonValueKind.Number)
            evt.QualityProfileId = qualityProfileIdValue.GetInt32();
    }

    evt.LastUpdate = DateTime.UtcNow;

    await db.SaveChangesAsync();

    // Reload with related entities
    evt = await db.Events
        .Include(e => e.League)
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .FirstOrDefaultAsync(e => e.Id == id);

    if (evt is null) return Results.NotFound();

    // Return DTO to avoid JsonPropertyName serialization issues
    return Results.Ok(EventResponse.FromEvent(evt));
});

// API: Delete event
app.MapDelete("/api/events/{id:int}", async (int id, SportarrDbContext db) =>
{
    var evt = await db.Events.FindAsync(id);
    if (evt is null) return Results.NotFound();

    db.Events.Remove(evt);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// REMOVED: FightCard endpoints (obsolete - universal approach uses Event.Monitored)
// REMOVED: Organization endpoints (obsolete - replaced with League-based API)
// - /api/organizations (GET) - Replaced with /api/leagues
// - /api/organizations/{name}/events (GET) - Replaced with /api/leagues/{id}/events

// API: Get tags
app.MapGet("/api/tag", async (SportarrDbContext db) =>
{
    var tags = await db.Tags.ToListAsync();
    return Results.Ok(tags);
});

// API: Get quality profiles
app.MapGet("/api/qualityprofile", async (SportarrDbContext db) =>
{
    var profiles = await db.QualityProfiles.ToListAsync();
    return Results.Ok(profiles);
});

// API: Get single quality profile
app.MapGet("/api/qualityprofile/{id}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.QualityProfiles.FindAsync(id);
    return profile == null ? Results.NotFound() : Results.Ok(profile);
});

// API: Create quality profile
app.MapPost("/api/qualityprofile", async (QualityProfile profile, SportarrDbContext db) =>
{
    // Check for duplicate name
    var existingWithName = await db.QualityProfiles
        .FirstOrDefaultAsync(p => p.Name == profile.Name);

    if (existingWithName != null)
    {
        return Results.BadRequest(new { error = "A quality profile with this name already exists" });
    }

    db.QualityProfiles.Add(profile);
    await db.SaveChangesAsync();
    return Results.Ok(profile);
});

// API: Update quality profile
app.MapPut("/api/qualityprofile/{id}", async (int id, QualityProfile profile, SportarrDbContext db) =>
{
    var existing = await db.QualityProfiles.FindAsync(id);
    if (existing == null) return Results.NotFound();

    // Check for duplicate name (excluding current profile)
    var duplicateName = await db.QualityProfiles
        .FirstOrDefaultAsync(p => p.Name == profile.Name && p.Id != id);

    if (duplicateName != null)
    {
        return Results.BadRequest(new { error = "A quality profile with this name already exists" });
    }

    existing.Name = profile.Name;
    existing.IsDefault = profile.IsDefault;
    existing.UpgradesAllowed = profile.UpgradesAllowed;
    existing.CutoffQuality = profile.CutoffQuality;
    existing.Items = profile.Items;
    existing.FormatItems = profile.FormatItems;
    existing.MinFormatScore = profile.MinFormatScore;
    existing.CutoffFormatScore = profile.CutoffFormatScore;
    existing.FormatScoreIncrement = profile.FormatScoreIncrement;
    existing.MinSize = profile.MinSize;
    existing.MaxSize = profile.MaxSize;

    await db.SaveChangesAsync();
    return Results.Ok(existing);
});

// API: Delete quality profile
app.MapDelete("/api/qualityprofile/{id}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.QualityProfiles.FindAsync(id);
    if (profile == null) return Results.NotFound();

    db.QualityProfiles.Remove(profile);
    await db.SaveChangesAsync();
    return Results.Ok();
});

// API: Get all custom formats
app.MapGet("/api/customformat", async (SportarrDbContext db) =>
{
    var formats = await db.CustomFormats.ToListAsync();
    return Results.Ok(formats);
});

// API: Get single custom format
app.MapGet("/api/customformat/{id}", async (int id, SportarrDbContext db) =>
{
    var format = await db.CustomFormats.FindAsync(id);
    return format == null ? Results.NotFound() : Results.Ok(format);
});

// API: Create custom format
app.MapPost("/api/customformat", async (CustomFormat format, SportarrDbContext db) =>
{
    format.Created = DateTime.UtcNow;
    db.CustomFormats.Add(format);
    await db.SaveChangesAsync();
    return Results.Ok(format);
});

// API: Update custom format
app.MapPut("/api/customformat/{id}", async (int id, CustomFormat format, SportarrDbContext db) =>
{
    var existing = await db.CustomFormats.FindAsync(id);
    if (existing == null) return Results.NotFound();

    existing.Name = format.Name;
    existing.IncludeCustomFormatWhenRenaming = format.IncludeCustomFormatWhenRenaming;
    existing.Specifications = format.Specifications;
    existing.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(existing);
});

// API: Delete custom format
app.MapDelete("/api/customformat/{id}", async (int id, SportarrDbContext db) =>
{
    var format = await db.CustomFormats.FindAsync(id);
    if (format == null) return Results.NotFound();

    db.CustomFormats.Remove(format);
    await db.SaveChangesAsync();
    return Results.Ok();
});

// API: Get all delay profiles
app.MapGet("/api/delayprofile", async (SportarrDbContext db) =>
{
    var profiles = await db.DelayProfiles.OrderBy(d => d.Order).ToListAsync();
    return Results.Ok(profiles);
});

// API: Get single delay profile
app.MapGet("/api/delayprofile/{id}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.DelayProfiles.FindAsync(id);
    return profile == null ? Results.NotFound() : Results.Ok(profile);
});

// API: Create delay profile
app.MapPost("/api/delayprofile", async (DelayProfile profile, SportarrDbContext db) =>
{
    profile.Created = DateTime.UtcNow;
    db.DelayProfiles.Add(profile);
    await db.SaveChangesAsync();
    return Results.Ok(profile);
});

// API: Update delay profile
app.MapPut("/api/delayprofile/{id}", async (int id, DelayProfile profile, SportarrDbContext db) =>
{
    var existing = await db.DelayProfiles.FindAsync(id);
    if (existing == null) return Results.NotFound();

    existing.Order = profile.Order;
    existing.PreferredProtocol = profile.PreferredProtocol;
    existing.UsenetDelay = profile.UsenetDelay;
    existing.TorrentDelay = profile.TorrentDelay;
    existing.BypassIfHighestQuality = profile.BypassIfHighestQuality;
    existing.BypassIfAboveCustomFormatScore = profile.BypassIfAboveCustomFormatScore;
    existing.MinimumCustomFormatScore = profile.MinimumCustomFormatScore;
    existing.Tags = profile.Tags;
    existing.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(existing);
});

// API: Delete delay profile
app.MapDelete("/api/delayprofile/{id}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.DelayProfiles.FindAsync(id);
    if (profile == null) return Results.NotFound();

    db.DelayProfiles.Remove(profile);
    await db.SaveChangesAsync();
    return Results.Ok();
});

// API: Reorder delay profiles
app.MapPut("/api/delayprofile/reorder", async (List<int> profileIds, SportarrDbContext db) =>
{
    for (int i = 0; i < profileIds.Count; i++)
    {
        var profile = await db.DelayProfiles.FindAsync(profileIds[i]);
        if (profile != null)
        {
            profile.Order = i + 1;
        }
    }
    await db.SaveChangesAsync();
    return Results.Ok();
});

// API: Release Profiles Management
app.MapGet("/api/releaseprofile", async (SportarrDbContext db) =>
{
    var profiles = await db.ReleaseProfiles.OrderBy(p => p.Name).ToListAsync();
    return Results.Ok(profiles);
});

app.MapGet("/api/releaseprofile/{id:int}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.ReleaseProfiles.FindAsync(id);
    return profile is not null ? Results.Ok(profile) : Results.NotFound();
});

app.MapPost("/api/releaseprofile", async (ReleaseProfile profile, SportarrDbContext db) =>
{
    profile.Created = DateTime.UtcNow;
    db.ReleaseProfiles.Add(profile);
    await db.SaveChangesAsync();
    return Results.Created($"/api/releaseprofile/{profile.Id}", profile);
});

app.MapPut("/api/releaseprofile/{id:int}", async (int id, ReleaseProfile updatedProfile, SportarrDbContext db) =>
{
    var profile = await db.ReleaseProfiles.FindAsync(id);
    if (profile is null) return Results.NotFound();

    profile.Name = updatedProfile.Name;
    profile.Enabled = updatedProfile.Enabled;
    profile.Required = updatedProfile.Required;
    profile.Ignored = updatedProfile.Ignored;
    profile.Preferred = updatedProfile.Preferred;
    profile.IncludePreferredWhenRenaming = updatedProfile.IncludePreferredWhenRenaming;
    profile.Tags = updatedProfile.Tags;
    profile.IndexerId = updatedProfile.IndexerId;
    profile.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(profile);
});

app.MapDelete("/api/releaseprofile/{id:int}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.ReleaseProfiles.FindAsync(id);
    if (profile is null) return Results.NotFound();

    db.ReleaseProfiles.Remove(profile);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Quality Definitions Management
app.MapGet("/api/qualitydefinition", async (SportarrDbContext db) =>
{
    var definitions = await db.QualityDefinitions.OrderBy(q => q.Quality).ToListAsync();
    return Results.Ok(definitions);
});

app.MapGet("/api/qualitydefinition/{id:int}", async (int id, SportarrDbContext db) =>
{
    var definition = await db.QualityDefinitions.FindAsync(id);
    return definition is not null ? Results.Ok(definition) : Results.NotFound();
});

app.MapPut("/api/qualitydefinition/{id:int}", async (int id, QualityDefinition updatedDef, SportarrDbContext db) =>
{
    var definition = await db.QualityDefinitions.FindAsync(id);
    if (definition is null) return Results.NotFound();

    definition.MinSize = updatedDef.MinSize;
    definition.MaxSize = updatedDef.MaxSize;
    definition.PreferredSize = updatedDef.PreferredSize;
    definition.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(definition);
});

app.MapPut("/api/qualitydefinition/bulk", async (List<QualityDefinition> definitions, SportarrDbContext db) =>
{
    foreach (var updatedDef in definitions)
    {
        var definition = await db.QualityDefinitions.FindAsync(updatedDef.Id);
        if (definition is not null)
        {
            definition.MinSize = updatedDef.MinSize;
            definition.MaxSize = updatedDef.MaxSize;
            definition.PreferredSize = updatedDef.PreferredSize;
            definition.LastModified = DateTime.UtcNow;
        }
    }
    await db.SaveChangesAsync();
    return Results.Ok();
});

// API: Import Lists Management
app.MapGet("/api/importlist", async (SportarrDbContext db) =>
{
    var lists = await db.ImportLists.OrderBy(l => l.Name).ToListAsync();
    return Results.Ok(lists);
});

app.MapGet("/api/importlist/{id:int}", async (int id, SportarrDbContext db) =>
{
    var list = await db.ImportLists.FindAsync(id);
    return list is not null ? Results.Ok(list) : Results.NotFound();
});

app.MapPost("/api/importlist", async (ImportList list, SportarrDbContext db) =>
{
    list.Created = DateTime.UtcNow;
    db.ImportLists.Add(list);
    await db.SaveChangesAsync();
    return Results.Created($"/api/importlist/{list.Id}", list);
});

app.MapPut("/api/importlist/{id:int}", async (int id, ImportList updatedList, SportarrDbContext db) =>
{
    var list = await db.ImportLists.FindAsync(id);
    if (list is null) return Results.NotFound();

    list.Name = updatedList.Name;
    list.Enabled = updatedList.Enabled;
    list.ListType = updatedList.ListType;
    list.Url = updatedList.Url;
    list.ApiKey = updatedList.ApiKey;
    list.QualityProfileId = updatedList.QualityProfileId;
    list.RootFolderPath = updatedList.RootFolderPath;
    list.MonitorEvents = updatedList.MonitorEvents;
    list.SearchOnAdd = updatedList.SearchOnAdd;
    list.Tags = updatedList.Tags;
    list.MinimumDaysBeforeEvent = updatedList.MinimumDaysBeforeEvent;
    list.LeagueFilter = updatedList.LeagueFilter;
    list.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(list);
});

app.MapDelete("/api/importlist/{id:int}", async (int id, SportarrDbContext db) =>
{
    var list = await db.ImportLists.FindAsync(id);
    if (list is null) return Results.NotFound();

    db.ImportLists.Remove(list);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapPost("/api/importlist/{id:int}/sync", async (int id, ImportListService importListService) =>
{
    var (success, message, eventsFound) = await importListService.SyncImportListAsync(id);

    if (success)
    {
        return Results.Ok(new
        {
            success = true,
            message,
            eventsFound,
            listId = id
        });
    }
    else
    {
        return Results.BadRequest(new
        {
            success = false,
            message,
            eventsFound = 0,
            listId = id
        });
    }
});

// API: Metadata Providers Management
app.MapGet("/api/metadata", async (SportarrDbContext db) =>
{
    var providers = await db.MetadataProviders.OrderBy(m => m.Name).ToListAsync();
    return Results.Ok(providers);
});

app.MapGet("/api/metadata/{id:int}", async (int id, SportarrDbContext db) =>
{
    var provider = await db.MetadataProviders.FindAsync(id);
    return provider is not null ? Results.Ok(provider) : Results.NotFound();
});

app.MapPost("/api/metadata", async (MetadataProvider provider, SportarrDbContext db) =>
{
    provider.Created = DateTime.UtcNow;
    db.MetadataProviders.Add(provider);
    await db.SaveChangesAsync();
    return Results.Created($"/api/metadata/{provider.Id}", provider);
});

app.MapPut("/api/metadata/{id:int}", async (int id, MetadataProvider provider, SportarrDbContext db) =>
{
    var existing = await db.MetadataProviders.FindAsync(id);
    if (existing is null) return Results.NotFound();

    existing.Name = provider.Name;
    existing.Type = provider.Type;
    existing.Enabled = provider.Enabled;
    existing.EventNfo = provider.EventNfo;
    existing.EventCardNfo = provider.EventCardNfo;
    existing.EventImages = provider.EventImages;
    existing.PlayerImages = provider.PlayerImages;
    existing.LeagueLogos = provider.LeagueLogos;
    existing.EventNfoFilename = provider.EventNfoFilename;
    existing.EventPosterFilename = provider.EventPosterFilename;
    existing.EventFanartFilename = provider.EventFanartFilename;
    existing.UseEventFolder = provider.UseEventFolder;
    existing.ImageQuality = provider.ImageQuality;
    existing.Tags = provider.Tags;
    existing.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(existing);
});

app.MapDelete("/api/metadata/{id:int}", async (int id, SportarrDbContext db) =>
{
    var provider = await db.MetadataProviders.FindAsync(id);
    if (provider is null) return Results.NotFound();

    db.MetadataProviders.Remove(provider);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Tags Management
app.MapPost("/api/tag", async (Tag tag, SportarrDbContext db) =>
{
    db.Tags.Add(tag);
    await db.SaveChangesAsync();
    return Results.Created($"/api/tag/{tag.Id}", tag);
});

app.MapPut("/api/tag/{id:int}", async (int id, Tag updatedTag, SportarrDbContext db) =>
{
    var tag = await db.Tags.FindAsync(id);
    if (tag is null) return Results.NotFound();

    tag.Label = updatedTag.Label;
    tag.Color = updatedTag.Color;
    await db.SaveChangesAsync();
    return Results.Ok(tag);
});

app.MapDelete("/api/tag/{id:int}", async (int id, SportarrDbContext db) =>
{
    var tag = await db.Tags.FindAsync(id);
    if (tag is null) return Results.NotFound();

    db.Tags.Remove(tag);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Root Folders Management
app.MapGet("/api/rootfolder", async (SportarrDbContext db) =>
{
    var folders = await db.RootFolders.ToListAsync();
    return Results.Ok(folders);
});

app.MapPost("/api/rootfolder", async (RootFolder folder, SportarrDbContext db) =>
{
    // Check if folder path already exists
    if (await db.RootFolders.AnyAsync(f => f.Path == folder.Path))
    {
        return Results.BadRequest(new { error = "Root folder already exists" });
    }

    // Check folder accessibility
    folder.Accessible = Directory.Exists(folder.Path);
    if (folder.Accessible)
    {
        try
        {
            var driveInfo = new DriveInfo(Path.GetPathRoot(folder.Path) ?? folder.Path);
            folder.FreeSpace = driveInfo.AvailableFreeSpace;
        }
        catch
        {
            folder.FreeSpace = 0;
        }
    }
    folder.LastChecked = DateTime.UtcNow;

    db.RootFolders.Add(folder);
    await db.SaveChangesAsync();
    return Results.Created($"/api/rootfolder/{folder.Id}", folder);
});

app.MapDelete("/api/rootfolder/{id:int}", async (int id, SportarrDbContext db) =>
{
    var folder = await db.RootFolders.FindAsync(id);
    if (folder is null) return Results.NotFound();

    db.RootFolders.Remove(folder);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Filesystem Browser (for root folder selection)
app.MapGet("/api/filesystem", (string? path, bool? includeFiles) =>
{
    try
    {
        // Default to root drives if no path provided
        if (string.IsNullOrEmpty(path))
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new
                {
                    type = "drive",
                    name = d.Name,
                    path = d.RootDirectory.FullName,
                    freeSpace = d.AvailableFreeSpace,
                    totalSpace = d.TotalSize
                })
                .ToList();

            return Results.Ok(new
            {
                parent = (string?)null,
                directories = drives
            });
        }

        // Ensure path exists
        if (!Directory.Exists(path))
        {
            return Results.BadRequest(new { error = "Directory does not exist" });
        }

        var directoryInfo = new DirectoryInfo(path);
        var parent = directoryInfo.Parent?.FullName;

        // Get subdirectories
        var directories = directoryInfo.GetDirectories()
            .Where(d => !d.Attributes.HasFlag(FileAttributes.System) && !d.Attributes.HasFlag(FileAttributes.Hidden))
            .Select(d => new
            {
                type = "folder",
                name = d.Name,
                path = d.FullName,
                lastModified = d.LastWriteTimeUtc
            })
            .OrderBy(d => d.name)
            .ToList();

        // Optionally include files
        object? files = null;
        if (includeFiles == true)
        {
            files = directoryInfo.GetFiles()
                .Where(f => !f.Attributes.HasFlag(FileAttributes.System) && !f.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(f => new
                {
                    type = "file",
                    name = f.Name,
                    path = f.FullName,
                    size = f.Length,
                    lastModified = f.LastWriteTimeUtc
                })
                .OrderBy(f => f.name)
                .ToList();
        }

        return Results.Ok(new
        {
            parent,
            directories,
            files
        });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.BadRequest(new { error = "Access denied to this directory" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// API: Notifications Management
app.MapGet("/api/notification", async (SportarrDbContext db) =>
{
    var notifications = await db.Notifications.ToListAsync();
    return Results.Ok(notifications);
});

app.MapPost("/api/notification", async (Notification notification, SportarrDbContext db) =>
{
    notification.Created = DateTime.UtcNow;
    notification.LastModified = DateTime.UtcNow;
    db.Notifications.Add(notification);
    await db.SaveChangesAsync();
    return Results.Created($"/api/notification/{notification.Id}", notification);
});

app.MapPut("/api/notification/{id:int}", async (int id, Notification updatedNotification, SportarrDbContext db) =>
{
    var notification = await db.Notifications.FindAsync(id);
    if (notification is null) return Results.NotFound();

    notification.Name = updatedNotification.Name;
    notification.Implementation = updatedNotification.Implementation;
    notification.Enabled = updatedNotification.Enabled;
    notification.ConfigJson = updatedNotification.ConfigJson;
    notification.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(notification);
});

app.MapDelete("/api/notification/{id:int}", async (int id, SportarrDbContext db) =>
{
    var notification = await db.Notifications.FindAsync(id);
    if (notification is null) return Results.NotFound();

    db.Notifications.Remove(notification);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Settings Management (using config.xml)
app.MapGet("/api/settings", async (Sportarr.Api.Services.ConfigService configService) =>
{
    var config = await configService.GetConfigAsync();

    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    // Convert Config to AppSettings format for frontend compatibility
    var settings = new AppSettings
    {
        HostSettings = System.Text.Json.JsonSerializer.Serialize(new HostSettings
        {
            BindAddress = config.BindAddress,
            Port = config.Port,
            UrlBase = config.UrlBase,
            InstanceName = config.InstanceName,
            EnableSsl = config.EnableSsl,
            SslPort = config.SslPort,
            SslCertPath = config.SslCertPath,
            SslCertPassword = config.SslCertPassword
        }, jsonOptions),

        SecuritySettings = System.Text.Json.JsonSerializer.Serialize(new SecuritySettings
        {
            AuthenticationMethod = config.AuthenticationMethod.ToLower(),
            AuthenticationRequired = config.AuthenticationRequired.ToLower(),
            Username = config.Username,
            Password = "",
            ApiKey = config.ApiKey,
            CertificateValidation = config.CertificateValidation.ToLower(),
            PasswordHash = config.PasswordHash,
            PasswordSalt = config.PasswordSalt,
            PasswordIterations = config.PasswordIterations
        }, jsonOptions),

        ProxySettings = System.Text.Json.JsonSerializer.Serialize(new ProxySettings
        {
            UseProxy = config.UseProxy,
            ProxyType = config.ProxyType.ToLower(),
            ProxyHostname = config.ProxyHostname,
            ProxyPort = config.ProxyPort,
            ProxyUsername = config.ProxyUsername,
            ProxyPassword = config.ProxyPassword,
            ProxyBypassFilter = config.ProxyBypassFilter,
            ProxyBypassLocalAddresses = config.ProxyBypassLocalAddresses
        }, jsonOptions),

        LoggingSettings = System.Text.Json.JsonSerializer.Serialize(new LoggingSettings
        {
            LogLevel = config.LogLevel.ToLower()
        }, jsonOptions),

        AnalyticsSettings = System.Text.Json.JsonSerializer.Serialize(new AnalyticsSettings
        {
            SendAnonymousUsageData = config.SendAnonymousUsageData
        }, jsonOptions),

        BackupSettings = System.Text.Json.JsonSerializer.Serialize(new BackupSettings
        {
            BackupFolder = config.BackupFolder,
            BackupInterval = config.BackupInterval,
            BackupRetention = config.BackupRetention
        }, jsonOptions),

        UpdateSettings = System.Text.Json.JsonSerializer.Serialize(new UpdateSettings
        {
            Branch = config.Branch.ToLower(),
            Automatic = config.UpdateAutomatically, // Use Sonarr field name
            Mechanism = config.UpdateMechanism.ToLower(), // Use Sonarr field name
            ScriptPath = config.UpdateScriptPath // Use Sonarr field name
        }, jsonOptions),

        UISettings = System.Text.Json.JsonSerializer.Serialize(new UISettings
        {
            FirstDayOfWeek = config.FirstDayOfWeek.ToLower(),
            CalendarWeekColumnHeader = config.CalendarWeekColumnHeader,
            ShortDateFormat = config.ShortDateFormat,
            LongDateFormat = config.LongDateFormat,
            TimeFormat = config.TimeFormat,
            ShowRelativeDates = config.ShowRelativeDates,
            Theme = config.Theme.ToLower(),
            EnableColorImpairedMode = config.EnableColorImpairedMode,
            UILanguage = config.UILanguage,
            ShowUnknownLeagueItems = config.ShowUnknownLeagueItems,
            ShowEventPath = config.ShowEventPath
        }, jsonOptions),

        MediaManagementSettings = System.Text.Json.JsonSerializer.Serialize(new MediaManagementSettings
        {
            RenameEvents = config.RenameEvents,
            ReplaceIllegalCharacters = config.ReplaceIllegalCharacters,
            StandardEventFormat = config.StandardEventFormat,
            CreateEventFolders = config.CreateEventFolders,
            DeleteEmptyFolders = config.DeleteEmptyFolders,
            SkipFreeSpaceCheck = config.SkipFreeSpaceCheck,
            MinimumFreeSpace = config.MinimumFreeSpace,
            UseHardlinks = config.UseHardlinks,
            ImportExtraFiles = config.ImportExtraFiles,
            ExtraFileExtensions = config.ExtraFileExtensions,
            ChangeFileDate = config.ChangeFileDate,
            RecycleBin = config.RecycleBin,
            RecycleBinCleanup = config.RecycleBinCleanup,
            SetPermissions = config.SetPermissions,
            ChmodFolder = config.ChmodFolder,
            ChownGroup = config.ChownGroup
        }, jsonOptions),

        LastModified = DateTime.UtcNow
    };

    return Results.Ok(settings);
});

app.MapPut("/api/settings", async (AppSettings updatedSettings, Sportarr.Api.Services.ConfigService configService, Sportarr.Api.Services.SimpleAuthService simpleAuthService, ILogger<Program> logger) =>
{
    logger.LogInformation("[CONFIG] Settings update requested");

    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    // Parse all settings from frontend format
    var hostSettings = System.Text.Json.JsonSerializer.Deserialize<HostSettings>(updatedSettings.HostSettings, jsonOptions);
    var securitySettings = System.Text.Json.JsonSerializer.Deserialize<SecuritySettings>(updatedSettings.SecuritySettings, jsonOptions);
    var proxySettings = System.Text.Json.JsonSerializer.Deserialize<ProxySettings>(updatedSettings.ProxySettings, jsonOptions);
    var loggingSettings = System.Text.Json.JsonSerializer.Deserialize<LoggingSettings>(updatedSettings.LoggingSettings, jsonOptions);
    var analyticsSettings = System.Text.Json.JsonSerializer.Deserialize<AnalyticsSettings>(updatedSettings.AnalyticsSettings, jsonOptions);
    var backupSettings = System.Text.Json.JsonSerializer.Deserialize<BackupSettings>(updatedSettings.BackupSettings, jsonOptions);
    var updateSettingsObj = System.Text.Json.JsonSerializer.Deserialize<UpdateSettings>(updatedSettings.UpdateSettings, jsonOptions);
    var uiSettings = System.Text.Json.JsonSerializer.Deserialize<UISettings>(updatedSettings.UISettings, jsonOptions);
    var mediaManagementSettings = System.Text.Json.JsonSerializer.Deserialize<MediaManagementSettings>(updatedSettings.MediaManagementSettings, jsonOptions);

    // Handle password hashing if needed
    if (securitySettings != null)
    {
        if (!string.IsNullOrWhiteSpace(securitySettings.Username) &&
            !string.IsNullOrWhiteSpace(securitySettings.Password))
        {
            logger.LogInformation("[AUTH] Updating credentials for user: {Username}", securitySettings.Username);
            await simpleAuthService.SetCredentialsAsync(securitySettings.Username, securitySettings.Password);
            logger.LogInformation("[AUTH] Credentials updated successfully");
        }
        else if (!string.IsNullOrWhiteSpace(securitySettings.Username))
        {
            logger.LogInformation("[AUTH] Username-only update requested for: {Username}", securitySettings.Username);
            await simpleAuthService.SetUsernameAsync(securitySettings.Username);
            logger.LogInformation("[AUTH] Username updated successfully");
        }
    }

    // Update config.xml with all settings
    await configService.UpdateConfigAsync(config =>
    {
        if (hostSettings != null)
        {
            config.BindAddress = hostSettings.BindAddress;
            config.Port = hostSettings.Port;
            config.UrlBase = hostSettings.UrlBase;
            config.InstanceName = hostSettings.InstanceName;
            config.EnableSsl = hostSettings.EnableSsl;
            config.SslPort = hostSettings.SslPort;
            config.SslCertPath = hostSettings.SslCertPath;
            config.SslCertPassword = hostSettings.SslCertPassword;
        }

        if (securitySettings != null)
        {
            config.AuthenticationMethod = securitySettings.AuthenticationMethod;
            config.AuthenticationRequired = securitySettings.AuthenticationRequired;
            config.Username = securitySettings.Username;
            // Don't overwrite API key from frontend (it's read-only, managed by regenerate endpoint)
            config.CertificateValidation = securitySettings.CertificateValidation;
            config.PasswordHash = securitySettings.PasswordHash;
            config.PasswordSalt = securitySettings.PasswordSalt;
            config.PasswordIterations = securitySettings.PasswordIterations;
        }

        if (proxySettings != null)
        {
            config.UseProxy = proxySettings.UseProxy;
            config.ProxyType = proxySettings.ProxyType;
            config.ProxyHostname = proxySettings.ProxyHostname;
            config.ProxyPort = proxySettings.ProxyPort;
            config.ProxyUsername = proxySettings.ProxyUsername;
            config.ProxyPassword = proxySettings.ProxyPassword;
            config.ProxyBypassFilter = proxySettings.ProxyBypassFilter;
            config.ProxyBypassLocalAddresses = proxySettings.ProxyBypassLocalAddresses;
        }

        if (loggingSettings != null)
        {
            config.LogLevel = loggingSettings.LogLevel;
        }

        if (analyticsSettings != null)
        {
            config.SendAnonymousUsageData = analyticsSettings.SendAnonymousUsageData;
        }

        if (backupSettings != null)
        {
            config.BackupFolder = backupSettings.BackupFolder;
            config.BackupInterval = backupSettings.BackupInterval;
            config.BackupRetention = backupSettings.BackupRetention;
        }

        if (updateSettingsObj != null)
        {
            config.Branch = updateSettingsObj.Branch;
            config.UpdateAutomatically = updateSettingsObj.Automatic; // Use Sonarr field name
            config.UpdateMechanism = updateSettingsObj.Mechanism; // Use Sonarr field name
            config.UpdateScriptPath = updateSettingsObj.ScriptPath; // Use Sonarr field name
        }

        if (uiSettings != null)
        {
            config.FirstDayOfWeek = uiSettings.FirstDayOfWeek;
            config.CalendarWeekColumnHeader = uiSettings.CalendarWeekColumnHeader;
            config.ShortDateFormat = uiSettings.ShortDateFormat;
            config.LongDateFormat = uiSettings.LongDateFormat;
            config.TimeFormat = uiSettings.TimeFormat;
            config.ShowRelativeDates = uiSettings.ShowRelativeDates;
            config.Theme = uiSettings.Theme;
            config.EnableColorImpairedMode = uiSettings.EnableColorImpairedMode;
            config.UILanguage = uiSettings.UILanguage;
            config.ShowUnknownLeagueItems = uiSettings.ShowUnknownLeagueItems;
            config.ShowEventPath = uiSettings.ShowEventPath;
        }

        if (mediaManagementSettings != null)
        {
            config.RenameEvents = mediaManagementSettings.RenameEvents;
            config.ReplaceIllegalCharacters = mediaManagementSettings.ReplaceIllegalCharacters;
            config.StandardEventFormat = mediaManagementSettings.StandardEventFormat;
            config.CreateEventFolders = mediaManagementSettings.CreateEventFolders;
            config.DeleteEmptyFolders = mediaManagementSettings.DeleteEmptyFolders;
            config.SkipFreeSpaceCheck = mediaManagementSettings.SkipFreeSpaceCheck;
            config.MinimumFreeSpace = (int)mediaManagementSettings.MinimumFreeSpace;
            config.UseHardlinks = mediaManagementSettings.UseHardlinks;
            config.ImportExtraFiles = mediaManagementSettings.ImportExtraFiles;
            config.ExtraFileExtensions = mediaManagementSettings.ExtraFileExtensions;
            config.ChangeFileDate = mediaManagementSettings.ChangeFileDate;
            config.RecycleBin = mediaManagementSettings.RecycleBin;
            config.RecycleBinCleanup = mediaManagementSettings.RecycleBinCleanup;
            config.SetPermissions = mediaManagementSettings.SetPermissions;
            config.ChmodFolder = mediaManagementSettings.ChmodFolder;
            config.ChownGroup = mediaManagementSettings.ChownGroup;
        }
    });

    logger.LogInformation("[CONFIG] Settings saved to config.xml successfully");
    return Results.Ok(updatedSettings);
});

// API: Regenerate API Key (Sonarr pattern - no restart required)
app.MapPost("/api/settings/apikey/regenerate", async (Sportarr.Api.Services.ConfigService configService, ILogger<Program> logger) =>
{
    logger.LogWarning("[API KEY] API key regeneration requested");
    var newApiKey = await configService.RegenerateApiKeyAsync();
    logger.LogWarning("[API KEY] API key regenerated and saved to config.xml - all connected applications must be updated!");
    return Results.Ok(new { apiKey = newApiKey, message = "API key regenerated successfully. Update all connected applications (Prowlarr, download clients, etc.) with the new key." });
});

// API: Download Clients Management
app.MapGet("/api/downloadclient", async (SportarrDbContext db) =>
{
    var clients = await db.DownloadClients.OrderBy(dc => dc.Priority).ToListAsync();
    return Results.Ok(clients);
});

app.MapGet("/api/downloadclient/{id:int}", async (int id, SportarrDbContext db) =>
{
    var client = await db.DownloadClients.FindAsync(id);
    return client is null ? Results.NotFound() : Results.Ok(client);
});

app.MapPost("/api/downloadclient", async (DownloadClient client, SportarrDbContext db) =>
{
    client.Created = DateTime.UtcNow;
    db.DownloadClients.Add(client);
    await db.SaveChangesAsync();
    return Results.Created($"/api/downloadclient/{client.Id}", client);
});

app.MapPut("/api/downloadclient/{id:int}", async (int id, DownloadClient updatedClient, SportarrDbContext db, ILogger<Program> logger) =>
{
    var client = await db.DownloadClients.FindAsync(id);
    if (client is null)
    {
        logger.LogWarning("[Download Client] Client ID {Id} not found for update", id);
        return Results.NotFound(new { error = $"Download client with ID {id} not found" });
    }

    client.Name = updatedClient.Name;
    client.Type = updatedClient.Type;
    client.Host = updatedClient.Host;
    client.Port = updatedClient.Port;
    client.Username = updatedClient.Username;
    client.Password = updatedClient.Password;
    client.ApiKey = updatedClient.ApiKey;
    client.Category = updatedClient.Category;
    client.UseSsl = updatedClient.UseSsl;
    client.Enabled = updatedClient.Enabled;
    client.Priority = updatedClient.Priority;
    client.LastModified = DateTime.UtcNow;

    try
    {
        await db.SaveChangesAsync();
        logger.LogInformation("[Download Client] Updated client {Name} (ID: {Id})", client.Name, id);
        return Results.Ok(client);
    }
    catch (DbUpdateConcurrencyException ex)
    {
        logger.LogError(ex, "[Download Client] Concurrency error updating client {Id}: Record may have been deleted", id);
        return Results.Conflict(new { error = "This download client was modified or deleted. Please refresh and try again." });
    }
});

app.MapDelete("/api/downloadclient/{id:int}", async (int id, SportarrDbContext db) =>
{
    var client = await db.DownloadClients.FindAsync(id);
    if (client is null) return Results.NotFound();

    db.DownloadClients.Remove(client);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Test download client connection - supports all client types
app.MapPost("/api/downloadclient/test", async (DownloadClient client, Sportarr.Api.Services.DownloadClientService downloadClientService) =>
{
    var (success, message) = await downloadClientService.TestConnectionAsync(client);

    if (success)
    {
        return Results.Ok(new { success = true, message });
    }

    return Results.BadRequest(new { success = false, message });
});

// API: Remote Path Mappings (for download client path translation)
app.MapGet("/api/remotepathmapping", async (SportarrDbContext db) =>
{
    var mappings = await db.RemotePathMappings.OrderBy(m => m.Host).ToListAsync();
    return Results.Ok(mappings);
});

app.MapGet("/api/remotepathmapping/{id:int}", async (int id, SportarrDbContext db) =>
{
    var mapping = await db.RemotePathMappings.FindAsync(id);
    return mapping is null ? Results.NotFound() : Results.Ok(mapping);
});

app.MapPost("/api/remotepathmapping", async (RemotePathMapping mapping, SportarrDbContext db) =>
{
    db.RemotePathMappings.Add(mapping);
    await db.SaveChangesAsync();
    return Results.Created($"/api/remotepathmapping/{mapping.Id}", mapping);
});

app.MapPut("/api/remotepathmapping/{id:int}", async (int id, RemotePathMapping updatedMapping, SportarrDbContext db) =>
{
    var mapping = await db.RemotePathMappings.FindAsync(id);
    if (mapping is null) return Results.NotFound();

    mapping.Host = updatedMapping.Host;
    mapping.RemotePath = updatedMapping.RemotePath;
    mapping.LocalPath = updatedMapping.LocalPath;

    await db.SaveChangesAsync();
    return Results.Ok(mapping);
});

app.MapDelete("/api/remotepathmapping/{id:int}", async (int id, SportarrDbContext db) =>
{
    var mapping = await db.RemotePathMappings.FindAsync(id);
    if (mapping is null) return Results.NotFound();

    db.RemotePathMappings.Remove(mapping);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Download Queue Management
app.MapGet("/api/queue", async (SportarrDbContext db) =>
{
    // Activity/Queue page: Only show items that haven't been imported yet
    // Imported items should only appear in History (Sonarr/Radarr behavior)
    var queue = await db.DownloadQueue
        .Include(dq => dq.Event)
        .Include(dq => dq.DownloadClient)
        .Where(dq => dq.Status != DownloadStatus.Imported)
        .OrderByDescending(dq => dq.Added)
        .ToListAsync();
    return Results.Ok(queue);
});

app.MapGet("/api/queue/{id:int}", async (int id, SportarrDbContext db) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.Event)
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapDelete("/api/queue/{id:int}", async (
    int id,
    string removalMethod,
    string blocklistAction,
    SportarrDbContext db,
    Sportarr.Api.Services.DownloadClientService downloadClientService,
    Sportarr.Api.Services.AutomaticSearchService automaticSearchService) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.DownloadClient)
        .Include(dq => dq.Event)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound();

    // Handle removal method (Sonarr-style)
    if (item.DownloadClient != null)
    {
        switch (removalMethod)
        {
            case "removeFromClient":
                // Remove download and files from download client
                await downloadClientService.RemoveDownloadAsync(item.DownloadClient, item.DownloadId, deleteFiles: true);
                break;

            case "changeCategory":
                // Change to post-import category (only for completed downloads with PostImportCategory set)
                if (!string.IsNullOrEmpty(item.DownloadClient.PostImportCategory))
                {
                    await downloadClientService.ChangeCategoryAsync(
                        item.DownloadClient,
                        item.DownloadId,
                        item.DownloadClient.PostImportCategory);
                }
                break;

            case "ignoreDownload":
                // Just remove from queue, don't touch download client
                break;

            default:
                return Results.BadRequest($"Invalid removal method: {removalMethod}");
        }
    }

    // Handle blocklist action (Sonarr-style)
    switch (blocklistAction)
    {
        case "blocklistAndSearch":
            // Add to blocklist
            if (!string.IsNullOrEmpty(item.TorrentInfoHash))
            {
                var existingBlock = await db.Blocklist
                    .FirstOrDefaultAsync(b => b.TorrentInfoHash == item.TorrentInfoHash);

                if (existingBlock == null)
                {
                    var blocklistItem = new BlocklistItem
                    {
                        EventId = item.EventId,
                        Title = item.Title,
                        TorrentInfoHash = item.TorrentInfoHash,
                        Indexer = item.Indexer ?? "Unknown",
                        Reason = BlocklistReason.ManualBlock,
                        Message = "Manually removed and blocklisted",
                        BlockedAt = DateTime.UtcNow
                    };
                    db.Blocklist.Add(blocklistItem);
                }
            }
            // Start automatic search for replacement
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000); // Small delay before searching
                await automaticSearchService.SearchAndDownloadEventAsync(item.EventId);
            });
            break;

        case "blocklistOnly":
            // Add to blocklist without searching
            if (!string.IsNullOrEmpty(item.TorrentInfoHash))
            {
                var existingBlock = await db.Blocklist
                    .FirstOrDefaultAsync(b => b.TorrentInfoHash == item.TorrentInfoHash);

                if (existingBlock == null)
                {
                    var blocklistItem = new BlocklistItem
                    {
                        EventId = item.EventId,
                        Title = item.Title,
                        TorrentInfoHash = item.TorrentInfoHash,
                        Indexer = item.Indexer ?? "Unknown",
                        Reason = BlocklistReason.ManualBlock,
                        Message = "Manually blocklisted",
                        BlockedAt = DateTime.UtcNow
                    };
                    db.Blocklist.Add(blocklistItem);
                }
            }
            break;

        case "none":
            // No blocklist action
            break;

        default:
            return Results.BadRequest($"Invalid blocklist action: {blocklistAction}");
    }

    // Remove from queue
    // First, delete any import history records that reference this queue item (foreign key constraint)
    var importHistories = await db.ImportHistories
        .Where(h => h.DownloadQueueItemId == item.Id)
        .ToListAsync();

    if (importHistories.Any())
    {
        db.ImportHistories.RemoveRange(importHistories);
    }

    db.DownloadQueue.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Queue Operations - Pause Download
app.MapPost("/api/queue/{id:int}/pause", async (int id, SportarrDbContext db, Sportarr.Api.Services.DownloadClientService downloadClientService) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound();
    if (item.DownloadClient is null) return Results.BadRequest("No download client assigned");

    // Pause in download client
    var success = await downloadClientService.PauseDownloadAsync(item.DownloadClient, item.DownloadId);

    if (success)
    {
        item.Status = DownloadStatus.Paused;
        item.LastUpdate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(item);
    }

    return Results.StatusCode(500);
});

// API: Queue Operations - Resume Download
app.MapPost("/api/queue/{id:int}/resume", async (int id, SportarrDbContext db, Sportarr.Api.Services.DownloadClientService downloadClientService) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound();
    if (item.DownloadClient is null) return Results.BadRequest("No download client assigned");

    // Resume in download client
    var success = await downloadClientService.ResumeDownloadAsync(item.DownloadClient, item.DownloadId);

    if (success)
    {
        item.Status = DownloadStatus.Downloading;
        item.LastUpdate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(item);
    }

    return Results.StatusCode(500);
});

// API: Queue Operations - Force Import
app.MapPost("/api/queue/{id:int}/import", async (int id, SportarrDbContext db, Sportarr.Api.Services.FileImportService fileImportService) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.Event)
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound();

    try
    {
        item.Status = DownloadStatus.Importing;
        await db.SaveChangesAsync();

        await fileImportService.ImportDownloadAsync(item);

        item.Status = DownloadStatus.Imported;
        item.ImportedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Results.Ok(item);
    }
    catch (Exception ex)
    {
        item.Status = DownloadStatus.Failed;
        item.ErrorMessage = $"Import failed: {ex.Message}";
        await db.SaveChangesAsync();
        return Results.BadRequest(new { error = ex.Message });
    }
});

// API: Import History Management
app.MapGet("/api/history", async (SportarrDbContext db, int page = 1, int pageSize = 50) =>
{
    var totalCount = await db.ImportHistories.CountAsync();
    var history = await db.ImportHistories
        .Include(h => h.Event)
        .Include(h => h.DownloadQueueItem)
        .OrderByDescending(h => h.ImportedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    return Results.Ok(new {
        history,
        page,
        pageSize,
        totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
        totalRecords = totalCount
    });
});

app.MapGet("/api/history/{id:int}", async (int id, SportarrDbContext db) =>
{
    var item = await db.ImportHistories
        .Include(h => h.Event)
        .Include(h => h.DownloadQueueItem)
        .FirstOrDefaultAsync(h => h.Id == id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapDelete("/api/history/{id:int}", async (int id, SportarrDbContext db) =>
{
    var item = await db.ImportHistories.FindAsync(id);
    if (item is null) return Results.NotFound();

    db.ImportHistories.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Blocklist Management
app.MapGet("/api/blocklist", async (SportarrDbContext db, int page = 1, int pageSize = 50) =>
{
    var totalCount = await db.Blocklist.CountAsync();
    var blocklist = await db.Blocklist
        .Include(b => b.Event)
        .OrderByDescending(b => b.BlockedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    return Results.Ok(new {
        blocklist,
        page,
        pageSize,
        totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
        totalRecords = totalCount
    });
});

app.MapGet("/api/blocklist/{id:int}", async (int id, SportarrDbContext db) =>
{
    var item = await db.Blocklist
        .Include(b => b.Event)
        .FirstOrDefaultAsync(b => b.Id == id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapPost("/api/blocklist", async (BlocklistItem item, SportarrDbContext db) =>
{
    item.BlockedAt = DateTime.UtcNow;
    db.Blocklist.Add(item);
    await db.SaveChangesAsync();
    return Results.Created($"/api/blocklist/{item.Id}", item);
});

app.MapDelete("/api/blocklist/{id:int}", async (int id, SportarrDbContext db) =>
{
    var item = await db.Blocklist.FindAsync(id);
    if (item is null) return Results.NotFound();

    db.Blocklist.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Wanted/Missing Events
app.MapGet("/api/wanted/missing", async (int page, int pageSize, SportarrDbContext db) =>
{
    var query = db.Events
        .Where(e => e.Monitored && !e.HasFile)
        .OrderBy(e => e.EventDate);

    var totalRecords = await query.CountAsync();
    var events = await query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    return Results.Ok(new
    {
        events,
        page,
        pageSize,
        totalRecords
    });
});

app.MapGet("/api/wanted/cutoff-unmet", async (int page, int pageSize, SportarrDbContext db) =>
{
    // Get all quality profiles to check cutoffs
    var qualityProfiles = await db.QualityProfiles
        .Include(qp => qp.Items)
        .ToListAsync();

    // For now, return events that have files but could be upgraded
    // In a full implementation, this would check against quality profile cutoffs
    var query = db.Events
        .Where(e => e.Monitored && e.HasFile && e.Quality != null)
        .OrderBy(e => e.EventDate);

    var totalRecords = await query.CountAsync();
    var events = await query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    // Filter events where quality is below cutoff
    // This is a simplified check - full implementation would compare quality scores
    var cutoffUnmetEvents = events.Where(e =>
    {
        // Simple heuristic: if quality contains "WEB" or "HDTV", it's below cutoff
        // A proper implementation would use the quality profile system
        var quality = e.Quality?.ToLower() ?? "";
        return quality.Contains("web") || quality.Contains("hdtv") || quality.Contains("720p");
    }).ToList();

    return Results.Ok(new
    {
        events = cutoffUnmetEvents,
        page,
        pageSize,
        totalRecords = cutoffUnmetEvents.Count
    });
});

// API: Indexers Management
app.MapGet("/api/indexer", async (SportarrDbContext db) =>
{
    var indexers = await db.Indexers.OrderBy(i => i.Priority).ToListAsync();

    // Transform to frontend-compatible format with implementation field
    var transformedIndexers = indexers.Select(i => new
    {
        id = i.Id,
        name = i.Name,
        implementation = i.Type.ToString(), // Convert enum to string (Torznab, Newznab, Rss, Torrent)
        enable = i.Enabled,
        enableRss = i.EnableRss,
        enableAutomaticSearch = i.EnableAutomaticSearch,
        enableInteractiveSearch = i.EnableInteractiveSearch,
        priority = i.Priority,
        fields = new object[]
        {
            new { name = "baseUrl", value = i.Url },
            new { name = "apiPath", value = i.ApiPath },
            new { name = "apiKey", value = i.ApiKey ?? "" },
            new { name = "categories", value = string.Join(",", i.Categories) },
            new { name = "animeCategories", value = i.AnimeCategories != null ? string.Join(",", i.AnimeCategories) : "" },
            new { name = "minimumSeeders", value = i.MinimumSeeders.ToString() },
            new { name = "seedRatio", value = i.SeedRatio?.ToString() ?? "" },
            new { name = "seedTime", value = i.SeedTime?.ToString() ?? "" },
            new { name = "seasonPackSeedTime", value = i.SeasonPackSeedTime?.ToString() ?? "" },
            new { name = "earlyReleaseLimit", value = i.EarlyReleaseLimit?.ToString() ?? "" },
            new { name = "additionalParameters", value = i.AdditionalParameters ?? "" },
            new { name = "multiLanguages", value = i.MultiLanguages != null ? string.Join(",", i.MultiLanguages) : "" },
            new { name = "rejectBlocklistedTorrentHashes", value = i.RejectBlocklistedTorrentHashes.ToString() },
            new { name = "downloadClientId", value = i.DownloadClientId?.ToString() ?? "" },
            new { name = "tags", value = i.Tags != null ? string.Join(",", i.Tags) : "" }
        }
    }).ToList();

    return Results.Ok(transformedIndexers);
});

app.MapPost("/api/indexer", async (HttpRequest request, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        // Read raw JSON to handle Prowlarr API format from frontend
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        logger.LogInformation("[INDEXER CREATE] Received payload: {Json}", json);

        // Deserialize as dynamic JSON to extract fields
        var apiIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Convert Prowlarr API format to Indexer model
        var indexer = new Indexer
        {
            Name = apiIndexer.GetProperty("name").GetString() ?? "Unknown",
            Type = apiIndexer.GetProperty("implementation").GetString()?.ToLower() == "newznab"
                ? IndexerType.Newznab
                : IndexerType.Torznab,
            Url = "",
            ApiKey = "",
            Created = DateTime.UtcNow
        };

        // Extract enable/disable flags if present
        if (apiIndexer.TryGetProperty("enable", out var enable))
        {
            indexer.Enabled = enable.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("enableRss", out var enableRss))
        {
            indexer.EnableRss = enableRss.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("enableAutomaticSearch", out var enableAuto))
        {
            indexer.EnableAutomaticSearch = enableAuto.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("enableInteractiveSearch", out var enableInteractive))
        {
            indexer.EnableInteractiveSearch = enableInteractive.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("priority", out var priority))
        {
            indexer.Priority = priority.GetInt32();
        }

        // Extract fields from the fields array
        if (apiIndexer.TryGetProperty("fields", out var fields))
        {
            foreach (var field in fields.EnumerateArray())
            {
                var fieldName = field.GetProperty("name").GetString();
                var fieldValue = field.TryGetProperty("value", out var val) ? val.GetString() : null;

                switch (fieldName)
                {
                    case "baseUrl":
                        indexer.Url = fieldValue?.TrimEnd('/') ?? "";
                        break;
                    case "apiPath":
                        var apiPath = fieldValue ?? "/api";
                        indexer.ApiPath = apiPath.StartsWith('/') ? apiPath : $"/{apiPath}";
                        break;
                    case "apiKey":
                        indexer.ApiKey = fieldValue;
                        break;
                    case "categories":
                        if (!string.IsNullOrEmpty(fieldValue))
                        {
                            indexer.Categories = fieldValue.Split(',').Select(c => c.Trim()).ToList();
                        }
                        break;
                    case "minimumSeeders":
                        if (int.TryParse(fieldValue, out var minSeeders))
                        {
                            indexer.MinimumSeeders = minSeeders;
                        }
                        break;
                    case "seedRatio":
                        if (double.TryParse(fieldValue, out var seedRatio))
                        {
                            indexer.SeedRatio = seedRatio;
                        }
                        break;
                    case "seedTime":
                        if (int.TryParse(fieldValue, out var seedTime))
                        {
                            indexer.SeedTime = seedTime;
                        }
                        break;
                }
            }
        }

        logger.LogInformation("[INDEXER CREATE] Creating {Type} indexer: {Name} at {Url}{ApiPath}",
            indexer.Type, indexer.Name, indexer.Url, indexer.ApiPath);

        db.Indexers.Add(indexer);
        await db.SaveChangesAsync();

        return Results.Created($"/api/indexer/{indexer.Id}", indexer);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[INDEXER CREATE] Failed to create indexer");
        return Results.BadRequest(new { success = false, message = $"Failed to create indexer: {ex.Message}" });
    }
});

app.MapPut("/api/indexer/{id:int}", async (int id, HttpRequest request, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        var indexer = await db.Indexers.FindAsync(id);
        if (indexer is null) return Results.NotFound();

        // Read raw JSON to handle Prowlarr API format from frontend
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        logger.LogInformation("[INDEXER UPDATE] Received payload for ID {Id}: {Json}", id, json);

        // Deserialize as dynamic JSON to extract fields
        var apiIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Update basic fields
        if (apiIndexer.TryGetProperty("name", out var name))
        {
            indexer.Name = name.GetString() ?? indexer.Name;
        }
        if (apiIndexer.TryGetProperty("implementation", out var impl))
        {
            indexer.Type = impl.GetString()?.ToLower() == "newznab" ? IndexerType.Newznab : IndexerType.Torznab;
        }

        // Update enable/disable flags
        if (apiIndexer.TryGetProperty("enable", out var enable))
        {
            indexer.Enabled = enable.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("enableRss", out var enableRss))
        {
            indexer.EnableRss = enableRss.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("enableAutomaticSearch", out var enableAuto))
        {
            indexer.EnableAutomaticSearch = enableAuto.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("enableInteractiveSearch", out var enableInteractive))
        {
            indexer.EnableInteractiveSearch = enableInteractive.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("priority", out var priority))
        {
            indexer.Priority = priority.GetInt32();
        }

        // Extract fields from the fields array
        if (apiIndexer.TryGetProperty("fields", out var fields))
        {
            foreach (var field in fields.EnumerateArray())
            {
                var fieldName = field.GetProperty("name").GetString();
                var fieldValue = field.TryGetProperty("value", out var val) ? val.GetString() : null;

                switch (fieldName)
                {
                    case "baseUrl":
                        if (!string.IsNullOrEmpty(fieldValue))
                        {
                            indexer.Url = fieldValue.TrimEnd('/');
                        }
                        break;
                    case "apiPath":
                        if (!string.IsNullOrEmpty(fieldValue))
                        {
                            var apiPath = fieldValue;
                            indexer.ApiPath = apiPath.StartsWith('/') ? apiPath : $"/{apiPath}";
                        }
                        break;
                    case "apiKey":
                        // Only update API key if a new value is provided (not empty)
                        if (!string.IsNullOrEmpty(fieldValue))
                        {
                            indexer.ApiKey = fieldValue;
                        }
                        break;
                    case "categories":
                        if (!string.IsNullOrEmpty(fieldValue))
                        {
                            indexer.Categories = fieldValue.Split(',').Select(c => c.Trim()).ToList();
                        }
                        break;
                    case "minimumSeeders":
                        if (int.TryParse(fieldValue, out var minSeeders))
                        {
                            indexer.MinimumSeeders = minSeeders;
                        }
                        break;
                    case "seedRatio":
                        if (double.TryParse(fieldValue, out var seedRatio))
                        {
                            indexer.SeedRatio = seedRatio;
                        }
                        break;
                    case "seedTime":
                        if (int.TryParse(fieldValue, out var seedTime))
                        {
                            indexer.SeedTime = seedTime;
                        }
                        break;
                }
            }
        }

        indexer.LastModified = DateTime.UtcNow;

        logger.LogInformation("[INDEXER UPDATE] Updated {Type} indexer: {Name} at {Url}{ApiPath}",
            indexer.Type, indexer.Name, indexer.Url, indexer.ApiPath);

        await db.SaveChangesAsync();
        return Results.Ok(indexer);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[INDEXER UPDATE] Failed to update indexer {Id}", id);
        return Results.BadRequest(new { success = false, message = $"Failed to update indexer: {ex.Message}" });
    }
});

app.MapDelete("/api/indexer/{id:int}", async (int id, SportarrDbContext db) =>
{
    var indexer = await db.Indexers.FindAsync(id);
    if (indexer is null) return Results.NotFound();

    db.Indexers.Remove(indexer);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Release Search (Indexer Integration)
app.MapPost("/api/release/search", async (
    ReleaseSearchRequest request,
    Sportarr.Api.Services.IndexerSearchService indexerSearchService,
    SportarrDbContext db) =>
{
    // Search all enabled indexers
    var results = await indexerSearchService.SearchAllIndexersAsync(request.Query, request.MaxResultsPerIndexer);

    // If quality profile ID is provided, select best release
    if (request.QualityProfileId.HasValue)
    {
        var qualityProfile = await db.QualityProfiles.FindAsync(request.QualityProfileId.Value);
        if (qualityProfile != null)
        {
            var bestRelease = indexerSearchService.SelectBestRelease(results, qualityProfile);
            if (bestRelease != null)
            {
                results = new List<ReleaseSearchResult> { bestRelease };
            }
        }
    }

    return Results.Ok(results);
});

// API: Test indexer connection
app.MapPost("/api/indexer/test", async (
    HttpRequest request,
    Sportarr.Api.Services.IndexerSearchService indexerSearchService,
    ILogger<Program> logger) =>
{
    try
    {
        // Read raw JSON to handle Prowlarr API format from frontend
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        logger.LogInformation("[INDEXER TEST] Received payload: {Json}", json);

        // Deserialize as dynamic JSON to extract fields
        var apiIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Convert Prowlarr API format to Indexer model
        var indexer = new Indexer
        {
            Name = apiIndexer.GetProperty("name").GetString() ?? "Test",
            Type = apiIndexer.GetProperty("implementation").GetString()?.ToLower() == "newznab"
                ? IndexerType.Newznab
                : IndexerType.Torznab,
            Url = "",
            ApiKey = ""
        };

        // Extract fields from the fields array
        if (apiIndexer.TryGetProperty("fields", out var fields))
        {
            foreach (var field in fields.EnumerateArray())
            {
                var fieldName = field.GetProperty("name").GetString();
                var fieldValue = field.GetProperty("value").GetString();

                switch (fieldName)
                {
                    case "baseUrl":
                        // Trim trailing slash from baseUrl to avoid double slashes
                        indexer.Url = fieldValue?.TrimEnd('/') ?? "";
                        break;
                    case "apiPath":
                        // Ensure apiPath starts with slash
                        var apiPath = fieldValue ?? "/api";
                        indexer.ApiPath = apiPath.StartsWith('/') ? apiPath : $"/{apiPath}";
                        break;
                    case "apiKey":
                        indexer.ApiKey = fieldValue;
                        break;
                    case "categories":
                        if (!string.IsNullOrEmpty(fieldValue))
                        {
                            indexer.Categories = fieldValue.Split(',').ToList();
                        }
                        break;
                    case "minimumSeeders":
                        if (int.TryParse(fieldValue, out var minSeeders))
                        {
                            indexer.MinimumSeeders = minSeeders;
                        }
                        break;
                }
            }
        }

        logger.LogInformation("[INDEXER TEST] Testing {Type} indexer: {Name} at {Url}{ApiPath}",
            indexer.Type, indexer.Name, indexer.Url, indexer.ApiPath);
        logger.LogInformation("[INDEXER TEST] ApiKey present: {HasApiKey}, Categories: {Categories}",
            !string.IsNullOrEmpty(indexer.ApiKey), string.Join(",", indexer.Categories ?? new List<string>()));

        var success = await indexerSearchService.TestIndexerAsync(indexer);

        if (success)
        {
            logger.LogInformation("[INDEXER TEST] ✓ Test succeeded for {Name}", indexer.Name);
            return Results.Ok(new { success = true, message = "Connection successful" });
        }

        logger.LogWarning("[INDEXER TEST] ✗ Test failed for {Name}", indexer.Name);
        return Results.BadRequest(new { success = false, message = "Connection failed" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[INDEXER TEST] Error testing indexer: {Message}", ex.Message);
        return Results.BadRequest(new { success = false, message = $"Test failed: {ex.Message}" });
    }
});

// API: Manual search for specific event (Universal: supports all sports)
app.MapPost("/api/event/{eventId:int}/search", async (
    int eventId,
    SportarrDbContext db,
    Sportarr.Api.Services.IndexerSearchService indexerSearchService,
    Sportarr.Api.Services.EventQueryService eventQueryService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[SEARCH] POST /api/event/{EventId}/search - Manual search initiated", eventId);

    var evt = await db.Events
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .Include(e => e.League)
        .FirstOrDefaultAsync(e => e.Id == eventId);

    if (evt == null)
    {
        logger.LogWarning("[SEARCH] Event {EventId} not found", eventId);
        return Results.NotFound();
    }

    if (!evt.Monitored)
    {
        logger.LogWarning("[SEARCH] Event {Title} is not monitored", evt.Title);
        return Results.Ok(new List<ReleaseSearchResult>());
    }

    logger.LogInformation("[SEARCH] Event: {Title} | Sport: {Sport}", evt.Title, evt.Sport);

    // Get default quality profile for evaluation
    var defaultProfile = await db.QualityProfiles.OrderBy(q => q.Id).FirstOrDefaultAsync();
    var qualityProfileId = defaultProfile?.Id;

    var allResults = new List<ReleaseSearchResult>();
    var seenGuids = new HashSet<string>();

    // UNIVERSAL: Build search queries using sport-agnostic approach
    var queries = eventQueryService.BuildEventQueries(evt);

    logger.LogInformation("[SEARCH] Built {Count} prioritized query variations", queries.Count);

    // OPTIMIZATION: Intelligent fallback search (matches AutomaticSearchService)
    // Try primary query first, only fallback if insufficient results
    int queriesAttempted = 0;
    const int MinimumResults = 10; // Minimum results before stopping (manual search wants more options)

    foreach (var query in queries)
    {
        queriesAttempted++;
        logger.LogInformation("[SEARCH] Trying query {Attempt}/{Total}: '{Query}'",
            queriesAttempted, queries.Count, query);

        var results = await indexerSearchService.SearchAllIndexersAsync(query, 50, qualityProfileId);

        // Deduplicate results by GUID
        foreach (var result in results)
        {
            if (!string.IsNullOrEmpty(result.Guid) && !seenGuids.Contains(result.Guid))
            {
                seenGuids.Add(result.Guid);
                allResults.Add(result);
            }
            else if (string.IsNullOrEmpty(result.Guid))
            {
                allResults.Add(result);
            }
        }

        // Success criteria: Found enough results for user to choose from
        if (allResults.Count >= MinimumResults)
        {
            logger.LogInformation("[SEARCH] Found {Count} results - skipping remaining {Remaining} fallback queries (rate limit optimization)",
                allResults.Count, queries.Count - queriesAttempted);
            break;
        }

        // Log progress if we found some results but not enough
        if (allResults.Count > 0 && allResults.Count < MinimumResults)
        {
            logger.LogInformation("[SEARCH] Found {Count} results (below minimum {Min}) - trying next query",
                allResults.Count, MinimumResults);
        }
        else if (allResults.Count == 0)
        {
            logger.LogWarning("[SEARCH] No results for query '{Query}' - trying next fallback", query);
        }

        // Hard limit: Stop at 100 total results
        if (allResults.Count >= 100)
        {
            logger.LogInformation("[SEARCH] Reached 100 results limit");
            break;
        }
    }

    logger.LogInformation("[SEARCH] Search completed. Returning {Count} unique results", allResults.Count);
    return Results.Ok(allResults);
});

// API: Get leagues (universal for all sports)
app.MapGet("/api/leagues", async (SportarrDbContext db, string? sport) =>
{
    var query = db.Leagues.AsQueryable();

    // Filter by sport if provided
    if (!string.IsNullOrEmpty(sport))
    {
        query = query.Where(l => l.Sport == sport);
    }

    var leagues = await query
        .OrderBy(l => l.Sport)
        .ThenBy(l => l.Name)
        .ToListAsync();

    // Convert to DTOs for frontend (avoids JsonPropertyName serialization)
    var response = leagues.Select(LeagueResponse.FromLeague).ToList();

    return Results.Ok(response);
});

// API: Get league by ID
app.MapGet("/api/leagues/{id:int}", async (int id, SportarrDbContext db) =>
{
    var league = await db.Leagues
        .Include(l => l.MonitoredTeams)
        .ThenInclude(lt => lt.Team)
        .FirstOrDefaultAsync(l => l.Id == id);

    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    // Get event count and stats
    var events = await db.Events
        .Where(e => e.LeagueId == id)
        .ToListAsync();

    return Results.Ok(new
    {
        league.Id,
        league.ExternalId,
        league.Name,
        league.Sport,
        league.Country,
        league.Description,
        league.Monitored,
        league.QualityProfileId,
        league.LogoUrl,
        league.BannerUrl,
        league.PosterUrl,
        league.Website,
        league.FormedYear,
        league.Added,
        league.LastUpdate,
        // Monitored teams
        MonitoredTeams = league.MonitoredTeams.Select(lt => new
        {
            lt.Id,
            lt.LeagueId,
            lt.TeamId,
            lt.Monitored,
            lt.Added,
            Team = lt.Team != null ? new
            {
                lt.Team.Id,
                lt.Team.ExternalId,
                lt.Team.Name,
                lt.Team.ShortName,
                lt.Team.BadgeUrl
            } : null
        }).ToList(),
        // Stats
        EventCount = events.Count,
        MonitoredEventCount = events.Count(e => e.Monitored),
        FileCount = events.Count(e => e.HasFile)
    });
});

// API: Get all events for a specific league
app.MapGet("/api/leagues/{id:int}/events", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Getting events for league ID: {LeagueId}", id);

    // Verify league exists
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        logger.LogWarning("[LEAGUES] League not found: {LeagueId}", id);
        return Results.NotFound(new { error = "League not found" });
    }

    // Get all events for this league
    var events = await db.Events
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .Where(e => e.LeagueId == id)
        .OrderByDescending(e => e.EventDate)
        .ToListAsync();

    // Convert to DTOs
    var response = events.Select(EventResponse.FromEvent).ToList();

    logger.LogInformation("[LEAGUES] Found {Count} events for league: {LeagueName}", response.Count, league.Name);
    return Results.Ok(response);
});

// API: Get teams by external league ID (for Add League modal - before league is added to DB)
app.MapGet("/api/leagues/external/{externalId}/teams", async (string externalId, TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Getting teams for external league ID: {ExternalId}", externalId);

    // Fetch teams from TheSportsDB
    var teams = await sportsDbClient.GetLeagueTeamsAsync(externalId);
    if (teams == null || !teams.Any())
    {
        logger.LogWarning("[LEAGUES] No teams found for external league ID: {ExternalId}", externalId);
        return Results.Ok(new List<object>()); // Return empty array instead of error
    }

    logger.LogInformation("[LEAGUES] Found {Count} teams for external league ID: {ExternalId}", teams.Count, externalId);
    return Results.Ok(teams);
});

// API: Get teams for a league (for team selection in Add League modal)
app.MapGet("/api/leagues/{id:int}/teams", async (int id, SportarrDbContext db, TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Getting teams for league ID: {LeagueId}", id);

    // Verify league exists
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        logger.LogWarning("[LEAGUES] League not found: {LeagueId}", id);
        return Results.NotFound(new { error = "League not found" });
    }

    // Check if league has external ID (required for TheSportsDB API)
    if (string.IsNullOrEmpty(league.ExternalId))
    {
        logger.LogWarning("[LEAGUES] League missing external ID: {LeagueName}", league.Name);
        return Results.BadRequest(new { error = "League is missing TheSportsDB external ID" });
    }

    // Fetch teams from TheSportsDB
    var teams = await sportsDbClient.GetLeagueTeamsAsync(league.ExternalId);
    if (teams == null || !teams.Any())
    {
        logger.LogWarning("[LEAGUES] No teams found for league: {LeagueName}", league.Name);
        return Results.Ok(new List<object>()); // Return empty array instead of error
    }

    logger.LogInformation("[LEAGUES] Found {Count} teams for league: {LeagueName}", teams.Count, league.Name);
    return Results.Ok(teams);
});

// API: Update league (including monitor toggle)
app.MapPut("/api/leagues/{id:int}", async (int id, JsonElement body, SportarrDbContext db, ILogger<Program> logger) =>
{
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    logger.LogInformation("[LEAGUES] Updating league: {Name} (ID: {Id})", league.Name, id);

    // Update properties from JSON body
    if (body.TryGetProperty("monitored", out var monitoredProp))
    {
        league.Monitored = monitoredProp.GetBoolean();
        logger.LogInformation("[LEAGUES] Updated monitored status to: {Monitored}", league.Monitored);
    }

    if (body.TryGetProperty("qualityProfileId", out var qualityProp))
    {
        league.QualityProfileId = qualityProp.ValueKind == JsonValueKind.Null ? null : qualityProp.GetInt32();
        logger.LogInformation("[LEAGUES] Updated quality profile ID to: {QualityProfileId}", league.QualityProfileId);
    }

    league.LastUpdate = DateTime.UtcNow;
    await db.SaveChangesAsync();

    logger.LogInformation("[LEAGUES] Successfully updated league: {Name}", league.Name);
    return Results.Ok(LeagueResponse.FromLeague(league));
});

// API: Get all leagues from TheSportsDB (cached)
app.MapGet("/api/leagues/all", async (Sportarr.Api.Services.TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Fetching all leagues from cache");

    var results = await sportsDbClient.GetAllLeaguesAsync();

    if (results == null || !results.Any())
    {
        logger.LogWarning("[LEAGUES] No leagues found in cache");
        return Results.Ok(new List<object>());
    }

    logger.LogInformation("[LEAGUES] Found {Count} leagues", results.Count);
    return Results.Ok(results);
});

// API: Search leagues from TheSportsDB
app.MapGet("/api/leagues/search/{query}", async (string query, Sportarr.Api.Services.TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES SEARCH] Searching for: {Query}", query);

    var results = await sportsDbClient.SearchLeagueAsync(query);

    if (results == null || !results.Any())
    {
        logger.LogWarning("[LEAGUES SEARCH] No results found for: {Query}", query);
        return Results.Ok(new List<object>());
    }

    logger.LogInformation("[LEAGUES SEARCH] Found {Count} results", results.Count);
    return Results.Ok(results);
});

// API: Add league to library
app.MapPost("/api/leagues", async (HttpContext context, SportarrDbContext db, IServiceScopeFactory scopeFactory, TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] POST /api/leagues - Request received");

    // Enable buffering to allow reading the request body multiple times
    context.Request.EnableBuffering();

    // Read and log the raw request body for debugging
    using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
    var requestBody = await reader.ReadToEndAsync();
    logger.LogInformation("[LEAGUES] Request body: {Body}", requestBody);

    // Reset stream position for potential re-reading
    context.Request.Body.Position = 0;

    // Deserialize the AddLeagueRequest DTO from the request body
    // Use DTO to avoid JsonPropertyName conflicts (strLeague vs name)
    AddLeagueRequest? request;
    try
    {
        request = JsonSerializer.Deserialize<AddLeagueRequest>(requestBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        if (request == null)
        {
            logger.LogError("[LEAGUES] Failed to deserialize league request from request body");
            return Results.BadRequest(new { error = "Invalid league data" });
        }

        logger.LogInformation("[LEAGUES] Deserialized request - Name: {Name}, Sport: {Sport}, ExternalId: {ExternalId}",
            request.Name, request.Sport, request.ExternalId);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] JSON deserialization error: {Message}", ex.Message);
        return Results.BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
    }

    try
    {
        // Convert DTO to League entity
        var league = request.ToLeague();

        // Enrich league with full details (including images) if missing
        // The /all/leagues endpoint doesn't include images, so fetch from lookup
        if (string.IsNullOrEmpty(league.LogoUrl) && !string.IsNullOrEmpty(league.ExternalId))
        {
            logger.LogInformation("[LEAGUES] Fetching full league details to get images for: {Name}", league.Name);
            var fullDetails = await sportsDbClient.LookupLeagueAsync(league.ExternalId);
            if (fullDetails != null)
            {
                league.LogoUrl = fullDetails.LogoUrl;
                league.BannerUrl = fullDetails.BannerUrl;
                league.PosterUrl = fullDetails.PosterUrl;
                league.Description = fullDetails.Description ?? league.Description;
                league.Website = fullDetails.Website ?? league.Website;
                logger.LogInformation("[LEAGUES] Enriched league with images - Logo: {HasLogo}, Banner: {HasBanner}, Poster: {HasPoster}",
                    !string.IsNullOrEmpty(league.LogoUrl), !string.IsNullOrEmpty(league.BannerUrl), !string.IsNullOrEmpty(league.PosterUrl));
            }
            else
            {
                logger.LogWarning("[LEAGUES] Could not fetch full details for league: {ExternalId}", league.ExternalId);
            }
        }

        logger.LogInformation("[LEAGUES] Adding league to database: {Name} ({Sport})", league.Name, league.Sport);

        // Check if league already exists
        var existing = await db.Leagues
            .FirstOrDefaultAsync(l => l.ExternalId == league.ExternalId && !string.IsNullOrEmpty(league.ExternalId));

        if (existing != null)
        {
            logger.LogWarning("[LEAGUES] League already exists: {Name} (ExternalId: {ExternalId})", league.Name, league.ExternalId);
            return Results.BadRequest(new { error = "League already exists in library" });
        }

        // Added timestamp is already set in ToLeague()
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        logger.LogInformation("[LEAGUES] Successfully added league: {Name} with ID {Id}", league.Name, league.Id);

        // Handle monitored teams if specified (for team-based filtering)
        if (request.MonitoredTeamIds != null && request.MonitoredTeamIds.Any())
        {
            logger.LogInformation("[LEAGUES] Processing {Count} monitored teams for league: {Name}",
                request.MonitoredTeamIds.Count, league.Name);

            foreach (var teamExternalId in request.MonitoredTeamIds)
            {
                // Find or create team in database
                var team = await db.Teams.FirstOrDefaultAsync(t => t.ExternalId == teamExternalId);

                if (team == null)
                {
                    // Fetch team details from TheSportsDB to populate Team table
                    var teams = await sportsDbClient.GetLeagueTeamsAsync(league.ExternalId!);
                    var teamData = teams?.FirstOrDefault(t => t.ExternalId == teamExternalId);

                    if (teamData != null)
                    {
                        team = teamData;
                        team.LeagueId = league.Id;
                        team.Sport = league.Sport; // Populate from league since API doesn't return it
                        db.Teams.Add(team);
                        await db.SaveChangesAsync();
                        logger.LogInformation("[LEAGUES] Added new team: {TeamName} (ExternalId: {ExternalId})",
                            team.Name, team.ExternalId);
                    }
                    else
                    {
                        logger.LogWarning("[LEAGUES] Could not find team with ExternalId: {ExternalId}", teamExternalId);
                        continue;
                    }
                }

                // Create LeagueTeam entry
                var leagueTeam = new LeagueTeam
                {
                    LeagueId = league.Id,
                    TeamId = team.Id,
                    Monitored = true
                };

                db.LeagueTeams.Add(leagueTeam);
                logger.LogInformation("[LEAGUES] Marked team as monitored: {TeamName} for league: {LeagueName}",
                    team.Name, league.Name);
            }

            await db.SaveChangesAsync();
            logger.LogInformation("[LEAGUES] Successfully configured {Count} monitored teams", request.MonitoredTeamIds.Count);
        }
        else
        {
            logger.LogInformation("[LEAGUES] No teams selected - league added but not monitored (no events will be synced)");
            league.Monitored = false;
            await db.SaveChangesAsync();

            // Don't trigger event sync if no teams are selected
            return Results.Ok(new {
                message = "League added successfully (not monitored - no teams selected)",
                leagueId = league.Id,
                monitored = false
            });
        }

        // Automatically sync events for the newly added league
        // This runs in the background to populate all events (past, present, future)
        logger.LogInformation("[LEAGUES] Triggering automatic event sync for league: {Name}", league.Name);
        var leagueId = league.Id;
        var leagueName = league.Name;
        _ = Task.Run(async () =>
        {
            try
            {
                // Create a new scope for the background task to avoid using disposed DbContext
                using var scope = scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<Sportarr.Api.Services.LeagueEventSyncService>();

                var syncResult = await syncService.SyncLeagueEventsAsync(leagueId);
                logger.LogInformation("[LEAGUES] Auto-sync completed for {Name}: {Message}",
                    leagueName, syncResult.Message);
            }
            catch (Exception syncEx)
            {
                logger.LogError(syncEx, "[LEAGUES] Auto-sync failed for {Name}: {Message}",
                    leagueName, syncEx.Message);
            }
        });

        // Convert to DTO for frontend response
        var response = LeagueResponse.FromLeague(league);
        return Results.Created($"/api/leagues/{league.Id}", response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] Error adding league: {Name}. Error: {Message}", request?.Name ?? "Unknown", ex.Message);
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Error adding league"
        );
    }
});

// API: Update league
// Removed duplicate PUT endpoint - now using JsonElement-based endpoint above for partial updates

// API: Update monitored teams for a league
app.MapPut("/api/leagues/{id:int}/teams", async (int id, UpdateMonitoredTeamsRequest request, SportarrDbContext db, TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[LEAGUES] Updating monitored teams for league ID: {LeagueId}", id);

        var league = await db.Leagues
            .Include(l => l.MonitoredTeams)
            .ThenInclude(lt => lt.Team)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (league == null)
        {
            return Results.NotFound(new { error = "League not found" });
        }

        // Remove existing monitored teams
        var existingTeams = await db.LeagueTeams.Where(lt => lt.LeagueId == id).ToListAsync();
        db.LeagueTeams.RemoveRange(existingTeams);
        await db.SaveChangesAsync();

        // If no teams provided, set league as not monitored
        if (request.MonitoredTeamIds == null || !request.MonitoredTeamIds.Any())
        {
            logger.LogInformation("[LEAGUES] No teams selected - setting league as not monitored");
            league.Monitored = false;
            await db.SaveChangesAsync();
            return Results.Ok(new { message = "League updated - no teams monitored", leagueId = league.Id });
        }

        // Add new monitored teams
        logger.LogInformation("[LEAGUES] Adding {Count} monitored teams", request.MonitoredTeamIds.Count);

        foreach (var teamExternalId in request.MonitoredTeamIds)
        {
            // Find or create team in database
            var team = await db.Teams.FirstOrDefaultAsync(t => t.ExternalId == teamExternalId);

            if (team == null)
            {
                // Fetch team details from TheSportsDB
                var teams = await sportsDbClient.GetLeagueTeamsAsync(league.ExternalId!);
                var teamData = teams?.FirstOrDefault(t => t.ExternalId == teamExternalId);

                if (teamData != null)
                {
                    team = teamData;
                    team.LeagueId = league.Id;
                    team.Sport = league.Sport; // Populate from league since API doesn't return it
                    db.Teams.Add(team);
                    await db.SaveChangesAsync();
                    logger.LogInformation("[LEAGUES] Added new team: {TeamName} (ExternalId: {ExternalId})",
                        team.Name, team.ExternalId);
                }
                else
                {
                    logger.LogWarning("[LEAGUES] Could not find team with ExternalId: {ExternalId}", teamExternalId);
                    continue;
                }
            }

            // Create LeagueTeam entry
            var leagueTeam = new LeagueTeam
            {
                LeagueId = league.Id,
                TeamId = team.Id,
                Monitored = true
            };

            db.LeagueTeams.Add(leagueTeam);
            logger.LogInformation("[LEAGUES] Marked team as monitored: {TeamName} for league: {LeagueName}",
                team.Name, league.Name);
        }

        // Set league as monitored
        league.Monitored = true;
        await db.SaveChangesAsync();

        logger.LogInformation("[LEAGUES] Successfully updated {Count} monitored teams", request.MonitoredTeamIds.Count);
        return Results.Ok(new { message = "Monitored teams updated successfully", leagueId = league.Id, teamCount = request.MonitoredTeamIds.Count });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] Error updating monitored teams for league ID: {LeagueId}", id);
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Error updating monitored teams"
        );
    }
});

// API: Delete league
app.MapDelete("/api/leagues/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
{
    var league = await db.Leagues.FindAsync(id);

    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    logger.LogInformation("[LEAGUES] Deleting league: {Name}", league.Name);

    // Delete all events associated with this league (cascade delete, like Sonarr deleting show + episodes)
    var events = await db.Events.Where(e => e.LeagueId == id).ToListAsync();
    if (events.Any())
    {
        logger.LogInformation("[LEAGUES] Deleting {Count} events for league: {Name}", events.Count, league.Name);
        db.Events.RemoveRange(events);
    }

    db.Leagues.Remove(league);
    await db.SaveChangesAsync();

    logger.LogInformation("[LEAGUES] Successfully deleted league: {Name} and {EventCount} events", league.Name, events.Count);
    return Results.Ok(new { success = true, message = $"League deleted successfully ({events.Count} events removed)" });
});

// API: Refresh events for a league from TheSportsDB
app.MapPost("/api/leagues/{id:int}/refresh-events", async (
    int id,
    SportarrDbContext db,
    Sportarr.Api.Services.LeagueEventSyncService syncService,
    ILogger<Program> logger,
    HttpContext context) =>
{
    logger.LogInformation("[LEAGUES] POST /api/leagues/{Id}/refresh-events - Refreshing events from TheSportsDB", id);

    try
    {
        // Parse request body for optional seasons
        List<string>? seasons = null;
        if (context.Request.ContentLength > 0)
        {
            var requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<RefreshEventsRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            seasons = request?.Seasons;
        }

        // Sync events
        var result = await syncService.SyncLeagueEventsAsync(id, seasons);

        if (!result.Success)
        {
            return Results.BadRequest(new { error = result.Message });
        }

        logger.LogInformation("[LEAGUES] Successfully synced events: {Message}", result.Message);

        return Results.Ok(new
        {
            success = true,
            message = result.Message,
            newEvents = result.NewCount,
            updatedEvents = result.UpdatedCount,
            skippedEvents = result.SkippedCount,
            failedEvents = result.FailedCount,
            totalEvents = result.TotalCount
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] Error refreshing events for league {Id}: {Message}", id, ex.Message);
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Error refreshing events"
        );
    }
});

// ====================================================================================
// TEAMS API - Universal Sports Support
// ====================================================================================

// API: Get all teams
app.MapGet("/api/teams", async (SportarrDbContext db, int? leagueId, string? sport) =>
{
    var query = db.Teams
        .Include(t => t.League)
        .AsQueryable();

    // Filter by league if provided
    if (leagueId.HasValue)
    {
        query = query.Where(t => t.LeagueId == leagueId.Value);
    }

    // Filter by sport if provided
    if (!string.IsNullOrEmpty(sport))
    {
        query = query.Where(t => t.Sport == sport);
    }

    var teams = await query
        .OrderBy(t => t.Sport)
        .ThenBy(t => t.Name)
        .ToListAsync();

    return Results.Ok(teams);
});

// API: Get team by ID
app.MapGet("/api/teams/{id:int}", async (int id, SportarrDbContext db) =>
{
    var team = await db.Teams
        .Include(t => t.League)
        .FirstOrDefaultAsync(t => t.Id == id);

    if (team == null)
    {
        return Results.NotFound(new { error = "Team not found" });
    }

    // Get event count and stats
    var homeEvents = await db.Events.Where(e => e.HomeTeamId == id).CountAsync();
    var awayEvents = await db.Events.Where(e => e.AwayTeamId == id).CountAsync();

    return Results.Ok(new
    {
        team.Id,
        team.ExternalId,
        team.Name,
        team.ShortName,
        team.AlternateName,
        team.LeagueId,
        League = team.League != null ? new { team.League.Name, team.League.Sport } : null,
        team.Sport,
        team.Country,
        team.Stadium,
        team.StadiumLocation,
        team.StadiumCapacity,
        team.Description,
        team.BadgeUrl,
        team.JerseyUrl,
        team.BannerUrl,
        team.Website,
        team.FormedYear,
        team.PrimaryColor,
        team.SecondaryColor,
        team.Added,
        team.LastUpdate,
        // Stats
        HomeEventCount = homeEvents,
        AwayEventCount = awayEvents,
        TotalEventCount = homeEvents + awayEvents
    });
});

// API: Search teams from TheSportsDB
app.MapGet("/api/teams/search/{query}", async (string query, Sportarr.Api.Services.TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[TEAMS SEARCH] Searching for: {Query}", query);

    var results = await sportsDbClient.SearchTeamAsync(query);

    if (results == null || !results.Any())
    {
        logger.LogWarning("[TEAMS SEARCH] No results found for: {Query}", query);
        return Results.Ok(new List<object>());
    }

    logger.LogInformation("[TEAMS SEARCH] Found {Count} results", results.Count);
    return Results.Ok(results);
});

// ========================================
// EVENT SEARCH ENDPOINTS (TheSportsDB)
// ========================================

// GET /api/events/tv-schedule?date=2024-01-15&sport=Soccer
// Get TV schedule for events on a specific date and sport
app.MapGet("/api/events/tv-schedule", async (
    string? date,
    string? sport,
    Sportarr.Api.Services.TheSportsDBClient sportsDbClient,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[EVENTS TV-SCHEDULE] GET /api/events/tv-schedule?date={Date}&sport={Sport}", date, sport);

    if (string.IsNullOrEmpty(date))
    {
        return Results.BadRequest("Date parameter is required (format: YYYY-MM-DD)");
    }

    try
    {
        List<TVSchedule>? results;

        if (!string.IsNullOrEmpty(sport))
        {
            // Get TV schedule for specific sport on specific date
            results = await sportsDbClient.GetTVScheduleBySportDateAsync(sport, date);
        }
        else
        {
            // Get TV schedule for all sports on specific date
            results = await sportsDbClient.GetTVScheduleByDateAsync(date);
        }

        logger.LogInformation("[EVENTS TV-SCHEDULE] Found {Count} events", results?.Count ?? 0);
        return Results.Ok(results ?? new List<TVSchedule>());
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[EVENTS TV-SCHEDULE] Error fetching TV schedule");
        return Results.Problem("Failed to fetch TV schedule from TheSportsDB");
    }
});

// GET /api/events/livescore?sport=Soccer
// Get live and recent events for a sport
app.MapGet("/api/events/livescore", async (
    string sport,
    Sportarr.Api.Services.TheSportsDBClient sportsDbClient,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[EVENTS LIVESCORE] GET /api/events/livescore?sport={Sport}", sport);

    if (string.IsNullOrEmpty(sport))
    {
        return Results.BadRequest("Sport parameter is required");
    }

    try
    {
        var results = await sportsDbClient.GetLivescoreBySportAsync(sport);
        logger.LogInformation("[EVENTS LIVESCORE] Found {Count} events", results?.Count ?? 0);
        return Results.Ok(results ?? new List<Event>());
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[EVENTS LIVESCORE] Error fetching livescore");
        return Results.Problem("Failed to fetch livescore from TheSportsDB");
    }
});

// API: Manual search for fight card
app.MapPost("/api/release/grab", async (
    HttpContext context,
    SportarrDbContext db,
    Sportarr.Api.Services.DownloadClientService downloadClientService,
    ConfigService configService,
    ILogger<Program> logger) =>
{
    // Parse the request body which contains both release and eventId
    var requestBody = await context.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
    if (requestBody == null)
    {
        logger.LogWarning("[GRAB] Invalid request body");
        return Results.BadRequest(new { success = false, message = "Invalid request body" });
    }

    // Extract eventId
    if (!requestBody.TryGetValue("eventId", out var eventIdElement) || !eventIdElement.TryGetInt32(out var eventId))
    {
        logger.LogWarning("[GRAB] Missing or invalid eventId");
        return Results.BadRequest(new { success = false, message = "Event ID is required" });
    }

    // Remove eventId from the dictionary before deserializing as ReleaseSearchResult
    requestBody.Remove("eventId");

    // Deserialize the release object from the remaining properties
    var releaseJson = System.Text.Json.JsonSerializer.Serialize(requestBody);
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var release = System.Text.Json.JsonSerializer.Deserialize<ReleaseSearchResult>(releaseJson, options);
    if (release == null)
    {
        logger.LogWarning("[GRAB] Invalid release data");
        return Results.BadRequest(new { success = false, message = "Invalid release data" });
    }

    logger.LogInformation("[GRAB] Manual grab requested for event {EventId}: {Title}", eventId, release.Title);

    var evt = await db.Events.FindAsync(eventId);
    if (evt == null)
    {
        logger.LogWarning("[GRAB] Event {EventId} not found", eventId);
        return Results.NotFound(new { success = false, message = "Event not found" });
    }

    // Get enabled download client matching the release protocol
    // Torrent releases need torrent clients (qBittorrent, Transmission, etc.)
    // Usenet releases need usenet clients (SABnzbd, NZBGet, etc.)
    var torrentClients = new[] { DownloadClientType.QBittorrent, DownloadClientType.Transmission,
                                 DownloadClientType.Deluge, DownloadClientType.RTorrent,
                                 DownloadClientType.UTorrent };
    var usenetClients = new[] { DownloadClientType.Sabnzbd, DownloadClientType.NzbGet };

    var downloadClient = await db.DownloadClients
        .Where(dc => dc.Enabled)
        .Where(dc => release.Protocol == "Torrent" ? torrentClients.Contains(dc.Type) : usenetClients.Contains(dc.Type))
        .OrderBy(dc => dc.Priority)
        .FirstOrDefaultAsync();

    if (downloadClient == null)
    {
        logger.LogWarning("[GRAB] No enabled {Protocol} download client configured", release.Protocol);
        return Results.BadRequest(new { success = false, message = $"No {release.Protocol} download client configured. This release requires a {(release.Protocol == "Torrent" ? "torrent" : "usenet")} client." });
    }

    logger.LogInformation("[GRAB] Using download client: {ClientName} ({ClientType})",
        downloadClient.Name, downloadClient.Type);

    // NOTE: We do NOT specify download path - download client uses its own configured directory
    // The category is used to track Sportarr downloads and create subdirectories
    // This matches Sonarr/Radarr behavior
    logger.LogInformation("[GRAB] Category: {Category}", downloadClient.Category);
    logger.LogInformation("[GRAB] ========== STARTING DOWNLOAD GRAB ==========");
    logger.LogInformation("[GRAB] Release Title: {Title}", release.Title);
    logger.LogInformation("[GRAB] Release Quality: {Quality}", release.Quality);
    logger.LogInformation("[GRAB] Release Size: {Size} bytes", release.Size);
    logger.LogInformation("[GRAB] Release Indexer: {Indexer}", release.Indexer);
    logger.LogInformation("[GRAB] Download URL: {Url}", release.DownloadUrl);
    logger.LogInformation("[GRAB] Download URL Type: {UrlType}",
        release.DownloadUrl.StartsWith("magnet:") ? "Magnet Link" :
        release.DownloadUrl.EndsWith(".torrent") ? "Torrent File URL" :
        "Unknown/Other");

    // Add download to client (category only, no path)
    string? downloadId;
    try
    {
        logger.LogInformation("[GRAB] Calling DownloadClientService.AddDownloadAsync...");
        downloadId = await downloadClientService.AddDownloadAsync(
            downloadClient,
            release.DownloadUrl,
            downloadClient.Category,
            release.Title  // Pass release title for better matching
        );
        logger.LogInformation("[GRAB] AddDownloadAsync returned: {DownloadId}", downloadId ?? "null");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[GRAB] ========== EXCEPTION DURING DOWNLOAD GRAB ==========");
        logger.LogError(ex, "[GRAB] Exception: {Message}", ex.Message);
        return Results.BadRequest(new
        {
            success = false,
            message = $"Failed to add download to {downloadClient.Name}: {ex.Message}"
        });
    }

    if (downloadId == null)
    {
        logger.LogError("[GRAB] ========== DOWNLOAD GRAB FAILED ==========");
        logger.LogError("[GRAB] AddDownloadAsync returned null - download was not added to client");
        logger.LogError("[GRAB] Check the logs above for qBittorrent/download client errors");
        return Results.BadRequest(new
        {
            success = false,
            message = $"Failed to add download to {downloadClient.Name}. Check download client connection and credentials in Settings > Download Clients."
        });
    }

    logger.LogInformation("[GRAB] Download added to client successfully!");
    logger.LogInformation("[GRAB] Download ID (Hash): {DownloadId}", downloadId);

    // Track download in database
    logger.LogInformation("[GRAB] Creating download queue item in database...");
    var queueItem = new DownloadQueueItem
    {
        EventId = eventId,
        Title = release.Title,
        DownloadId = downloadId,
        DownloadClientId = downloadClient.Id,
        Status = DownloadStatus.Queued,
        Quality = release.Quality,
        Size = release.Size,
        Downloaded = 0,
        Progress = 0,
        Indexer = release.Indexer,
        Protocol = release.Protocol,
        TorrentInfoHash = release.TorrentInfoHash,
        RetryCount = 0,
        LastUpdate = DateTime.UtcNow
    };

    db.DownloadQueue.Add(queueItem);
    await db.SaveChangesAsync();

    // Immediately check download status (Sonarr/Radarr behavior)
    // This ensures the download appears in the Activity page with real-time status
    logger.LogInformation("[GRAB] Performing immediate status check...");
    try
    {
        // Give SABnzbd a moment to register the download in its queue
        // SABnzbd may need 1-2 seconds after AddNzbAsync returns before the download appears in queue API
        await Task.Delay(2000); // 2 second delay
        logger.LogDebug("[GRAB] Checking status after 2s delay...");

        var status = await downloadClientService.GetDownloadStatusAsync(downloadClient, downloadId);
        if (status != null)
        {
            queueItem.Status = status.Status switch
            {
                "downloading" => DownloadStatus.Downloading,
                "paused" => DownloadStatus.Paused,
                "completed" => DownloadStatus.Completed,
                "queued" or "waiting" => DownloadStatus.Queued,
                _ => DownloadStatus.Queued
            };
            queueItem.Progress = status.Progress;
            queueItem.Downloaded = status.Downloaded;
            queueItem.Size = status.Size > 0 ? status.Size : release.Size;
            queueItem.LastUpdate = DateTime.UtcNow;
            await db.SaveChangesAsync();
            logger.LogInformation("[GRAB] Initial status: {Status}, Progress: {Progress:F1}%",
                queueItem.Status, queueItem.Progress);
        }
        else
        {
            logger.LogDebug("[GRAB] Status not available yet (download still initializing)");
        }
    }
    catch (Exception ex)
    {
        // Don't fail the grab if status check fails
        logger.LogWarning(ex, "[GRAB] Failed to get initial status (download will be tracked by monitor)");
    }

    logger.LogInformation("[GRAB] Download queued in database:");
    logger.LogInformation("[GRAB]   Queue ID: {QueueId}", queueItem.Id);
    logger.LogInformation("[GRAB]   Event ID: {EventId}", queueItem.EventId);
    logger.LogInformation("[GRAB]   Download ID: {DownloadId}", queueItem.DownloadId);
    logger.LogInformation("[GRAB]   Status: {Status}", queueItem.Status);
    logger.LogInformation("[GRAB] ========== DOWNLOAD GRAB COMPLETE ==========");
    logger.LogInformation("[GRAB] The download monitor service will track this download and update its status");

    return Results.Ok(new
    {
        success = true,
        message = "Download started successfully",
        downloadId = downloadId,
        queueId = queueItem.Id
    });
});

// API: Automatic search and download for event
app.MapPost("/api/event/{eventId:int}/automatic-search", async (
    int eventId,
    int? qualityProfileId,
    Sportarr.Api.Services.TaskService taskService,
    SportarrDbContext db) =>
{
    // Get event title for task name
    var evt = await db.Events.FindAsync(eventId);
    var eventTitle = evt?.Title ?? $"Event {eventId}";

    // Queue a search task
    var task = await taskService.QueueTaskAsync(
        name: $"Search: {eventTitle}",
        commandName: "EventSearch",
        priority: 10,
        body: eventId.ToString()
    );

    return Results.Ok(new {
        success = true,
        message = "Search queued",
        taskId = task.Id
    });
});

// API: Search all monitored events
app.MapPost("/api/automatic-search/all", async (
    Sportarr.Api.Services.AutomaticSearchService automaticSearchService) =>
{
    var results = await automaticSearchService.SearchAllMonitoredEventsAsync();
    return Results.Ok(results);
});

// API: Search all monitored events in a specific league (Sonarr-style league/show-level search)
app.MapPost("/api/league/{leagueId:int}/automatic-search", async (
    int leagueId,
    SportarrDbContext db,
    Sportarr.Api.Services.AutomaticSearchService automaticSearchService,
    Sportarr.Api.Services.TaskService taskService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[AUTOMATIC SEARCH] POST /api/league/{LeagueId}/automatic-search - Searching all monitored events in league", leagueId);

    var league = await db.Leagues.FindAsync(leagueId);
    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    // Get all monitored events in this league (searches for missing files and upgrades)
    var events = await db.Events
        .Where(e => e.LeagueId == leagueId && e.Monitored)
        .ToListAsync();

    if (!events.Any())
    {
        return Results.Ok(new
        {
            success = true,
            message = $"No monitored events found in {league.Name}",
            eventsSearched = 0
        });
    }

    logger.LogInformation("[AUTOMATIC SEARCH] Found {Count} monitored events in league: {League}", events.Count, league.Name);

    // Queue search tasks for all events
    var taskIds = new List<int>();
    foreach (var evt in events)
    {
        var task = await taskService.QueueTaskAsync(
            name: $"Search: {evt.Title}",
            commandName: "EventSearch",
            priority: 10,
            body: evt.Id.ToString()
        );
        taskIds.Add(task.Id);
    }

    return Results.Ok(new
    {
        success = true,
        message = $"Queued {events.Count} automatic searches for {league.Name}",
        eventsSearched = events.Count,
        taskIds = taskIds
    });
});

// ========================================
// PROWLARR INTEGRATION - Sonarr/Radarr-Compatible Application API
// ========================================

// Prowlarr uses /api/v1/indexer to sync indexers to applications
// These endpoints allow Prowlarr to automatically push indexers to Sportarr

// GET /api/v1/indexer - List all indexers (Prowlarr uses this to check existing)
app.MapGet("/api/v1/indexer", async (SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v1/indexer - Listing indexers for Prowlarr");
    var indexers = await db.Indexers.OrderBy(i => i.Priority).ToListAsync();

    // Transform to Prowlarr-compatible format
    var prowlarrIndexers = indexers.Select(i => new
    {
        id = i.Id,
        name = i.Name,
        implementation = i.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
        enable = i.Enabled,
        priority = i.Priority,
        fields = new object[]
        {
            new { name = "baseUrl", value = i.Url },
            new { name = "apiKey", value = i.ApiKey ?? "" },
            new { name = "categories", value = string.Join(",", i.Categories) }
        }
    }).ToList();

    return Results.Ok(prowlarrIndexers);
});

// POST /api/v1/indexer - Add new indexer (Prowlarr pushes indexers here)
app.MapPost("/api/v1/indexer", async (
    HttpRequest request,
    SportarrDbContext db,
    ILogger<Program> logger) =>
{
    try
    {
        // Read raw JSON body
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        logger.LogInformation("[PROWLARR] POST /api/v1/indexer - Received: {Json}", json);

        // Parse Prowlarr payload
        var prowlarrIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Extract fields from Prowlarr format
        var name = prowlarrIndexer.GetProperty("name").GetString() ?? "Unknown";
        var implementation = prowlarrIndexer.GetProperty("implementation").GetString() ?? "Torznab";
        var enabled = prowlarrIndexer.TryGetProperty("enable", out var enableProp) ? enableProp.GetBoolean() : true;
        var priority = prowlarrIndexer.TryGetProperty("priority", out var priorityProp) ? priorityProp.GetInt32() : 50;

        // Extract fields array
        var fields = prowlarrIndexer.GetProperty("fields");
        string? baseUrl = null;
        string? apiKey = null;
        string? categories = null;

        foreach (var field in fields.EnumerateArray())
        {
            var fieldName = field.GetProperty("name").GetString();
            var fieldValue = field.TryGetProperty("value", out var val) ? val.GetString() : null;

            if (fieldName == "baseUrl") baseUrl = fieldValue;
            else if (fieldName == "apiKey" || fieldName == "apikey") apiKey = fieldValue;
            else if (fieldName == "categories") categories = fieldValue;
        }

        // Check if indexer with same URL already exists
        var existing = await db.Indexers.FirstOrDefaultAsync(i => i.Url == baseUrl);
        if (existing != null)
        {
            // Update existing
            logger.LogInformation("[PROWLARR] Updating existing indexer: {Name}", name);
            existing.Name = name;
            existing.Type = implementation.ToLower().Contains("newznab") ? IndexerType.Newznab : IndexerType.Torznab;
            existing.ApiKey = apiKey;
            existing.Categories = !string.IsNullOrWhiteSpace(categories)
                ? categories.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                : new List<string>();
            existing.Enabled = enabled;
            existing.Priority = priority;
            existing.LastModified = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(new { id = existing.Id });
        }

        // Create new indexer
        var indexer = new Indexer
        {
            Name = name,
            Type = implementation.ToLower().Contains("newznab") ? IndexerType.Newznab : IndexerType.Torznab,
            Url = baseUrl ?? "",
            ApiKey = apiKey,
            Categories = !string.IsNullOrWhiteSpace(categories)
                ? categories.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                : new List<string>(),
            Enabled = enabled,
            Priority = priority,
            MinimumSeeders = 1,
            Created = DateTime.UtcNow
        };

        db.Indexers.Add(indexer);
        await db.SaveChangesAsync();

        logger.LogInformation("[PROWLARR] Created new indexer: {Name} (ID: {Id})", name, indexer.Id);
        return Results.Created($"/api/v1/indexer/{indexer.Id}", new { id = indexer.Id });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PROWLARR] Error adding indexer: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

// PUT /api/v1/indexer/{id} - Update indexer
app.MapPut("/api/v1/indexer/{id:int}", async (
    int id,
    HttpRequest request,
    SportarrDbContext db,
    ILogger<Program> logger) =>
{
    try
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        logger.LogInformation("[PROWLARR] PUT /api/v1/indexer/{Id} - Received: {Json}", id, json);

        var indexer = await db.Indexers.FindAsync(id);
        if (indexer is null) return Results.NotFound();

        // Parse and update
        var prowlarrIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        indexer.Name = prowlarrIndexer.GetProperty("name").GetString() ?? indexer.Name;
        indexer.Enabled = prowlarrIndexer.TryGetProperty("enable", out var enableProp) ? enableProp.GetBoolean() : indexer.Enabled;
        indexer.Priority = prowlarrIndexer.TryGetProperty("priority", out var priorityProp) ? priorityProp.GetInt32() : indexer.Priority;
        indexer.LastModified = DateTime.UtcNow;

        await db.SaveChangesAsync();
        logger.LogInformation("[PROWLARR] Updated indexer: {Name} (ID: {Id})", indexer.Name, id);

        return Results.Ok(new { id = indexer.Id });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PROWLARR] Error updating indexer: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

// DELETE /api/v1/indexer/{id} - Delete indexer
app.MapDelete("/api/v1/indexer/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] DELETE /api/v1/indexer/{Id}", id);
    var indexer = await db.Indexers.FindAsync(id);
    if (indexer is null) return Results.NotFound();

    db.Indexers.Remove(indexer);
    await db.SaveChangesAsync();
    logger.LogInformation("[PROWLARR] Deleted indexer: {Name} (ID: {Id})", indexer.Name, id);

    return Results.Ok();
});

// GET /api/v1/system/status - System info (Prowlarr uses this for connection test)
app.MapGet("/api/v1/system/status", (HttpContext context, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v1/system/status - Connection test from Prowlarr");

    // Log all headers for debugging
    logger.LogInformation("[PROWLARR AUTH] Headers: {Headers}",
        string.Join(", ", context.Request.Headers.Select(h => $"{h.Key}={h.Value}")));

    // Check if API key was provided
    var hasApiKey = context.Request.Headers.ContainsKey("X-Api-Key") ||
                    context.Request.Query.ContainsKey("apikey") ||
                    context.Request.Headers.ContainsKey("Authorization");
    logger.LogInformation("[PROWLARR AUTH] Has API Key: {HasApiKey}", hasApiKey);
    logger.LogInformation("[PROWLARR AUTH] User authenticated: {IsAuthenticated}, User: {User}",
        context.User?.Identity?.IsAuthenticated, context.User?.Identity?.Name);

    return Results.Ok(new
    {
        appName = "Sportarr",
        instanceName = "Sportarr",
        version = Sportarr.Api.Version.AppVersion,
        buildTime = DateTime.UtcNow,
        isDebug = false,
        isProduction = true,
        isAdmin = false,
        isUserInteractive = false,
        startupPath = Directory.GetCurrentDirectory(),
        appData = dataPath,
        osName = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        osVersion = Environment.OSVersion.VersionString,
        isNetCore = true,
        isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux),
        isOsx = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX),
        isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows),
        isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true",
        mode = "console",
        branch = "main",
        authentication = "forms",
        sqliteVersion = "3.0",
        urlBase = "",
        runtimeVersion = Environment.Version.ToString(),
        runtimeName = ".NET",
        startTime = DateTime.UtcNow
    });
});

// GET /api/v3/system/status - System status (Radarr v3 API for Prowlarr)
app.MapGet("/api/v3/system/status", (HttpContext context, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v3/system/status - Prowlarr requesting system status (v3 API)");

    // Return same data as v1 endpoint
    return Results.Ok(new
    {
        appName = "Sportarr",
        instanceName = "Sportarr",
        version = Sportarr.Api.Version.AppVersion,
        buildTime = DateTime.UtcNow,
        isDebug = false,
        isProduction = true,
        isAdmin = false,
        isUserInteractive = false,
        startupPath = Directory.GetCurrentDirectory(),
        appData = dataPath,
        osName = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        osVersion = Environment.OSVersion.VersionString,
        isNetCore = true,
        isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux),
        isOsx = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX),
        isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows),
        isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true",
        mode = "console",
        branch = "main",
        authentication = "forms",
        sqliteVersion = "3.0",
        urlBase = "",
        runtimeVersion = Environment.Version.ToString(),
        runtimeName = ".NET",
        startTime = DateTime.UtcNow
    });
});

// POST /api/v3/indexer/test - Test indexer connection (Radarr v3 API for Prowlarr)
app.MapPost("/api/v3/indexer/test", async (HttpRequest request, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] POST /api/v3/indexer/test - Prowlarr testing indexer");

    // Read the test indexer payload from Prowlarr
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    logger.LogInformation("[PROWLARR] Test indexer payload: {Json}", json);

    // For now, just return success - Prowlarr is testing if we can receive indexer configs
    // In a real implementation, we might test the indexer URL, but for connection testing this is enough
    return Results.Ok(new
    {
        id = 0,
        name = "Test",
        message = "Connection test successful"
    });
});

// GET /api/v3/indexer/schema - Indexer schema (Radarr v3 API for Prowlarr)
app.MapGet("/api/v3/indexer/schema", (ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v3/indexer/schema - Prowlarr requesting indexer schema");

    // Return Torznab/Newznab indexer schema matching Radarr/Sonarr format exactly
    return Results.Ok(new object[]
    {
        new
        {
            id = 0,
            enableRss = true,
            enableAutomaticSearch = true,
            enableInteractiveSearch = true,
            supportsRss = true,
            supportsSearch = true,
            protocol = "torrent",
            priority = 25,
            downloadClientId = 0,
            name = "",
            implementation = "Torznab",
            implementationName = "Torznab",
            configContract = "TorznabSettings",
            infoLink = "https://github.com/Prowlarr/Prowlarr",
            seedCriteria = new
            {
                seedRatio = 1.0,
                seedTime = 1,
                seasonPackSeedTime = 1
            },
            tags = new int[] { },
            presets = new object[] { },
            fields = new object[]
            {
                new
                {
                    order = 0,
                    name = "baseUrl",
                    label = "URL",
                    helpText = "Torznab feed URL",
                    helpLink = (string?)null,
                    value = "",
                    type = "textbox",
                    advanced = false,
                    hidden = false
                },
                new
                {
                    order = 1,
                    name = "apiPath",
                    label = "API Path",
                    helpText = "Path to the api, usually /api",
                    helpLink = (string?)null,
                    value = "/api",
                    type = "textbox",
                    advanced = true,
                    hidden = false
                },
                new
                {
                    order = 2,
                    name = "apiKey",
                    label = "API Key",
                    helpText = (string?)null,
                    helpLink = (string?)null,
                    value = "",
                    type = "textbox",
                    privacy = "apiKey",
                    advanced = false,
                    hidden = false
                },
                new
                {
                    order = 3,
                    name = "categories",
                    label = "Categories",
                    helpText = "Comma separated list of categories",
                    helpLink = (string?)null,
                    value = new int[] { 2000, 2010, 2020, 2030, 2040, 2045, 2050, 2060 },
                    type = "select",
                    selectOptions = new object[] { },
                    advanced = false,
                    hidden = false
                },
                new
                {
                    order = 4,
                    name = "minimumSeeders",
                    label = "Minimum Seeders",
                    helpText = "Minimum number of seeders required",
                    helpLink = (string?)null,
                    value = 1,
                    type = "number",
                    advanced = true,
                    hidden = false
                },
                new
                {
                    order = 5,
                    name = "seedCriteria.seedRatio",
                    label = "Seed Ratio",
                    helpText = "The ratio a torrent should reach before stopping, empty is download client's default",
                    helpLink = (string?)null,
                    value = 1.0,
                    type = "number",
                    advanced = true,
                    hidden = false
                },
                new
                {
                    order = 6,
                    name = "seedCriteria.seedTime",
                    label = "Seed Time",
                    helpText = "The time a torrent should be seeded before stopping, empty is download client's default",
                    helpLink = (string?)null,
                    value = 1,
                    type = "number",
                    advanced = true,
                    hidden = false
                },
                new
                {
                    order = 7,
                    name = "seedCriteria.seasonPackSeedTime",
                    label = "Season Pack Seed Time",
                    helpText = "The time a season pack torrent should be seeded before stopping, empty is download client's default",
                    helpLink = (string?)null,
                    value = (int?)null,
                    type = "number",
                    advanced = true,
                    hidden = false
                }
            }
        },
        new
        {
            id = 0,
            enableRss = true,
            enableAutomaticSearch = true,
            enableInteractiveSearch = true,
            supportsRss = true,
            supportsSearch = true,
            protocol = "usenet",
            priority = 25,
            downloadClientId = 0,
            name = "",
            implementation = "Newznab",
            implementationName = "Newznab",
            configContract = "NewznabSettings",
            infoLink = "https://github.com/Prowlarr/Prowlarr",
            tags = new int[] { },
            presets = new object[] { },
            fields = new object[]
            {
                new
                {
                    order = 0,
                    name = "baseUrl",
                    label = "URL",
                    helpText = "Newznab feed URL",
                    helpLink = (string?)null,
                    value = "",
                    type = "textbox",
                    advanced = false,
                    hidden = false
                },
                new
                {
                    order = 1,
                    name = "apiPath",
                    label = "API Path",
                    helpText = "Path to the api, usually /api",
                    helpLink = (string?)null,
                    value = "/api",
                    type = "textbox",
                    advanced = true,
                    hidden = false
                },
                new
                {
                    order = 2,
                    name = "apiKey",
                    label = "API Key",
                    helpText = (string?)null,
                    helpLink = (string?)null,
                    value = "",
                    type = "textbox",
                    privacy = "apiKey",
                    advanced = false,
                    hidden = false
                },
                new
                {
                    order = 3,
                    name = "categories",
                    label = "Categories",
                    helpText = "Comma separated list of categories",
                    helpLink = (string?)null,
                    value = new int[] { 2000, 2010, 2020, 2030, 2040, 2045, 2050, 2060 },
                    type = "select",
                    selectOptions = new object[] { },
                    advanced = false,
                    hidden = false
                }
            }
        }
    });
});

// GET /api/v3/indexer - List all indexers (Radarr v3 API for Prowlarr)
app.MapGet("/api/v3/indexer", async (SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v3/indexer - Prowlarr requesting indexer list");

    var indexers = await db.Indexers.ToListAsync();

    // Convert our indexers to Radarr v3 format
    var radarrIndexers = indexers.Select(i =>
    {
        var isTorznab = i.Type == IndexerType.Torznab;
        var fields = new List<object>
        {
            new {  order = 0, name = "baseUrl", label = "URL", helpText = isTorznab ? "Torznab feed URL" : "Newznab feed URL", helpLink = (string?)null, value = i.Url, type = "textbox", advanced = false, hidden = false },
            new { order = 1, name = "apiPath", label = "API Path", helpText = "Path to the api, usually /api", helpLink = (string?)null, value = "/api", type = "textbox", advanced = true, hidden = false },
            new { order = 2, name = "apiKey", label = "API Key", helpText = (string?)null, helpLink = (string?)null, value = i.ApiKey ?? "", type = "textbox", privacy = "apiKey", advanced = false, hidden = false },
            new { order = 3, name = "categories", label = "Categories", helpText = "Comma separated list of categories", helpLink = (string?)null, value = i.Categories.Select(c => int.TryParse(c, out var cat) ? cat : 0).ToArray(), type = "select", advanced = false, hidden = false },
            new { order = 4, name = "minimumSeeders", label = "Minimum Seeders", helpText = "Minimum number of seeders required", helpLink = (string?)null, value = i.MinimumSeeders, type = "number", advanced = false, hidden = false }
        };

        // Add optional fields if present (NOT seed criteria - those go in seedCriteria object)
        var fieldOrder = 5;
        if (i.EarlyReleaseLimit.HasValue)
            fields.Add(new { order = fieldOrder++, name = "earlyReleaseLimit", label = "Early Release Limit", helpText = (string?)null, helpLink = (string?)null, value = i.EarlyReleaseLimit.Value, type = "number", advanced = true, hidden = false });
        // Note: animeCategories is a Sonarr-only field, not used in Radarr API (Sportarr uses Radarr template only)

        return new
        {
            id = i.Id,
            name = i.Name,
            enableRss = i.EnableRss,
            enableAutomaticSearch = i.EnableAutomaticSearch,
            enableInteractiveSearch = i.EnableInteractiveSearch,
            priority = i.Priority,
            implementation = i.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
            implementationName = i.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
            configContract = i.Type == IndexerType.Torznab ? "TorznabSettings" : "NewznabSettings",
            infoLink = "https://github.com/Prowlarr/Prowlarr",
            protocol = i.Type == IndexerType.Torznab ? "torrent" : "usenet",
            supportsRss = i.EnableRss,
            supportsSearch = i.EnableAutomaticSearch || i.EnableInteractiveSearch,
            downloadClientId = i.DownloadClientId ?? 0,
            // Prowlarr expects seedCriteria as a top-level object (always present, values > 0 for torrents, null for usenet)
            seedCriteria = new
            {
                seedRatio = i.Type == IndexerType.Torznab ? (double?)(i.SeedRatio ?? 1.0) : null,
                seedTime = i.Type == IndexerType.Torznab ? (int?)(i.SeedTime ?? 1) : null,
                seasonPackSeedTime = i.Type == IndexerType.Torznab ? (int?)(i.SeasonPackSeedTime ?? 1) : null
            },
            tags = i.Tags.ToArray(),
            fields = fields.ToArray(),
            // Prowlarr expects capabilities object with categories
            capabilities = new
            {
                categories = i.Categories.Select(c =>
                {
                    var catId = int.TryParse(c, out var cat) ? cat : 0;
                    return new
                    {
                        id = catId,
                        name = CategoryHelper.GetCategoryName(catId),
                        subCategories = new object[] { }
                    };
                }).ToArray(),
                supportsRawSearch = true,
                searchParams = new[] { "q" },
                tvSearchParams = new[] { "q", "season", "ep" },
                movieSearchParams = new[] { "q", "imdbid" },
                musicSearchParams = new[] { "q" },
                bookSearchParams = new[] { "q" }
            }
        };
    }).ToList();

    return Results.Ok(radarrIndexers);
});

// GET /api/v3/indexer/{id} - Get specific indexer (Radarr v3 API for Prowlarr)
app.MapGet("/api/v3/indexer/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v3/indexer/{Id}", id);

    var indexer = await db.Indexers.FindAsync(id);
    if (indexer == null)
        return Results.NotFound();

    return Results.Ok(new
    {
        id = indexer.Id,
        name = indexer.Name,
        enableRss = indexer.EnableRss,
        enableAutomaticSearch = indexer.EnableAutomaticSearch,
        enableInteractiveSearch = indexer.EnableInteractiveSearch,
        priority = indexer.Priority,
        implementation = indexer.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
        implementationName = indexer.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
        configContract = indexer.Type == IndexerType.Torznab ? "TorznabSettings" : "NewznabSettings",
        infoLink = "https://github.com/Prowlarr/Prowlarr",
        protocol = indexer.Type == IndexerType.Torznab ? "torrent" : "usenet",
        supportsRss = indexer.EnableRss,
        supportsSearch = indexer.EnableAutomaticSearch || indexer.EnableInteractiveSearch,
        downloadClientId = indexer.DownloadClientId ?? 0,
        // Prowlarr expects seedCriteria as a top-level object (always present, values > 0 for torrents, null for usenet)
        seedCriteria = new
        {
            seedRatio = indexer.Type == IndexerType.Torznab ? (double?)(indexer.SeedRatio ?? 1.0) : null,
            seedTime = indexer.Type == IndexerType.Torznab ? (int?)(indexer.SeedTime ?? 1) : null,
            seasonPackSeedTime = indexer.Type == IndexerType.Torznab ? (int?)(indexer.SeasonPackSeedTime ?? 1) : null
        },
        tags = indexer.Tags.ToArray(),
        fields = new object[]
        {
            new { order = 0, name = "baseUrl", label = "URL", helpText = indexer.Type == IndexerType.Torznab ? "Torznab feed URL" : "Newznab feed URL", helpLink = (string?)null, value = indexer.Url, type = "textbox", advanced = false, hidden = false },
            new { order = 1, name = "apiPath", label = "API Path", helpText = "Path to the api, usually /api", helpLink = (string?)null, value = "/api", type = "textbox", advanced = true, hidden = false },
            new { order = 2, name = "apiKey", label = "API Key", helpText = (string?)null, helpLink = (string?)null, value = indexer.ApiKey ?? "", type = "textbox", privacy = "apiKey", advanced = false, hidden = false },
            new { order = 3, name = "categories", label = "Categories", helpText = "Comma separated list of categories", helpLink = (string?)null, value = indexer.Categories.Select(c => int.TryParse(c, out var cat) ? cat : 0).ToArray(), type = "select", advanced = false, hidden = false },
            new { order = 4, name = "minimumSeeders", label = "Minimum Seeders", helpText = "Minimum number of seeders required", helpLink = (string?)null, value = indexer.MinimumSeeders, type = "number", advanced = false, hidden = false }
        },
        // Prowlarr expects capabilities object with categories
        capabilities = new
        {
            categories = indexer.Categories.Select(c =>
            {
                var catId = int.TryParse(c, out var cat) ? cat : 0;
                return new
                {
                    id = catId,
                    name = CategoryHelper.GetCategoryName(catId),
                    subCategories = new object[] { }
                };
            }).ToArray(),
            supportsRawSearch = true,
            searchParams = new[] { "q" },
            tvSearchParams = new[] { "q", "season", "ep" },
            movieSearchParams = new[] { "q", "imdbid" },
            musicSearchParams = new[] { "q" },
            bookSearchParams = new[] { "q" }
        }
    });
});

// POST /api/v3/indexer - Add new indexer (Radarr v3 API for Prowlarr)
app.MapPost("/api/v3/indexer", async (HttpRequest request, SportarrDbContext db, ILogger<Program> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    logger.LogInformation("[PROWLARR] POST /api/v3/indexer - Creating indexer: {Json}", json);

    try
    {
        var prowlarrIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Extract fields from Prowlarr's format
        var name = prowlarrIndexer.GetProperty("name").GetString() ?? "Unknown";
        var implementation = prowlarrIndexer.GetProperty("implementation").GetString() ?? "Newznab";
        var fieldsArray = prowlarrIndexer.GetProperty("fields").EnumerateArray();

        var baseUrl = "";
        var apiKey = "";
        var categories = new List<string>();
        var minimumSeeders = 1;
        double? seedRatio = null;
        int? seedTime = null;
        int? seasonPackSeedTime = null;
        int? earlyReleaseLimit = null;

        // Parse seedCriteria object if present (Prowlarr sends this for torrent indexers)
        if (prowlarrIndexer.TryGetProperty("seedCriteria", out var seedCriteria))
        {
            if (seedCriteria.TryGetProperty("seedRatio", out var seedRatioValue) && seedRatioValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                seedRatio = seedRatioValue.GetDouble();
            if (seedCriteria.TryGetProperty("seedTime", out var seedTimeValue) && seedTimeValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                seedTime = seedTimeValue.GetInt32();
            if (seedCriteria.TryGetProperty("seasonPackSeedTime", out var seasonValue) && seasonValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                seasonPackSeedTime = seasonValue.GetInt32();
        }

        foreach (var field in fieldsArray)
        {
            var fieldName = field.GetProperty("name").GetString();
            if (fieldName == "baseUrl")
                baseUrl = field.GetProperty("value").GetString() ?? "";
            else if (fieldName == "apiKey")
                apiKey = field.GetProperty("value").GetString() ?? "";
            else if (fieldName == "categories" && field.TryGetProperty("value", out var catValue) && catValue.ValueKind == System.Text.Json.JsonValueKind.Array)
                categories = catValue.EnumerateArray().Select(c => c.GetInt32().ToString()).ToList();
            else if (fieldName == "minimumSeeders" && field.TryGetProperty("value", out var seedValue))
                minimumSeeders = seedValue.GetInt32();
            else if (fieldName == "earlyReleaseLimit" && field.TryGetProperty("value", out var earlyValue))
                earlyReleaseLimit = earlyValue.GetInt32();
            // Note: animeCategories is a Sonarr-only field, not used in Radarr API
        }

        var indexer = new Indexer
        {
            Name = name,
            Type = implementation == "Torznab" ? IndexerType.Torznab : IndexerType.Newznab,
            Url = baseUrl,
            ApiKey = apiKey,
            Categories = categories,
            Enabled = prowlarrIndexer.TryGetProperty("enableRss", out var enableRssProp) ? enableRssProp.GetBoolean() : true,
            EnableRss = prowlarrIndexer.TryGetProperty("enableRss", out var rss) ? rss.GetBoolean() : true,
            EnableAutomaticSearch = prowlarrIndexer.TryGetProperty("enableAutomaticSearch", out var autoSearch) ? autoSearch.GetBoolean() : true,
            EnableInteractiveSearch = prowlarrIndexer.TryGetProperty("enableInteractiveSearch", out var intSearch) ? intSearch.GetBoolean() : true,
            Priority = prowlarrIndexer.TryGetProperty("priority", out var priorityProp) ? priorityProp.GetInt32() : 25,
            MinimumSeeders = minimumSeeders,
            SeedRatio = seedRatio,
            SeedTime = seedTime,
            SeasonPackSeedTime = seasonPackSeedTime,
            EarlyReleaseLimit = earlyReleaseLimit,
            AnimeCategories = null, // Radarr doesn't use anime categories (Sonarr-only field)
            Tags = prowlarrIndexer.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == System.Text.Json.JsonValueKind.Array
                ? tagsProp.EnumerateArray().Select(t => t.GetInt32()).ToList()
                : new List<int>(),
            Created = DateTime.UtcNow
        };

        db.Indexers.Add(indexer);
        await db.SaveChangesAsync();

        logger.LogInformation("[PROWLARR] Created indexer {Name} with ID {Id}", indexer.Name, indexer.Id);

        var responseFields = new List<object>
        {
            new { name = "baseUrl", value = indexer.Url },
            new { name = "apiPath", value = indexer.ApiPath },
            new { name = "apiKey", value = indexer.ApiKey },
            new { name = "categories", value = indexer.Categories.Select(c => int.TryParse(c, out var cat) ? cat : 0).ToArray() },
            new { name = "minimumSeeders", value = indexer.MinimumSeeders }
        };

        // Add optional fields if present (NOT seed criteria - those go in seedCriteria object)
        if (indexer.EarlyReleaseLimit.HasValue)
            responseFields.Add(new { name = "earlyReleaseLimit", value = indexer.EarlyReleaseLimit.Value });
        // Note: animeCategories is a Sonarr-only field, not used in Radarr API
        if (!string.IsNullOrEmpty(indexer.AdditionalParameters))
            responseFields.Add(new { name = "additionalParameters", value = indexer.AdditionalParameters });
        if (indexer.MultiLanguages != null && indexer.MultiLanguages.Count > 0)
            responseFields.Add(new { name = "multiLanguages", value = string.Join(",", indexer.MultiLanguages) });
        responseFields.Add(new { name = "rejectBlocklistedTorrentHashes", value = indexer.RejectBlocklistedTorrentHashes });
        if (indexer.DownloadClientId.HasValue)
            responseFields.Add(new { name = "downloadClientId", value = indexer.DownloadClientId.Value });
        if (indexer.Tags.Count > 0)
            responseFields.Add(new { name = "tags", value = string.Join(",", indexer.Tags) });

        return Results.Ok(new
        {
            id = indexer.Id,
            name = indexer.Name,
            enableRss = indexer.EnableRss,
            enableAutomaticSearch = indexer.EnableAutomaticSearch,
            enableInteractiveSearch = indexer.EnableInteractiveSearch,
            priority = indexer.Priority,
            implementation = indexer.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
            implementationName = indexer.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
            configContract = indexer.Type == IndexerType.Torznab ? "TorznabSettings" : "NewznabSettings",
            protocol = indexer.Type == IndexerType.Torznab ? "torrent" : "usenet",
            supportsRss = indexer.EnableRss,
            supportsSearch = indexer.EnableAutomaticSearch || indexer.EnableInteractiveSearch,
            downloadClientId = indexer.DownloadClientId ?? 0,
            // Prowlarr expects seedCriteria as a top-level object (always present, null values for usenet)
            seedCriteria = new
            {
                seedRatio = indexer.Type == IndexerType.Torznab ? indexer.SeedRatio : (double?)null,
                seedTime = indexer.Type == IndexerType.Torznab ? indexer.SeedTime : (int?)null,
                seasonPackSeedTime = indexer.Type == IndexerType.Torznab ? indexer.SeasonPackSeedTime : (int?)null
            },
            tags = indexer.Tags.ToArray(),
            fields = responseFields.ToArray(),
            // Add capabilities object (required for Prowlarr's BuildRadarrIndexer at line 269)
            capabilities = new
            {
                categories = indexer.Categories.Select(c =>
                {
                    var catId = int.TryParse(c, out var cat) ? cat : 0;
                    return new
                    {
                        id = catId,
                        name = CategoryHelper.GetCategoryName(catId),
                        subCategories = new object[] { }
                    };
                }).ToArray(),
                supportsRawSearch = true,
                searchParams = new[] { "q" },
                tvSearchParams = new[] { "q", "season", "ep" },
                movieSearchParams = new[] { "q", "imdbid" },
                musicSearchParams = new[] { "q" },
                bookSearchParams = new[] { "q" }
            }
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PROWLARR] Error creating indexer");
        return Results.BadRequest(new { error = ex.Message });
    }
});

// PUT /api/v3/indexer/{id} - Update indexer (Radarr v3 API for Prowlarr)
app.MapPut("/api/v3/indexer/{id:int}", async (int id, HttpRequest request, SportarrDbContext db, ILogger<Program> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    logger.LogInformation("[PROWLARR] PUT /api/v3/indexer/{Id} - Updating indexer: {Json}", id, json);

    var indexer = await db.Indexers.FindAsync(id);
    if (indexer == null)
        return Results.NotFound();

    try
    {
        var prowlarrIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        indexer.Name = prowlarrIndexer.GetProperty("name").GetString() ?? indexer.Name;
        indexer.Type = prowlarrIndexer.GetProperty("implementation").GetString() == "Torznab" ? IndexerType.Torznab : IndexerType.Newznab;

        // Parse seedCriteria object if present (Prowlarr sends this for torrent indexers)
        if (prowlarrIndexer.TryGetProperty("seedCriteria", out var seedCriteria))
        {
            if (seedCriteria.TryGetProperty("seedRatio", out var seedRatioValue) && seedRatioValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                indexer.SeedRatio = seedRatioValue.GetDouble();
            else
                indexer.SeedRatio = null;

            if (seedCriteria.TryGetProperty("seedTime", out var seedTimeValue) && seedTimeValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                indexer.SeedTime = seedTimeValue.GetInt32();
            else
                indexer.SeedTime = null;

            if (seedCriteria.TryGetProperty("seasonPackSeedTime", out var seasonValue) && seasonValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                indexer.SeasonPackSeedTime = seasonValue.GetInt32();
            else
                indexer.SeasonPackSeedTime = null;
        }

        var fieldsArray = prowlarrIndexer.GetProperty("fields").EnumerateArray();
        foreach (var field in fieldsArray)
        {
            var fieldName = field.GetProperty("name").GetString();
            if (fieldName == "baseUrl")
                indexer.Url = field.GetProperty("value").GetString() ?? indexer.Url;
            else if (fieldName == "apiKey")
                indexer.ApiKey = field.GetProperty("value").GetString();
            else if (fieldName == "categories" && field.TryGetProperty("value", out var catValue) && catValue.ValueKind == System.Text.Json.JsonValueKind.Array)
                indexer.Categories = catValue.EnumerateArray().Select(c => c.GetInt32().ToString()).ToList();
            else if (fieldName == "minimumSeeders" && field.TryGetProperty("value", out var seedValue))
                indexer.MinimumSeeders = seedValue.GetInt32();
        }

        if (prowlarrIndexer.TryGetProperty("priority", out var priorityProp))
            indexer.Priority = priorityProp.GetInt32();
        if (prowlarrIndexer.TryGetProperty("enableRss", out var rss))
            indexer.EnableRss = rss.GetBoolean();
        if (prowlarrIndexer.TryGetProperty("enableAutomaticSearch", out var autoSearch))
            indexer.EnableAutomaticSearch = autoSearch.GetBoolean();
        if (prowlarrIndexer.TryGetProperty("enableInteractiveSearch", out var intSearch))
            indexer.EnableInteractiveSearch = intSearch.GetBoolean();

        indexer.LastModified = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("[PROWLARR] Updated indexer {Name} (ID: {Id})", indexer.Name, indexer.Id);

        return Results.Ok(new
        {
            id = indexer.Id,
            name = indexer.Name,
            enableRss = indexer.EnableRss,
            enableAutomaticSearch = indexer.EnableAutomaticSearch,
            enableInteractiveSearch = indexer.EnableInteractiveSearch,
            priority = indexer.Priority,
            implementation = indexer.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
            implementationName = indexer.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
            configContract = indexer.Type == IndexerType.Torznab ? "TorznabSettings" : "NewznabSettings",
            protocol = indexer.Type == IndexerType.Torznab ? "torrent" : "usenet",
            supportsRss = indexer.EnableRss,
            supportsSearch = indexer.EnableAutomaticSearch || indexer.EnableInteractiveSearch,
            downloadClientId = indexer.DownloadClientId ?? 0,
            // Prowlarr expects seedCriteria as a top-level object (always present, null values for usenet)
            seedCriteria = new
            {
                seedRatio = indexer.Type == IndexerType.Torznab ? indexer.SeedRatio : (double?)null,
                seedTime = indexer.Type == IndexerType.Torznab ? indexer.SeedTime : (int?)null,
                seasonPackSeedTime = indexer.Type == IndexerType.Torznab ? indexer.SeasonPackSeedTime : (int?)null
            },
            tags = indexer.Tags.ToArray(),
            fields = new object[]
            {
                new { name = "baseUrl", value = indexer.Url },
                new { name = "apiPath", value = "/api" },
                new { name = "apiKey", value = indexer.ApiKey },
                new { name = "categories", value = indexer.Categories.Select(c => int.TryParse(c, out var cat) ? cat : 0).ToArray() },
                new { name = "minimumSeeders", value = indexer.MinimumSeeders }
            },
            // Add capabilities object (required for Prowlarr's BuildRadarrIndexer at line 269)
            capabilities = new
            {
                categories = indexer.Categories.Select(c =>
                {
                    var catId = int.TryParse(c, out var cat) ? cat : 0;
                    return new
                    {
                        id = catId,
                        name = CategoryHelper.GetCategoryName(catId),
                        subCategories = new object[] { }
                    };
                }).ToArray(),
                supportsRawSearch = true,
                searchParams = new[] { "q" },
                tvSearchParams = new[] { "q", "season", "ep" },
                movieSearchParams = new[] { "q", "imdbid" },
                musicSearchParams = new[] { "q" },
                bookSearchParams = new[] { "q" }
            }
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PROWLARR] Error updating indexer");
        return Results.BadRequest(new { error = ex.Message });
    }
});

// DELETE /api/v3/indexer/{id} - Delete indexer (Radarr v3 API for Prowlarr)
app.MapDelete("/api/v3/indexer/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] DELETE /api/v3/indexer/{Id}", id);

    var indexer = await db.Indexers.FindAsync(id);
    if (indexer == null)
        return Results.NotFound();

    db.Indexers.Remove(indexer);
    await db.SaveChangesAsync();

    logger.LogInformation("[PROWLARR] Deleted indexer {Name} (ID: {Id})", indexer.Name, id);

    return Results.Ok(new { });
});

// GET /api/v3/downloadclient - Get download clients (Radarr v3 API for Prowlarr)
// Prowlarr uses this to determine which protocols are supported (torrent vs usenet)
// Returns actual download clients configured by the user
app.MapGet("/api/v3/downloadclient", async (SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogWarning("[PROWLARR] *** GET /api/v3/downloadclient - ENDPOINT WAS CALLED! ***");

    var downloadClients = await db.DownloadClients.ToListAsync();
    logger.LogWarning("[PROWLARR] Found {Count} download clients in database", downloadClients.Count);

    var radarrClients = downloadClients.Select(dc =>
    {
        // Map Sportarr download client type to protocol (torrent vs usenet)
        var protocol = dc.Type switch
        {
            DownloadClientType.QBittorrent => "torrent",
            DownloadClientType.Transmission => "torrent",
            DownloadClientType.Deluge => "torrent",
            DownloadClientType.RTorrent => "torrent",
            DownloadClientType.UTorrent => "torrent",
            DownloadClientType.Sabnzbd => "usenet",
            DownloadClientType.NzbGet => "usenet",
            _ => "torrent"
        };

        // Map type to Radarr implementation name
        var (implementation, implementationName, configContract, infoLink) = dc.Type switch
        {
            DownloadClientType.QBittorrent => ("QBittorrent", "qBittorrent", "QBittorrentSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.Transmission => ("Transmission", "Transmission", "TransmissionSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.Deluge => ("Deluge", "Deluge", "DelugeSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.RTorrent => ("RTorrent", "rTorrent", "RTorrentSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.UTorrent => ("UTorrent", "uTorrent", "UTorrentSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.Sabnzbd => ("Sabnzbd", "SABnzbd", "SabnzbdSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.NzbGet => ("NzbGet", "NZBGet", "NzbGetSettings", "https://github.com/Sportarr/Sportarr"),
            _ => ("QBittorrent", "qBittorrent", "QBittorrentSettings", "https://github.com/Sportarr/Sportarr")
        };

        return new
        {
            enable = dc.Enabled,
            protocol = protocol,
            priority = dc.Priority,
            removeCompletedDownloads = true,
            removeFailedDownloads = true,
            name = dc.Name,
            fields = new object[] { },
            implementationName = implementationName,
            implementation = implementation,
            configContract = configContract,
            infoLink = infoLink,
            tags = new int[] { },
            id = dc.Id
        };
    }).ToList();

    logger.LogInformation("[PROWLARR] Returning {Count} download clients", radarrClients.Count);
    return Results.Ok(radarrClients);
});

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

// Automatically fix download client types on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("[STARTUP] Checking download client types...");
        var clients = await db.DownloadClients.ToListAsync();
        var fixedCount = 0;

        foreach (var client in clients)
        {
            var oldType = client.Type;
            var oldTypeInt = (int)oldType;

            // Log current state for debugging
            logger.LogInformation("[STARTUP] Client: {Name}, Type: {Type} ({TypeInt}), Port: {Port}, HasApiKey: {HasApiKey}",
                client.Name, oldType, oldTypeInt, client.Port, !string.IsNullOrEmpty(client.ApiKey));

            // COMPREHENSIVE FIX: Check multiple indicators to determine correct type
            bool isSabnzbd = false;
            bool isNzbGet = false;

            // Check #1: Name contains "SAB" or "NZB"
            var nameLower = client.Name.ToLower();
            if (nameLower.Contains("sab"))
            {
                isSabnzbd = true;
            }
            else if (nameLower.Contains("nzb"))
            {
                isNzbGet = true;
            }
            // Check #2: Port detection (SABnzbd typically 8080/8090, NZBGet typically 6789)
            else if (client.Port == 6789)
            {
                isNzbGet = true;
            }
            else if (client.Port == 8080 || client.Port == 8090)
            {
                // Probably SABnzbd if it has an API key (SABnzbd uses API key, NZBGet uses username/password)
                if (!string.IsNullOrEmpty(client.ApiKey))
                {
                    isSabnzbd = true;
                }
            }
            // Check #3: If type is currently 4 (UTorrent - which was never available), it's SABnzbd
            else if (oldTypeInt == 4)
            {
                isSabnzbd = true;
            }
            // Check #4: If type is 0-3 (torrent clients) but has API key and port 8080/8090, it's SABnzbd
            else if (oldTypeInt >= 0 && oldTypeInt <= 3 && !string.IsNullOrEmpty(client.ApiKey) && (client.Port == 8080 || client.Port == 8090))
            {
                isSabnzbd = true;
            }

            // Apply the fix
            if (isSabnzbd && client.Type != DownloadClientType.Sabnzbd)
            {
                client.Type = DownloadClientType.Sabnzbd;
                logger.LogWarning("[STARTUP] Fixed {ClientName}: Type {OldType} ({OldTypeInt}) -> Type {NewType} (5) [SABnzbd detected]",
                    client.Name, oldType, oldTypeInt, client.Type);
                fixedCount++;
            }
            else if (isNzbGet && client.Type != DownloadClientType.NzbGet)
            {
                client.Type = DownloadClientType.NzbGet;
                logger.LogWarning("[STARTUP] Fixed {ClientName}: Type {OldType} ({OldTypeInt}) -> Type {NewType} (6) [NZBGet detected]",
                    client.Name, oldType, oldTypeInt, client.Type);
                fixedCount++;
            }
        }

        if (fixedCount > 0)
        {
            await db.SaveChangesAsync();
            logger.LogWarning("[STARTUP] Fixed {Count} download client type(s)", fixedCount);
        }
        else
        {
            logger.LogInformation("[STARTUP] All download client types are correct");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[STARTUP] Error checking download client types: {Message}", ex.Message);
    }
}

try
{
    Log.Information("[Sportarr] Starting web host");
    app.Run();
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

// Make Program class accessible to integration tests
public partial class Program { }
