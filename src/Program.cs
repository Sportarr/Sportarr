using Microsoft.EntityFrameworkCore;
using Fightarr.Api.Data;
using Fightarr.Api.Models;
using Fightarr.Api.Services;
using Fightarr.Api.Middleware;
using Fightarr.Api.Helpers;
using Serilog;
using Serilog.Events;
using System.Text.Json;

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
        path: Path.Combine(logsPath, "fightarr.txt"),
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
var apiKey = builder.Configuration["Fightarr:ApiKey"] ?? Guid.NewGuid().ToString("N");
var dataPath = builder.Configuration["Fightarr:DataPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "data");

try
{
    Directory.CreateDirectory(dataPath);
    Console.WriteLine($"[Fightarr] Data directory: {dataPath}");
}
catch (Exception ex)
{
    Console.WriteLine($"[Fightarr] ERROR: Failed to create data directory: {ex.Message}");
    throw;
}

builder.Configuration["Fightarr:ApiKey"] = apiKey;

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient(); // For calling Fightarr-API
builder.Services.AddControllers() // Add MVC controllers for AuthenticationController
    .AddJsonOptions(options =>
    {
        // Configure enum serialization to use string names instead of integer values
        // This ensures IndexerType.Torznab serializes as "Torznab" not 0
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
// Configure minimal API JSON options as well
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddSingleton<Fightarr.Api.Services.ConfigService>();
builder.Services.AddScoped<Fightarr.Api.Services.UserService>();
builder.Services.AddScoped<Fightarr.Api.Services.AuthenticationService>();
builder.Services.AddScoped<Fightarr.Api.Services.SimpleAuthService>();
builder.Services.AddScoped<Fightarr.Api.Services.SessionService>();
builder.Services.AddScoped<Fightarr.Api.Services.DownloadClientService>();
builder.Services.AddScoped<Fightarr.Api.Services.IndexerSearchService>();
builder.Services.AddScoped<Fightarr.Api.Services.AutomaticSearchService>();
builder.Services.AddScoped<Fightarr.Api.Services.ReleaseEvaluator>();
builder.Services.AddScoped<Fightarr.Api.Services.MediaFileParser>();
builder.Services.AddScoped<Fightarr.Api.Services.FileNamingService>();
builder.Services.AddScoped<Fightarr.Api.Services.FileImportService>();
builder.Services.AddScoped<Fightarr.Api.Services.CustomFormatService>();
builder.Services.AddScoped<Fightarr.Api.Services.HealthCheckService>();
builder.Services.AddScoped<Fightarr.Api.Services.BackupService>();
builder.Services.AddScoped<Fightarr.Api.Services.LibraryImportService>();
builder.Services.AddScoped<Fightarr.Api.Services.ImportListService>();
builder.Services.AddScoped<Fightarr.Api.Services.ImportService>(); // New: Handles completed download imports
builder.Services.AddScoped<Fightarr.Api.Services.FightCardService>(); // New: Manages fight cards within events
builder.Services.AddSingleton<Fightarr.Api.Services.TaskService>();
builder.Services.AddHostedService<Fightarr.Api.Services.DownloadMonitorService>();
builder.Services.AddHostedService<Fightarr.Api.Services.CompletedDownloadHandlingService>(); // New: Monitors for completed downloads

// Configure Fightarr Metadata API client
builder.Services.AddHttpClient<Fightarr.Api.Services.MetadataApiClient>()
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

// Add ASP.NET Core Authentication (Sonarr/Radarr pattern)
Fightarr.Api.Authentication.AuthenticationBuilderExtensions.AddAppAuthentication(builder.Services);

// Configure database
var dbPath = Path.Combine(dataPath, "fightarr.db");
Console.WriteLine($"[Fightarr] Database path: {dbPath}");
builder.Services.AddDbContext<FightarrDbContext>(options =>
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

// Apply migrations automatically
try
{
    Console.WriteLine("[Fightarr] Applying database migrations...");
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();
        db.Database.Migrate();
    }
    Console.WriteLine("[Fightarr] Database migrations completed successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"[Fightarr] ERROR: Database migration failed: {ex.Message}");
    Console.WriteLine($"[Fightarr] Stack trace: {ex.StackTrace}");
    throw;
}

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

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
        release = Fightarr.Api.Version.GetFullVersion(),
        version = Fightarr.Api.Version.GetFullVersion(),
        instanceName = "Fightarr",
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
    Fightarr.Api.Services.SimpleAuthService authService,
    Fightarr.Api.Services.SessionService sessionService,
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
        context.Response.Cookies.Append("FightarrAuth", sessionId, cookieOptions);

        logger.LogInformation("[AUTH LOGIN] Session created from IP: {IP}", ipAddress);

        return Results.Ok(new LoginResponse { Success = true, Token = sessionId, Message = "Login successful" });
    }

    logger.LogWarning("[AUTH LOGIN] Login failed for user: {Username}", request.Username);
    return Results.Unauthorized();
});

app.MapPost("/api/logout", async (
    Fightarr.Api.Services.SessionService sessionService,
    HttpContext context,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[AUTH LOGOUT] Logout requested");

    // Get session ID from cookie
    var sessionId = context.Request.Cookies["FightarrAuth"];
    if (!string.IsNullOrEmpty(sessionId))
    {
        // Delete session from database
        await sessionService.DeleteSessionAsync(sessionId);
    }

    // Delete cookie
    context.Response.Cookies.Delete("FightarrAuth");
    return Results.Ok(new { message = "Logged out successfully" });
});

// NEW SIMPLE FLOW: Check if initial setup is complete
app.MapGet("/api/auth/check", async (
    Fightarr.Api.Services.SimpleAuthService authService,
    Fightarr.Api.Services.SessionService sessionService,
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
        var sessionId = context.Request.Cookies["FightarrAuth"];
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
            context.Response.Cookies.Delete("FightarrAuth");
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
app.MapPost("/api/setup", async (SetupRequest request, Fightarr.Api.Services.SimpleAuthService authService, ILogger<Program> logger) =>
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
        AppName = "Fightarr",
        Version = Fightarr.Api.Version.GetFullVersion(),  // Use full 4-part version (e.g., 4.0.81.140)
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
app.MapGet("/api/system/health", async (Fightarr.Api.Services.HealthCheckService healthCheckService) =>
{
    var healthResults = await healthCheckService.PerformAllChecksAsync();
    return Results.Ok(healthResults);
});

// API: System Backup Management
app.MapGet("/api/system/backup", async (Fightarr.Api.Services.BackupService backupService) =>
{
    var backups = await backupService.GetBackupsAsync();
    return Results.Ok(backups);
});

app.MapPost("/api/system/backup", async (Fightarr.Api.Services.BackupService backupService, string? note) =>
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

app.MapPost("/api/system/backup/restore/{backupName}", async (string backupName, Fightarr.Api.Services.BackupService backupService) =>
{
    try
    {
        await backupService.RestoreBackupAsync(backupName);
        return Results.Ok(new { message = "Backup restored successfully. Please restart Fightarr for changes to take effect." });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapDelete("/api/system/backup/{backupName}", async (string backupName, Fightarr.Api.Services.BackupService backupService) =>
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

app.MapPost("/api/system/backup/cleanup", async (Fightarr.Api.Services.BackupService backupService) =>
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
        var currentVersion = Fightarr.Api.Version.GetFullVersion();

        logger.LogInformation("[UPDATES] Current version: {Version}", currentVersion);

        // Fetch releases from GitHub API
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", $"Fightarr/{currentVersion}");

        var response = await httpClient.GetAsync("https://api.github.com/repos/Fightarr/Fightarr/releases");

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
app.MapGet("/api/system/event", async (FightarrDbContext db, int page = 1, int pageSize = 50, string? type = null, string? category = null) =>
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

app.MapDelete("/api/system/event/{id:int}", async (int id, FightarrDbContext db) =>
{
    var systemEvent = await db.SystemEvents.FindAsync(id);
    if (systemEvent is null) return Results.NotFound();

    db.SystemEvents.Remove(systemEvent);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapPost("/api/system/event/cleanup", async (FightarrDbContext db, int days = 30) =>
{
    var cutoffDate = DateTime.UtcNow.AddDays(-days);
    var oldEvents = db.SystemEvents.Where(e => e.Timestamp < cutoffDate);
    db.SystemEvents.RemoveRange(oldEvents);
    var deleted = await db.SaveChangesAsync();
    return Results.Ok(new { message = $"Deleted {deleted} old system events", deletedCount = deleted });
});

// API: Library Import - Scan filesystem for existing event files
app.MapPost("/api/library/scan", async (Fightarr.Api.Services.LibraryImportService service, string folderPath, bool includeSubfolders = true) =>
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

app.MapPost("/api/library/import", async (Fightarr.Api.Services.LibraryImportService service, List<Fightarr.Api.Services.FileImportRequest> requests) =>
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
app.MapGet("/api/task", async (Fightarr.Api.Services.TaskService taskService, int? pageSize) =>
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
app.MapGet("/api/task/{id:int}", async (int id, Fightarr.Api.Services.TaskService taskService) =>
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
app.MapPost("/api/task", async (Fightarr.Api.Services.TaskService taskService, TaskRequest request) =>
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
app.MapDelete("/api/task/{id:int}", async (int id, Fightarr.Api.Services.TaskService taskService) =>
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
app.MapPost("/api/task/cleanup", async (Fightarr.Api.Services.TaskService taskService, int? keepCount) =>
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

// API: Get all events
app.MapGet("/api/events", async (FightarrDbContext db, FightCardService fightCardService) =>
{
    var events = await db.Events
        .Include(e => e.Fights)
        .Include(e => e.FightCards)
        .OrderByDescending(e => e.EventDate)
        .ToListAsync();

    // Auto-generate fight cards for events that don't have any
    foreach (var evt in events.Where(e => e.FightCards.Count == 0))
    {
        await fightCardService.EnsureFightCardsExistAsync(evt.Id);
    }

    // Reload events with fight cards
    if (events.Any(e => e.FightCards.Count == 0))
    {
        events = await db.Events
            .Include(e => e.Fights)
            .Include(e => e.FightCards)
            .OrderByDescending(e => e.EventDate)
            .ToListAsync();
    }

    // Project to DTOs to break circular references (FightCard.Event -> Event)
    var eventsDto = events.Select(e => new
    {
        e.Id,
        e.Title,
        e.Organization,
        e.EventDate,
        e.Venue,
        e.Location,
        e.Monitored,
        e.HasFile,
        e.FilePath,
        e.Quality,
        e.FileSize,
        e.QualityProfileId,
        e.Images,
        Fights = e.Fights,
        FightCards = e.FightCards.Select(fc => new
        {
            fc.Id,
            fc.EventId,
            fc.CardType,
            fc.CardNumber,
            fc.AirDate,
            fc.Monitored,
            fc.HasFile,
            fc.FilePath,
            fc.Quality,
            fc.FileSize,
            // Explicitly exclude Event navigation property to break circular reference
        }).ToList()
    }).ToList();

    return Results.Ok(eventsDto);
});

// API: Get single event
app.MapGet("/api/events/{id:int}", async (int id, FightarrDbContext db, FightCardService fightCardService) =>
{
    var evt = await db.Events
        .Include(e => e.Fights)
        .Include(e => e.FightCards)
        .FirstOrDefaultAsync(e => e.Id == id);

    if (evt is null) return Results.NotFound();

    // Auto-generate fight cards if they don't exist
    if (evt.FightCards.Count == 0)
    {
        await fightCardService.EnsureFightCardsExistAsync(evt.Id);

        // Reload event with fight cards
        evt = await db.Events
            .Include(e => e.Fights)
            .Include(e => e.FightCards)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    // Project to DTO to break circular references (FightCard.Event -> Event)
    var eventDto = new
    {
        evt.Id,
        evt.Title,
        evt.Organization,
        evt.EventDate,
        evt.Venue,
        evt.Location,
        evt.Monitored,
        evt.HasFile,
        evt.FilePath,
        evt.Quality,
        evt.FileSize,
        evt.QualityProfileId,
        evt.Images,
        Fights = evt.Fights,
        FightCards = evt.FightCards.Select(fc => new
        {
            fc.Id,
            fc.EventId,
            fc.CardType,
            fc.CardNumber,
            fc.AirDate,
            fc.Monitored,
            fc.HasFile,
            fc.FilePath,
            fc.Quality,
            fc.FileSize,
            // Explicitly exclude Event navigation property to break circular reference
        }).ToList()
    };

    return Results.Ok(eventDto);
});

// API: Create event
app.MapPost("/api/events", async (Event evt, FightarrDbContext db, FightCardService fightCardService) =>
{
    // Check if event already exists (by Title + Organization + EventDate)
    var existingEvent = await db.Events
        .Include(e => e.Fights)
        .Include(e => e.FightCards)
        .FirstOrDefaultAsync(e =>
            e.Title == evt.Title &&
            e.Organization == evt.Organization &&
            e.EventDate.Date == evt.EventDate.Date);

    if (existingEvent != null)
    {
        // Event already exists - return it with a 200 OK instead of 409 Conflict
        // This allows the frontend to show "Already Added" status
        var existingEventDto = new
        {
            existingEvent.Id,
            existingEvent.Title,
            existingEvent.Organization,
            existingEvent.EventDate,
            existingEvent.Venue,
            existingEvent.Location,
            existingEvent.Monitored,
            existingEvent.HasFile,
            existingEvent.FilePath,
            existingEvent.Quality,
            existingEvent.FileSize,
            existingEvent.QualityProfileId,
            existingEvent.Images,
            Fights = existingEvent.Fights,
            FightCards = existingEvent.FightCards.Select(fc => new
            {
                fc.Id,
                fc.EventId,
                fc.CardType,
                fc.CardNumber,
                fc.AirDate,
                fc.Monitored,
                fc.HasFile,
                fc.FilePath,
                fc.Quality,
                fc.FileSize,
            }).ToList(),
            AlreadyAdded = true // Flag to indicate this was already in the database
        };

        return Results.Ok(existingEventDto);
    }

    db.Events.Add(evt);
    await db.SaveChangesAsync();

    // Auto-generate fight cards for this event
    await fightCardService.EnsureFightCardsExistAsync(evt.Id);

    // Reload event with fight cards to return complete object
    var createdEvent = await db.Events
        .Include(e => e.Fights)
        .Include(e => e.FightCards)
        .FirstOrDefaultAsync(e => e.Id == evt.Id);

    if (createdEvent is null) return Results.Problem("Failed to create event");

    // Project to DTO to avoid circular reference with FightCard.Event navigation property
    var eventDto = new
    {
        createdEvent.Id,
        createdEvent.Title,
        createdEvent.Organization,
        createdEvent.EventDate,
        createdEvent.Venue,
        createdEvent.Location,
        createdEvent.Monitored,
        createdEvent.HasFile,
        createdEvent.FilePath,
        createdEvent.Quality,
        createdEvent.FileSize,
        createdEvent.QualityProfileId,
        createdEvent.Images,
        Fights = createdEvent.Fights,
        FightCards = createdEvent.FightCards.Select(fc => new
        {
            fc.Id,
            fc.EventId,
            fc.CardType,
            fc.CardNumber,
            fc.AirDate,
            fc.Monitored,
            fc.HasFile,
            fc.FilePath,
            fc.Quality,
            fc.FileSize,
        }).ToList()
    };

    return Results.Created($"/api/events/{evt.Id}", eventDto);
});

// API: Update event
app.MapPut("/api/events/{id:int}", async (int id, Event updatedEvent, FightarrDbContext db, FightCardService fightCardService) =>
{
    var evt = await db.Events.FindAsync(id);
    if (evt is null) return Results.NotFound();

    // Track if monitored status changed
    bool monitoredChanged = evt.Monitored != updatedEvent.Monitored;

    evt.Title = updatedEvent.Title;
    evt.Organization = updatedEvent.Organization;
    evt.EventDate = updatedEvent.EventDate;
    evt.Venue = updatedEvent.Venue;
    evt.Location = updatedEvent.Location;
    evt.Monitored = updatedEvent.Monitored;
    evt.QualityProfileId = updatedEvent.QualityProfileId;
    evt.LastUpdate = DateTime.UtcNow;

    await db.SaveChangesAsync();

    // If event monitoring status changed, update all fight cards to match
    if (monitoredChanged)
    {
        await fightCardService.UpdateFightCardMonitoringAsync(id, updatedEvent.Monitored);
    }

    // Reload with fight cards to get updated state after potential fight card monitoring changes
    evt = await db.Events
        .Include(e => e.Fights)
        .Include(e => e.FightCards)
        .FirstOrDefaultAsync(e => e.Id == id);

    if (evt is null) return Results.NotFound();

    // Project to DTO to avoid circular reference with FightCard.Event navigation property
    var eventDto = new
    {
        evt.Id,
        evt.Title,
        evt.Organization,
        evt.EventDate,
        evt.Venue,
        evt.Location,
        evt.Monitored,
        evt.HasFile,
        evt.FilePath,
        evt.Quality,
        evt.FileSize,
        evt.QualityProfileId,
        evt.Images,
        Fights = evt.Fights,
        FightCards = evt.FightCards.Select(fc => new
        {
            fc.Id,
            fc.EventId,
            fc.CardType,
            fc.CardNumber,
            fc.AirDate,
            fc.Monitored,
            fc.HasFile,
            fc.FilePath,
            fc.Quality,
            fc.FileSize,
        }).ToList()
    };

    return Results.Ok(eventDto);
});

// API: Delete event
app.MapDelete("/api/events/{id:int}", async (int id, FightarrDbContext db) =>
{
    var evt = await db.Events.FindAsync(id);
    if (evt is null) return Results.NotFound();

    db.Events.Remove(evt);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Get fight cards for an event
app.MapGet("/api/events/{eventId:int}/fightcards", async (int eventId, FightarrDbContext db) =>
{
    var fightCards = await db.FightCards
        .Where(fc => fc.EventId == eventId)
        .OrderBy(fc => fc.CardNumber)
        .ToListAsync();
    return Results.Ok(fightCards);
});

// API: Toggle fight card monitoring
app.MapPut("/api/fightcards/{id:int}", async (int id, JsonElement body, FightarrDbContext db) =>
{
    var fightCard = await db.FightCards.FindAsync(id);
    if (fightCard is null) return Results.NotFound();

    // Extract monitored status from request body
    if (body.TryGetProperty("monitored", out var monitoredValue))
    {
        fightCard.Monitored = monitoredValue.GetBoolean();
    }

    await db.SaveChangesAsync();

    // Project to DTO to avoid circular reference with Event navigation property
    var fightCardDto = new
    {
        fightCard.Id,
        fightCard.EventId,
        fightCard.CardType,
        fightCard.CardNumber,
        fightCard.AirDate,
        fightCard.Monitored,
        fightCard.HasFile,
        fightCard.FilePath,
        fightCard.Quality,
        fightCard.FileSize,
    };

    return Results.Ok(fightCardDto);
});

// API: Get organizations (grouped events)
app.MapGet("/api/organizations", async (FightarrDbContext db, FightCardService fightCardService) =>
{
    var events = await db.Events
        .Include(e => e.Fights)
        .Include(e => e.FightCards)
        .OrderByDescending(e => e.EventDate)
        .ToListAsync();

    // Auto-generate fight cards for events that don't have any
    foreach (var evt in events.Where(e => e.FightCards.Count == 0))
    {
        await fightCardService.EnsureFightCardsExistAsync(evt.Id);
    }

    // Reload events with fight cards if any were generated
    if (events.Any(e => e.FightCards.Count == 0))
    {
        events = await db.Events
            .Include(e => e.Fights)
            .Include(e => e.FightCards)
            .OrderByDescending(e => e.EventDate)
            .ToListAsync();
    }

    // Group events by organization
    var organizations = events
        .GroupBy(e => e.Organization)
        .Select(g => new
        {
            Name = g.Key,
            EventCount = g.Count(),
            MonitoredCount = g.Count(e => e.Monitored),
            FileCount = g.Count(e => e.HasFile),
            NextEvent = g.Where(e => e.EventDate >= DateTime.UtcNow)
                         .OrderBy(e => e.EventDate)
                         .Select(e => new { e.Title, e.EventDate })
                         .FirstOrDefault(),
            LatestEvent = g.OrderByDescending(e => e.EventDate)
                          .Select(e => new { e.Id, e.Title, e.EventDate })
                          .First(),
            // Get poster from latest event
            PosterUrl = g.OrderByDescending(e => e.EventDate)
                         .First()
                         .Images
                         .FirstOrDefault()
        })
        .OrderBy(o => o.Name)
        .ToList();

    return Results.Ok(organizations);
});

// API: Get events for a specific organization
app.MapGet("/api/organizations/{name}/events", async (string name, FightarrDbContext db, FightCardService fightCardService) =>
{
    var events = await db.Events
        .Include(e => e.Fights)
        .Include(e => e.FightCards)
        .Where(e => e.Organization == name)
        .OrderByDescending(e => e.EventDate)
        .ToListAsync();

    // Auto-generate fight cards for events that don't have any
    foreach (var evt in events.Where(e => e.FightCards.Count == 0))
    {
        await fightCardService.EnsureFightCardsExistAsync(evt.Id);
    }

    // Reload if fight cards were generated
    if (events.Any(e => e.FightCards.Count == 0))
    {
        events = await db.Events
            .Include(e => e.Fights)
            .Include(e => e.FightCards)
            .Where(e => e.Organization == name)
            .OrderByDescending(e => e.EventDate)
            .ToListAsync();
    }

    // Project to break circular references (FightCard.Event -> Event)
    var eventsDto = events.Select(e => new
    {
        e.Id,
        e.Title,
        e.Organization,
        e.EventDate,
        e.Venue,
        e.Location,
        e.Monitored,
        e.HasFile,
        e.FilePath,
        e.FileSize,
        e.Quality,
        e.QualityProfileId,
        e.Images,
        e.Added,
        e.LastUpdate,
        Fights = e.Fights,
        FightCards = e.FightCards.Select(fc => new
        {
            fc.Id,
            fc.EventId,
            fc.CardType,
            fc.CardNumber,
            fc.Monitored,
            fc.HasFile,
            fc.FilePath,
            fc.FileSize,
            fc.Quality,
            fc.AirDate
        }).ToList()
    }).ToList();

    return Results.Ok(eventsDto);
});

// API: Get tags
app.MapGet("/api/tag", async (FightarrDbContext db) =>
{
    var tags = await db.Tags.ToListAsync();
    return Results.Ok(tags);
});

// API: Get quality profiles
app.MapGet("/api/qualityprofile", async (FightarrDbContext db) =>
{
    var profiles = await db.QualityProfiles.ToListAsync();
    return Results.Ok(profiles);
});

// API: Get single quality profile
app.MapGet("/api/qualityprofile/{id}", async (int id, FightarrDbContext db) =>
{
    var profile = await db.QualityProfiles.FindAsync(id);
    return profile == null ? Results.NotFound() : Results.Ok(profile);
});

// API: Create quality profile
app.MapPost("/api/qualityprofile", async (QualityProfile profile, FightarrDbContext db) =>
{
    db.QualityProfiles.Add(profile);
    await db.SaveChangesAsync();
    return Results.Ok(profile);
});

// API: Update quality profile
app.MapPut("/api/qualityprofile/{id}", async (int id, QualityProfile profile, FightarrDbContext db) =>
{
    var existing = await db.QualityProfiles.FindAsync(id);
    if (existing == null) return Results.NotFound();

    existing.Name = profile.Name;
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
app.MapDelete("/api/qualityprofile/{id}", async (int id, FightarrDbContext db) =>
{
    var profile = await db.QualityProfiles.FindAsync(id);
    if (profile == null) return Results.NotFound();

    db.QualityProfiles.Remove(profile);
    await db.SaveChangesAsync();
    return Results.Ok();
});

// API: Get all custom formats
app.MapGet("/api/customformat", async (FightarrDbContext db) =>
{
    var formats = await db.CustomFormats.ToListAsync();
    return Results.Ok(formats);
});

// API: Get single custom format
app.MapGet("/api/customformat/{id}", async (int id, FightarrDbContext db) =>
{
    var format = await db.CustomFormats.FindAsync(id);
    return format == null ? Results.NotFound() : Results.Ok(format);
});

// API: Create custom format
app.MapPost("/api/customformat", async (CustomFormat format, FightarrDbContext db) =>
{
    format.Created = DateTime.UtcNow;
    db.CustomFormats.Add(format);
    await db.SaveChangesAsync();
    return Results.Ok(format);
});

// API: Update custom format
app.MapPut("/api/customformat/{id}", async (int id, CustomFormat format, FightarrDbContext db) =>
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
app.MapDelete("/api/customformat/{id}", async (int id, FightarrDbContext db) =>
{
    var format = await db.CustomFormats.FindAsync(id);
    if (format == null) return Results.NotFound();

    db.CustomFormats.Remove(format);
    await db.SaveChangesAsync();
    return Results.Ok();
});

// API: Get all delay profiles
app.MapGet("/api/delayprofile", async (FightarrDbContext db) =>
{
    var profiles = await db.DelayProfiles.OrderBy(d => d.Order).ToListAsync();
    return Results.Ok(profiles);
});

// API: Get single delay profile
app.MapGet("/api/delayprofile/{id}", async (int id, FightarrDbContext db) =>
{
    var profile = await db.DelayProfiles.FindAsync(id);
    return profile == null ? Results.NotFound() : Results.Ok(profile);
});

// API: Create delay profile
app.MapPost("/api/delayprofile", async (DelayProfile profile, FightarrDbContext db) =>
{
    profile.Created = DateTime.UtcNow;
    db.DelayProfiles.Add(profile);
    await db.SaveChangesAsync();
    return Results.Ok(profile);
});

// API: Update delay profile
app.MapPut("/api/delayprofile/{id}", async (int id, DelayProfile profile, FightarrDbContext db) =>
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
app.MapDelete("/api/delayprofile/{id}", async (int id, FightarrDbContext db) =>
{
    var profile = await db.DelayProfiles.FindAsync(id);
    if (profile == null) return Results.NotFound();

    db.DelayProfiles.Remove(profile);
    await db.SaveChangesAsync();
    return Results.Ok();
});

// API: Reorder delay profiles
app.MapPut("/api/delayprofile/reorder", async (List<int> profileIds, FightarrDbContext db) =>
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
app.MapGet("/api/releaseprofile", async (FightarrDbContext db) =>
{
    var profiles = await db.ReleaseProfiles.OrderBy(p => p.Name).ToListAsync();
    return Results.Ok(profiles);
});

app.MapGet("/api/releaseprofile/{id:int}", async (int id, FightarrDbContext db) =>
{
    var profile = await db.ReleaseProfiles.FindAsync(id);
    return profile is not null ? Results.Ok(profile) : Results.NotFound();
});

app.MapPost("/api/releaseprofile", async (ReleaseProfile profile, FightarrDbContext db) =>
{
    profile.Created = DateTime.UtcNow;
    db.ReleaseProfiles.Add(profile);
    await db.SaveChangesAsync();
    return Results.Created($"/api/releaseprofile/{profile.Id}", profile);
});

app.MapPut("/api/releaseprofile/{id:int}", async (int id, ReleaseProfile updatedProfile, FightarrDbContext db) =>
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

app.MapDelete("/api/releaseprofile/{id:int}", async (int id, FightarrDbContext db) =>
{
    var profile = await db.ReleaseProfiles.FindAsync(id);
    if (profile is null) return Results.NotFound();

    db.ReleaseProfiles.Remove(profile);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Quality Definitions Management
app.MapGet("/api/qualitydefinition", async (FightarrDbContext db) =>
{
    var definitions = await db.QualityDefinitions.OrderBy(q => q.Quality).ToListAsync();
    return Results.Ok(definitions);
});

app.MapGet("/api/qualitydefinition/{id:int}", async (int id, FightarrDbContext db) =>
{
    var definition = await db.QualityDefinitions.FindAsync(id);
    return definition is not null ? Results.Ok(definition) : Results.NotFound();
});

app.MapPut("/api/qualitydefinition/{id:int}", async (int id, QualityDefinition updatedDef, FightarrDbContext db) =>
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

app.MapPut("/api/qualitydefinition/bulk", async (List<QualityDefinition> definitions, FightarrDbContext db) =>
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
app.MapGet("/api/importlist", async (FightarrDbContext db) =>
{
    var lists = await db.ImportLists.OrderBy(l => l.Name).ToListAsync();
    return Results.Ok(lists);
});

app.MapGet("/api/importlist/{id:int}", async (int id, FightarrDbContext db) =>
{
    var list = await db.ImportLists.FindAsync(id);
    return list is not null ? Results.Ok(list) : Results.NotFound();
});

app.MapPost("/api/importlist", async (ImportList list, FightarrDbContext db) =>
{
    list.Created = DateTime.UtcNow;
    db.ImportLists.Add(list);
    await db.SaveChangesAsync();
    return Results.Created($"/api/importlist/{list.Id}", list);
});

app.MapPut("/api/importlist/{id:int}", async (int id, ImportList updatedList, FightarrDbContext db) =>
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
    list.OrganizationFilter = updatedList.OrganizationFilter;
    list.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(list);
});

app.MapDelete("/api/importlist/{id:int}", async (int id, FightarrDbContext db) =>
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
app.MapGet("/api/metadata", async (FightarrDbContext db) =>
{
    var providers = await db.MetadataProviders.OrderBy(m => m.Name).ToListAsync();
    return Results.Ok(providers);
});

app.MapGet("/api/metadata/{id:int}", async (int id, FightarrDbContext db) =>
{
    var provider = await db.MetadataProviders.FindAsync(id);
    return provider is not null ? Results.Ok(provider) : Results.NotFound();
});

app.MapPost("/api/metadata", async (MetadataProvider provider, FightarrDbContext db) =>
{
    provider.Created = DateTime.UtcNow;
    db.MetadataProviders.Add(provider);
    await db.SaveChangesAsync();
    return Results.Created($"/api/metadata/{provider.Id}", provider);
});

app.MapPut("/api/metadata/{id:int}", async (int id, MetadataProvider provider, FightarrDbContext db) =>
{
    var existing = await db.MetadataProviders.FindAsync(id);
    if (existing is null) return Results.NotFound();

    existing.Name = provider.Name;
    existing.Type = provider.Type;
    existing.Enabled = provider.Enabled;
    existing.EventNfo = provider.EventNfo;
    existing.FightCardNfo = provider.FightCardNfo;
    existing.EventImages = provider.EventImages;
    existing.FighterImages = provider.FighterImages;
    existing.OrganizationLogos = provider.OrganizationLogos;
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

app.MapDelete("/api/metadata/{id:int}", async (int id, FightarrDbContext db) =>
{
    var provider = await db.MetadataProviders.FindAsync(id);
    if (provider is null) return Results.NotFound();

    db.MetadataProviders.Remove(provider);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Tags Management
app.MapPost("/api/tag", async (Tag tag, FightarrDbContext db) =>
{
    db.Tags.Add(tag);
    await db.SaveChangesAsync();
    return Results.Created($"/api/tag/{tag.Id}", tag);
});

app.MapPut("/api/tag/{id:int}", async (int id, Tag updatedTag, FightarrDbContext db) =>
{
    var tag = await db.Tags.FindAsync(id);
    if (tag is null) return Results.NotFound();

    tag.Label = updatedTag.Label;
    tag.Color = updatedTag.Color;
    await db.SaveChangesAsync();
    return Results.Ok(tag);
});

app.MapDelete("/api/tag/{id:int}", async (int id, FightarrDbContext db) =>
{
    var tag = await db.Tags.FindAsync(id);
    if (tag is null) return Results.NotFound();

    db.Tags.Remove(tag);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Root Folders Management
app.MapGet("/api/rootfolder", async (FightarrDbContext db) =>
{
    var folders = await db.RootFolders.ToListAsync();
    return Results.Ok(folders);
});

app.MapPost("/api/rootfolder", async (RootFolder folder, FightarrDbContext db) =>
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

app.MapDelete("/api/rootfolder/{id:int}", async (int id, FightarrDbContext db) =>
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
app.MapGet("/api/notification", async (FightarrDbContext db) =>
{
    var notifications = await db.Notifications.ToListAsync();
    return Results.Ok(notifications);
});

app.MapPost("/api/notification", async (Notification notification, FightarrDbContext db) =>
{
    notification.Created = DateTime.UtcNow;
    notification.LastModified = DateTime.UtcNow;
    db.Notifications.Add(notification);
    await db.SaveChangesAsync();
    return Results.Created($"/api/notification/{notification.Id}", notification);
});

app.MapPut("/api/notification/{id:int}", async (int id, Notification updatedNotification, FightarrDbContext db) =>
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

app.MapDelete("/api/notification/{id:int}", async (int id, FightarrDbContext db) =>
{
    var notification = await db.Notifications.FindAsync(id);
    if (notification is null) return Results.NotFound();

    db.Notifications.Remove(notification);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Settings Management (using config.xml)
app.MapGet("/api/settings", async (Fightarr.Api.Services.ConfigService configService) =>
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
            ShowUnknownOrganizationItems = config.ShowUnknownOrganizationItems,
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

app.MapPut("/api/settings", async (AppSettings updatedSettings, Fightarr.Api.Services.ConfigService configService, Fightarr.Api.Services.SimpleAuthService simpleAuthService, ILogger<Program> logger) =>
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
            config.ShowUnknownOrganizationItems = uiSettings.ShowUnknownOrganizationItems;
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
app.MapPost("/api/settings/apikey/regenerate", async (Fightarr.Api.Services.ConfigService configService, ILogger<Program> logger) =>
{
    logger.LogWarning("[API KEY] API key regeneration requested");
    var newApiKey = await configService.RegenerateApiKeyAsync();
    logger.LogWarning("[API KEY] API key regenerated and saved to config.xml - all connected applications must be updated!");
    return Results.Ok(new { apiKey = newApiKey, message = "API key regenerated successfully. Update all connected applications (Prowlarr, download clients, etc.) with the new key." });
});

// API: Get upcoming events from metadata API
app.MapGet("/api/metadata/events/upcoming", async (
    int page,
    int limit,
    Fightarr.Api.Services.MetadataApiClient metadataApi) =>
{
    var eventsResponse = await metadataApi.GetUpcomingEventsAsync(page, limit);
    return eventsResponse != null ? Results.Ok(eventsResponse) : Results.Ok(new { events = Array.Empty<object>(), pagination = new { page, totalPages = 0, totalEvents = 0, pageSize = limit } });
});

// API: Get event by ID from metadata API
app.MapGet("/api/metadata/events/{id:int}", async (int id, Fightarr.Api.Services.MetadataApiClient metadataApi) =>
{
    var evt = await metadataApi.GetEventByIdAsync(id);
    return evt != null ? Results.Ok(evt) : Results.NotFound();
});

// API: Get event by slug from metadata API
app.MapGet("/api/metadata/events/slug/{slug}", async (string slug, Fightarr.Api.Services.MetadataApiClient metadataApi) =>
{
    var evt = await metadataApi.GetEventBySlugAsync(slug);
    return evt != null ? Results.Ok(evt) : Results.NotFound();
});

// API: Get organizations from metadata API
app.MapGet("/api/metadata/organizations", async (Fightarr.Api.Services.MetadataApiClient metadataApi) =>
{
    var organizations = await metadataApi.GetOrganizationsAsync();
    return organizations != null ? Results.Ok(organizations) : Results.Ok(Array.Empty<object>());
});

// API: Get fighter by ID from metadata API
app.MapGet("/api/metadata/fighters/{id:int}", async (int id, Fightarr.Api.Services.MetadataApiClient metadataApi) =>
{
    var fighter = await metadataApi.GetFighterByIdAsync(id);
    return fighter != null ? Results.Ok(fighter) : Results.NotFound();
});

// API: Check metadata API health
app.MapGet("/api/metadata/health", async (Fightarr.Api.Services.MetadataApiClient metadataApi) =>
{
    var health = await metadataApi.GetHealthAsync();
    return health != null ? Results.Ok(health) : Results.Ok(new { status = "unavailable" });
});

// API: Search for events (connects to Fightarr Metadata API)
app.MapGet("/api/search/events", async (string? q, Fightarr.Api.Services.MetadataApiClient metadataApi, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(q) || q.Length < 3)
    {
        return Results.Ok(Array.Empty<object>());
    }

    try
    {
        // Use MetadataApiClient to search events, fighters, and organizations
        var searchResponse = await metadataApi.GlobalSearchAsync(q);

        var allEvents = new List<Fightarr.Api.Models.Metadata.MetadataEvent>();

        // Add directly matched events
        if (searchResponse?.Events != null && searchResponse.Events.Any())
        {
            logger.LogInformation("[SEARCH] Found {Count} events matching query: {Query}", searchResponse.Events.Count, q);
            allEvents.AddRange(searchResponse.Events);
        }

        // If fighters are found, fetch their events using raw HTTP call
        if (searchResponse?.Fighters != null && searchResponse.Fighters.Any())
        {
            logger.LogInformation("[SEARCH] Found {Count} fighters matching query: {Query}", searchResponse.Fighters.Count, q);

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Fightarr/1.0");

            foreach (var fighter in searchResponse.Fighters)
            {
                try
                {
                    // Fetch full fighter details with fight history using raw HTTP
                    var fighterResponse = await httpClient.GetStringAsync($"https://api.fightarr.net/api/fighters/{fighter.Id}");
                    var fighterJson = System.Text.Json.JsonDocument.Parse(fighterResponse);
                    var root = fighterJson.RootElement;

                    var fighterEventIds = new HashSet<int>();

                    // Extract event IDs from fightsAsFighter1
                    if (root.TryGetProperty("fightsAsFighter1", out var fights1))
                    {
                        foreach (var fight in fights1.EnumerateArray())
                        {
                            if (fight.TryGetProperty("event", out var evt) &&
                                evt.TryGetProperty("id", out var eventId))
                            {
                                fighterEventIds.Add(eventId.GetInt32());
                            }
                        }
                    }

                    // Extract event IDs from fightsAsFighter2
                    if (root.TryGetProperty("fightsAsFighter2", out var fights2))
                    {
                        foreach (var fight in fights2.EnumerateArray())
                        {
                            if (fight.TryGetProperty("event", out var evt) &&
                                evt.TryGetProperty("id", out var eventId))
                            {
                                fighterEventIds.Add(eventId.GetInt32());
                            }
                        }
                    }

                    logger.LogInformation("[SEARCH] Fighter {FighterName} has {Count} unique events", fighter.Name, fighterEventIds.Count);

                    // Fetch full details for each event
                    foreach (var eventId in fighterEventIds)
                    {
                        try
                        {
                            var eventDetails = await metadataApi.GetEventByIdAsync(eventId);
                            if (eventDetails != null && !allEvents.Any(e => e.Id == eventId))
                            {
                                allEvents.Add(eventDetails);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "[SEARCH] Failed to fetch event {EventId}", eventId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[SEARCH] Failed to fetch fighter details for {FighterId}", fighter.Id);
                }
            }

            httpClient.Dispose();
        }

        if (!allEvents.Any())
        {
            return Results.Ok(Array.Empty<object>());
        }

        // Sort by date - most recent first
        var sortedEvents = allEvents
            .Distinct()
            .OrderByDescending(e => e.EventDate)
            .ToList();

        logger.LogInformation("[SEARCH] Returning {Count} total events for query: {Query}", sortedEvents.Count, q);

        // Transform events to match frontend expectations
        var transformedEvents = sortedEvents.Select(e => new
        {
            id = e.Id,
            slug = e.Slug,
            title = e.Title,
            organization = e.Organization?.Name ?? "Unknown",
            eventNumber = e.EventNumber,
            eventDate = e.EventDate.ToString("yyyy-MM-dd"),
            eventType = e.EventType,
            venue = e.Venue,
            location = e.Location,
            broadcaster = e.Broadcaster,
            status = e.Status,
            posterUrl = e.PosterUrl,
            fightCount = e.Count?.Fights ?? e.Fights?.Count ?? 0,
            fights = e.Fights?.Select(f => new
            {
                id = f.Id,
                fighter1 = new
                {
                    id = f.Fighter1.Id,
                    name = f.Fighter1.Name,
                    slug = f.Fighter1.Slug,
                    nickname = f.Fighter1.Nickname,
                    imageUrl = f.Fighter1.ImageUrl
                },
                fighter2 = new
                {
                    id = f.Fighter2.Id,
                    name = f.Fighter2.Name,
                    slug = f.Fighter2.Slug,
                    nickname = f.Fighter2.Nickname,
                    imageUrl = f.Fighter2.ImageUrl
                },
                weightClass = f.WeightClass,
                isTitleFight = f.IsTitleFight,
                isMainEvent = f.IsMainEvent,
                fightOrder = f.FightOrder,
                result = f.Result,
                method = f.Method,
                round = f.Round,
                time = f.Time
            }).ToList()
        }).ToList();

        return Results.Ok(transformedEvents);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[SEARCH] Search API error: {Message}", ex.Message);
        return Results.Ok(Array.Empty<object>());
    }
});

// API: Download Clients Management
app.MapGet("/api/downloadclient", async (FightarrDbContext db) =>
{
    var clients = await db.DownloadClients.OrderBy(dc => dc.Priority).ToListAsync();
    return Results.Ok(clients);
});

app.MapGet("/api/downloadclient/{id:int}", async (int id, FightarrDbContext db) =>
{
    var client = await db.DownloadClients.FindAsync(id);
    return client is null ? Results.NotFound() : Results.Ok(client);
});

app.MapPost("/api/downloadclient", async (DownloadClient client, FightarrDbContext db) =>
{
    client.Created = DateTime.UtcNow;
    db.DownloadClients.Add(client);
    await db.SaveChangesAsync();
    return Results.Created($"/api/downloadclient/{client.Id}", client);
});

app.MapPut("/api/downloadclient/{id:int}", async (int id, DownloadClient updatedClient, FightarrDbContext db) =>
{
    var client = await db.DownloadClients.FindAsync(id);
    if (client is null) return Results.NotFound();

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

    await db.SaveChangesAsync();
    return Results.Ok(client);
});

app.MapDelete("/api/downloadclient/{id:int}", async (int id, FightarrDbContext db) =>
{
    var client = await db.DownloadClients.FindAsync(id);
    if (client is null) return Results.NotFound();

    db.DownloadClients.Remove(client);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Test download client connection - supports all client types
app.MapPost("/api/downloadclient/test", async (DownloadClient client, Fightarr.Api.Services.DownloadClientService downloadClientService) =>
{
    var (success, message) = await downloadClientService.TestConnectionAsync(client);

    if (success)
    {
        return Results.Ok(new { success = true, message });
    }

    return Results.BadRequest(new { success = false, message });
});

// API: Remote Path Mappings (for download client path translation)
app.MapGet("/api/remotepathmapping", async (FightarrDbContext db) =>
{
    var mappings = await db.RemotePathMappings.OrderBy(m => m.Host).ToListAsync();
    return Results.Ok(mappings);
});

app.MapGet("/api/remotepathmapping/{id:int}", async (int id, FightarrDbContext db) =>
{
    var mapping = await db.RemotePathMappings.FindAsync(id);
    return mapping is null ? Results.NotFound() : Results.Ok(mapping);
});

app.MapPost("/api/remotepathmapping", async (RemotePathMapping mapping, FightarrDbContext db) =>
{
    db.RemotePathMappings.Add(mapping);
    await db.SaveChangesAsync();
    return Results.Created($"/api/remotepathmapping/{mapping.Id}", mapping);
});

app.MapPut("/api/remotepathmapping/{id:int}", async (int id, RemotePathMapping updatedMapping, FightarrDbContext db) =>
{
    var mapping = await db.RemotePathMappings.FindAsync(id);
    if (mapping is null) return Results.NotFound();

    mapping.Host = updatedMapping.Host;
    mapping.RemotePath = updatedMapping.RemotePath;
    mapping.LocalPath = updatedMapping.LocalPath;

    await db.SaveChangesAsync();
    return Results.Ok(mapping);
});

app.MapDelete("/api/remotepathmapping/{id:int}", async (int id, FightarrDbContext db) =>
{
    var mapping = await db.RemotePathMappings.FindAsync(id);
    if (mapping is null) return Results.NotFound();

    db.RemotePathMappings.Remove(mapping);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Download Queue Management
app.MapGet("/api/queue", async (FightarrDbContext db) =>
{
    var queue = await db.DownloadQueue
        .Include(dq => dq.Event)
        .Include(dq => dq.DownloadClient)
        .OrderByDescending(dq => dq.Added)
        .ToListAsync();
    return Results.Ok(queue);
});

app.MapGet("/api/queue/{id:int}", async (int id, FightarrDbContext db) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.Event)
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapDelete("/api/queue/{id:int}", async (int id, bool removeFromClient, FightarrDbContext db, Fightarr.Api.Services.DownloadClientService downloadClientService) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound();

    // Remove from download client if requested
    if (removeFromClient && item.DownloadClient != null)
    {
        await downloadClientService.RemoveDownloadAsync(item.DownloadClient, item.DownloadId, deleteFiles: true);
    }

    db.DownloadQueue.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Import History Management
app.MapGet("/api/history", async (FightarrDbContext db, int page = 1, int pageSize = 50) =>
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

app.MapGet("/api/history/{id:int}", async (int id, FightarrDbContext db) =>
{
    var item = await db.ImportHistories
        .Include(h => h.Event)
        .Include(h => h.DownloadQueueItem)
        .FirstOrDefaultAsync(h => h.Id == id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapDelete("/api/history/{id:int}", async (int id, FightarrDbContext db) =>
{
    var item = await db.ImportHistories.FindAsync(id);
    if (item is null) return Results.NotFound();

    db.ImportHistories.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Blocklist Management
app.MapGet("/api/blocklist", async (FightarrDbContext db, int page = 1, int pageSize = 50) =>
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

app.MapGet("/api/blocklist/{id:int}", async (int id, FightarrDbContext db) =>
{
    var item = await db.Blocklist
        .Include(b => b.Event)
        .FirstOrDefaultAsync(b => b.Id == id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapPost("/api/blocklist", async (BlocklistItem item, FightarrDbContext db) =>
{
    item.BlockedAt = DateTime.UtcNow;
    db.Blocklist.Add(item);
    await db.SaveChangesAsync();
    return Results.Created($"/api/blocklist/{item.Id}", item);
});

app.MapDelete("/api/blocklist/{id:int}", async (int id, FightarrDbContext db) =>
{
    var item = await db.Blocklist.FindAsync(id);
    if (item is null) return Results.NotFound();

    db.Blocklist.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Wanted/Missing Events
app.MapGet("/api/wanted/missing", async (int page, int pageSize, FightarrDbContext db) =>
{
    var query = db.Events
        .Include(e => e.Fights)
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

app.MapGet("/api/wanted/cutoff-unmet", async (int page, int pageSize, FightarrDbContext db) =>
{
    // Get all quality profiles to check cutoffs
    var qualityProfiles = await db.QualityProfiles
        .Include(qp => qp.Items)
        .ToListAsync();

    // For now, return events that have files but could be upgraded
    // In a full implementation, this would check against quality profile cutoffs
    var query = db.Events
        .Include(e => e.Fights)
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
app.MapGet("/api/indexer", async (FightarrDbContext db) =>
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

app.MapPost("/api/indexer", async (Indexer indexer, FightarrDbContext db) =>
{
    indexer.Created = DateTime.UtcNow;
    db.Indexers.Add(indexer);
    await db.SaveChangesAsync();
    return Results.Created($"/api/indexer/{indexer.Id}", indexer);
});

app.MapPut("/api/indexer/{id:int}", async (int id, Indexer updatedIndexer, FightarrDbContext db) =>
{
    var indexer = await db.Indexers.FindAsync(id);
    if (indexer is null) return Results.NotFound();

    // Basic fields
    indexer.Name = updatedIndexer.Name;
    indexer.Type = updatedIndexer.Type;
    indexer.Url = updatedIndexer.Url;
    indexer.ApiPath = updatedIndexer.ApiPath;
    indexer.ApiKey = updatedIndexer.ApiKey;
    indexer.Categories = updatedIndexer.Categories;
    indexer.AnimeCategories = updatedIndexer.AnimeCategories;

    // Enable/Disable controls
    indexer.Enabled = updatedIndexer.Enabled;
    indexer.EnableRss = updatedIndexer.EnableRss;
    indexer.EnableAutomaticSearch = updatedIndexer.EnableAutomaticSearch;
    indexer.EnableInteractiveSearch = updatedIndexer.EnableInteractiveSearch;

    // Priority and seeding
    indexer.Priority = updatedIndexer.Priority;
    indexer.MinimumSeeders = updatedIndexer.MinimumSeeders;
    indexer.SeedRatio = updatedIndexer.SeedRatio;
    indexer.SeedTime = updatedIndexer.SeedTime;
    indexer.SeasonPackSeedTime = updatedIndexer.SeasonPackSeedTime;

    // Advanced settings
    indexer.AdditionalParameters = updatedIndexer.AdditionalParameters;
    indexer.MultiLanguages = updatedIndexer.MultiLanguages;
    indexer.RejectBlocklistedTorrentHashes = updatedIndexer.RejectBlocklistedTorrentHashes;
    indexer.EarlyReleaseLimit = updatedIndexer.EarlyReleaseLimit;
    indexer.DownloadClientId = updatedIndexer.DownloadClientId;
    indexer.Tags = updatedIndexer.Tags;

    indexer.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(indexer);
});

app.MapDelete("/api/indexer/{id:int}", async (int id, FightarrDbContext db) =>
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
    Fightarr.Api.Services.IndexerSearchService indexerSearchService,
    FightarrDbContext db) =>
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
app.MapPost("/api/indexer/test", async (Indexer indexer, Fightarr.Api.Services.IndexerSearchService indexerSearchService) =>
{
    var success = await indexerSearchService.TestIndexerAsync(indexer);

    if (success)
    {
        return Results.Ok(new { success = true, message = "Connection successful" });
    }

    return Results.BadRequest(new { success = false, message = "Connection failed" });
});

// API: Manual search for specific event
app.MapPost("/api/event/{eventId:int}/search", async (
    int eventId,
    FightarrDbContext db,
    Fightarr.Api.Services.IndexerSearchService indexerSearchService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[SEARCH] POST /api/event/{EventId}/search - Manual search initiated", eventId);

    var evt = await db.Events
        .Include(e => e.Fights)
        .FirstOrDefaultAsync(e => e.Id == eventId);

    if (evt == null)
    {
        logger.LogWarning("[SEARCH] Event {EventId} not found", eventId);
        return Results.NotFound();
    }

    // Build multiple search queries to try different naming conventions
    var queries = new List<string>();

    // Primary query: Title + Year (most common format)
    queries.Add($"{evt.Title} {evt.EventDate:yyyy}");

    // If title doesn't contain organization, add it
    if (!evt.Title.Contains(evt.Organization, StringComparison.OrdinalIgnoreCase))
    {
        queries.Add($"{evt.Organization} {evt.Title} {evt.EventDate:yyyy}");
    }

    // Just the title alone (for releases that don't include year)
    queries.Add(evt.Title);

    // Try with date formatted differently (some releases use YYYY.MM.DD or YYYY-MM-DD)
    queries.Add($"{evt.Title} {evt.EventDate:yyyy.MM.dd}");
    queries.Add($"{evt.Title} {evt.EventDate:yyyy-MM-dd}");

    // Extract fighter names from title if present (e.g., "UFC Fight Night Lewis vs. Teixeira")
    // Many Fight Night events are named with fighters, but torrents often simplify the name
    var fighterPattern = new System.Text.RegularExpressions.Regex(
        @"(?:UFC\s+Fight\s+Night\s+|Bellator\s+|PFL\s+|ONE\s+Championship\s+)?(.+?\s+vs\.?\s+.+?)(?:\s+\d{4})?$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    var fighterMatch = fighterPattern.Match(evt.Title);
    if (fighterMatch.Success)
    {
        var fightersOnly = fighterMatch.Groups[1].Value.Trim();

        // Just the fighter names (e.g., "Lewis vs Teixeira")
        queries.Add(fightersOnly);
        queries.Add($"{fightersOnly} {evt.EventDate:yyyy}");

        // Organization + fighters (e.g., "UFC Lewis vs Teixeira")
        queries.Add($"{evt.Organization} {fightersOnly}");
        queries.Add($"{evt.Organization} {fightersOnly} {evt.EventDate:yyyy}");

        logger.LogInformation("[SEARCH] Extracted fighter names from title: '{Fighters}'", fightersOnly);
    }

    // Fighter-based searches (many releases include main card fighters)
    // Get main event and co-main event fighters
    var mainEventFights = evt.Fights
        .Where(f => f.IsMainEvent)
        .OrderByDescending(f => f.Id)
        .Take(2) // Main event and co-main
        .ToList();

    if (mainEventFights.Any())
    {
        foreach (var fight in mainEventFights)
        {
            // Format: "Fighter1 vs Fighter2" (most common)
            queries.Add($"{fight.Fighter1} vs {fight.Fighter2}");
            queries.Add($"{fight.Fighter1} {fight.Fighter2}");

            // With organization
            queries.Add($"{evt.Organization} {fight.Fighter1} vs {fight.Fighter2}");

            // With date
            queries.Add($"{fight.Fighter1} vs {fight.Fighter2} {evt.EventDate:yyyy}");
            queries.Add($"{fight.Fighter1} vs {fight.Fighter2} {evt.EventDate:yyyy.MM.dd}");
        }

        logger.LogInformation("[SEARCH] Added {Count} fighter-based queries for main card fights",
            mainEventFights.Count * 5);
    }

    logger.LogInformation("[SEARCH] Event: {Title} | Organization: {Organization} | Date: {Date}",
        evt.Title, evt.Organization, evt.EventDate);
    logger.LogInformation("[SEARCH] Trying {Count} query variations", queries.Count);

    // Get default quality profile for evaluation (matches Sonarr behavior)
    // This provides scoring and rejection reasons but doesn't block manual downloads
    var defaultProfile = await db.QualityProfiles.OrderBy(q => q.Id).FirstOrDefaultAsync();
    var qualityProfileId = defaultProfile?.Id;

    // Search all indexers with each query and combine results
    var allResults = new List<ReleaseSearchResult>();
    var seenGuids = new HashSet<string>();

    foreach (var query in queries)
    {
        logger.LogInformation("[SEARCH] Query: '{Query}'", query);
        var results = await indexerSearchService.SearchAllIndexersAsync(query, 100, qualityProfileId);

        // Deduplicate by GUID to avoid showing same release multiple times
        foreach (var result in results)
        {
            if (!string.IsNullOrEmpty(result.Guid) && !seenGuids.Contains(result.Guid))
            {
                seenGuids.Add(result.Guid);
                allResults.Add(result);
            }
            else if (string.IsNullOrEmpty(result.Guid))
            {
                // If no GUID, add anyway (can't deduplicate)
                allResults.Add(result);
            }
        }

        // If we already found plenty of results, no need to try more queries
        if (allResults.Count >= 50)
        {
            logger.LogInformation("[SEARCH] Found {Count} results, stopping search", allResults.Count);
            break;
        }
    }

    logger.LogInformation("[SEARCH] Search completed. Returning {Count} unique results to UI", allResults.Count);

    return Results.Ok(allResults);
});

// API: Manual search for organization (all monitored events)
app.MapPost("/api/organization/{organizationName}/search", async (
    string organizationName,
    FightarrDbContext db,
    Fightarr.Api.Services.IndexerSearchService indexerSearchService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[SEARCH] POST /api/organization/{OrganizationName}/search - Manual search initiated", organizationName);

    // Get all monitored events for this organization
    var events = await db.Events
        .Include(e => e.Fights)
        .Where(e => e.Organization == organizationName && e.Monitored)
        .OrderByDescending(e => e.EventDate)
        .ToListAsync();

    if (!events.Any())
    {
        logger.LogWarning("[SEARCH] No monitored events found for organization {OrganizationName}", organizationName);
        return Results.Ok(new List<ReleaseSearchResult>());
    }

    logger.LogInformation("[SEARCH] Organization: {OrganizationName} | Found {Count} monitored events",
        organizationName, events.Count);

    // Get default quality profile
    var defaultProfile = await db.QualityProfiles.OrderBy(q => q.Id).FirstOrDefaultAsync();
    var qualityProfileId = defaultProfile?.Id;

    // Search for all events combined
    var allResults = new List<ReleaseSearchResult>();
    var seenGuids = new HashSet<string>();

    foreach (var evt in events)
    {
        // Build search queries for this event
        var queries = new List<string>
        {
            $"{evt.Title} {evt.EventDate:yyyy}",
            evt.Title
        };

        // If title doesn't contain organization, add it
        if (!evt.Title.Contains(evt.Organization, StringComparison.OrdinalIgnoreCase))
        {
            queries.Add($"{evt.Organization} {evt.Title} {evt.EventDate:yyyy}");
        }

        foreach (var query in queries)
        {
            var results = await indexerSearchService.SearchAllIndexersAsync(query, 50, qualityProfileId);

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

            // Limit total results to avoid overwhelming the UI
            if (allResults.Count >= 100)
            {
                logger.LogInformation("[SEARCH] Reached 100 results, stopping search");
                break;
            }
        }

        if (allResults.Count >= 100) break;
    }

    logger.LogInformation("[SEARCH] Organization search completed. Returning {Count} unique results", allResults.Count);
    return Results.Ok(allResults);
});

// API: Manual search for fight card
app.MapPost("/api/fightcard/{fightCardId:int}/search", async (
    int fightCardId,
    FightarrDbContext db,
    Fightarr.Api.Services.IndexerSearchService indexerSearchService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[SEARCH] POST /api/fightcard/{FightCardId}/search - Manual search initiated", fightCardId);

    var fightCard = await db.FightCards
        .Include(fc => fc.Event)
        .ThenInclude(e => e.Fights)
        .FirstOrDefaultAsync(fc => fc.Id == fightCardId);

    if (fightCard == null || fightCard.Event == null)
    {
        logger.LogWarning("[SEARCH] Fight card {FightCardId} not found", fightCardId);
        return Results.NotFound();
    }

    var evt = fightCard.Event;

    // Build search queries specific to this fight card
    var queries = new List<string>();

    // Primary query: Title + Card Type + Year
    queries.Add($"{evt.Title} {fightCard.CardType} {evt.EventDate:yyyy}");

    // Title + Card Type (some releases don't include year)
    queries.Add($"{evt.Title} {fightCard.CardType}");

    // Organization + Title + Card Type
    if (!evt.Title.Contains(evt.Organization, StringComparison.OrdinalIgnoreCase))
    {
        queries.Add($"{evt.Organization} {evt.Title} {fightCard.CardType}");
    }

    // Just the main event title without card type (sometimes full event is released as one file)
    queries.Add($"{evt.Title} {evt.EventDate:yyyy}");
    queries.Add(evt.Title);

    logger.LogInformation("[SEARCH] Fight Card: {Title} - {CardType} | Date: {Date}",
        evt.Title, fightCard.CardType, evt.EventDate);
    logger.LogInformation("[SEARCH] Trying {Count} query variations", queries.Count);

    // Get default quality profile
    var defaultProfile = await db.QualityProfiles.OrderBy(q => q.Id).FirstOrDefaultAsync();
    var qualityProfileId = defaultProfile?.Id;

    // Search all indexers
    var allResults = new List<ReleaseSearchResult>();
    var seenGuids = new HashSet<string>();

    foreach (var query in queries)
    {
        logger.LogInformation("[SEARCH] Query: '{Query}'", query);
        var results = await indexerSearchService.SearchAllIndexersAsync(query, 50, qualityProfileId);

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

        if (allResults.Count >= 50)
        {
            logger.LogInformation("[SEARCH] Found {Count} results, stopping search", allResults.Count);
            break;
        }
    }

    logger.LogInformation("[SEARCH] Fight card search completed. Returning {Count} unique results", allResults.Count);
    return Results.Ok(allResults);
});

// API: Import all events for an organization from metadata API
app.MapPost("/api/organization/import", async (
    HttpContext context,
    FightarrDbContext db,
    MetadataApiClient metadataApiClient,
    FightCardService fightCardService,
    ILogger<Program> logger) =>
{
    var requestBody = await context.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
    if (requestBody == null)
    {
        return Results.BadRequest(new { success = false, message = "Invalid request body" });
    }

    // Extract organization name
    if (!requestBody.TryGetValue("organizationName", out var orgNameElement))
    {
        return Results.BadRequest(new { success = false, message = "Organization name is required" });
    }
    var organizationName = orgNameElement.GetString();
    if (string.IsNullOrEmpty(organizationName))
    {
        return Results.BadRequest(new { success = false, message = "Organization name is required" });
    }

    // Extract quality profile ID (optional)
    int? qualityProfileId = null;
    if (requestBody.TryGetValue("qualityProfileId", out var profileElement) && profileElement.TryGetInt32(out var profileId))
    {
        qualityProfileId = profileId;
    }

    // Extract monitored flag (default true)
    bool monitored = true;
    if (requestBody.TryGetValue("monitored", out var monitoredElement))
    {
        monitored = monitoredElement.GetBoolean();
    }

    // Extract date filter option: "all", "future", or "none" (default "future")
    string dateFilter = "future";
    if (requestBody.TryGetValue("dateFilter", out var dateFilterElement))
    {
        var filter = dateFilterElement.GetString();
        if (!string.IsNullOrEmpty(filter))
        {
            dateFilter = filter.ToLower();
        }
    }

    // Extract card monitor option (optional, for future use)
    string? cardMonitorOption = null;
    if (requestBody.TryGetValue("cardMonitorOption", out var cardMonitorElement))
    {
        cardMonitorOption = cardMonitorElement.GetString();
    }

    // Extract root folder (optional, for future use)
    string? rootFolder = null;
    if (requestBody.TryGetValue("rootFolder", out var rootFolderElement))
    {
        rootFolder = rootFolderElement.GetString();
    }

    // Extract organization folder flag (optional, for future use)
    bool? organizationFolder = null;
    if (requestBody.TryGetValue("organizationFolder", out var orgFolderElement))
    {
        organizationFolder = orgFolderElement.GetBoolean();
    }

    // Extract tags (optional, for future use)
    string[]? tags = null;
    if (requestBody.TryGetValue("tags", out var tagsElement))
    {
        try
        {
            tags = System.Text.Json.JsonSerializer.Deserialize<string[]>(tagsElement.GetRawText());
        }
        catch
        {
            // Ignore if tags can't be parsed
        }
    }

    logger.LogInformation("[IMPORT] Starting bulk import for organization: {OrganizationName} | DateFilter: {DateFilter} | CardMonitor: {CardMonitor}",
        organizationName, dateFilter, cardMonitorOption ?? "all");

    // Determine if we should filter by upcoming events
    bool? upcomingFilter = dateFilter == "future" ? true : (dateFilter == "all" ? (bool?)null : (bool?)null);

    var importedCount = 0;
    var skippedCount = 0;
    var failedCount = 0;
    var page = 1;
    var hasMore = true;

    while (hasMore)
    {
        logger.LogInformation("[IMPORT] Fetching page {Page} from metadata API", page);

        var response = await metadataApiClient.GetEventsAsync(
            page: page,
            limit: 50,
            organization: organizationName,
            upcoming: upcomingFilter,
            includeFights: true
        );

        if (response == null || response.Events == null || !response.Events.Any())
        {
            logger.LogInformation("[IMPORT] No more events found on page {Page}", page);
            break;
        }

        logger.LogInformation("[IMPORT] Processing {Count} events from page {Page}", response.Events.Count, page);

        foreach (var metadataEvent in response.Events)
        {
            try
            {
                // Check if event already exists
                var orgName = metadataEvent.Organization?.Name ?? organizationName;
                var existingEvent = await db.Events
                    .FirstOrDefaultAsync(e =>
                        e.Title == metadataEvent.Title &&
                        e.Organization == orgName &&
                        e.EventDate.Date == metadataEvent.EventDate.Date);

                if (existingEvent != null)
                {
                    logger.LogDebug("[IMPORT] Event already exists: {Title}", metadataEvent.Title);
                    skippedCount++;
                    continue;
                }

                // Create new event
                var newEvent = new Event
                {
                    Title = metadataEvent.Title,
                    Organization = metadataEvent.Organization?.Name ?? organizationName,
                    EventDate = metadataEvent.EventDate,
                    Venue = metadataEvent.Venue,
                    Location = metadataEvent.Location,
                    Monitored = monitored,
                    QualityProfileId = qualityProfileId,
                    Images = !string.IsNullOrEmpty(metadataEvent.PosterUrl)
                        ? new List<string> { metadataEvent.PosterUrl }
                        : new List<string>(),
                    Fights = metadataEvent.Fights?.Select(f => new Fight
                    {
                        Fighter1 = f.Fighter1?.Name ?? "",
                        Fighter2 = f.Fighter2?.Name ?? "",
                        WeightClass = f.WeightClass,
                        IsMainEvent = f.IsMainEvent,
                        Result = f.Result,
                        Method = f.Method,
                        Round = f.Round,
                        Time = f.Time
                    }).ToList() ?? new List<Fight>(),
                    FightCards = new List<FightCard>()
                };

                db.Events.Add(newEvent);
                await db.SaveChangesAsync();

                // Generate fight cards for the new event
                await fightCardService.EnsureFightCardsExistAsync(newEvent.Id);

                logger.LogDebug("[IMPORT] Successfully imported: {Title}", metadataEvent.Title);
                importedCount++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[IMPORT] Failed to import event: {Title}", metadataEvent.Title);
                failedCount++;
            }
        }

        // Check if there are more pages
        if (response.Pagination != null && response.Pagination.Page < response.Pagination.TotalPages)
        {
            page++;
        }
        else
        {
            hasMore = false;
        }
    }

    logger.LogInformation("[IMPORT] Import completed. Imported: {Imported}, Skipped: {Skipped}, Failed: {Failed}",
        importedCount, skippedCount, failedCount);

    return Results.Ok(new
    {
        success = true,
        imported = importedCount,
        skipped = skippedCount,
        failed = failedCount,
        message = $"Successfully imported {importedCount} events. {skippedCount} already existed. {failedCount} failed."
    });
});

// API: Manual grab/download of specific release
app.MapPost("/api/release/grab", async (
    HttpContext context,
    FightarrDbContext db,
    Fightarr.Api.Services.DownloadClientService downloadClientService,
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

    // Get enabled download client
    var downloadClient = await db.DownloadClients
        .Where(dc => dc.Enabled)
        .OrderBy(dc => dc.Priority)
        .FirstOrDefaultAsync();

    if (downloadClient == null)
    {
        logger.LogWarning("[GRAB] No enabled download client configured");
        return Results.BadRequest(new { success = false, message = "No download client configured" });
    }

    logger.LogInformation("[GRAB] Using download client: {ClientName} ({ClientType})",
        downloadClient.Name, downloadClient.Type);

    // NOTE: We do NOT specify download path - download client uses its own configured directory
    // The category is used to track Fightarr downloads and create subdirectories
    // This matches Sonarr/Radarr behavior
    logger.LogInformation("[GRAB] Category: {Category}", downloadClient.Category);

    // Add download to client (category only, no path)
    string? downloadId;
    try
    {
        downloadId = await downloadClientService.AddDownloadAsync(
            downloadClient,
            release.DownloadUrl,
            downloadClient.Category
        );
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[GRAB] Exception adding download to client: {Message}", ex.Message);
        return Results.BadRequest(new
        {
            success = false,
            message = $"Failed to add download to {downloadClient.Name}: {ex.Message}"
        });
    }

    if (downloadId == null)
    {
        logger.LogError("[GRAB] Failed to add download to client (returned null)");
        return Results.BadRequest(new
        {
            success = false,
            message = $"Failed to add download to {downloadClient.Name}. Check download client connection and credentials in Settings > Download Clients."
        });
    }

    logger.LogInformation("[GRAB] Download added successfully with ID: {DownloadId}", downloadId);

    // Track download in database
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
        Progress = 0
    };

    db.DownloadQueue.Add(queueItem);
    await db.SaveChangesAsync();

    logger.LogInformation("[GRAB] Download queued in database with ID: {QueueId}", queueItem.Id);

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
    Fightarr.Api.Services.AutomaticSearchService automaticSearchService) =>
{
    var result = await automaticSearchService.SearchAndDownloadEventAsync(eventId, qualityProfileId);
    return Results.Ok(result);
});

// API: Search all monitored events
app.MapPost("/api/automatic-search/all", async (
    Fightarr.Api.Services.AutomaticSearchService automaticSearchService) =>
{
    var results = await automaticSearchService.SearchAllMonitoredEventsAsync();
    return Results.Ok(results);
});

// ========================================
// PROWLARR INTEGRATION - Sonarr/Radarr-Compatible Application API
// ========================================

// Prowlarr uses /api/v1/indexer to sync indexers to applications
// These endpoints allow Prowlarr to automatically push indexers to Fightarr

// GET /api/v1/indexer - List all indexers (Prowlarr uses this to check existing)
app.MapGet("/api/v1/indexer", async (FightarrDbContext db, ILogger<Program> logger) =>
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
    FightarrDbContext db,
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
    FightarrDbContext db,
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
app.MapDelete("/api/v1/indexer/{id:int}", async (int id, FightarrDbContext db, ILogger<Program> logger) =>
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
        appName = "Fightarr",
        instanceName = "Fightarr",
        version = Fightarr.Api.Version.AppVersion,
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
        appName = "Fightarr",
        instanceName = "Fightarr",
        version = Fightarr.Api.Version.AppVersion,
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
app.MapGet("/api/v3/indexer", async (FightarrDbContext db, ILogger<Program> logger) =>
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
        // Note: animeCategories is a Sonarr-only field, not used in Radarr API (Fightarr uses Radarr template only)

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
app.MapGet("/api/v3/indexer/{id:int}", async (int id, FightarrDbContext db, ILogger<Program> logger) =>
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
app.MapPost("/api/v3/indexer", async (HttpRequest request, FightarrDbContext db, ILogger<Program> logger) =>
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
app.MapPut("/api/v3/indexer/{id:int}", async (int id, HttpRequest request, FightarrDbContext db, ILogger<Program> logger) =>
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
app.MapDelete("/api/v3/indexer/{id:int}", async (int id, FightarrDbContext db, ILogger<Program> logger) =>
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
app.MapGet("/api/v3/downloadclient", async (FightarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogWarning("[PROWLARR] *** GET /api/v3/downloadclient - ENDPOINT WAS CALLED! ***");

    var downloadClients = await db.DownloadClients.ToListAsync();
    logger.LogWarning("[PROWLARR] Found {Count} download clients in database", downloadClients.Count);

    var radarrClients = downloadClients.Select(dc =>
    {
        // Map Fightarr download client type to protocol (torrent vs usenet)
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
            DownloadClientType.QBittorrent => ("QBittorrent", "qBittorrent", "QBittorrentSettings", "https://github.com/Fightarr/Fightarr"),
            DownloadClientType.Transmission => ("Transmission", "Transmission", "TransmissionSettings", "https://github.com/Fightarr/Fightarr"),
            DownloadClientType.Deluge => ("Deluge", "Deluge", "DelugeSettings", "https://github.com/Fightarr/Fightarr"),
            DownloadClientType.RTorrent => ("RTorrent", "rTorrent", "RTorrentSettings", "https://github.com/Fightarr/Fightarr"),
            DownloadClientType.UTorrent => ("UTorrent", "uTorrent", "UTorrentSettings", "https://github.com/Fightarr/Fightarr"),
            DownloadClientType.Sabnzbd => ("Sabnzbd", "SABnzbd", "SabnzbdSettings", "https://github.com/Fightarr/Fightarr"),
            DownloadClientType.NzbGet => ("NzbGet", "NZBGet", "NzbGetSettings", "https://github.com/Fightarr/Fightarr"),
            _ => ("QBittorrent", "qBittorrent", "QBittorrentSettings", "https://github.com/Fightarr/Fightarr")
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
Log.Information("Fightarr is starting...");
Log.Information("App Version: {AppVersion}", Fightarr.Api.Version.GetFullVersion());
Log.Information("API Version: {ApiVersion}", Fightarr.Api.Version.ApiVersion);
Log.Information("Environment: {Environment}", app.Environment.EnvironmentName);
Log.Information("URL: http://localhost:1867");
Log.Information("Logs Directory: {LogsPath}", logsPath);
Log.Information("========================================");

try
{
    Log.Information("[Fightarr] Starting web host");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "[Fightarr] Application terminated unexpectedly");
    throw;
}
finally
{
    Log.Information("[Fightarr] Shutting down...");
    Log.CloseAndFlush();
}
