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
                    Monitored = g.Any(e => e.Monitored),
                    EventCount = g.Count(),
                    FileCount = g.Sum(e => e.HasFile ? 1 : 0),
                    SizeOnDisk = g.Sum(e => e.FileSize ?? 0L)
                })
                .ToListAsync();
            var seasonsByLeague = seasonRows
                .GroupBy(s => s.LeagueId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(s => s.SeasonNumber)
                          .Select(s => (object)new
                          {
                              seasonNumber = s.SeasonNumber,
                              monitored = s.Monitored,
                              statistics = new
                              {
                                  episodeCount = s.EventCount,
                                  totalEpisodeCount = s.EventCount,
                                  episodeFileCount = s.FileCount,
                                  sizeOnDisk = s.SizeOnDisk,
                                  percentOfEpisodes = s.EventCount > 0
                                      ? Math.Round(100.0 * s.FileCount / s.EventCount, 1)
                                      : 0.0
                              }
                          })
                          .ToArray());

            var rootFolder = await db.RootFolders.FirstOrDefaultAsync();
            var rootPath = rootFolder?.Path ?? "/data";

            var series = leagues.Select(league =>
            {
                var stat = stats.GetValueOrDefault(league.Id);
                var leaguePath = Path.Combine(rootPath, league.Name.Replace(" ", "-"));
                var externalId = Helpers.NumericIdAlias.FromExternalId(league.ExternalId);

                var images = new List<object>();
                if (!string.IsNullOrEmpty(league.PosterUrl))
                    images.Add(new { coverType = "poster", url = league.PosterUrl });
                if (!string.IsNullOrEmpty(league.BannerUrl))
                    images.Add(new { coverType = "banner", url = league.BannerUrl });
                if (!string.IsNullOrEmpty(league.LogoUrl))
                    images.Add(new { coverType = "clearlogo", url = league.LogoUrl });

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
                        : new object[]
                        {
                            new
                            {
                                seasonNumber = DateTime.Now.Year,
                                monitored = league.Monitored,
                                statistics = new
                                {
                                    episodeCount = 0,
                                    totalEpisodeCount = 0,
                                    episodeFileCount = 0,
                                    sizeOnDisk = 0L,
                                    percentOfEpisodes = 0.0
                                }
                            }
                        },
                    year = DateTime.Now.Year,
                    path = leaguePath,
                    rootFolderPath = rootPath,
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
                        seasonCount = seasonsByLeague.TryGetValue(league.Id, out var statSeasons) ? statSeasons.Length : 1,
                        episodeCount = stat?.EventCount ?? 0,
                        totalEpisodeCount = stat?.EventCount ?? 0,
                        episodeFileCount = stat?.FileCount ?? 0,
                        sizeOnDisk = stat?.SizeOnDisk ?? 0L,
                        percentOfEpisodes = (stat?.EventCount ?? 0) > 0
                            ? Math.Round(100.0 * (stat?.FileCount ?? 0) / (stat?.EventCount ?? 1), 1)
                            : 0.0
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
                    Monitored = g.Any(e => e.Monitored),
                    EventCount = g.Count(),
                    FileCount = g.Sum(e => e.HasFile ? 1 : 0),
                    SizeOnDisk = g.Sum(e => e.FileSize ?? 0L)
                })
                .OrderBy(s => s.SeasonNumber)
                .ToListAsync();

            var rootFolder = await db.RootFolders.FirstOrDefaultAsync();
            var leaguePath = Path.Combine(rootFolder?.Path ?? "/data", league.Name.Replace(" ", "-"));
            var externalId = Helpers.NumericIdAlias.FromExternalId(league.ExternalId);

            var images = new List<object>();
            if (!string.IsNullOrEmpty(league.PosterUrl))
                images.Add(new { coverType = "poster", url = league.PosterUrl });
            if (!string.IsNullOrEmpty(league.BannerUrl))
                images.Add(new { coverType = "banner", url = league.BannerUrl });
            if (!string.IsNullOrEmpty(league.LogoUrl))
                images.Add(new { coverType = "clearlogo", url = league.LogoUrl });

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
                    ? seasonEntries.Select(s => (object)new
                    {
                        seasonNumber = s.SeasonNumber,
                        monitored = s.Monitored,
                        statistics = new
                        {
                            episodeCount = s.EventCount,
                            totalEpisodeCount = s.EventCount,
                            episodeFileCount = s.FileCount,
                            sizeOnDisk = s.SizeOnDisk,
                            percentOfEpisodes = s.EventCount > 0
                                ? Math.Round(100.0 * s.FileCount / s.EventCount, 1)
                                : 0.0
                        }
                    }).ToArray()
                    : new object[]
                    {
                        new
                        {
                            seasonNumber = DateTime.Now.Year,
                            monitored = league.Monitored,
                            statistics = new
                            {
                                episodeCount = 0,
                                totalEpisodeCount = 0,
                                episodeFileCount = 0,
                                sizeOnDisk = 0L,
                                percentOfEpisodes = 0.0
                            }
                        }
                    },
                year = DateTime.Now.Year,
                path = leaguePath,
                rootFolderPath = rootFolder?.Path ?? "/data",
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
                    seasonCount = seasonEntries.Count > 0 ? seasonEntries.Count : 1,
                    episodeCount = stats?.EventCount ?? 0,
                    totalEpisodeCount = stats?.EventCount ?? 0,
                    episodeFileCount = stats?.FileCount ?? 0,
                    sizeOnDisk = stats?.SizeOnDisk ?? 0L,
                    percentOfEpisodes = (stats?.EventCount ?? 0) > 0
                        ? Math.Round(100.0 * (stats?.FileCount ?? 0) / (stats?.EventCount ?? 1), 1)
                        : 0.0
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
            ConfigService configService,
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

            var failedPaths = new List<string>();

            if (deleteFiles && eventFiles.Any())
            {
                logger.LogInformation("[V3-COMPAT] Deleting {Count} files for league {Name}",
                    eventFiles.Count, league.Name);

                var foldersToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // The recycle bin applies here too. Sonarr sends deleted series
                // files to its own recycle bin, and so does every other delete
                // in this app, so an automated cleanup calling this shim must
                // not be the one path that destroys files outright.
                var recycleBin = (await configService.GetConfigAsync()).RecycleBin;
                var useRecycleBin = !string.IsNullOrEmpty(recycleBin) && Directory.Exists(recycleBin);

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

                            if (useRecycleBin)
                            {
                                var recyclePath = Sportarr.Api.Helpers.RecyclePaths.FindFree(
                                    recycleBin!, Path.GetFileName(eventFile.FilePath));
                                File.Move(eventFile.FilePath, recyclePath);
                                logger.LogDebug("[V3-COMPAT] Moved file to recycle bin: {Path}", eventFile.FilePath);
                            }
                            else
                            {
                                File.Delete(eventFile.FilePath);
                                logger.LogDebug("[V3-COMPAT] Deleted file: {Path}", eventFile.FilePath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[V3-COMPAT] Failed to delete file: {Path}", eventFile.FilePath);
                        failedPaths.Add(eventFile.FilePath);
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

            if (failedPaths.Count > 0)
            {
                // The caller asked for the files and some are still there. It
                // has no other way to learn that, and an automated cleaner that
                // believes the space came back will keep filling the disk.
                //
                // The rows that named these files are gone with the league, so
                // this is the last point at which anything knows where they
                // are. Name them rather than counting them.
                logger.LogWarning("[V3-COMPAT] {Count} file(s) for league {Name} could not be removed and are still on disk: {Paths}",
                    failedPaths.Count, league.Name, string.Join(", ", failedPaths));
                return Results.Ok(new { filesNotDeleted = failedPaths.Count, paths = failedPaths });
            }

            return Results.Ok();
        });

        // GET /api/v3/series/lookup?term= - Title search (Maintainerr matches
        // Plex items to series by title when it has no id to go on).
        app.MapGet("/api/v3/series/lookup", async (string? term, SportarrDbContext db, SportarrApiClient sportarrApi, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/series/lookup - term={Term}", term);

            if (string.IsNullOrWhiteSpace(term))
            {
                return Results.Ok(Array.Empty<object>());
            }

            // Some Starr clients encode spaces as literal plus signs in the
            // lookup term (Sonarr tolerates it); league names never contain
            // one, so normalizing is safe.
            var trimmed = term.Trim().Replace('+', ' ');

            // Starr-family consumers (the request managers especially) look up
            // by "tvdb:<id>" before adding. League tvdbIds are the numeric
            // aliases of their external ids, so resolve those directly - local
            // library first, metadata catalog second.
            if (trimmed.StartsWith("tvdb:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(trimmed.AsSpan(5), out var tvdbAlias))
            {
                foreach (var candidate in Helpers.NumericIdAlias.LeagueExternalIdCandidates(tvdbAlias))
                {
                    var local = await db.Leagues.FirstOrDefaultAsync(l => l.ExternalId == candidate);
                    if (local != null)
                    {
                        var localSeasons = await db.Events
                            .Where(e => e.LeagueId == local.Id && e.SeasonNumber.HasValue)
                            .GroupBy(e => e.SeasonNumber)
                            .Select(g => new { SeasonNumber = g.Key!.Value, Monitored = g.Any(e => e.Monitored) })
                            .OrderBy(s => s.SeasonNumber)
                            .ToListAsync();
                        return Results.Ok(new[] { LookupResult(local.Id, local.Name, local.Monitored,
                            Helpers.NumericIdAlias.FromExternalId(local.ExternalId),
                            local.QualityProfileId ?? 1, local.Tags.ToArray(), local.Added.Year,
                            local.Description, local.LogoUrl, local.PosterUrl,
                            localSeasons.Select(s => (object)new { seasonNumber = s.SeasonNumber, monitored = s.Monitored }).ToArray()) });
                    }

                    var catalogLeague = await sportarrApi.LookupLeagueAsync(candidate);
                    if (catalogLeague != null)
                    {
                        return Results.Ok(new[] { LookupResult(0, catalogLeague.Name, false,
                            tvdbAlias, 1, Array.Empty<int>(), DateTime.UtcNow.Year,
                            catalogLeague.Description, catalogLeague.LogoUrl, catalogLeague.PosterUrl) });
                    }
                }

                return Results.Ok(Array.Empty<object>());
            }

            var needle = trimmed.ToLowerInvariant();
            var leagues = await db.Leagues
                .Where(l => l.Name.ToLower().Contains(needle))
                .ToListAsync();

            var libraryIds = leagues.Select(l => l.Id).ToList();
            var librarySeasonRows = await db.Events
                .Where(e => e.LeagueId.HasValue && libraryIds.Contains(e.LeagueId.Value) && e.SeasonNumber.HasValue)
                .GroupBy(e => new { e.LeagueId, e.SeasonNumber })
                .Select(g => new { LeagueId = g.Key.LeagueId!.Value, SeasonNumber = g.Key.SeasonNumber!.Value, Monitored = g.Any(e => e.Monitored) })
                .ToListAsync();
            var librarySeasons = librarySeasonRows
                .GroupBy(s => s.LeagueId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(s => s.SeasonNumber)
                          .Select(s => (object)new { seasonNumber = s.SeasonNumber, monitored = s.Monitored })
                          .ToArray());

            var results = leagues.Select(league => LookupResult(league.Id, league.Name, league.Monitored,
                Helpers.NumericIdAlias.FromExternalId(league.ExternalId),
                league.QualityProfileId ?? 1, league.Tags.ToArray(), league.Added.Year,
                league.Description, league.LogoUrl, league.PosterUrl,
                librarySeasons.GetValueOrDefault(league.Id))).ToList();

            // Text terms also consult the metadata catalog so a league that
            // isn't in the library yet can be discovered and then added via
            // POST /api/v3/series (id 0 marks a lookup-only result, matching
            // Sonarr's convention for series not yet in the library).
            var knownExternalIds = leagues
                .Where(l => !string.IsNullOrEmpty(l.ExternalId))
                .Select(l => l.ExternalId!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var catalogResults = await sportarrApi.SearchLeagueAsync(trimmed);
            foreach (var catalogLeague in (catalogResults ?? new List<League>())
                .Where(c => !string.IsNullOrEmpty(c.ExternalId) && !knownExternalIds.Contains(c.ExternalId!))
                .Take(10))
            {
                results.Add(LookupResult(0, catalogLeague.Name, false,
                    Helpers.NumericIdAlias.FromExternalId(catalogLeague.ExternalId),
                    1, Array.Empty<int>(), DateTime.UtcNow.Year,
                    catalogLeague.Description, catalogLeague.LogoUrl, catalogLeague.PosterUrl));
            }

            return Results.Ok(results);
        });

        // POST /api/v3/series - Add a league through the Sonarr contract.
        // Request managers look a series up (tvdb:<alias> or text), then POST
        // it here with the alias tvdbId; the alias resolves back to the
        // league's external id and the add runs through LeagueAddService -
        // the exact same path the native POST /api/leagues takes.
        app.MapPost("/api/v3/series", async (
            HttpContext context,
            SportarrDbContext db,
            SportarrApiClient sportarrApi,
            LeagueAddService leagueAddService,
            ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();

            int tvdbId = 0;
            string? title = null;
            int? qualityProfileId = null;
            bool monitored = true;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("tvdbId", out var tvdbElement) && tvdbElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                    tvdbId = tvdbElement.GetInt32();
                if (root.TryGetProperty("title", out var titleElement))
                    title = titleElement.GetString();
                if (root.TryGetProperty("qualityProfileId", out var qpElement) && qpElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                    qualityProfileId = qpElement.GetInt32();
                if (root.TryGetProperty("monitored", out var monitoredElement) &&
                    (monitoredElement.ValueKind == System.Text.Json.JsonValueKind.True || monitoredElement.ValueKind == System.Text.Json.JsonValueKind.False))
                    monitored = monitoredElement.GetBoolean();
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new[] { new { errorMessage = "Invalid JSON body" } });
            }

            logger.LogInformation("[V3-COMPAT] POST /api/v3/series - tvdbId={TvdbId}, title={Title}", tvdbId, title);

            // Resolve the league's external id from the alias, falling back to
            // a catalog title search when only a title arrived.
            string? externalId = null;
            League? catalogLeague = null;
            foreach (var candidate in Helpers.NumericIdAlias.LeagueExternalIdCandidates(tvdbId))
            {
                catalogLeague = await sportarrApi.LookupLeagueAsync(candidate);
                if (catalogLeague != null)
                {
                    externalId = candidate;
                    break;
                }
            }

            if (catalogLeague == null && !string.IsNullOrWhiteSpace(title))
            {
                var candidates = await sportarrApi.SearchLeagueAsync(title);
                catalogLeague = candidates?.FirstOrDefault(c =>
                    string.Equals(c.Name, title, StringComparison.OrdinalIgnoreCase)) ?? candidates?.FirstOrDefault();
                externalId = catalogLeague?.ExternalId;
            }

            if (catalogLeague == null)
            {
                return Results.BadRequest(new[] { new { errorMessage = $"No league matches tvdbId {tvdbId} / title '{title}'" } });
            }

            var existing = await db.Leagues.FirstOrDefaultAsync(l => l.ExternalId == externalId);
            if (existing != null)
            {
                // Sonarr answers an add of an existing series with a validation
                // failure; consumers treat it as "already added".
                return Results.BadRequest(new[] { new { errorMessage = "This series has already been added" } });
            }

            var addResult = await leagueAddService.AddLeagueAsync(new AddLeagueRequest
            {
                ExternalId = externalId,
                Name = catalogLeague.Name,
                Sport = catalogLeague.Sport ?? "Unknown",
                Country = catalogLeague.Country,
                Description = catalogLeague.Description,
                Monitored = monitored,
                QualityProfileId = qualityProfileId,
            });

            if (!addResult.Success || addResult.League == null)
            {
                return Results.BadRequest(new[] { new { errorMessage = addResult.ErrorMessage ?? "Failed to add league" } });
            }

            var added = addResult.League;
            return Results.Created($"/api/v3/series/{added.Id}", new
            {
                id = added.Id,
                title = added.Name,
                sortTitle = added.Name.ToLowerInvariant(),
                status = "continuing",
                monitored = added.Monitored,
                tvdbId = Helpers.NumericIdAlias.FromExternalId(added.ExternalId),
                qualityProfileId = added.QualityProfileId ?? qualityProfileId ?? 1,
                seriesType = "standard",
                titleSlug = added.Name.ToLowerInvariant().Replace(" ", "-"),
                genres = new[] { "Sports" },
                tags = Array.Empty<int>(),
                seasons = Array.Empty<object>(),
                year = DateTime.UtcNow.Year,
                added = DateTime.UtcNow.ToString("o"),
            });
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

        // GET /api/v3/history - Paged activity history (Sonarr's shape).
        // Merges grab history (eventType "grabbed") with import history
        // (eventType "downloadFolderImported"), newest first. Exporters read
        // totalRecords; queue tools read per-record sourceTitle/downloadId.
        // Import ids are offset so the two sources never collide on id.
        app.MapGet("/api/v3/history", async (
            SportarrDbContext db,
            ILogger<Program> logger,
            int? page,
            int? pageSize,
            string? sortKey,
            string? sortDirection) =>
        {
            var pageNumber = page is > 0 ? page.Value : 1;
            var effectivePageSize = pageSize is > 0 && pageSize.Value <= 500 ? pageSize.Value : 20;
            var ascending = string.Equals(sortDirection, "ascending", StringComparison.OrdinalIgnoreCase);

            logger.LogDebug("[V3-COMPAT] GET /api/v3/history - page={Page}, pageSize={PageSize}", pageNumber, effectivePageSize);

            var grabTotal = await db.GrabHistory.CountAsync();
            var importTotal = await db.ImportHistories.CountAsync();

            // Both sources contribute up to the page window's end, then the
            // merged stream is cut to the requested page. Correct for any
            // interleaving because each side is already sorted by date.
            var window = pageNumber * effectivePageSize;

            var grabQuery = ascending
                ? db.GrabHistory.OrderBy(g => g.GrabbedAt)
                : db.GrabHistory.OrderByDescending(g => g.GrabbedAt);
            var grabs = await grabQuery
                .Take(window)
                .Select(g => new { g.Id, EventId = (int?)g.EventId, g.Title, g.GrabbedAt, g.DownloadId, g.TorrentInfoHash, g.Indexer, g.Quality })
                .ToListAsync();

            var importQuery = ascending
                ? db.ImportHistories.OrderBy(h => h.ImportedAt)
                : db.ImportHistories.OrderByDescending(h => h.ImportedAt);
            var imports = await importQuery
                .Take(window)
                .Select(h => new { h.Id, h.EventId, h.SourcePath, h.ImportedAt, h.Quality })
                .ToListAsync();

            var merged =
                grabs.Select(g => new
                {
                    id = g.Id,
                    episodeId = g.EventId,
                    seriesId = 0,
                    sourceTitle = (string?)g.Title,
                    date = g.GrabbedAt,
                    eventType = "grabbed",
                    downloadId = g.TorrentInfoHash ?? g.DownloadId,
                    quality = g.Quality,
                    data = (object)new { indexer = g.Indexer, torrentInfoHash = g.TorrentInfoHash }
                })
                .Concat(imports.Select(h => new
                {
                    id = h.Id + 1_000_000,
                    episodeId = h.EventId,
                    seriesId = 0,
                    sourceTitle = (string?)System.IO.Path.GetFileName(h.SourcePath),
                    date = h.ImportedAt,
                    eventType = "downloadFolderImported",
                    downloadId = (string?)null,
                    quality = (string?)h.Quality,
                    data = (object)new { droppedPath = h.SourcePath }
                }));

            merged = ascending ? merged.OrderBy(r => r.date) : merged.OrderByDescending(r => r.date);

            var records = merged
                .Skip((pageNumber - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .Select(r => new
                {
                    r.id,
                    episodeId = r.episodeId ?? 0,
                    r.seriesId,
                    r.sourceTitle,
                    languages = Array.Empty<object>(),
                    quality = new
                    {
                        quality = new { id = 0, name = r.quality ?? "Unknown", source = "unknown", resolution = 0 },
                        revision = new { version = 1, real = 0, isRepack = false }
                    },
                    customFormats = Array.Empty<object>(),
                    customFormatScore = 0,
                    qualityCutoffNotMet = false,
                    date = r.date.ToString("o"),
                    r.downloadId,
                    r.eventType,
                    r.data
                })
                .ToList();

            return Results.Ok(new
            {
                page = pageNumber,
                pageSize = effectivePageSize,
                sortKey = sortKey ?? "date",
                sortDirection = ascending ? "ascending" : "descending",
                totalRecords = grabTotal + importTotal,
                records,
            });
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


        // POST /api/v3/seasonPass - Bulk-monitor many leagues at once
        // (Sonarr's "season pass" flow). Toggles League.Monitored per id and,
        // when monitoringOptions.monitor is provided, maps it onto the same
        // MonitorType Sportarr's own league settings use.
        app.MapPost("/api/v3/seasonPass", async (HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogInformation("[V3-COMPAT] POST /api/v3/seasonPass - {Json}", json);

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            using (doc)
            {
                var root = doc.RootElement;

                MonitorType? monitorType = null;
                if (root.TryGetProperty("monitoringOptions", out var options)
                    && options.TryGetProperty("monitor", out var monitorEl))
                {
                    monitorType = monitorEl.GetString() switch
                    {
                        "all" => MonitorType.All,
                        "future" => MonitorType.Future,
                        "existing" => MonitorType.CurrentSeason,
                        "latestSeason" => MonitorType.NextSeason,
                        "missing" => MonitorType.Recent,
                        _ => (MonitorType?)null,
                    };
                }

                if (root.TryGetProperty("series", out var seriesEl) && seriesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in seriesEl.EnumerateArray())
                    {
                        if (!entry.TryGetProperty("id", out var idEl))
                        {
                            continue;
                        }

                        var league = await db.Leagues.FindAsync(idEl.GetInt32());
                        if (league == null)
                        {
                            continue;
                        }

                        if (entry.TryGetProperty("monitored", out var monitoredEl))
                        {
                            league.Monitored = monitoredEl.GetBoolean();
                        }

                        if (monitorType.HasValue)
                        {
                            league.MonitorType = monitorType.Value;
                        }
                    }

                    await db.SaveChangesAsync();
                }
            }

            return Results.Ok(new { });
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
                rootFolderPath = rootFolder?.Path ?? "/data",
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

    /// <summary>
    /// Series-shaped lookup entry. id 0 marks a catalog-only result (not in
    /// the library yet), matching Sonarr's convention for lookup hits that
    /// have not been added.
    /// </summary>
    private static object LookupResult(
        int id,
        string name,
        bool monitored,
        int tvdbId,
        int qualityProfileId,
        int[] tags,
        int year,
        string? overview,
        string? logoUrl,
        string? posterUrl = null,
        object[]? seasons = null)
    {
        var seasonEntries = seasons ?? Array.Empty<object>();
        var images = new List<object>();
        if (!string.IsNullOrEmpty(posterUrl))
            images.Add(new { coverType = "poster", url = posterUrl, remoteUrl = posterUrl });
        if (!string.IsNullOrEmpty(logoUrl))
            images.Add(new { coverType = "clearlogo", url = logoUrl, remoteUrl = logoUrl });

        return new
        {
            id,
            title = name,
            sortTitle = name.ToLowerInvariant(),
            status = "continuing",
            overview = overview ?? string.Empty,
            monitored,
            tvdbId,
            tvRageId = 0,
            tvMazeId = 0,
            imdbId = string.Empty,
            network = string.Empty,
            certification = string.Empty,
            qualityProfileId,
            seriesType = "standard",
            titleSlug = name.ToLowerInvariant().Replace(" ", "-"),
            cleanTitle = name.ToLowerInvariant().Replace(" ", string.Empty),
            genres = new[] { "Sports" },
            images = images.ToArray(),
            // Sonarr's lookup carries the poster twice: in images and as the
            // flat remotePoster that request bots render directly.
            remotePoster = posterUrl ?? logoUrl ?? string.Empty,
            seasons = seasonEntries,
            // Lookup consumers read seasonCount from statistics. In-library
            // leagues carry their real seasons; catalog-only results report 0
            // like Sonarr does for series not yet added.
            statistics = new { seasonCount = seasonEntries.Length, episodeCount = 0, totalEpisodeCount = 0, sizeOnDisk = 0L, percentOfEpisodes = 0.0 },
            tags,
            year,
        };
    }
}
