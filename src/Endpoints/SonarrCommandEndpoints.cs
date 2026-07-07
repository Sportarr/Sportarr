using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
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
        app.MapGet("/api/v3/manualimport", async (
            HttpContext context,
            SportarrDbContext db,
            LibraryImportService libraryImport,
            ILogger<Program> logger,
            string? folder,
            string? downloadId,
            int? seriesId,
            int? seasonNumber,
            bool? filterExistingFiles) =>
        {
            logger.LogDebug("[DECYPHARR] GET /api/v3/manualimport - folder={Folder}, downloadId={DownloadId}, seriesId={SeriesId}",
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

                    // Run the real match engine over the folder once, so the
                    // response carries the actual matched league / season /
                    // episode instead of a fabricated 'Unknown League' row
                    // whenever the filename isn't in the renamer's own
                    // 'League - SxxxxEyy - Title' shape.
                    var analysisByPath = new Dictionary<string, ImportableFile>(StringComparer.Ordinal);
                    var matchedEventsById = new Dictionary<int, Event>();
                    try
                    {
                        var scan = await libraryImport.ScanFolderAsync(folder, includeSubfolders: true);
                        foreach (var f in scan.MatchedFiles)
                        {
                            analysisByPath[f.FilePath] = f;
                        }
                        var matchedIds = scan.MatchedFiles
                            .Where(f => f.MatchedEventId.HasValue)
                            .Select(f => f.MatchedEventId!.Value)
                            .Distinct()
                            .ToList();
                        if (matchedIds.Count > 0)
                        {
                            matchedEventsById = await db.Events
                                .Include(e => e.League)
                                .Where(e => matchedIds.Contains(e.Id))
                                .ToDictionaryAsync(e => e.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[DECYPHARR] Match engine failed for {Folder}; responses fall back to filename parsing", folder);
                    }

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

                        // Prefer the engine's match over the filename parse.
                        Event? matchedEvent = null;
                        if (analysisByPath.TryGetValue(file, out var analysis) &&
                            analysis.MatchedEventId.HasValue &&
                            matchedEventsById.TryGetValue(analysis.MatchedEventId.Value, out matchedEvent) &&
                            matchedEvent != null)
                        {
                            leagueName = matchedEvent.League?.Name ?? leagueName;
                            season = matchedEvent.SeasonNumber ?? season;
                            episode = matchedEvent.EpisodeNumber ?? episode;
                            eventTitle = matchedEvent.Title;
                        }

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
                                id = matchedEvent?.LeagueId ?? seriesId ?? 1,
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
                                    id = matchedEvent?.Id ?? id,
                                    seriesId = matchedEvent?.LeagueId ?? seriesId ?? 1,
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
        app.MapPost("/api/v3/command", async (HttpContext context, SportarrDbContext db, FileImportService fileImportService, LibraryImportService libraryImport, ILogger<Program> logger) =>
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
                        // Actually import. This handler previously counted the
                        // files and did nothing, which left completed
                        // downloads stranded until a manual recursive library
                        // scan happened to pick them up. Each file is matched
                        // with the real engine; confident matches import
                        // immediately, everything else lands in Activity as a
                        // pending import for review instead of vanishing.
                        var importedCount = 0;
                        var pendedCount = 0;

                        foreach (var fileElement in filesElement.EnumerateArray())
                        {
                            var path = fileElement.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
                            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                            {
                                continue;
                            }

                            try
                            {
                                var parentFolder = Path.GetDirectoryName(path);
                                ImportableFile? analysis = null;
                                if (!string.IsNullOrEmpty(parentFolder))
                                {
                                    var scan = await libraryImport.ScanFolderAsync(parentFolder, includeSubfolders: false);
                                    analysis = scan.MatchedFiles.FirstOrDefault(f => f.FilePath == path);
                                    if (scan.AlreadyInLibrary.Any(f => f.FilePath == path))
                                    {
                                        continue; // nothing to do
                                    }
                                }

                                if (analysis?.MatchedEventId != null &&
                                    (analysis.MatchConfidence ?? 0) >= LibraryImportService.AutoImportConfidenceFloor &&
                                    analysis.ExistingEventId == null)
                                {
                                    var importResult = await libraryImport.ImportFilesAsync(new List<FileImportRequest>
                                    {
                                        new()
                                        {
                                            FilePath = path,
                                            EventId = analysis.MatchedEventId,
                                            Quality = analysis.Quality
                                        }
                                    });
                                    if (importResult.Imported.Count + importResult.Created.Count > 0)
                                    {
                                        importedCount++;
                                        logger.LogInformation("[DECYPHARR] Imported {Path} (event {EventId}, confidence {Confidence}%)",
                                            path, analysis.MatchedEventId, analysis.MatchConfidence);
                                        continue;
                                    }
                                }

                                // Below the confidence floor (or unmatched):
                                // queue for review rather than dropping it.
                                var alreadyPending = await db.PendingImports
                                    .AnyAsync(pi => pi.FilePath == path && pi.Status == PendingImportStatus.Pending);
                                if (!alreadyPending)
                                {
                                    var info = new FileInfo(path);
                                    db.PendingImports.Add(new PendingImport
                                    {
                                        DownloadClientId = null,
                                        DownloadId = $"manualimport-{Guid.NewGuid():N}",
                                        Title = info.Name,
                                        FilePath = path,
                                        Size = info.Length,
                                        Quality = analysis?.Quality,
                                        SuggestedEventId = analysis?.MatchedEventId,
                                        SuggestionConfidence = analysis?.MatchConfidence ?? 0,
                                        Detected = DateTime.UtcNow,
                                        Status = PendingImportStatus.Pending
                                    });
                                    await db.SaveChangesAsync();
                                }
                                pendedCount++;
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "[DECYPHARR] Failed to import {Path}", path);
                            }
                        }

                        logger.LogInformation("[DECYPHARR] ManualImport imported {Imported} file(s), queued {Pended} for review",
                            importedCount, pendedCount);
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

        // GET /api/v3/command/{id} - Command status poll. POST /command
        // executes synchronously and answers status=completed, so any
        // follow-up poll (Maintainerr fetches the command right after
        // queueing it) can simply confirm completion.
        app.MapGet("/api/v3/command/{id:int}", (int id, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/command/{Id}", id);
            return Results.Ok(new
            {
                id,
                name = "",
                status = "completed"
            });
        });

        return app;
    }
}
