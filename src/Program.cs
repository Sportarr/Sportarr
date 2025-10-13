using Microsoft.EntityFrameworkCore;
using Fightarr.Api.Data;
using Fightarr.Api.Models;
using Fightarr.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var apiKey = builder.Configuration["Fightarr:ApiKey"] ?? Guid.NewGuid().ToString("N");
var dataPath = builder.Configuration["Fightarr:DataPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
Directory.CreateDirectory(dataPath);

builder.Configuration["Fightarr:ApiKey"] = apiKey;

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure database
var dbPath = Path.Combine(dataPath, "fightarr.db");
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
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();
    db.Database.Migrate();
}

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseApiKeyAuth(); // Custom API key middleware

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
        release = "1.0.0",
        version = "1.0.0",
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

// API: System Status
app.MapGet("/api/system/status", (HttpContext context) =>
{
    var status = new SystemStatus
    {
        AppName = "Fightarr",
        Version = "1.0.0",
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

// API: Get all events (series endpoint for compatibility)
app.MapGet("/api/series", async (FightarrDbContext db) =>
{
    var events = await db.Events
        .Include(e => e.Fights)
        .OrderByDescending(e => e.EventDate)
        .ToListAsync();
    return Results.Ok(events);
});

// API: Get single event
app.MapGet("/api/series/{id:int}", async (int id, FightarrDbContext db) =>
{
    var evt = await db.Events
        .Include(e => e.Fights)
        .FirstOrDefaultAsync(e => e.Id == id);

    return evt is null ? Results.NotFound() : Results.Ok(evt);
});

// API: Create event
app.MapPost("/api/series", async (Event evt, FightarrDbContext db) =>
{
    db.Events.Add(evt);
    await db.SaveChangesAsync();
    return Results.Created($"/api/series/{evt.Id}", evt);
});

// API: Update event
app.MapPut("/api/series/{id:int}", async (int id, Event updatedEvent, FightarrDbContext db) =>
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
app.MapDelete("/api/series/{id:int}", async (int id, FightarrDbContext db) =>
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

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

app.Run();
