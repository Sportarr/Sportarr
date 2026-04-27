using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;

namespace Sportarr.Api.Endpoints;

public static class SonarrCalendarEndpoint
{
    public static IEndpointRouteBuilder MapSonarrCalendarEndpoint(this IEndpointRouteBuilder app)
    {
        // GET /api/v3/calendar - Sonarr-compatible calendar endpoint
        // Returns events in the date range as Sonarr Episode objects so clients
        // configured with the Sonarr template (ArrControl, Maintainerr, dashboards, etc.)
        // can show Sportarr events alongside Sonarr/Radarr ones.
        app.MapGet("/api/v3/calendar", async (
            DateTime? start,
            DateTime? end,
            bool? unmonitored,
            bool? includeSeries,
            bool? includeEpisodeFile,
            SportarrDbContext db,
            ILogger<SonarrCalendarEndpoint> logger) =>
        {
            // Sonarr default: today + 7 days when no range supplied
            var rangeStart = start ?? DateTime.UtcNow.Date;
            var rangeEnd = end ?? DateTime.UtcNow.Date.AddDays(7);

            logger.LogInformation("[SONARR-V3] GET /api/v3/calendar - start={Start}, end={End}, unmonitored={Unmon}, includeSeries={IncSeries}",
                rangeStart, rangeEnd, unmonitored, includeSeries);

            var query = db.Events
                .Include(e => e.League)
                .Include(e => e.Files)
                .Where(e => e.EventDate >= rangeStart && e.EventDate <= rangeEnd);

            if (unmonitored != true)
                query = query.Where(e => e.Monitored);

            var events = await query.OrderBy(e => e.EventDate).ToListAsync();

            var rootFolder = await db.RootFolders.FirstOrDefaultAsync();
            var rootPath = rootFolder?.Path ?? "/data";

            var episodes = events.Select(e =>
            {
                var firstFile = e.Files.FirstOrDefault(f => f.Exists);
                var hasFile = firstFile != null;
                var episodeSeason = e.SeasonNumber ?? e.EventDate.Year;
                var leagueId = e.LeagueId ?? 0;

                object? seriesObj = null;
                if (includeSeries == true && e.League != null)
                {
                    var leaguePath = Path.Combine(rootPath, e.League.Name.Replace(" ", "-"));
                    var images = new List<object>();
                    if (!string.IsNullOrEmpty(e.League.LogoUrl))
                        images.Add(new { coverType = "poster", url = e.League.LogoUrl });
                    if (!string.IsNullOrEmpty(e.League.BannerUrl))
                        images.Add(new { coverType = "banner", url = e.League.BannerUrl });
                    if (!string.IsNullOrEmpty(e.League.PosterUrl))
                        images.Add(new { coverType = "fanart", url = e.League.PosterUrl });

                    var leagueExternalId = int.TryParse(e.League.ExternalId, out var lExt) ? lExt : 0;
                    seriesObj = new
                    {
                        id = e.League.Id,
                        title = e.League.Name,
                        sortTitle = e.League.Name.ToLowerInvariant(),
                        status = "continuing",
                        overview = e.League.Description ?? string.Empty,
                        network = string.Empty,
                        images = images.ToArray(),
                        year = episodeSeason,
                        path = leaguePath,
                        qualityProfileId = e.League.QualityProfileId ?? 1,
                        languageProfileId = 1,
                        seasonFolder = true,
                        monitored = e.League.Monitored,
                        useSceneNumbering = false,
                        runtime = 0,
                        tvdbId = leagueExternalId,
                        tvRageId = 0,
                        tvMazeId = 0,
                        seriesType = "standard",
                        cleanTitle = e.League.Name.ToLowerInvariant().Replace(" ", string.Empty),
                        titleSlug = e.League.Name.ToLowerInvariant().Replace(" ", "-"),
                        genres = new[] { "Sports" },
                        tags = Array.Empty<int>(),
                        added = e.League.Added.ToString("o"),
                        ratings = new { votes = 0, value = 0.0 }
                    };
                }

                object? episodeFileObj = null;
                if (includeEpisodeFile == true && hasFile)
                {
                    episodeFileObj = new
                    {
                        id = firstFile!.Id,
                        seriesId = leagueId,
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
                    };
                }

                return new
                {
                    id = e.Id,
                    seriesId = leagueId,
                    tvdbId = int.TryParse(e.ExternalId, out var extId) ? extId : 0,
                    episodeFileId = firstFile?.Id ?? 0,
                    seasonNumber = episodeSeason,
                    episodeNumber = e.EpisodeNumber ?? 0,
                    title = e.Title,
                    airDate = e.EventDate.ToString("yyyy-MM-dd"),
                    airDateUtc = e.EventDate.ToUniversalTime().ToString("o"),
                    overview = string.Empty,
                    hasFile = hasFile,
                    monitored = e.Monitored,
                    absoluteEpisodeNumber = e.EpisodeNumber ?? 0,
                    unverifiedSceneNumbering = false,
                    grabbed = false,
                    series = seriesObj,
                    episodeFile = episodeFileObj
                };
            }).ToList();

            logger.LogInformation("[SONARR-V3] Returning {Count} calendar episodes", episodes.Count);
            return Results.Ok(episodes);
        });

        return app;
    }
}
