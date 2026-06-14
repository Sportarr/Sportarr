using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

/// <summary>
/// Local media-server agent metadata API. Mirrors the JSON shape of the
/// hub's /api/metadata/agents/* routes but serves from THIS instance's own
/// database, so the Plex, Emby, and Jellyfin plugins can be pointed at a
/// local Sportarr instance instead of the cloud.
///
/// Episode numbers are the values already stored on each event - the same
/// numbers the renamer wrote into the filenames - so what an agent reads
/// here can never drift from the files on disk. The numbers only change on
/// a season refresh, which also renames the files. Cancelled and postponed
/// events are excluded (they carry no episode number), matching the hub and
/// the renamer. Read-only; no writes.
/// </summary>
public static class MetadataAgentEndpoints
{
    public static IEndpointRouteBuilder MapMetadataAgentEndpoints(this IEndpointRouteBuilder app)
    {
        // Search leagues by title (the agent's "series" match step).
        app.MapGet("/api/metadata/agents/search", async (string? title, int? year, SportarrDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(title))
                return Results.Ok(new { results = Array.Empty<object>() });

            var term = title.Trim();
            var leagues = await db.Leagues
                .Where(l => EF.Functions.Like(l.Name, $"%{term}%"))
                .OrderBy(l => l.Name)
                .Take(25)
                .ToListAsync();

            var results = leagues
                .Where(l => year == null || ParseYear(l.FormedYear) == year)
                .Select(l => new
                {
                    id = l.ExternalId,
                    title = l.Name,
                    year = ParseYear(l.FormedYear),
                    poster_url = l.PosterUrl,
                    sport = l.Sport
                })
                .ToList();

            return Results.Ok(new { results });
        });

        // League (series) details.
        app.MapGet("/api/metadata/agents/series/{leagueId}", async (string leagueId, SportarrDbContext db) =>
        {
            var league = await db.Leagues.FirstOrDefaultAsync(l => l.ExternalId == leagueId);
            if (league == null)
                return Results.Ok(new { error = "Series not found" });

            return Results.Ok(new
            {
                id = league.ExternalId,
                title = league.Name,
                sort_title = league.Name,
                summary = league.Description,
                poster_url = league.PosterUrl,
                banner_url = league.BannerUrl,
                fanart_url = (string?)null,
                year = ParseYear(league.FormedYear),
                studio = (string?)null,
                genres = Array.Empty<string>(),
                content_rating = (string?)null,
                sport = league.Sport
            });
        });

        // Seasons for a league.
        app.MapGet("/api/metadata/agents/series/{leagueId}/seasons", async (string leagueId, SportarrDbContext db) =>
        {
            var league = await db.Leagues.FirstOrDefaultAsync(l => l.ExternalId == leagueId);
            if (league == null)
                return Results.Ok(new { error = "Series not found" });

            var events = await db.Events
                .Where(e => e.LeagueId == league.Id && e.SeasonNumber != null)
                .ToListAsync();

            var seasons = events
                .GroupBy(e => e.SeasonNumber!.Value)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var seasonLabel = g.Select(e => e.Season).FirstOrDefault(s => !string.IsNullOrEmpty(s));
                    return new
                    {
                        season_number = g.Key,
                        title = seasonLabel ?? $"Season {g.Key}",
                        summary = "",
                        poster_url = (string?)null,
                        episode_count = g.Count(e => !IsExcluded(e.Status)),
                        year = ParseYear(seasonLabel) ?? g.Key
                    };
                })
                .ToList();

            return Results.Ok(new { seasons });
        });

        // Episodes for one season.
        app.MapGet("/api/metadata/agents/series/{leagueId}/season/{seasonNumber}/episodes", async (string leagueId, string seasonNumber, SportarrDbContext db) =>
        {
            var league = await db.Leagues.FirstOrDefaultAsync(l => l.ExternalId == leagueId);
            if (league == null)
                return Results.Ok(new { episodes = Array.Empty<object>(), count = 0, leagueId, seasonNumber });

            if (!int.TryParse(seasonNumber, out var sn))
                return Results.Ok(new { episodes = Array.Empty<object>(), count = 0, leagueId, seasonNumber });

            var events = await db.Events
                .Where(e => e.LeagueId == league.Id && e.SeasonNumber == sn)
                .ToListAsync();

            var episodes = events
                .Where(e => !IsExcluded(e.Status))
                .OrderBy(e => e.EpisodeNumber == null)
                .ThenBy(e => e.EpisodeNumber)
                .ThenBy(e => e.EventDate)
                .Select(ToEpisode)
                .ToList();

            return Results.Ok(new { episodes, count = episodes.Count, leagueId, seasonNumber });
        });

        // Single episode (event) by external id. Lets an agent resolve one
        // game without pulling the whole season list.
        app.MapGet("/api/metadata/agents/episode/{eventId}", async (string eventId, SportarrDbContext db, SportarrApiClient apiClient) =>
        {
            var evt = await db.Events.FirstOrDefaultAsync(e => e.ExternalId == eventId);
            if (evt == null)
                return Results.Ok(new { error = "Episode not found" });

            var cast = await apiClient.GetEventCastAsync(evt.ExternalId);
            return Results.Ok(ToEpisode(evt, cast));
        });

        // Resolve a single game by series + season + episode number, mirroring
        // the hub's /match route. Lets an agent fetch one event per file
        // instead of the whole season list (the expensive pattern against the
        // cloud). Same numbering source as /episodes, so the resolved event
        // matches the season list and the filename.
        app.MapGet("/api/metadata/match", async (string? series, string? season, int? episode, SportarrDbContext db, SportarrApiClient apiClient) =>
        {
            if (string.IsNullOrWhiteSpace(series) || string.IsNullOrWhiteSpace(season) || episode == null)
                return Results.Ok(new { error = "series, season and episode are required" });

            var league = await db.Leagues.FirstOrDefaultAsync(l => l.ExternalId == series);
            if (league == null)
                return Results.Ok(new { error = "Series not found" });

            if (!int.TryParse(season, out var sn))
                return Results.Ok(new { error = "Invalid season" });

            var events = await db.Events
                .Where(e => e.LeagueId == league.Id && e.SeasonNumber == sn)
                .ToListAsync();

            var evt = events
                .Where(e => !IsExcluded(e.Status) && e.EpisodeNumber == episode)
                .OrderBy(e => e.EventDate)
                .FirstOrDefault();

            if (evt == null)
                return Results.Ok(new { error = "Episode not found" });

            var seasonLabel = events.Select(e => e.Season).FirstOrDefault(s => !string.IsNullOrEmpty(s));
            var cast = await apiClient.GetEventCastAsync(evt.ExternalId);

            return Results.Ok(new
            {
                match = new
                {
                    league_id = league.ExternalId,
                    event_id = evt.ExternalId,
                    series = new
                    {
                        id = league.ExternalId,
                        title = league.Name,
                        sort_title = league.Name,
                        summary = league.Description,
                        poster_url = league.PosterUrl,
                        banner_url = league.BannerUrl,
                        fanart_url = (string?)null,
                        year = ParseYear(league.FormedYear),
                        studio = (string?)null,
                        genres = Array.Empty<string>(),
                        content_rating = (string?)null,
                        sport = league.Sport
                    },
                    season = new
                    {
                        season_number = sn,
                        title = seasonLabel ?? $"Season {sn}",
                        summary = "",
                        poster_url = (string?)null,
                        episode_count = events.Count(e => !IsExcluded(e.Status)),
                        year = ParseYear(seasonLabel) ?? sn
                    },
                    episode = ToEpisode(evt, cast),
                    confidence = 1.0
                },
                confidence = 1.0,
                query = new { series, season, episode }
            });
        });

        // Health endpoint the agents probe to validate a configured URL. The
        // Plex/Emby/Jellyfin plugins call {ApiUrl}/api/health and require
        // status == "healthy" before they'll accept a local instance, so this
        // must return the same shape the cloud does.
        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "healthy",
            version = Sportarr.Api.Version.AppVersion,
            build = Sportarr.Api.Version.GetFullVersion(),
            timestamp = DateTime.UtcNow
        }));

        // Season poster proxy. The Jellyfin/Emby image providers build
        // {ApiUrl}/api/images/league/{id}/poster directly for season art, so
        // serve it locally by redirecting to the league's stored poster URL
        // (the image bytes still come from wherever that URL points - the hub
        // - which keeps image hosting off this instance).
        app.MapGet("/api/images/league/{leagueId}/poster", async (string leagueId, SportarrDbContext db) =>
        {
            var league = await db.Leagues.FirstOrDefaultAsync(l => l.ExternalId == leagueId);
            if (league == null || string.IsNullOrWhiteSpace(league.PosterUrl))
                return Results.NotFound();

            return Results.Redirect(league.PosterUrl);
        });

        return app;
    }

    // Cancelled and postponed events never occupy an episode slot, matching
    // the hub and the renamer (which leave their EpisodeNumber null).
    private static bool IsExcluded(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        return status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Canceled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Postponed", StringComparison.OrdinalIgnoreCase);
    }

    // Pull a 4-digit year out of a season label ("2024", "2024-2025") or a
    // league formed-year. Returns null when no 4-digit run is present.
    private static int? ParseYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).Take(4).ToArray());
        return digits.Length == 4 && int.TryParse(digits, out var y) ? y : (int?)null;
    }

    // Bulk/season-list callers use the cast-free overload so the whole-season
    // response stays lean (no per-event hub call). The per-episode handlers
    // pass the cast they fetched from the hub.
    private static object ToEpisode(Event e) => ToEpisode(e, null);

    private static object ToEpisode(Event e, IReadOnlyList<HubCastMember>? cast) => new
    {
        id = e.ExternalId,
        title = e.Title,
        summary = e.Description,
        thumb_url = e.Images != null ? e.Images.FirstOrDefault() : null,
        air_date = e.EventDate.ToString("yyyy-MM-dd"),
        broadcast_date = e.BroadcastDate?.ToString("yyyy-MM-dd"),
        season_number = e.SeasonNumber,
        episode_number = e.EpisodeNumber,
        part_number = (int?)null,
        part_name = (string?)null,
        duration_minutes = (int?)null,
        round = e.Round,
        venue = e.Venue,
        home_team = e.HomeTeamName,
        away_team = e.AwayTeamName,
        sport = e.Sport,
        cast = cast == null
            ? Array.Empty<object>()
            : cast.Select(c => new
            {
                name = c.Name,
                team = c.Team,
                side = c.Side,
                position = c.Position,
                number = c.Number
            }).ToArray()
    };
}
