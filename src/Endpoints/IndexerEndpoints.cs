using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            new { name = "downloadClientId", value = i.DownloadClientId?.ToString() ?? "" },
            new { name = "cookie", value = i.Cookie ?? "" },
            new { name = "allowZeroSize", value = i.RssAllowZeroSize.ToString().ToLowerInvariant() },
            new { name = "failDownloads", value = string.Join(",", i.FailDownloads ?? new List<int>()) }
        },
        tags = i.Tags ?? new List<int>()
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
            Type = ResolveIndexerType(apiIndexer.GetProperty("implementation").GetString()),
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
                        var apiPath = fieldValue ?? "";
                        indexer.ApiPath = string.IsNullOrEmpty(apiPath) ? "" : (apiPath.StartsWith('/') ? apiPath : $"/{apiPath}");
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
                    case "earlyReleaseLimit":
                        // Empty, 0, negative, or non-numeric all persist as null so the
                        // matcher's opt-in check stays off. Only a positive int enables it.
                        indexer.EarlyReleaseLimit = int.TryParse(fieldValue, out var earlyLimit) && earlyLimit > 0
                            ? earlyLimit
                            : null;
                        break;
                    case "cookie":
                        indexer.Cookie = string.IsNullOrWhiteSpace(fieldValue) ? null : fieldValue;
                        break;
                    case "allowZeroSize":
                        indexer.RssAllowZeroSize = string.Equals(fieldValue, "true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "failDownloads":
                        indexer.FailDownloads = string.IsNullOrWhiteSpace(fieldValue)
                            ? new List<int>()
                            : fieldValue.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => int.TryParse(s.Trim(), out var v) ? (int?)v : null)
                                .Where(v => v.HasValue)
                                .Select(v => v!.Value)
                                .ToList();
                        break;
                }
            }
        }

        // Auto-detect the parser variant for plain-RSS feeds so the user
        // doesn't have to fiddle with the ezRSS / enclosure /
        // description-regex switches by hand. Searches against RSS fetch
        // the current feed and filter locally, so the search-enable flags
        // are honored as saved (previously they were forced off here,
        // which read as the checkboxes silently unchecking).
        if (indexer.Type == IndexerType.Rss)
        {
            var detectorService = request.HttpContext.RequestServices.GetRequiredService<IndexerSearchService>();
            var detection = await detectorService.DetectRssSettingsAsync(indexer);
            if (!detection.Success)
            {
                logger.LogWarning("[INDEXER CREATE] RSS auto-detect failed for {Name}: {Reason}", indexer.Name, detection.Message);
                return Results.BadRequest(new { success = false, message = detection.Message });
            }
            logger.LogInformation("[INDEXER CREATE] RSS auto-detect: {Summary}", detection.Message);
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
            indexer.Type = ResolveIndexerType(impl.GetString());
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
                        var updatedApiPath = fieldValue ?? "";
                        indexer.ApiPath = string.IsNullOrEmpty(updatedApiPath) ? "" : (updatedApiPath.StartsWith('/') ? updatedApiPath : $"/{updatedApiPath}");
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
                    case "earlyReleaseLimit":
                        // Empty, 0, negative, or non-numeric all persist as null so the
                        // matcher's opt-in check stays off. Only a positive int enables it.
                        indexer.EarlyReleaseLimit = int.TryParse(fieldValue, out var earlyLimitUpdate) && earlyLimitUpdate > 0
                            ? earlyLimitUpdate
                            : null;
                        break;
                    case "cookie":
                        indexer.Cookie = string.IsNullOrWhiteSpace(fieldValue) ? null : fieldValue;
                        break;
                    case "allowZeroSize":
                        indexer.RssAllowZeroSize = string.Equals(fieldValue, "true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "failDownloads":
                        indexer.FailDownloads = string.IsNullOrWhiteSpace(fieldValue)
                            ? new List<int>()
                            : fieldValue.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => int.TryParse(s.Trim(), out var v) ? (int?)v : null)
                                .Where(v => v.HasValue)
                                .Select(v => v!.Value)
                                .ToList();
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
app.MapPost("/api/indexer/bulk/delete", async (HttpRequest request, SportarrDbContext db, ILogger<Program> logger) =>
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
    ILogger<Program> logger) =>
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

// API: Fetch supported categories for an in-progress indexer config.
// Used by the indexer edit form to render a Sonarr-style multi-select
// of named categories rather than a free-text comma-separated ID box.
// Takes the same payload shape as /api/indexer/test (Prowlarr-style
// fields array) so the frontend can probe before save without
// persisting anything.
app.MapPost("/api/indexer/caps", async (
    HttpRequest request,
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    ILogger<Program> logger) =>
{
    try
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        var apiIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        var implementation = apiIndexer.TryGetProperty("implementation", out var implProp)
            ? implProp.GetString()
            : null;
        var indexerType = ResolveIndexerType(implementation);

        // Caps lookup only makes sense for Newznab/Torznab. Plain RSS
        // feeds don't expose a /caps endpoint.
        if (indexerType != IndexerType.Torznab && indexerType != IndexerType.Newznab)
        {
            return Results.BadRequest(new { success = false, message = "Caps lookup is only supported for Newznab and Torznab indexers." });
        }

        var probe = new Indexer
        {
            Name = apiIndexer.TryGetProperty("name", out var n) ? (n.GetString() ?? "Probe") : "Probe",
            Type = indexerType,
            Url = "",
            ApiKey = ""
        };

        if (apiIndexer.TryGetProperty("fields", out var fields))
        {
            foreach (var field in fields.EnumerateArray())
            {
                var fieldName = field.GetProperty("name").GetString();
                var fieldValue = field.GetProperty("value").GetString();
                switch (fieldName)
                {
                    case "baseUrl":
                        probe.Url = fieldValue?.TrimEnd('/') ?? "";
                        break;
                    case "apiPath":
                        var apiPath = fieldValue ?? "/api";
                        probe.ApiPath = apiPath.StartsWith('/') ? apiPath : $"/{apiPath}";
                        break;
                    case "apiKey":
                        probe.ApiKey = fieldValue;
                        break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(probe.Url))
        {
            return Results.BadRequest(new { success = false, message = "baseUrl is required to fetch categories." });
        }

        // Newznab and Torznab share the same caps XML schema, so we
        // route both through TorznabClient.GetCapabilitiesAsync. Caps
        // lookup doesn't need quality detection, so we omit it.
        var httpClient = httpClientFactory.CreateClient("IndexerClient");
        var torznabLogger = loggerFactory.CreateLogger<TorznabClient>();
        var client = new TorznabClient(httpClient, torznabLogger);

        var caps = await client.GetCapabilitiesAsync(probe);
        if (caps == null)
        {
            return Results.BadRequest(new { success = false, message = "Could not reach the indexer's caps endpoint. Check the URL and API key." });
        }

        var categoryDtos = caps.Categories
            .Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => new { id = c.Id, name = c.Name })
            .ToList();

        logger.LogInformation("[INDEXER CAPS] Fetched {Count} categories from {Url}", categoryDtos.Count, probe.Url);
        return Results.Ok(new { success = true, categories = categoryDtos });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[INDEXER CAPS] Error fetching caps: {Message}", ex.Message);
        return Results.BadRequest(new { success = false, message = $"Failed to fetch categories: {ex.Message}" });
    }
});

// API: Test indexer connection
app.MapPost("/api/indexer/test", async (
    HttpRequest request,
    IndexerSearchService indexerSearchService,
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
            Type = ResolveIndexerType(apiIndexer.GetProperty("implementation").GetString()),
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
                    case "cookie":
                        indexer.Cookie = string.IsNullOrWhiteSpace(fieldValue) ? null : fieldValue;
                        break;
                    case "allowZeroSize":
                        indexer.RssAllowZeroSize = string.Equals(fieldValue, "true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "failDownloads":
                        indexer.FailDownloads = string.IsNullOrWhiteSpace(fieldValue)
                            ? new List<int>()
                            : fieldValue.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => int.TryParse(s.Trim(), out var v) ? (int?)v : null)
                                .Where(v => v.HasValue)
                                .Select(v => v!.Value)
                                .ToList();
                        break;
                }
            }
        }

        logger.LogInformation("[INDEXER TEST] Testing {Type} indexer: {Name} at {Url}{ApiPath}",
            indexer.Type, indexer.Name, indexer.Url, indexer.ApiPath);
        logger.LogInformation("[INDEXER TEST] ApiKey present: {HasApiKey}, Categories: {Categories}",
            !string.IsNullOrEmpty(indexer.ApiKey), string.Join(",", indexer.Categories ?? new List<string>()));

        // Plain-RSS test path is special: instead of a yes/no probe, run
        // the auto-detector. The user gets back a friendly summary of
        // what was discovered ("Detected ezRSS" / "Generic — size from
        // <description>") so they can verify the parser variant before
        // saving.
        if (indexer.Type == IndexerType.Rss)
        {
            var detection = await indexerSearchService.DetectRssSettingsAsync(indexer);
            if (detection.Success)
            {
                logger.LogInformation("[INDEXER TEST] ✓ RSS test succeeded for {Name}: {Summary}", indexer.Name, detection.Message);
                return Results.Ok(new { success = true, message = detection.Message });
            }
            logger.LogWarning("[INDEXER TEST] ✗ RSS test failed for {Name}: {Reason}", indexer.Name, detection.Message);
            return Results.BadRequest(new { success = false, message = detection.Message });
        }

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

    /// <summary>
    /// Map the upstream-style implementation string to our IndexerType
    /// enum. The frontend templates send "Newznab" / "Torznab" / "Rss"
    /// / "TorrentRss" / "Torrent RSS Feed" interchangeably depending on
    /// where the template label was authored, so we tolerate all of them
    /// and default to Torznab on anything unrecognized to preserve
    /// existing behavior.
    /// </summary>
    private static IndexerType ResolveIndexerType(string? implementation)
    {
        var key = implementation?.Trim().ToLowerInvariant() ?? "";
        return key switch
        {
            "newznab" => IndexerType.Newznab,
            "rss" or "torrentrss" or "torrent rss feed" => IndexerType.Rss,
            "broadcasthenet" or "btn" => IndexerType.BroadcasTheNet, // "btn" kept for legacy/import compat
            _ => IndexerType.Torznab,
        };
    }
}
