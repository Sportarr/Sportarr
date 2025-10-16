using Microsoft.EntityFrameworkCore;
using Fightarr.Api.Data;
using Fightarr.Api.Models;
using Fightarr.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add console logging for troubleshooting
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

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
builder.Services.AddControllers(); // Add MVC controllers for AuthenticationController
builder.Services.AddScoped<Fightarr.Api.Services.UserService>();
builder.Services.AddScoped<Fightarr.Api.Services.AuthenticationService>();

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

// ASP.NET Core Authentication & Authorization (Sonarr/Radarr pattern)
app.UseAuthentication();
app.UseAuthorization();
app.UseDynamicAuthentication(); // Dynamic scheme selection based on settings

// Map controller routes (for AuthenticationController)
app.MapControllers();

// Configure static files (UI from wwwroot)
app.UseDefaultFiles();
app.UseStaticFiles();

// Initialize endpoint (for frontend)
app.MapGet("/initialize.json", () =>
{
    return Results.Json(new
    {
        apiRoot = "/api",
        apiKey,
        release = Fightarr.Api.Version.AppVersion,
        version = Fightarr.Api.Version.AppVersion,
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
app.MapPost("/api/login", async (LoginRequest request, Fightarr.Api.Services.AuthenticationService authService, HttpContext context) =>
{
    var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var userAgent = context.Request.Headers["User-Agent"].ToString();

    var (success, sessionId, message) = await authService.AuthenticateAsync(
        request.Username,
        request.Password,
        request.RememberMe,
        ipAddress,
        userAgent
    );

    if (success && !string.IsNullOrEmpty(sessionId))
    {
        // Set session cookie
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = false, // Set to true in production with HTTPS
            SameSite = SameSiteMode.Lax,
            Expires = request.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddDays(1)
        };
        context.Response.Cookies.Append("FightarrSession", sessionId, cookieOptions);

        return Results.Ok(new LoginResponse { Success = true, Token = sessionId, Message = "Login successful" });
    }

    return Results.Unauthorized();
});

app.MapPost("/api/logout", async (Fightarr.Api.Services.AuthenticationService authService, HttpContext context) =>
{
    var sessionId = context.Request.Cookies["FightarrSession"];
    if (!string.IsNullOrEmpty(sessionId))
    {
        await authService.LogoutAsync(sessionId);
        context.Response.Cookies.Delete("FightarrSession");
    }
    return Results.Ok(new { message = "Logged out successfully" });
});

app.MapGet("/api/auth/check", async (Fightarr.Api.Services.AuthenticationService authService, HttpContext context, ILogger<Program> logger) =>
{
    var authMethod = await authService.GetAuthenticationMethodAsync();
    var authRequirement = await authService.GetAuthenticationRequirementAsync();
    logger.LogInformation("[AUTH CHECK] Method={Method}, Requirement={Requirement}", authMethod, authRequirement);

    var isAuthRequired = await authService.IsAuthenticationRequiredAsync();
    logger.LogInformation("[AUTH CHECK] IsAuthRequired={IsRequired}", isAuthRequired);

    if (!isAuthRequired)
    {
        logger.LogInformation("[AUTH CHECK] Auth not required, returning authenticated=true, required=false");
        return Results.Ok(new { authenticated = true, required = false });
    }

    var sessionId = context.Request.Cookies["FightarrSession"];
    logger.LogInformation("[AUTH CHECK] Auth required, checking session. HasSession={HasSession}", !string.IsNullOrEmpty(sessionId));

    if (string.IsNullOrEmpty(sessionId))
    {
        logger.LogInformation("[AUTH CHECK] No session found, returning authenticated=false, required=true");
        return Results.Ok(new { authenticated = false, required = true });
    }

    var isValid = await authService.ValidateSessionAsync(sessionId);
    logger.LogInformation("[AUTH CHECK] Session validated. IsValid={IsValid}", isValid);
    return Results.Ok(new { authenticated = isValid, required = true });
});

// API: System Status
app.MapGet("/api/system/status", (HttpContext context) =>
{
    var status = new SystemStatus
    {
        AppName = "Fightarr",
        Version = Fightarr.Api.Version.AppVersion,  // Use AppVersion for user-facing version display
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

// API: Get all events
app.MapGet("/api/events", async (FightarrDbContext db) =>
{
    var events = await db.Events
        .Include(e => e.Fights)
        .OrderByDescending(e => e.EventDate)
        .ToListAsync();
    return Results.Ok(events);
});

// API: Get single event
app.MapGet("/api/events/{id:int}", async (int id, FightarrDbContext db) =>
{
    var evt = await db.Events
        .Include(e => e.Fights)
        .FirstOrDefaultAsync(e => e.Id == id);

    return evt is null ? Results.NotFound() : Results.Ok(evt);
});

// API: Create event
app.MapPost("/api/events", async (Event evt, FightarrDbContext db) =>
{
    db.Events.Add(evt);
    await db.SaveChangesAsync();
    return Results.Created($"/api/events/{evt.Id}", evt);
});

// API: Update event
app.MapPut("/api/events/{id:int}", async (int id, Event updatedEvent, FightarrDbContext db) =>
{
    var evt = await db.Events.FindAsync(id);
    if (evt is null) return Results.NotFound();

    evt.Title = updatedEvent.Title;
    evt.Organization = updatedEvent.Organization;
    evt.EventDate = updatedEvent.EventDate;
    evt.Venue = updatedEvent.Venue;
    evt.Location = updatedEvent.Location;
    evt.Monitored = updatedEvent.Monitored;
    evt.LastUpdate = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(evt);
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

// API: Settings Management
app.MapGet("/api/settings", async (FightarrDbContext db) =>
{
    var settings = await db.AppSettings.FirstOrDefaultAsync();
    if (settings is null)
    {
        // Create default settings
        settings = new AppSettings();
        db.AppSettings.Add(settings);
        await db.SaveChangesAsync();
    }
    return Results.Ok(settings);
});

app.MapPut("/api/settings", async (AppSettings updatedSettings, FightarrDbContext db, Fightarr.Api.Services.UserService userService, ILogger<Program> logger) =>
{
    logger.LogInformation("[AUTH] Settings update requested");

    var settings = await db.AppSettings.FirstOrDefaultAsync();
    if (settings is null)
    {
        logger.LogInformation("[AUTH] No existing settings, creating new record");
        updatedSettings.Id = 1;
        db.AppSettings.Add(updatedSettings);
    }
    else
    {
        logger.LogInformation("[AUTH] Updating existing settings");
        settings.HostSettings = updatedSettings.HostSettings;
        settings.SecuritySettings = updatedSettings.SecuritySettings;
        settings.ProxySettings = updatedSettings.ProxySettings;
        settings.LoggingSettings = updatedSettings.LoggingSettings;
        settings.AnalyticsSettings = updatedSettings.AnalyticsSettings;
        settings.BackupSettings = updatedSettings.BackupSettings;
        settings.UpdateSettings = updatedSettings.UpdateSettings;
        settings.UISettings = updatedSettings.UISettings;
        settings.MediaManagementSettings = updatedSettings.MediaManagementSettings;
        settings.LastModified = DateTime.UtcNow;
    }

    // Handle user creation/update when authentication is enabled
    try
    {
        var securitySettings = System.Text.Json.JsonSerializer.Deserialize<Fightarr.Api.Models.SecuritySettings>(
            updatedSettings.SecuritySettings);

        logger.LogInformation("[AUTH] SecuritySettings parsed: AuthMethod={AuthMethod}, AuthRequired={AuthRequired}, HasUsername={HasUsername}, HasPassword={HasPassword}",
            securitySettings?.AuthenticationMethod ?? "null",
            securitySettings?.AuthenticationRequired ?? "null",
            !string.IsNullOrWhiteSpace(securitySettings?.Username),
            !string.IsNullOrWhiteSpace(securitySettings?.Password));

        if (securitySettings != null && securitySettings.AuthenticationMethod != "none")
        {
            logger.LogInformation("[AUTH] Authentication is enabled with method: {Method}", securitySettings.AuthenticationMethod);

            // Check if username and password are provided in the SecuritySettings
            // This is a migration path - we'll create/update the user, then remove credentials from SecuritySettings
            if (!string.IsNullOrWhiteSpace(securitySettings.Username) &&
                !string.IsNullOrWhiteSpace(securitySettings.Password))
            {
                logger.LogInformation("[AUTH] Creating/updating user: {Username}", securitySettings.Username);

                // Create or update user with hashed password
                await userService.UpsertUserAsync(securitySettings.Username, securitySettings.Password);
                logger.LogInformation("[AUTH] User created/updated successfully");

                // Clear the plaintext credentials from SecuritySettings
                securitySettings.Username = "";
                securitySettings.Password = "";

                // Update the SecuritySettings JSON without the credentials
                var updatedSecurityJson = System.Text.Json.JsonSerializer.Serialize(securitySettings);
                if (settings != null)
                {
                    settings.SecuritySettings = updatedSecurityJson;
                }
                else
                {
                    updatedSettings.SecuritySettings = updatedSecurityJson;
                }
                logger.LogInformation("[AUTH] Credentials cleared from SecuritySettings");
            }
            else
            {
                logger.LogWarning("[AUTH] Authentication enabled but no username/password provided");
            }
        }
        else
        {
            logger.LogInformation("[AUTH] Authentication is disabled");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[AUTH] Error creating/updating user: {Message}", ex.Message);
        // Continue even if user creation fails - settings will still be saved
    }

    await db.SaveChangesAsync();
    logger.LogInformation("[AUTH] Settings saved to database successfully");

    return Results.Ok(settings ?? updatedSettings);
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
app.MapGet("/api/search/events", async (string? q, Fightarr.Api.Services.MetadataApiClient metadataApi) =>
{
    if (string.IsNullOrWhiteSpace(q) || q.Length < 3)
    {
        return Results.Ok(Array.Empty<object>());
    }

    try
    {
        // Use MetadataApiClient to search events
        var searchResponse = await metadataApi.GlobalSearchAsync(q);

        if (searchResponse?.Events == null || !searchResponse.Events.Any())
        {
            return Results.Ok(Array.Empty<object>());
        }

        // Transform events to match frontend expectations
        var transformedEvents = searchResponse.Events.Select(e => new
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
        Console.WriteLine($"[Fightarr] Search API error: {ex.Message}");
        return Results.Ok(Array.Empty<object>());
    }
});

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

Console.WriteLine("[Fightarr] ========================================");
Console.WriteLine("[Fightarr] Fightarr is starting...");
Console.WriteLine($"[Fightarr] App Version: {Fightarr.Api.Version.AppVersion}");
Console.WriteLine($"[Fightarr] API Version: {Fightarr.Api.Version.ApiVersion}");
Console.WriteLine($"[Fightarr] Environment: {app.Environment.EnvironmentName}");
Console.WriteLine($"[Fightarr] URL: http://localhost:1867");
Console.WriteLine("[Fightarr] ========================================");

app.Run();

Console.WriteLine("[Fightarr] Shutting down...");
