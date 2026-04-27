using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Services;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Endpoints;

public static class SonarrCommandEndpoints
{
    public static IEndpointRouteBuilder MapSonarrCommandEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/v3/manualimport - Get files ready for manual import
        // Decypharr calls this after a download completes to get files to import
        app.MapGet("/api/v3/manualimport", (
            HttpContext context,
            SportarrDbContext db,
            ILogger<Program> logger,
            string? folder,
            string? downloadId,
            int? seriesId,
            int? seasonNumber,
            bool? filterExistingFiles) =>
        {
            logger.LogInformation("[DECYPHARR] GET /api/v3/manualimport - folder={Folder}, downloadId={DownloadId}, seriesId={SeriesId}",
                folder, downloadId, seriesId);

            if (string.IsNullOrEmpty(folder))
            {
                return Results.Ok(Array.Empty<object>());
            }

            var importFiles = new List<object>();

            try
            {
                if (Directory.Exists(folder))
                {
                    var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => new[] { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v" }
                            .Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToList();

                    logger.LogInformation("[DECYPHARR] Found {Count} video files in {Folder}", files.Count, folder);

                    int id = 1;
                    foreach (var file in files)
                    {
                        var fileInfo = new FileInfo(file);
                        var fileName = Path.GetFileNameWithoutExtension(file);

                        var eventMatch = Regex.Match(fileName,
                            @"(.+?)\s*-\s*S(\d{4})E(\d+)\s*-\s*(.+?)(?:\s*-\s*(\d+p|\w+))?$",
                            RegexOptions.IgnoreCase);

                        string leagueName = eventMatch.Success ? eventMatch.Groups[1].Value.Trim() : "Unknown League";
                        int season = eventMatch.Success && int.TryParse(eventMatch.Groups[2].Value, out var s) ? s : DateTime.Now.Year;
                        int episode = eventMatch.Success && int.TryParse(eventMatch.Groups[3].Value, out var e) ? e : 1;
                        string eventTitle = eventMatch.Success ? eventMatch.Groups[4].Value.Trim() : fileName;

                        importFiles.Add(new
                        {
                            id = id++,
                            path = file,
                            relativePath = Path.GetRelativePath(folder, file),
                            folderName = Path.GetFileName(folder),
                            name = fileName,
                            size = fileInfo.Length,
                            series = new
                            {
                                id = seriesId ?? 1,
                                title = leagueName,
                                sortTitle = leagueName.ToLowerInvariant(),
                                status = "continuing",
                                overview = "",
                                network = "",
                                images = Array.Empty<object>(),
                                seasons = new[] { new { seasonNumber = season, monitored = true } },
                                year = season,
                                path = folder,
                                qualityProfileId = 1,
                                languageProfileId = 1,
                                seasonFolder = true,
                                monitored = true,
                                useSceneNumbering = false,
                                runtime = 0,
                                tvdbId = 0,
                                tvRageId = 0,
                                tvMazeId = 0,
                                firstAired = $"{season}-01-01",
                                seriesType = "standard",
                                cleanTitle = leagueName.ToLowerInvariant().Replace(" ", ""),
                                titleSlug = leagueName.ToLowerInvariant().Replace(" ", "-"),
                                genres = new[] { "Sports" },
                                tags = Array.Empty<int>(),
                                added = DateTime.UtcNow.ToString("o"),
                                ratings = new { votes = 0, value = 0.0 }
                            },
                            seasonNumber = season,
                            episodes = new[]
                            {
                                new
                                {
                                    id = id,
                                    seriesId = seriesId ?? 1,
                                    episodeFileId = 0,
                                    seasonNumber = season,
                                    episodeNumber = episode,
                                    title = eventTitle,
                                    airDate = DateTime.Now.ToString("yyyy-MM-dd"),
                                    airDateUtc = DateTime.UtcNow.ToString("o"),
                                    overview = "",
                                    hasFile = false,
                                    monitored = true,
                                    absoluteEpisodeNumber = episode,
                                    unverifiedSceneNumbering = false
                                }
                            },
                            quality = new
                            {
                                quality = new { id = 7, name = "WEBDL-1080p", source = "web", resolution = 1080 },
                                revision = new { version = 1, real = 0, isRepack = false }
                            },
                            languages = new[] { new { id = 1, name = "English" } },
                            releaseGroup = "",
                            customFormats = Array.Empty<object>(),
                            customFormatScore = 0,
                            indexerFlags = 0,
                            rejections = Array.Empty<object>()
                        });
                    }
                }
                else
                {
                    logger.LogWarning("[DECYPHARR] Folder does not exist: {Folder}", folder);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DECYPHARR] Error scanning folder: {Folder}", folder);
            }

            logger.LogInformation("[DECYPHARR] Returning {Count} files for manual import", importFiles.Count);
            return Results.Ok(importFiles);
        });

        // POST /api/v3/command - Execute commands (used by Decypharr for ManualImport)
        app.MapPost("/api/v3/command", async (HttpContext context, SportarrDbContext db, FileImportService fileImportService, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogInformation("[DECYPHARR] POST /api/v3/command - {Json}", json);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var commandName = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : "";

                if (commandName?.Equals("ManualImport", StringComparison.OrdinalIgnoreCase) == true)
                {
                    logger.LogInformation("[DECYPHARR] Processing ManualImport command");

                    if (root.TryGetProperty("files", out var filesElement) && filesElement.ValueKind == JsonValueKind.Array)
                    {
                        var importedCount = 0;

                        foreach (var fileElement in filesElement.EnumerateArray())
                        {
                            var path = fileElement.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;

                            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            {
                                logger.LogInformation("[DECYPHARR] Would import file: {Path}", path);
                                importedCount++;
                            }
                        }

                        logger.LogInformation("[DECYPHARR] ManualImport processed {Count} files", importedCount);
                    }

                    return Results.Ok(new
                    {
                        id = new Random().Next(1, 10000),
                        name = "ManualImport",
                        commandName = "ManualImport",
                        message = "Completed",
                        body = new { },
                        priority = "normal",
                        status = "completed",
                        queued = DateTime.UtcNow.ToString("o"),
                        started = DateTime.UtcNow.ToString("o"),
                        ended = DateTime.UtcNow.ToString("o"),
                        duration = "00:00:00.0000000",
                        trigger = "manual",
                        stateChangeTime = DateTime.UtcNow.ToString("o"),
                        sendUpdatesToClient = true,
                        updateScheduledTask = false
                    });
                }
                else
                {
                    logger.LogInformation("[DECYPHARR] Unknown command: {Command}", commandName);
                    return Results.Ok(new
                    {
                        id = new Random().Next(1, 10000),
                        name = commandName,
                        status = "completed"
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DECYPHARR] Error processing command");
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }
}
