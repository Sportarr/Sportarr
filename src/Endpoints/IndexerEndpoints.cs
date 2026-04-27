using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

public static class IndexerEndpoints
{
    public static IEndpointRouteBuilder MapIndexerEndpoints(this IEndpointRouteBuilder app)
    {
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
            new { name = "downloadClientId", value = i.DownloadClientId?.ToString() ?? "" }
        },
        tags = i.Tags ?? new List<int>()
    }).ToList();

    return Results.Ok(transformedIndexers);
});

app.MapPost("/api/indexer", async (HttpRequest request, SportarrDbContext db, ILogger<IndexerEndpoints> logger) =>
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

app.MapPut("/api/indexer/{id:int}", async (int id, HttpRequest request, SportarrDbContext db, ILogger<IndexerEndpoints> logger) =>
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

        // Update tags (explicitly mark as modified to ensure EF Core detects JSON list changes)
        if (apiIndexer.TryGetProperty("tags", out var indexerTags))
        {
            indexer.Tags = System.Text.Json.JsonSerializer.Deserialize<List<int>>(indexerTags.GetRawText()) ?? new();
            db.Entry(indexer).Property(i => i.Tags).IsModified = true;
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

// API: Bulk delete indexers
app.MapPost("/api/indexer/bulk/delete", async (HttpRequest request, SportarrDbContext db, ILogger<IndexerEndpoints> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    logger.LogInformation("[INDEXER] POST /api/indexer/bulk/delete - Request: {Json}", json);

    try
    {
        var bulkRequest = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Parse IDs from request body { "ids": [1, 2, 3] }
        var ids = new List<int>();
        if (bulkRequest.TryGetProperty("ids", out var idsArray) && idsArray.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            ids = idsArray.EnumerateArray().Select(x => x.GetInt32()).ToList();
        }

        if (!ids.Any())
        {
            return Results.BadRequest(new { error = "No indexer IDs provided" });
        }

        // Find all indexers to delete
        var indexersToDelete = await db.Indexers
            .Where(i => ids.Contains(i.Id))
            .ToListAsync();

        if (!indexersToDelete.Any())
        {
            return Results.NotFound(new { error = "No indexers found with the provided IDs" });
        }

        var deletedNames = indexersToDelete.Select(i => i.Name).ToList();
        var deletedCount = indexersToDelete.Count;

        db.Indexers.RemoveRange(indexersToDelete);
        await db.SaveChangesAsync();

        logger.LogInformation("[INDEXER] Bulk deleted {Count} indexers: {Names}", deletedCount, string.Join(", ", deletedNames));

        return Results.Ok(new { deletedCount, deletedIds = ids, deletedNames });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[INDEXER] Error during bulk delete");
        return Results.BadRequest(new { error = ex.Message });
    }
});

// API: Clear all indexer rate limits
app.MapPost("/api/indexer/clearratelimits", async (
    IndexerStatusService indexerStatusService,
    ILogger<IndexerEndpoints> logger) =>
{
    logger.LogInformation("[INDEXER] Clearing all indexer rate limits");
    var clearedCount = await indexerStatusService.ClearAllRateLimitsAsync();
    return Results.Ok(new { success = true, cleared = clearedCount });
});

// API: Release Search (Indexer Integration)
app.MapPost("/api/release/search", async (
    ReleaseSearchRequest request,
    IndexerSearchService indexerSearchService,
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
    IndexerSearchService indexerSearchService,
    ILogger<IndexerEndpoints> logger) =>
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

        return app;
    }
}
