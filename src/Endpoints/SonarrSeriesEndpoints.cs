using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

public static class SonarrSeriesEndpoints
{
    public static IEndpointRouteBuilder MapSonarrSeriesEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/v3/series - Get series list (Sonarr v3 API for Decypharr/Maintainerr)
        // Supports ?tvdbId={id} query parameter for lookup by Sportarr API external ID
        app.MapGet("/api/v3/series", async (SportarrDbContext db, ILogger<Program> logger, int? tvdbId) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/series - tvdbId={TvdbId}", tvdbId);

            IQueryable<League> query = db.Leagues;

            if (tvdbId.HasValue)
            {
                // Reverse the numeric alias to the ExternalId form(s) it
                // can represent: lg-XXXXXX for alias-range values, the raw
                // TheSportsDB id for legacy pre-flip rows.
                var candidates = Helpers.NumericIdAlias.LeagueExternalIdCandidates(tvdbId.Value);
                query = candidates.Count > 0
                    ? query.Where(l => l.ExternalId != null && candidates.Contains(l.ExternalId))
                    : query.Where(l => false);
            }

            var leagues = await query.ToListAsync();

            var leagueIds = leagues.Select(l => l.Id).ToList();
            var stats = await db.Events
                .Where(e => e.LeagueId.HasValue && leagueIds.Contains(e.LeagueId.Value))
                .GroupBy(e => e.LeagueId)
                .Select(g => new
                {
                    LeagueId = g.Key,
                    EventCount = g.Count(),
                    FileCount = g.Sum(e => e.HasFile ? 1 : 0),
                    SizeOnDisk = g.Sum(e => e.FileSize ?? 0L)
                })
                .ToDictionaryAsync(x => x.LeagueId ?? 0);

            // Real per-season entries: consumers like Maintainerr read the
            // seasons array to decide what to unmonitor, so a single
            // current-year placeholder breaks their season handling.
            var seasonRows = await db.Events
                .Where(e => e.LeagueId.HasValue && leagueIds.Contains(e.LeagueId.Value) && e.SeasonNumber.HasValue)
                .GroupBy(e => new { e.LeagueId, e.SeasonNumber })
                .Select(g => new
                {
                    LeagueId = g.Key.LeagueId!.Value,
                    SeasonNumber = g.Key.SeasonNumber!.Value,
                    Monitored = g.Any(e => e.Monitored)
                })
                .ToListAsync();
            var seasonsByLeague = seasonRows
                .GroupBy(s => s.LeagueId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(s => s.SeasonNumber)
                          .Select(s => (object)new { seasonNumber = s.SeasonNumber, monitored = s.Monitored })
                          .ToArray());

            var rootFolder = await db.RootFolders.FirstOrDefaultAsync();
            var rootPath = rootFolder?.Path ?? "/data";

            var series = leagues.Select(league =>
            {
                var stat = stats.GetValueOrDefault(league.Id);
                var leaguePath = Path.Combine(rootPath, league.Name.Replace(" ", "-"));
                var externalId = Helpers.NumericIdAlias.FromExternalId(league.ExternalId);

                var images = new List<object>();
                if (!string.IsNullOrEmpty(league.LogoUrl))
                    images.Add(new { coverType = "poster", url = league.LogoUrl });
                if (!string.IsNullOrEmpty(league.BannerUrl))
                    images.Add(new { coverType = "banner", url = league.BannerUrl });
                if (!string.IsNullOrEmpty(league.PosterUrl))
                    images.Add(new { coverType = "fanart", url = league.PosterUrl });

                return new
                {
                    id = league.Id,
                    title = league.Name,
                    sortTitle = league.Name.ToLowerInvariant(),
                    status = "continuing",
                    overview = league.Description ?? $"Sports events from {league.Name}",
                    network = "",
                    images = images.ToArray(),
                    seasons = seasonsByLeague.TryGetValue(league.Id, out var leagueSeasons)
                        ? leagueSeasons
                        : new object[] { new { seasonNumber = DateTime.Now.Year, monitored = league.Monitored } },
                    year = DateTime.Now.Year,
                    path = leaguePath,
                    qualityProfileId = league.QualityProfileId ?? 1,
                    languageProfileId = 1,
                    seasonFolder = true,
                    monitored = league.Monitored,
                    useSceneNumbering = false,
                    runtime = 0,
                    tvdbId = externalId,
                    tvRageId = 0,
                    tvMazeId = 0,
                    seriesType = "standard",
                    cleanTitle = league.Name.ToLowerInvariant().Replace(" ", ""),
                    titleSlug = league.Name.ToLowerInvariant().Replace(" ", "-"),
                    genres = new[] { "Sports" },
                    // Bazarr indexes alternateTitles and tags directly
                    // (KeyError on absence), so both must always be present.
                    alternateTitles = Array.Empty<object>(),
                    tags = league.Tags.ToArray(),
                    added = league.Added.ToString("o"),
                    ratings = new { votes = 0, value = 0.0 },
                    statistics = new
                    {
                        episodeCount = stat?.EventCount ?? 0,
                        episodeFileCount = stat?.FileCount ?? 0,
                        sizeOnDisk = stat?.SizeOnDisk ?? 0L
                    }
                };
            }).ToList();

            logger.LogInformation("[V3-COMPAT] Returning {SeriesCount} series", series.Count);
            return Results.Ok(series);
        });

        // GET /api/v3/series/{id} - Get specific series by ID (Maintainerr compatibility)
        app.MapGet("/api/v3/series/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/series/{Id}", id);

            var league = await db.Leagues.FindAsync(id);
            if (league == null)
            {
                return Results.NotFound(new { message = "Series not found" });
            }

            var stats = await db.Events
                .Where(e => e.LeagueId == id)
                .GroupBy(e => 1)
                .Select(g => new
                {
                    EventCount = g.Count(),
                    FileCount = g.Sum(e => e.HasFile ? 1 : 0),
                    SizeOnDisk = g.Sum(e => e.FileSize ?? 0L)
                })
                .FirstOrDefaultAsync();

            var seasonEntries = await db.Events
                .Where(e => e.LeagueId == id && e.SeasonNumber.HasValue)
                .GroupBy(e => e.SeasonNumber)
                .Select(g => new
                {
                    SeasonNumber = g.Key!.Value,
                    Monitored = g.Any(e => e.Monitored)
                })
                .OrderBy(s => s.SeasonNumber)
                .ToListAsync();

            var rootFolder = await db.RootFolders.FirstOrDefaultAsync();
            var leaguePath = Path.Combine(rootFolder?.Path ?? "/data", league.Name.Replace(" ", "-"));
            var externalId = Helpers.NumericIdAlias.FromExternalId(league.ExternalId);

            var images = new List<object>();
            if (!string.IsNullOrEmpty(league.LogoUrl))
                images.Add(new { coverType = "poster", url = league.LogoUrl });
            if (!string.IsNullOrEmpty(league.BannerUrl))
                images.Add(new { coverType = "banner", url = league.BannerUrl });
            if (!string.IsNullOrEmpty(league.PosterUrl))
                images.Add(new { coverType = "fanart", url = league.PosterUrl });

            var series = new
            {
                id = league.Id,
                title = league.Name,
                sortTitle = league.Name.ToLowerInvariant(),
                status = "continuing",
                overview = league.Description ?? $"Sports events from {league.Name}",
                network = "",
                images = images.ToArray(),
                seasons = seasonEntries.Count > 0
                    ? seasonEntries.Select(s => (object)new { seasonNumber = s.SeasonNumber, monitored = s.Monitored }).ToArray()
                    : new object[] { new { seasonNumber = DateTime.Now.Year, monitored = league.Monitored } },
                year = DateTime.Now.Year,
                path = leaguePath,
                qualityProfileId = league.QualityProfileId ?? 1,
                languageProfileId = 1,
                seasonFolder = true,
                monitored = league.Monitored,
                useSceneNumbering = false,
                runtime = 0,
                tvdbId = externalId,
                tvRageId = 0,
                tvMazeId = 0,
                seriesType = "standard",
                cleanTitle = league.Name.ToLowerInvariant().Replace(" ", ""),
                titleSlug = league.Name.ToLowerInvariant().Replace(" ", "-"),
                genres = new[] { "Sports" },
                // Bazarr indexes alternateTitles and tags directly
                // (KeyError on absence), so both must always be present.
                alternateTitles = Array.Empty<object>(),
                tags = league.Tags.ToArray(),
                added = league.Added.ToString("o"),
                ratings = new { votes = 0, value = 0.0 },
                statistics = new
                {
                    episodeCount = stats?.EventCount ?? 0,
                    episodeFileCount = stats?.FileCount ?? 0,
                    sizeOnDisk = stats?.SizeOnDisk ?? 0L
                }
            };

            return Results.Ok(series);
        });

        // PUT /api/v3/series/{id} - Update series (Maintainerr unmonitor support)
        app.MapPut("/api/v3/series/{id:int}", async (int id, HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            return await UpdateSeriesAsync(id, json, db, logger);
        });

        // PUT /api/v3/series - Sonarr also accepts the id in the BODY (its
        // [RestPutById] convention), and Maintainerr uses exactly that form
        // ('series' and 'series/') for its unmonitor-season flow.
        app.MapPut("/api/v3/series", async (HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();

            int id;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out id))
                {
                    return Results.BadRequest(new { error = "Series id missing from body" });
                }
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            return await UpdateSeriesAsync(id, json, db, logger);
        });

        // DELETE /api/v3/series/{id} - Delete series with files (Maintainerr delete support)
        app.MapDelete("/api/v3/series/{id:int}", async (
            int id,
            SportarrDbContext db,
            ILogger<Program> logger,
            bool deleteFiles = false,
            bool addImportListExclusion = false) =>
        {
            logger.LogInformation("[V3-COMPAT] DELETE /api/v3/series/{Id} - deleteFiles={DeleteFiles}, addExclusion={AddExclusion}",
                id, deleteFiles, addImportListExclusion);

            var league = await db.Leagues.FindAsync(id);
            if (league == null)
            {
                return Results.NotFound(new { message = "Series not found" });
            }

            if (addImportListExclusion && !string.IsNullOrEmpty(league.ExternalId))
            {
                var tvdbId = Helpers.NumericIdAlias.FromExternalId(league.ExternalId);
                if (tvdbId != 0)
                {
                    var existingExclusion = await db.ImportListExclusions
                        .FirstOrDefaultAsync(e => e.TvdbId == tvdbId);

                    if (existingExclusion == null)
                    {
                        db.ImportListExclusions.Add(new ImportListExclusion
                        {
                            TvdbId = tvdbId,
                            Title = league.Name,
                            Added = DateTime.UtcNow
                        });
                        logger.LogInformation("[V3-COMPAT] Added league {Name} (tvdbId={TvdbId}) to exclusion list",
                            league.Name, tvdbId);
                    }
                }
            }

            var events = await db.Events.Where(e => e.LeagueId == id).ToListAsync();
            var eventIds = events.Select(e => e.Id).ToList();

            var eventFiles = eventIds.Any()
                ? await db.EventFiles.Where(ef => eventIds.Contains(ef.EventId)).ToListAsync()
                : new List<EventFile>();

            if (deleteFiles && eventFiles.Any())
            {
                logger.LogInformation("[V3-COMPAT] Deleting {Count} files for league {Name}",
                    eventFiles.Count, league.Name);

                var foldersToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var eventFile in eventFiles)
                {
                    try
                    {
                        if (File.Exists(eventFile.FilePath))
                        {
                            var fileDir = Path.GetDirectoryName(eventFile.FilePath);
                            if (!string.IsNullOrEmpty(fileDir))
                            {
                                foldersToDelete.Add(fileDir);
                            }
                            File.Delete(eventFile.FilePath);
                            logger.LogDebug("[V3-COMPAT] Deleted file: {Path}", eventFile.FilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[V3-COMPAT] Failed to delete file: {Path}", eventFile.FilePath);
                    }
                }

                foreach (var folder in foldersToDelete.OrderByDescending(f => f.Length))
                {
                    try
                    {
                        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                        {
                            Directory.Delete(folder);
                            logger.LogDebug("[V3-COMPAT] Deleted empty folder: {Path}", folder);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[V3-COMPAT] Failed to delete folder: {Path}", folder);
                    }
                }
            }

            if (eventFiles.Any())
            {
                db.EventFiles.RemoveRange(eventFiles);
            }

            if (events.Any())
            {
                db.Events.RemoveRange(events);
            }

            db.Leagues.Remove(league);
            await db.SaveChangesAsync();

            logger.LogInformation("[V3-COMPAT] Deleted league {Name} and {EventCount} events",
                league.Name, events.Count);

            return Results.Ok();
        });

        // GET /api/v3/series/lookup?term= - Title search (Maintainerr matches
        // Plex items to series by title when it has no id to go on).
        app.MapGet("/api/v3/series/lookup", async (string? term, SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/series/lookup - term={Term}", term);

            if (string.IsNullOrWhiteSpace(term))
            {
                return Results.Ok(Array.Empty<object>());
            }

            var needle = term.Trim().ToLowerInvariant();
            var leagues = await db.Leagues
                .Where(l => l.Name.ToLower().Contains(needle))
                .ToListAsync();

            var results = leagues.Select(league => (object)new
            {
                id = league.Id,
                title = league.Name,
                sortTitle = league.Name.ToLowerInvariant(),
                status = "continuing",
                monitored = league.Monitored,
                tvdbId = Helpers.NumericIdAlias.FromExternalId(league.ExternalId),
                qualityProfileId = league.QualityProfileId ?? 1,
                seriesType = "standard",
                titleSlug = league.Name.ToLowerInvariant().Replace(" ", "-"),
                genres = new[] { "Sports" },
                tags = league.Tags.ToArray(),
                year = league.Added.Year
            }).ToArray();

            return Results.Ok(results);
        });

        // PUT /api/v3/series/editor - Batch tag add/remove (Maintainerr's
        // exclusion-tag feature). Only 'add' and 'remove' arrive in practice;
        // 'replace' overwrites the full tag list, matching Sonarr.
        app.MapPut("/api/v3/series/editor", async (HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogInformation("[V3-COMPAT] PUT /api/v3/series/editor - {Json}", json);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var seriesIds = root.TryGetProperty("seriesIds", out var idsElement) && idsElement.ValueKind == JsonValueKind.Array
                    ? idsElement.EnumerateArray().Where(e => e.TryGetInt32(out _)).Select(e => e.GetInt32()).ToList()
                    : new List<int>();
                var tagIds = root.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array
                    ? tagsElement.EnumerateArray().Where(e => e.TryGetInt32(out _)).Select(e => e.GetInt32()).ToList()
                    : new List<int>();
                var applyTags = root.TryGetProperty("applyTags", out var applyElement) ? applyElement.GetString() : "add";

                var leagues = await db.Leagues.Where(l => seriesIds.Contains(l.Id)).ToListAsync();
                foreach (var league in leagues)
                {
                    league.Tags = applyTags?.ToLowerInvariant() switch
                    {
                        "remove" => league.Tags.Where(t => !tagIds.Contains(t)).ToList(),
                        "replace" => tagIds.ToList(),
                        _ => league.Tags.Union(tagIds).ToList()
                    };
                    league.LastUpdate = DateTime.UtcNow;
                }
                await db.SaveChangesAsync();

                return Results.Ok(leagues.Select(l => new { id = l.Id, tags = l.Tags.ToArray() }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[V3-COMPAT] Error in series editor");
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // GET /api/v3/history/series?seriesId= - Grab history for a series as
        // a flat array (Sonarr's shape). Maintainerr derives torrent
        // infohashes from this to clean the download client when deleting.
        app.MapGet("/api/v3/history/series", async (int seriesId, SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/history/series - seriesId={SeriesId}", seriesId);

            var records = await db.GrabHistory
                .Join(db.Events.Where(e => e.LeagueId == seriesId),
                    g => g.EventId,
                    e => e.Id,
                    (g, e) => new { Grab = g, EventId = e.Id })
                .OrderByDescending(x => x.Grab.GrabbedAt)
                .Take(1000)
                .ToListAsync();

            var history = records.Select(x => (object)new
            {
                episodeId = x.EventId,
                seriesId,
                eventType = "grabbed",
                date = x.Grab.GrabbedAt.ToString("o"),
                downloadId = x.Grab.TorrentInfoHash ?? x.Grab.DownloadId,
                sourceTitle = x.Grab.Title,
                data = new
                {
                    torrentInfoHash = x.Grab.TorrentInfoHash,
                    indexer = x.Grab.Indexer
                }
            }).ToArray();

            return Results.Ok(history);
        });

        return app;
    }

    /// <summary>
    /// Shared body for PUT /api/v3/series/{id} and PUT /api/v3/series (id in
    /// body). Handles league-level monitored (cascading to events ONLY when
    /// the value actually changes - Maintainerr PUTs the whole series object
    /// with monitored unchanged while flipping one season, and an
    /// unconditional cascade would wipe that season change), a per-season
    /// monitored array, and qualityProfileId.
    /// </summary>
    private static async Task<IResult> UpdateSeriesAsync(int id, string json, SportarrDbContext db, ILogger<Program> logger)
    {
        logger.LogInformation("[V3-COMPAT] PUT series {Id} - {Json}", id, json);

        var league = await db.Leagues.FindAsync(id);
        if (league == null)
        {
            return Results.NotFound(new { message = "Series not found" });
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("monitored", out var monitoredElement))
            {
                var newMonitored = monitoredElement.GetBoolean();
                if (league.Monitored != newMonitored)
                {
                    logger.LogInformation("[V3-COMPAT] Changing league {Name} monitored: {Old} -> {New}",
                        league.Name, league.Monitored, newMonitored);

                    league.Monitored = newMonitored;

                    var events = await db.Events
                        .Where(e => e.LeagueId == id)
                        .ToListAsync();

                    foreach (var evt in events)
                    {
                        evt.Monitored = newMonitored;
                        evt.LastUpdate = DateTime.UtcNow;
                    }

                    logger.LogInformation("[V3-COMPAT] Updated {Count} events to monitored={Monitored}",
                        events.Count, newMonitored);
                }
            }

            // Per-season monitored flags (Maintainerr's unmonitor-season flow
            // PUTs the seasons array with the target season flipped).
            if (root.TryGetProperty("seasons", out var seasonsElement) && seasonsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var seasonElement in seasonsElement.EnumerateArray())
                {
                    if (!seasonElement.TryGetProperty("seasonNumber", out var numElement) || !numElement.TryGetInt32(out var seasonNumber))
                        continue;
                    if (!seasonElement.TryGetProperty("monitored", out var seasonMonElement))
                        continue;

                    var seasonMonitored = seasonMonElement.GetBoolean();
                    var changed = await db.Events
                        .Where(e => e.LeagueId == id && e.SeasonNumber == seasonNumber && e.Monitored != seasonMonitored)
                        .ToListAsync();

                    if (changed.Count == 0)
                        continue;

                    foreach (var evt in changed)
                    {
                        evt.Monitored = seasonMonitored;
                        evt.LastUpdate = DateTime.UtcNow;
                    }

                    logger.LogInformation("[V3-COMPAT] Season {Season}: set {Count} events to monitored={Monitored}",
                        seasonNumber, changed.Count, seasonMonitored);
                }
            }

            if (root.TryGetProperty("qualityProfileId", out var qpElement) && qpElement.TryGetInt32(out var qpId))
            {
                league.QualityProfileId = qpId;
            }

            league.LastUpdate = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var rootFolder = await db.RootFolders.FirstOrDefaultAsync();
            var leaguePath = Path.Combine(rootFolder?.Path ?? "/data", league.Name.Replace(" ", "-"));
            var externalId = Helpers.NumericIdAlias.FromExternalId(league.ExternalId);

            var seasonEntries = await db.Events
                .Where(e => e.LeagueId == id && e.SeasonNumber.HasValue)
                .GroupBy(e => e.SeasonNumber)
                .Select(g => new { SeasonNumber = g.Key!.Value, Monitored = g.Any(e => e.Monitored) })
                .OrderBy(s => s.SeasonNumber)
                .ToListAsync();

            return Results.Ok(new
            {
                id = league.Id,
                title = league.Name,
                sortTitle = league.Name.ToLowerInvariant(),
                status = "continuing",
                monitored = league.Monitored,
                seasons = seasonEntries.Select(s => new { seasonNumber = s.SeasonNumber, monitored = s.Monitored }).ToArray(),
                tvdbId = externalId,
                path = leaguePath,
                qualityProfileId = league.QualityProfileId ?? 1,
                genres = new[] { "Sports" },
                added = league.Added.ToString("o")
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[V3-COMPAT] Error updating series {Id}", id);
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
