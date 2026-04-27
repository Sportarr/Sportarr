using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

public static class SonarrEpisodeFileEndpoints
{
    public static IEndpointRouteBuilder MapSonarrEpisodeFileEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/v3/episodefile - Get episode files (Sonarr v3 API for Decypharr repair)
        app.MapGet("/api/v3/episodefile", async (SportarrDbContext db, ILogger<SonarrEpisodeFileEndpoints> logger, int? seriesId) =>
        {
            logger.LogInformation("[SONARR-V3] GET /api/v3/episodefile - seriesId={SeriesId}", seriesId);

            if (!seriesId.HasValue)
            {
                return Results.BadRequest(new { message = "seriesId parameter is required" });
            }

            var eventFiles = await db.EventFiles
                .Include(ef => ef.Event)
                .Where(ef => ef.Event != null && ef.Event.LeagueId == seriesId.Value && ef.Exists)
                .ToListAsync();

            var result = eventFiles.Select(ef => new
            {
                id = ef.Id,
                seriesId = seriesId.Value,
                seasonNumber = ef.Event?.SeasonNumber ?? DateTime.Now.Year,
                episodeNumber = ef.Event?.EpisodeNumber ?? 0,
                relativePath = Path.GetFileName(ef.FilePath),
                path = ef.FilePath,
                size = ef.Size,
                dateAdded = ef.Added.ToString("o"),
                quality = new
                {
                    quality = new
                    {
                        id = ef.QualityScore,
                        name = ef.Quality ?? "Unknown",
                        source = "unknown",
                        resolution = 0
                    },
                    revision = new { version = 1, real = 0, isRepack = false }
                },
                mediaInfo = new
                {
                    audioBitrate = 0,
                    audioChannels = 2.0,
                    audioCodec = "",
                    audioLanguages = "",
                    audioStreamCount = 1,
                    videoBitDepth = 8,
                    videoBitrate = 0,
                    videoCodec = ef.Codec ?? "",
                    videoDynamicRange = "",
                    videoDynamicRangeType = "",
                    videoFps = 0.0,
                    resolution = "",
                    runTime = "",
                    scanType = "",
                    subtitles = ""
                },
                qualityCutoffNotMet = false,
                languageCutoffNotMet = false
            }).ToList();

            logger.LogInformation("[SONARR-V3] Returning {Count} episode files for seriesId={SeriesId}",
                result.Count, seriesId.Value);

            return Results.Ok(result);
        });

        // GET /api/v3/episodefile/{id} - Get specific episode file by ID
        app.MapGet("/api/v3/episodefile/{id:int}", async (int id, SportarrDbContext db, ILogger<SonarrEpisodeFileEndpoints> logger) =>
        {
            logger.LogInformation("[SONARR-V3] GET /api/v3/episodefile/{Id}", id);

            var eventFile = await db.EventFiles
                .Include(ef => ef.Event)
                .ThenInclude(e => e!.League)
                .FirstOrDefaultAsync(ef => ef.Id == id);

            if (eventFile == null)
            {
                return Results.NotFound(new { message = "Episode file not found" });
            }

            var result = new
            {
                id = eventFile.Id,
                seriesId = eventFile.Event?.LeagueId ?? 0,
                seasonNumber = eventFile.Event?.SeasonNumber ?? DateTime.Now.Year,
                episodeNumber = eventFile.Event?.EpisodeNumber ?? 0,
                relativePath = Path.GetFileName(eventFile.FilePath),
                path = eventFile.FilePath,
                size = eventFile.Size,
                dateAdded = eventFile.Added.ToString("o"),
                quality = new
                {
                    quality = new
                    {
                        id = eventFile.QualityScore,
                        name = eventFile.Quality ?? "Unknown",
                        source = "unknown",
                        resolution = 0
                    },
                    revision = new { version = 1, real = 0, isRepack = false }
                },
                qualityCutoffNotMet = false
            };

            return Results.Ok(result);
        });

        // DELETE /api/v3/episodefile/{id} - Delete specific episode file
        app.MapDelete("/api/v3/episodefile/{id:int}", async (int id, SportarrDbContext db, ILogger<SonarrEpisodeFileEndpoints> logger) =>
        {
            logger.LogInformation("[SONARR-V3] DELETE /api/v3/episodefile/{Id}", id);

            var eventFile = await db.EventFiles
                .Include(ef => ef.Event)
                .FirstOrDefaultAsync(ef => ef.Id == id);

            if (eventFile == null)
            {
                return Results.NotFound(new { message = "Episode file not found" });
            }

            try
            {
                if (File.Exists(eventFile.FilePath))
                {
                    File.Delete(eventFile.FilePath);
                    logger.LogInformation("[SONARR-V3] Deleted file: {Path}", eventFile.FilePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[SONARR-V3] Failed to delete file: {Path}", eventFile.FilePath);
            }

            if (eventFile.Event != null)
            {
                var remainingFiles = await db.EventFiles
                    .Where(ef => ef.EventId == eventFile.EventId && ef.Id != id && ef.Exists)
                    .CountAsync();

                if (remainingFiles == 0)
                {
                    eventFile.Event.HasFile = false;
                    eventFile.Event.FilePath = null;
                    eventFile.Event.FileSize = null;
                }
            }

            db.EventFiles.Remove(eventFile);
            await db.SaveChangesAsync();

            logger.LogInformation("[SONARR-V3] Deleted episode file {Id}", id);
            return Results.Ok();
        });

        // DELETE /api/v3/episodefile/bulk - Bulk delete episode files (Decypharr repair)
        app.MapDelete("/api/v3/episodefile/bulk", async (HttpContext context, SportarrDbContext db, ILogger<SonarrEpisodeFileEndpoints> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogInformation("[SONARR-V3] DELETE /api/v3/episodefile/bulk - {Json}", json);

            try
            {
                var doc = JsonDocument.Parse(json);
                var episodeFileIds = new List<int>();

                if (doc.RootElement.TryGetProperty("episodeFileIds", out var idsElement) &&
                    idsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var idElement in idsElement.EnumerateArray())
                    {
                        if (idElement.TryGetInt32(out var fileId))
                        {
                            episodeFileIds.Add(fileId);
                        }
                    }
                }

                if (!episodeFileIds.Any())
                {
                    return Results.BadRequest(new { message = "No episodeFileIds provided" });
                }

                logger.LogInformation("[SONARR-V3] Bulk deleting {Count} episode files", episodeFileIds.Count);

                var eventFiles = await db.EventFiles
                    .Include(ef => ef.Event)
                    .Where(ef => episodeFileIds.Contains(ef.Id))
                    .ToListAsync();

                var affectedEventIds = eventFiles.Where(ef => ef.Event != null).Select(ef => ef.EventId).Distinct().ToList();

                var deletedCount = 0;
                foreach (var eventFile in eventFiles)
                {
                    try
                    {
                        if (File.Exists(eventFile.FilePath))
                        {
                            File.Delete(eventFile.FilePath);
                            logger.LogDebug("[SONARR-V3] Deleted file: {Path}", eventFile.FilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[SONARR-V3] Failed to delete file: {Path}", eventFile.FilePath);
                    }

                    deletedCount++;
                }

                db.EventFiles.RemoveRange(eventFiles);
                await db.SaveChangesAsync();

                foreach (var eventId in affectedEventIds)
                {
                    var evt = await db.Events.FindAsync(eventId);
                    if (evt != null)
                    {
                        var remainingFiles = await db.EventFiles
                            .Where(ef => ef.EventId == eventId && ef.Exists)
                            .CountAsync();

                        if (remainingFiles == 0)
                        {
                            evt.HasFile = false;
                            evt.FilePath = null;
                            evt.FileSize = null;
                        }
                    }
                }
                await db.SaveChangesAsync();

                logger.LogInformation("[SONARR-V3] Bulk deleted {Count} episode files", deletedCount);
                return Results.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[SONARR-V3] Error processing bulk delete");
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // GET /api/v3/episode - Get episodes (Sonarr v3 API for Decypharr repair)
        app.MapGet("/api/v3/episode", async (SportarrDbContext db, ILogger<SonarrEpisodeFileEndpoints> logger, int? seriesId, int? seasonNumber) =>
        {
            logger.LogInformation("[SONARR-V3] GET /api/v3/episode - seriesId={SeriesId}, seasonNumber={SeasonNumber}",
                seriesId, seasonNumber);

            if (!seriesId.HasValue)
            {
                return Results.BadRequest(new { message = "seriesId parameter is required" });
            }

            var baseQuery = db.Events.Where(e => e.LeagueId == seriesId.Value);

            if (seasonNumber.HasValue)
            {
                baseQuery = baseQuery.Where(e => e.SeasonNumber == seasonNumber.Value);
            }

            var events = await baseQuery.Include(e => e.Files).ToListAsync();

            var episodes = events.Select(e =>
            {
                var firstFile = e.Files.FirstOrDefault(f => f.Exists);
                var hasFile = firstFile != null;
                var episodeSeason = e.SeasonNumber ?? DateTime.Now.Year;

                return new
                {
                    id = e.Id,
                    seriesId = seriesId.Value,
                    tvdbId = int.TryParse(e.ExternalId, out var extId) ? extId : 0,
                    episodeFileId = firstFile?.Id ?? 0,
                    seasonNumber = episodeSeason,
                    episodeNumber = e.EpisodeNumber ?? 0,
                    title = e.Title,
                    airDate = e.EventDate.ToString("yyyy-MM-dd"),
                    airDateUtc = e.EventDate.ToUniversalTime().ToString("o"),
                    overview = "",
                    hasFile = hasFile,
                    monitored = e.Monitored,
                    absoluteEpisodeNumber = e.EpisodeNumber ?? 0,
                    unverifiedSceneNumbering = false,
                    grabbed = false,
                    episodeFile = hasFile ? new
                    {
                        id = firstFile!.Id,
                        seriesId = seriesId.Value,
                        seasonNumber = episodeSeason,
                        relativePath = Path.GetFileName(firstFile.FilePath),
                        path = firstFile.FilePath,
                        size = firstFile.Size,
                        dateAdded = firstFile.Added.ToString("o"),
                        quality = new
                        {
                            quality = new
                            {
                                id = firstFile.QualityScore,
                                name = firstFile.Quality ?? "Unknown"
                            },
                            revision = new { version = 1, real = 0, isRepack = false }
                        }
                    } : (object?)null
                };
            }).ToList();

            logger.LogInformation("[SONARR-V3] Returning {Count} episodes for seriesId={SeriesId}",
                episodes.Count, seriesId.Value);

            return Results.Ok(episodes);
        });

        // GET /api/v3/episode/{id} - Get specific episode by ID
        app.MapGet("/api/v3/episode/{id:int}", async (int id, SportarrDbContext db, ILogger<SonarrEpisodeFileEndpoints> logger) =>
        {
            logger.LogInformation("[SONARR-V3] GET /api/v3/episode/{Id}", id);

            var eventItem = await db.Events
                .Include(e => e.Files)
                .Include(e => e.League)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (eventItem == null)
            {
                return Results.NotFound(new { message = "Episode not found" });
            }

            var firstFile = eventItem.Files.FirstOrDefault(f => f.Exists);
            var hasFile = firstFile != null;
            var episodeSeason = eventItem.SeasonNumber ?? DateTime.Now.Year;

            var result = new
            {
                id = eventItem.Id,
                seriesId = eventItem.LeagueId ?? 0,
                tvdbId = int.TryParse(eventItem.ExternalId, out var extId) ? extId : 0,
                episodeFileId = firstFile?.Id ?? 0,
                seasonNumber = episodeSeason,
                episodeNumber = eventItem.EpisodeNumber ?? 0,
                title = eventItem.Title,
                airDate = eventItem.EventDate.ToString("yyyy-MM-dd"),
                airDateUtc = eventItem.EventDate.ToUniversalTime().ToString("o"),
                overview = "",
                hasFile = hasFile,
                monitored = eventItem.Monitored,
                absoluteEpisodeNumber = eventItem.EpisodeNumber ?? 0,
                unverifiedSceneNumbering = false,
                grabbed = false
            };

            return Results.Ok(result);
        });

        return app;
    }
}
