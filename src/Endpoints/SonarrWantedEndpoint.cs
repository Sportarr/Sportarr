using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

/// <summary>
/// Sonarr v3 wanted/missing compatibility shim. Dashboard and library tools
/// in the Starr family (Homarr's media widgets among them) list an
/// instance's missing items through GET /api/v3/wanted/missing and expect
/// Sonarr's paged episode records with an optional nested series object.
/// Maps Sportarr's monitored-without-file events onto that shape, mirroring
/// the league-as-series conventions the shim calendar endpoint established.
/// </summary>
public static class SonarrWantedEndpoint
{
    public static IEndpointRouteBuilder MapSonarrWantedEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v3/wanted/missing", async (
            SportarrDbContext db,
            ILogger<Program> logger,
            int? page,
            int? pageSize,
            string? sortKey,
            string? sortDirection,
            bool? includeSeries) =>
        {
            var pageNumber = page is > 0 ? page.Value : 1;
            var effectivePageSize = pageSize is > 0 ? pageSize.Value : 10;
            var ascending = string.Equals(sortDirection, "ascending", StringComparison.OrdinalIgnoreCase);

            logger.LogDebug("[V3-COMPAT] GET /api/v3/wanted/missing - page={Page}, pageSize={PageSize}, includeSeries={IncludeSeries}",
                pageNumber, effectivePageSize, includeSeries);

            var now = DateTime.UtcNow;
            var query = db.Events
                .Include(e => e.League)
                .Where(e => e.Monitored && !e.HasFile && e.EventDate <= now);

            // Sonarr's default missing sort is airDateUtc; EventDate is the
            // equivalent field, so both sort keys land on the same ordering.
            query = ascending
                ? query.OrderBy(e => e.EventDate)
                : query.OrderByDescending(e => e.EventDate);

            var totalRecords = await query.CountAsync();
            var events = await query
                .Skip((pageNumber - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToListAsync();

            var records = events.Select(e =>
            {
                var episodeSeason = e.SeasonNumber ?? e.EventDate.Year;

                object? seriesObj = null;
                if (includeSeries == true && e.League != null)
                {
                    var images = new List<object>();
                    if (!string.IsNullOrEmpty(e.League.LogoUrl))
                        images.Add(new { coverType = "poster", url = e.League.LogoUrl, remoteUrl = e.League.LogoUrl });
                    if (!string.IsNullOrEmpty(e.League.BannerUrl))
                        images.Add(new { coverType = "banner", url = e.League.BannerUrl, remoteUrl = e.League.BannerUrl });
                    if (!string.IsNullOrEmpty(e.League.PosterUrl))
                        images.Add(new { coverType = "fanart", url = e.League.PosterUrl, remoteUrl = e.League.PosterUrl });

                    seriesObj = new
                    {
                        id = e.League.Id,
                        title = e.League.Name,
                        year = episodeSeason,
                        titleSlug = e.League.Name.ToLowerInvariant().Replace(" ", "-"),
                        images = images.ToArray(),
                        monitored = e.League.Monitored,
                    };
                }

                return new
                {
                    id = e.Id,
                    seriesId = e.LeagueId ?? 0,
                    title = e.Title,
                    seasonNumber = episodeSeason,
                    episodeNumber = e.EpisodeNumber ?? 0,
                    airDateUtc = e.EventDate.ToString("o"),
                    monitored = e.Monitored,
                    hasFile = e.HasFile,
                    series = seriesObj,
                };
            }).ToList();

            return Results.Ok(new
            {
                page = pageNumber,
                pageSize = effectivePageSize,
                sortKey = sortKey ?? "airDateUtc",
                sortDirection = ascending ? "ascending" : "descending",
                totalRecords,
                records,
            });
        });

        // GET /api/v3/wanted/cutoff - downloaded episodes whose file quality
        // sits below the profile cutoff. Prometheus exporters chart this as
        // the upgrade backlog. Uses the same profile rank the automatic
        // search upgrade gate applies, so the count matches what Sportarr
        // would actually try to upgrade. Files with an unparseable quality
        // have rank 0. The automatic search also refuses to upgrade them.
        app.MapGet("/api/v3/wanted/cutoff", async (
            SportarrDbContext db,
            ConfigService configService,
            ILogger<Program> logger,
            int? page,
            int? pageSize,
            string? sortKey,
            string? sortDirection) =>
        {
            var pageNumber = page is > 0 ? page.Value : 1;
            var effectivePageSize = pageSize is > 0 ? pageSize.Value : 10;
            var ascending = string.Equals(sortDirection, "ascending", StringComparison.OrdinalIgnoreCase);

            logger.LogDebug("[V3-COMPAT] GET /api/v3/wanted/cutoff - page={Page}, pageSize={PageSize}",
                pageNumber, effectivePageSize);

            var config = await configService.GetConfigAsync();
            var profiles = await db.QualityProfiles.ToListAsync();
            var cutoffRanks = profiles
                .Where(p => p.UpgradesAllowed && p.CutoffQuality.HasValue)
                .ToDictionary(p => p.Id, p => Helpers.QualityProfileRanker.GetCutoffRank(p, p.CutoffQuality!.Value));

            var candidates = await db.Events
                .AsNoTracking()
                .Include(e => e.League)
                .Where(e => e.Monitored && e.HasFile && e.LeagueId != null)
                .Select(e => new
                {
                    Event = e,
                    Qualities = e.Files.Select(f => f.Quality).ToList(),
                    HasIptvRecording = e.Files.Any(f => f.Exists && f.IsIptvRecording)
                })
                .ToListAsync();

            var unmet = candidates
                .Where(x =>
                {
                    if (Helpers.SourcePrecedence.AwaitsIndexerRelease(config, x.HasIptvRecording))
                        return true;
                    var profile = RssSyncService.ResolveQualityProfile(x.Event, profiles);
                    if (profile == null || !cutoffRanks.TryGetValue(profile.Id, out var cutoffRank) || cutoffRank <= 0)
                        return false;
                    var best = x.Qualities.Count == 0
                        ? 0
                        : x.Qualities.Max(q => Helpers.QualityProfileRanker.GetRank(profile, q));
                    // Rank 0 is unparseable, unless the profile lists the
                    // quality without allowing it: that is below the cutoff.
                    return best > 0
                        ? best < cutoffRank
                        : x.Qualities.Any(q => Helpers.QualityProfileRanker.IsListedButDisallowed(profile, q));
                })
                .Select(x => x.Event);

            unmet = ascending
                ? unmet.OrderBy(e => e.EventDate)
                : unmet.OrderByDescending(e => e.EventDate);

            var unmetList = unmet.ToList();
            var records = unmetList
                .Skip((pageNumber - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .Select(e => new
                {
                    id = e.Id,
                    seriesId = e.LeagueId ?? 0,
                    title = e.Title,
                    seasonNumber = e.SeasonNumber ?? e.EventDate.Year,
                    episodeNumber = e.EpisodeNumber ?? 0,
                    airDateUtc = e.EventDate.ToString("o"),
                    monitored = e.Monitored,
                    hasFile = e.HasFile,
                })
                .ToList();

            return Results.Ok(new
            {
                page = pageNumber,
                pageSize = effectivePageSize,
                sortKey = sortKey ?? "airDateUtc",
                sortDirection = ascending ? "ascending" : "descending",
                totalRecords = unmetList.Count,
                records,
            });
        });

        return app;
    }

}
