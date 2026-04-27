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
        app.MapGet("/api/v3/series", async (SportarrDbContext db, ILogger<SonarrSeriesEndpoints> logger, int? tvdbId) =>
        {
            logger.LogInformation("[SONARR-V3] GET /api/v3/series - tvdbId={TvdbId}", tvdbId);

            IQueryable<League> query = db.Leagues;

            if (tvdbId.HasValue)
            {
                var tvdbIdStr = tvdbId.Value.ToString();
                query = query.Where(l => l.ExternalId == tvdbIdStr);
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

            var rootFolder = await db.RootFolders.FirstOrDefaultAsync();
            var rootPath = rootFolder?.Path ?? "/data";

            var series = leagues.Select(league =>
            {
                var stat = stats.GetValueOrDefault(league.Id);
                var leaguePath = Path.Combine(rootPath, league.Name.Replace(" ", "-"));
                var externalId = int.TryParse(league.ExternalId, out var id) ? id : 0;

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
                    seasons = new[] { new { seasonNumber = DateTime.Now.Year, monitored = league.Monitored } },
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
                    tags = Array.Empty<int>(),
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

            logger.LogInformation("[SONARR-V3] Returning {SeriesCount} series", series.Count);
            return Results.Ok(series);
        });

        // GET /api/v3/series/{id} - Get specific series by ID (Maintainerr compatibility)
        app.MapGet("/api/v3/series/{id:int}", async (int id, SportarrDbContext db, ILogger<SonarrSeriesEndpoints> logger) =>
        {
            logger.LogInformation("[SONARR-V3] GET /api/v3/series/{Id}", id);

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

            var rootFolder = await db.RootFolders.FirstOrDefaultAsync();
            var leaguePath = Path.Combine(rootFolder?.Path ?? "/data", league.Name.Replace(" ", "-"));
            var externalId = int.TryParse(league.ExternalId, out var extId) ? extId : 0;

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
                seasons = new[] { new { seasonNumber = DateTime.Now.Year, monitored = league.Monitored } },
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
                tags = Array.Empty<int>(),
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
        app.MapPut("/api/v3/series/{id:int}", async (int id, HttpContext context, SportarrDbContext db, ILogger<SonarrSeriesEndpoints> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogInformation("[SONARR-V3] PUT /api/v3/series/{Id} - {Json}", id, json);

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
                    logger.LogInformation("[SONARR-V3] Changing league {Name} monitored: {Old} -> {New}",
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

                    logger.LogInformation("[SONARR-V3] Updated {Count} events to monitored={Monitored}",
                        events.Count, newMonitored);
                }

                if (root.TryGetProperty("qualityProfileId", out var qpElement))
                {
                    league.QualityProfileId = qpElement.GetInt32();
                }

                league.LastUpdate = DateTime.UtcNow;
                await db.SaveChangesAsync();

                var rootFolder = await db.RootFolders.FirstOrDefaultAsync();
                var leaguePath = Path.Combine(rootFolder?.Path ?? "/data", league.Name.Replace(" ", "-"));
                var externalId = int.TryParse(league.ExternalId, out var extId) ? extId : 0;

                return Results.Ok(new
                {
                    id = league.Id,
                    title = league.Name,
                    sortTitle = league.Name.ToLowerInvariant(),
                    status = "continuing",
                    monitored = league.Monitored,
                    tvdbId = externalId,
                    path = leaguePath,
                    qualityProfileId = league.QualityProfileId ?? 1,
                    genres = new[] { "Sports" },
                    added = league.Added.ToString("o")
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[SONARR-V3] Error updating series {Id}", id);
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // DELETE /api/v3/series/{id} - Delete series with files (Maintainerr delete support)
        app.MapDelete("/api/v3/series/{id:int}", async (
            int id,
            SportarrDbContext db,
            ILogger<SonarrSeriesEndpoints> logger,
            bool deleteFiles = false,
            bool addImportListExclusion = false) =>
        {
            logger.LogInformation("[SONARR-V3] DELETE /api/v3/series/{Id} - deleteFiles={DeleteFiles}, addExclusion={AddExclusion}",
                id, deleteFiles, addImportListExclusion);

            var league = await db.Leagues.FindAsync(id);
            if (league == null)
            {
                return Results.NotFound(new { message = "Series not found" });
            }

            if (addImportListExclusion && !string.IsNullOrEmpty(league.ExternalId))
            {
                if (int.TryParse(league.ExternalId, out var tvdbId))
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
                        logger.LogInformation("[SONARR-V3] Added league {Name} (tvdbId={TvdbId}) to exclusion list",
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
                logger.LogInformation("[SONARR-V3] Deleting {Count} files for league {Name}",
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
                            logger.LogDebug("[SONARR-V3] Deleted file: {Path}", eventFile.FilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[SONARR-V3] Failed to delete file: {Path}", eventFile.FilePath);
                    }
                }

                foreach (var folder in foldersToDelete.OrderByDescending(f => f.Length))
                {
                    try
                    {
                        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                        {
                            Directory.Delete(folder);
                            logger.LogDebug("[SONARR-V3] Deleted empty folder: {Path}", folder);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[SONARR-V3] Failed to delete folder: {Path}", folder);
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

            logger.LogInformation("[SONARR-V3] Deleted league {Name} and {EventCount} events",
                league.Name, events.Count);

            return Results.Ok();
        });

        return app;
    }
}
