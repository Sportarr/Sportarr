using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

public static class SonarrIndexerEndpoints
{
    public static IEndpointRouteBuilder MapSonarrIndexerEndpoints(this IEndpointRouteBuilder app)
    {
// POST /api/v3/indexer/test - Test indexer connection (Sonarr v3 API for Prowlarr)
app.MapPost("/api/v3/indexer/test", async (HttpRequest request, ILogger<SonarrIndexerEndpoints> logger) =>
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

// GET /api/v3/indexer/schema - Indexer schema (Sonarr v3 API for Prowlarr)
app.MapGet("/api/v3/indexer/schema", (ILogger<SonarrIndexerEndpoints> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v3/indexer/schema - Prowlarr requesting indexer schema");

    // Return Torznab/Newznab indexer schema matching Sonarr format exactly
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
                    hidden = "false"
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
                    hidden = "false"
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
                    hidden = "false"
                },
                new
                {
                    order = 3,
                    name = "categories",
                    label = "Categories",
                    helpText = "Comma separated list of categories",
                    helpLink = (string?)null,
                    value = new int[] { 5000, 5040, 5045, 5060 },
                    type = "select",
                    selectOptions = new object[] { },
                    advanced = false,
                    hidden = "false"
                },
                new
                {
                    order = 4,
                    name = "animeCategories",
                    label = "Anime Categories",
                    helpText = "Categories to use for Anime (not used by Sportarr)",
                    helpLink = (string?)null,
                    value = new int[] { },
                    type = "select",
                    selectOptions = new object[] { },
                    advanced = true,
                    hidden = "false"
                },
                new
                {
                    order = 5,
                    name = "animeStandardFormatSearch",
                    label = "Anime Standard Format Search",
                    helpText = "Search for anime using standard numbering",
                    helpLink = (string?)null,
                    value = false,
                    type = "checkbox",
                    advanced = true,
                    hidden = "false"
                },
                new
                {
                    order = 6,
                    name = "minimumSeeders",
                    label = "Minimum Seeders",
                    helpText = "Minimum number of seeders required",
                    helpLink = (string?)null,
                    value = 1,
                    type = "number",
                    advanced = true,
                    hidden = "false"
                },
                new
                {
                    order = 7,
                    name = "seedCriteria.seedRatio",
                    label = "Seed Ratio",
                    helpText = "The ratio a torrent should reach before stopping, empty is download client's default",
                    helpLink = (string?)null,
                    value = 1.0,
                    type = "number",
                    advanced = true,
                    hidden = "false"
                },
                new
                {
                    order = 8,
                    name = "seedCriteria.seedTime",
                    label = "Seed Time",
                    helpText = "The time a torrent should be seeded before stopping, empty is download client's default",
                    helpLink = (string?)null,
                    value = 1,
                    type = "number",
                    advanced = true,
                    hidden = "false"
                },
                new
                {
                    order = 9,
                    name = "seedCriteria.seasonPackSeedTime",
                    label = "Season Pack Seed Time",
                    helpText = "The time a season pack torrent should be seeded before stopping, empty is download client's default",
                    helpLink = (string?)null,
                    value = (int?)null,
                    type = "number",
                    advanced = true,
                    hidden = "false"
                },
                new
                {
                    order = 10,
                    name = "rejectBlocklistedTorrentHashesWhileGrabbing",
                    label = "Reject Blocklisted Torrent Hashes While Grabbing",
                    helpText = "If a torrent is blocked, also reject releases with the same torrent hash",
                    helpLink = (string?)null,
                    value = true,
                    type = "checkbox",
                    advanced = true,
                    hidden = "false"
                },
                new
                {
                    order = 11,
                    name = "additionalParameters",
                    label = "Additional Parameters",
                    helpText = "Additional Torznab parameters",
                    helpLink = (string?)null,
                    value = "",
                    type = "textbox",
                    advanced = true,
                    hidden = "false"
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
                    hidden = "false"
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
                    hidden = "false"
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
                    hidden = "false"
                },
                new
                {
                    order = 3,
                    name = "categories",
                    label = "Categories",
                    helpText = "Comma separated list of categories",
                    helpLink = (string?)null,
                    value = new int[] { 5000, 5040, 5045, 5060 },
                    type = "select",
                    selectOptions = new object[] { },
                    advanced = false,
                    hidden = "false"
                },
                new
                {
                    order = 4,
                    name = "animeCategories",
                    label = "Anime Categories",
                    helpText = "Categories to use for Anime (not used by Sportarr)",
                    helpLink = (string?)null,
                    value = new int[] { },
                    type = "select",
                    selectOptions = new object[] { },
                    advanced = true,
                    hidden = "false"
                },
                new
                {
                    order = 5,
                    name = "animeStandardFormatSearch",
                    label = "Anime Standard Format Search",
                    helpText = "Search for anime using standard numbering",
                    helpLink = (string?)null,
                    value = false,
                    type = "checkbox",
                    advanced = true,
                    hidden = "false"
                },
                new
                {
                    order = 6,
                    name = "additionalParameters",
                    label = "Additional Parameters",
                    helpText = "Additional Newznab parameters",
                    helpLink = (string?)null,
                    value = "",
                    type = "textbox",
                    advanced = true,
                    hidden = "false"
                }
            }
        }
    });
});

// GET /api/v3/indexer - List all indexers (Sonarr v3 API for Prowlarr)
app.MapGet("/api/v3/indexer", async (SportarrDbContext db, ILogger<SonarrIndexerEndpoints> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v3/indexer - Prowlarr requesting indexer list");

    var indexers = await db.Indexers.ToListAsync();

    // Convert our indexers to Sonarr v3 format
    var sonarrIndexers = indexers.Select(i =>
    {
        var isTorznab = i.Type == IndexerType.Torznab;
        var fields = new List<object>
        {
            new { order = 0, name = "baseUrl", label = "URL", helpText = isTorznab ? "Torznab feed URL" : "Newznab feed URL", helpLink = (string?)null, value = i.Url, type = "textbox", advanced = false, hidden = "false" },
            new { order = 1, name = "apiPath", label = "API Path", helpText = "Path to the api, usually /api", helpLink = (string?)null, value = "/api", type = "textbox", advanced = true, hidden = "false" },
            new { order = 2, name = "apiKey", label = "API Key", helpText = (string?)null, helpLink = (string?)null, value = i.ApiKey ?? "", type = "textbox", privacy = "apiKey", advanced = false, hidden = "false" },
            new { order = 3, name = "categories", label = "Categories", helpText = "Comma separated list of categories", helpLink = (string?)null, value = i.Categories.Select(c => int.TryParse(c, out var cat) ? cat : 0).ToArray(), type = "select", advanced = false, hidden = "false" },
            // animeCategories and animeStandardFormatSearch required by Prowlarr's Sonarr integration
            new { order = 4, name = "animeCategories", label = "Anime Categories", helpText = "Categories to use for Anime (not used by Sportarr)", helpLink = (string?)null, value = new int[] { }, type = "select", advanced = true, hidden = "false" },
            new { order = 5, name = "animeStandardFormatSearch", label = "Anime Standard Format Search", helpText = "Search for anime using standard numbering", helpLink = (string?)null, value = false, type = "checkbox", advanced = true, hidden = "false" },
            new { order = 6, name = "minimumSeeders", label = "Minimum Seeders", helpText = "Minimum number of seeders required", helpLink = (string?)null, value = i.MinimumSeeders, type = "number", advanced = false, hidden = "false" },
            // Seed criteria fields required by Prowlarr's Sonarr integration (separate from seedCriteria object)
            new { order = 7, name = "seedCriteria.seedRatio", label = "Seed Ratio", helpText = "The ratio a torrent should reach before stopping", helpLink = (string?)null, value = isTorznab ? (double?)(i.SeedRatio ?? 1.0) : null, type = "number", advanced = true, hidden = "false" },
            new { order = 8, name = "seedCriteria.seedTime", label = "Seed Time", helpText = "The time a torrent should be seeded before stopping", helpLink = (string?)null, value = isTorznab ? (int?)(i.SeedTime ?? 1) : null, type = "number", advanced = true, hidden = "false" },
            new { order = 9, name = "seedCriteria.seasonPackSeedTime", label = "Season Pack Seed Time", helpText = "The time a season pack torrent should be seeded", helpLink = (string?)null, value = isTorznab ? (int?)(i.SeasonPackSeedTime ?? 1) : null, type = "number", advanced = true, hidden = "false" },
            new { order = 10, name = "rejectBlocklistedTorrentHashesWhileGrabbing", label = "Reject Blocklisted Torrent Hashes While Grabbing", helpText = "If a torrent is blocked, also reject releases with the same torrent hash", helpLink = (string?)null, value = i.RejectBlocklistedTorrentHashes, type = "checkbox", advanced = true, hidden = "false" },
            new { order = 11, name = "additionalParameters", label = "Additional Parameters", helpText = "Additional Torznab/Newznab parameters", helpLink = (string?)null, value = i.AdditionalParameters ?? "", type = "textbox", advanced = true, hidden = "false" }
        };

        // Add optional fields if present
        var fieldOrder = 12;
        if (i.EarlyReleaseLimit.HasValue)
            fields.Add(new { order = fieldOrder++, name = "earlyReleaseLimit", label = "Early Release Limit", helpText = (string?)null, helpLink = (string?)null, value = i.EarlyReleaseLimit.Value, type = "number", advanced = true, hidden = "false" });

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
                tvSearchParams = new[] { "q", "season", "ep" }
            }
        };
    }).ToList();

    return Results.Ok(sonarrIndexers);
});

// GET /api/v3/indexer/{id} - Get specific indexer (Sonarr v3 API for Prowlarr)
app.MapGet("/api/v3/indexer/{id:int}", async (int id, SportarrDbContext db, ILogger<SonarrIndexerEndpoints> logger) =>
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
            new { order = 0, name = "baseUrl", label = "URL", helpText = indexer.Type == IndexerType.Torznab ? "Torznab feed URL" : "Newznab feed URL", helpLink = (string?)null, value = indexer.Url, type = "textbox", advanced = false, hidden = "false" },
            new { order = 1, name = "apiPath", label = "API Path", helpText = "Path to the api, usually /api", helpLink = (string?)null, value = "/api", type = "textbox", advanced = true, hidden = "false" },
            new { order = 2, name = "apiKey", label = "API Key", helpText = (string?)null, helpLink = (string?)null, value = indexer.ApiKey ?? "", type = "textbox", privacy = "apiKey", advanced = false, hidden = "false" },
            new { order = 3, name = "categories", label = "Categories", helpText = "Comma separated list of categories", helpLink = (string?)null, value = indexer.Categories.Select(c => int.TryParse(c, out var cat) ? cat : 0).ToArray(), type = "select", advanced = false, hidden = "false" },
            // animeCategories and animeStandardFormatSearch required by Prowlarr's Sonarr integration
            new { order = 4, name = "animeCategories", label = "Anime Categories", helpText = "Categories to use for Anime (not used by Sportarr)", helpLink = (string?)null, value = new int[] { }, type = "select", advanced = true, hidden = "false" },
            new { order = 5, name = "animeStandardFormatSearch", label = "Anime Standard Format Search", helpText = "Search for anime using standard numbering", helpLink = (string?)null, value = false, type = "checkbox", advanced = true, hidden = "false" },
            new { order = 6, name = "minimumSeeders", label = "Minimum Seeders", helpText = "Minimum number of seeders required", helpLink = (string?)null, value = indexer.MinimumSeeders, type = "number", advanced = false, hidden = "false" },
            // Seed criteria fields required by Prowlarr's Sonarr integration
            new { order = 7, name = "seedCriteria.seedRatio", label = "Seed Ratio", helpText = "The ratio a torrent should reach before stopping", helpLink = (string?)null, value = indexer.Type == IndexerType.Torznab ? (double?)(indexer.SeedRatio ?? 1.0) : null, type = "number", advanced = true, hidden = "false" },
            new { order = 8, name = "seedCriteria.seedTime", label = "Seed Time", helpText = "The time a torrent should be seeded before stopping", helpLink = (string?)null, value = indexer.Type == IndexerType.Torznab ? (int?)(indexer.SeedTime ?? 1) : null, type = "number", advanced = true, hidden = "false" },
            new { order = 9, name = "seedCriteria.seasonPackSeedTime", label = "Season Pack Seed Time", helpText = "The time a season pack torrent should be seeded", helpLink = (string?)null, value = indexer.Type == IndexerType.Torznab ? (int?)(indexer.SeasonPackSeedTime ?? 1) : null, type = "number", advanced = true, hidden = "false" },
            new { order = 10, name = "rejectBlocklistedTorrentHashesWhileGrabbing", label = "Reject Blocklisted Torrent Hashes While Grabbing", helpText = "If a torrent is blocked, also reject releases with the same torrent hash", helpLink = (string?)null, value = indexer.RejectBlocklistedTorrentHashes, type = "checkbox", advanced = true, hidden = "false" },
            new { order = 11, name = "additionalParameters", label = "Additional Parameters", helpText = "Additional Torznab/Newznab parameters", helpLink = (string?)null, value = indexer.AdditionalParameters ?? "", type = "textbox", advanced = true, hidden = "false" }
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
            tvSearchParams = new[] { "q", "season", "ep" }
        }
    });
});

// POST /api/v3/indexer - Add new indexer (Sonarr v3 API for Prowlarr)
app.MapPost("/api/v3/indexer", async (HttpRequest request, SportarrDbContext db, ILogger<SonarrIndexerEndpoints> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    logger.LogInformation("[PROWLARR] POST /api/v3/indexer - Creating/updating indexer: {Json}", json);

    try
    {
        var prowlarrIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Extract fields from Prowlarr's format
        var name = prowlarrIndexer.GetProperty("name").GetString() ?? "Unknown";
        var implementation = prowlarrIndexer.GetProperty("implementation").GetString() ?? "Newznab";
        var fieldsArray = prowlarrIndexer.GetProperty("fields").EnumerateArray().ToList();

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
                baseUrl = (field.GetProperty("value").GetString() ?? "").TrimEnd('/');  // Normalize URL
            else if (fieldName == "apiKey")
                apiKey = field.GetProperty("value").GetString() ?? "";
            else if (fieldName == "categories" && field.TryGetProperty("value", out var catValue) && catValue.ValueKind == System.Text.Json.JsonValueKind.Array)
                categories = catValue.EnumerateArray().Select(c => c.GetInt32().ToString()).ToList();
            else if (fieldName == "minimumSeeders" && field.TryGetProperty("value", out var seedValue))
                minimumSeeders = seedValue.GetInt32();
            else if (fieldName == "earlyReleaseLimit" && field.TryGetProperty("value", out var earlyValue))
                earlyReleaseLimit = earlyValue.GetInt32();
            // Note: animeCategories is not used by Sportarr (sports only, no anime)
        }

        // DUPLICATE PREVENTION: Check if an indexer with the same baseUrl already exists
        // Prowlarr identifies its indexers by the baseUrl pattern (e.g., http://prowlarr:9696/7/api)
        // The baseUrl contains Prowlarr's URL + indexer ID, making it unique per Prowlarr instance + indexer
        // Check both with and without trailing slash to handle legacy data
        var normalizedBaseUrl = baseUrl.TrimEnd('/').ToLowerInvariant();
        var normalizedBaseUrlWithSlash = normalizedBaseUrl + "/";
        var existingIndexer = await db.Indexers
            .FirstOrDefaultAsync(i => i.Url.ToLower() == normalizedBaseUrl || i.Url.ToLower() == normalizedBaseUrlWithSlash);

        Indexer indexer;
        bool isUpdate = false;

        if (existingIndexer != null)
        {
            // Update existing indexer instead of creating a duplicate
            logger.LogInformation("[PROWLARR] Found existing indexer with same baseUrl, updating instead of creating duplicate. BaseUrl: {BaseUrl}, ExistingId: {Id}", baseUrl, existingIndexer.Id);

            existingIndexer.Name = name;
            existingIndexer.Type = implementation == "Torznab" ? IndexerType.Torznab : IndexerType.Newznab;
            existingIndexer.ApiKey = apiKey;
            existingIndexer.Categories = categories;
            existingIndexer.Enabled = prowlarrIndexer.TryGetProperty("enableRss", out var enableRssProp2) ? enableRssProp2.GetBoolean() : true;
            existingIndexer.EnableRss = prowlarrIndexer.TryGetProperty("enableRss", out var rss2) ? rss2.GetBoolean() : true;
            existingIndexer.EnableAutomaticSearch = prowlarrIndexer.TryGetProperty("enableAutomaticSearch", out var autoSearch2) ? autoSearch2.GetBoolean() : true;
            existingIndexer.EnableInteractiveSearch = prowlarrIndexer.TryGetProperty("enableInteractiveSearch", out var intSearch2) ? intSearch2.GetBoolean() : true;
            existingIndexer.Priority = prowlarrIndexer.TryGetProperty("priority", out var priorityProp2) ? priorityProp2.GetInt32() : 25;
            existingIndexer.MinimumSeeders = minimumSeeders;
            existingIndexer.SeedRatio = seedRatio;
            existingIndexer.SeedTime = seedTime;
            existingIndexer.SeasonPackSeedTime = seasonPackSeedTime;
            existingIndexer.EarlyReleaseLimit = earlyReleaseLimit;
            existingIndexer.Tags = prowlarrIndexer.TryGetProperty("tags", out var tagsProp2) && tagsProp2.ValueKind == System.Text.Json.JsonValueKind.Array
                ? tagsProp2.EnumerateArray().Select(t => t.GetInt32()).ToList()
                : new List<int>();

            indexer = existingIndexer;
            isUpdate = true;
        }
        else
        {
            // Create new indexer - no duplicate found
            indexer = new Indexer
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
                AnimeCategories = null, // Not used by Sportarr (sports only, no anime)
                Tags = prowlarrIndexer.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? tagsProp.EnumerateArray().Select(t => t.GetInt32()).ToList()
                    : new List<int>(),
                Created = DateTime.UtcNow
            };
            db.Indexers.Add(indexer);
        }

        await db.SaveChangesAsync();

        logger.LogInformation("[PROWLARR] {Action} indexer {Name} with ID {Id}", isUpdate ? "Updated existing" : "Created new", indexer.Name, indexer.Id);

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
        // Note: animeCategories is not used by Sportarr (sports only, no anime)
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
            // Add capabilities object (required for Prowlarr's BuildSonarrIndexer)
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
                tvSearchParams = new[] { "q", "season", "ep" }
            }
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PROWLARR] Error creating indexer");
        return Results.BadRequest(new { error = ex.Message });
    }
});

// PUT /api/v3/indexer/{id} - Update indexer (Sonarr v3 API for Prowlarr)
app.MapPut("/api/v3/indexer/{id:int}", async (int id, HttpRequest request, SportarrDbContext db, ILogger<SonarrIndexerEndpoints> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    logger.LogInformation("[PROWLARR] PUT /api/v3/indexer/{Id} - Updating indexer: {Json}", id, json);

    try
    {
        var prowlarrIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Extract baseUrl to identify the unique indexer (contains Prowlarr's indexer ID like /7/ or /1/)
        // Normalize by trimming trailing slash to match stored format
        var fieldsArray = prowlarrIndexer.GetProperty("fields").EnumerateArray();
        var baseUrl = "";
        foreach (var field in fieldsArray)
        {
            if (field.GetProperty("name").GetString() == "baseUrl")
            {
                baseUrl = (field.GetProperty("value").GetString() ?? "").TrimEnd('/');  // Normalize URL
                break;
            }
        }

        if (string.IsNullOrEmpty(baseUrl))
        {
            logger.LogWarning("[PROWLARR] No baseUrl found in PUT request for ID {Id}", id);
            return Results.BadRequest(new { error = "baseUrl is required" });
        }

        // Find indexer by baseUrl (unique identifier) instead of by ID
        // This prevents Prowlarr from overwriting indexers when IDs don't match
        // URLs are normalized (no trailing slash) for consistent matching
        // Check both with and without trailing slash to handle legacy data
        var baseUrlWithSlash = baseUrl + "/";
        var indexer = await db.Indexers.FirstOrDefaultAsync(i => i.Url == baseUrl || i.Url == baseUrlWithSlash);

        if (indexer == null)
        {
            // Indexer doesn't exist yet - create it instead of returning NotFound
            // This handles the case where Prowlarr tries to update before creating
            logger.LogInformation("[PROWLARR] Indexer with baseUrl {BaseUrl} not found, creating new one", baseUrl);

            var name = prowlarrIndexer.GetProperty("name").GetString() ?? "Unknown";
            var implementation = prowlarrIndexer.GetProperty("implementation").GetString() ?? "Newznab";
            var categories = new List<string>();
            var minimumSeeders = 1;
            var apiKey = "";
            double? seedRatio = null;
            int? seedTime = null;
            int? seasonPackSeedTime = null;

            // Parse seedCriteria
            if (prowlarrIndexer.TryGetProperty("seedCriteria", out var seedCriteriaCreate))
            {
                if (seedCriteriaCreate.TryGetProperty("seedRatio", out var seedRatioValue) && seedRatioValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                    seedRatio = seedRatioValue.GetDouble();
                if (seedCriteriaCreate.TryGetProperty("seedTime", out var seedTimeValue) && seedTimeValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                    seedTime = seedTimeValue.GetInt32();
                if (seedCriteriaCreate.TryGetProperty("seasonPackSeedTime", out var seasonValue) && seasonValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                    seasonPackSeedTime = seasonValue.GetInt32();
            }

            // Parse fields
            foreach (var field in fieldsArray)
            {
                var fieldName = field.GetProperty("name").GetString();
                if (fieldName == "apiKey")
                    apiKey = field.GetProperty("value").GetString() ?? "";
                else if (fieldName == "categories" && field.TryGetProperty("value", out var catValue) && catValue.ValueKind == System.Text.Json.JsonValueKind.Array)
                    categories = catValue.EnumerateArray().Select(c => c.GetInt32().ToString()).ToList();
                else if (fieldName == "minimumSeeders" && field.TryGetProperty("value", out var seedValue))
                    minimumSeeders = seedValue.GetInt32();
            }

            indexer = new Indexer
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
                Tags = prowlarrIndexer.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? tagsProp.EnumerateArray().Select(t => t.GetInt32()).ToList()
                    : new List<int>(),
                Created = DateTime.UtcNow
            };

            db.Indexers.Add(indexer);
            await db.SaveChangesAsync();
            logger.LogInformation("[PROWLARR] Created new indexer {Name} (ID: {Id}) via PUT endpoint", indexer.Name, indexer.Id);
        }
        else
        {
            // Update existing indexer
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

            // Update fields
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
        }

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
            // Add capabilities object (required for Prowlarr's BuildSonarrIndexer)
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
                tvSearchParams = new[] { "q", "season", "ep" }
            }
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PROWLARR] Error updating indexer");
        return Results.BadRequest(new { error = ex.Message });
    }
});

// DELETE /api/v3/indexer/{id} - Delete indexer (Sonarr v3 API for Prowlarr)
app.MapDelete("/api/v3/indexer/{id:int}", async (int id, SportarrDbContext db, ILogger<SonarrIndexerEndpoints> logger) =>
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

// DELETE /api/v3/indexer/bulk - Bulk delete indexers
app.MapDelete("/api/v3/indexer/bulk", async (HttpRequest request, SportarrDbContext db, ILogger<SonarrIndexerEndpoints> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    logger.LogInformation("[INDEXER] DELETE /api/v3/indexer/bulk - Request: {Json}", json);

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

// POST /api/v3/indexer/bulk - Bulk delete indexers (alternative endpoint for UI compatibility)
app.MapPost("/api/v3/indexer/bulk/delete", async (HttpRequest request, SportarrDbContext db, ILogger<SonarrIndexerEndpoints> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    logger.LogInformation("[INDEXER] POST /api/v3/indexer/bulk/delete - Request: {Json}", json);

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

        return app;
    }
}
