using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Endpoints;

public static class V1ProwlarrEndpoints
{
    public static IEndpointRouteBuilder MapV1ProwlarrEndpoints(this IEndpointRouteBuilder app, string dataPath)
    {
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

            if (fieldName == "baseUrl") baseUrl = fieldValue?.TrimEnd('/');
            else if (fieldName == "apiKey" || fieldName == "apikey") apiKey = fieldValue;
            else if (fieldName == "categories") categories = fieldValue;
        }

        // DUPLICATE PREVENTION: Check if an indexer with the same baseUrl already exists
        // Prowlarr identifies its indexers by the baseUrl pattern (e.g., http://prowlarr:9696/7/api)
        // Check both with and without trailing slash to handle legacy data
        //
        // ENHANCED: Also check by URL path to handle different hostnames pointing to the same Prowlarr instance
        // e.g., http://192.168.1.5:9696/2/, http://host.docker.internal:9696/2/, http://prowlarr:9696/2/
        // All three have path "/2/" which is the Prowlarr indexer ID - they're the same indexer
        var normalizedBaseUrl = (baseUrl ?? "").TrimEnd('/').ToLowerInvariant();
        var normalizedBaseUrlWithSlash = normalizedBaseUrl + "/";

        // Extract URL path for secondary dedup (handles different hostnames for same Prowlarr instance)
        string? urlPath = null;
        if (Uri.TryCreate(normalizedBaseUrlWithSlash, UriKind.Absolute, out var parsedUri))
        {
            urlPath = parsedUri.AbsolutePath.TrimEnd('/');
        }

        var existingIndexer = await db.Indexers
            .FirstOrDefaultAsync(i => i.Url.ToLower() == normalizedBaseUrl || i.Url.ToLower() == normalizedBaseUrlWithSlash);

        // If no exact URL match, try matching by name + URL path (same Prowlarr indexer via different hostname)
        if (existingIndexer == null && !string.IsNullOrEmpty(urlPath) && urlPath != "")
        {
            var allIndexers = await db.Indexers.ToListAsync();
            existingIndexer = allIndexers.FirstOrDefault(i =>
            {
                if (!Uri.TryCreate(i.Url.TrimEnd('/') + "/", UriKind.Absolute, out var existingUri))
                    return false;
                var existingPath = existingUri.AbsolutePath.TrimEnd('/');
                // Same Prowlarr indexer path AND same indexer name = duplicate via different hostname
                return existingPath == urlPath && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
            });

            if (existingIndexer != null)
            {
                logger.LogInformation("[PROWLARR] Found duplicate indexer by path match: existing URL={ExistingUrl}, new URL={NewUrl}, path={Path}",
                    existingIndexer.Url, baseUrl, urlPath);
            }
        }

        Indexer indexer;
        bool isUpdate = false;

        if (existingIndexer != null && !string.IsNullOrEmpty(baseUrl))
        {
            // Update existing indexer instead of creating a duplicate
            logger.LogInformation("[PROWLARR] Found existing indexer with same baseUrl, updating instead of creating duplicate. BaseUrl: {BaseUrl}, ExistingId: {Id}", baseUrl, existingIndexer.Id);

            existingIndexer.Name = name;
            existingIndexer.Type = implementation.ToLower().Contains("newznab") ? IndexerType.Newznab : IndexerType.Torznab;
            existingIndexer.Url = baseUrl ?? existingIndexer.Url; // Update URL to latest hostname
            existingIndexer.ApiKey = apiKey;
            existingIndexer.Categories = !string.IsNullOrWhiteSpace(categories)
                ? categories.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                : new List<string>();
            existingIndexer.Enabled = enabled;
            existingIndexer.Priority = priority;

            indexer = existingIndexer;
            isUpdate = true;
        }
        else
        {
            // Create new indexer - no duplicate found
            indexer = new Indexer
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
        }

        await db.SaveChangesAsync();

        logger.LogInformation("[PROWLARR] {Action} indexer: {Name} (ID: {Id})", isUpdate ? "Updated existing" : "Created new", name, indexer.Id);
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

        return app;
    }
}
