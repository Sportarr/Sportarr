using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

internal static class EventDateMatchContext
{
    public static bool ShouldLoadPeers(Event evt) =>
        !string.IsNullOrWhiteSpace(evt.HomeTeamName) &&
        !string.IsNullOrWhiteSpace(evt.AwayTeamName) &&
        (!evt.BroadcastDateVerified ||
            LeagueReleaseNamePolicy.CanUseVerifiedCopaDateWindow(evt) ||
            LeagueReleaseNamePolicy.CanUseVerifiedLibertadoresUtcDate(evt) ||
            LeagueReleaseNamePolicy.AllowsAdjacentDateDriftLeague(evt));

    public static async Task<IReadOnlyCollection<Event>> LoadAsync(
        SportarrDbContext db,
        Event evt,
        CancellationToken cancellationToken = default)
    {
        if (!evt.LeagueId.HasValue)
            return Array.Empty<Event>();

        var eventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
        var windowStart = eventDate.AddDays(-2);
        var windowEnd = eventDate.AddDays(3);

        var query = db.Events
            .AsNoTracking()
            .Where(candidate => candidate.LeagueId == evt.LeagueId
                && (candidate.BroadcastDate ?? candidate.EventDate) >= windowStart
                && (candidate.BroadcastDate ?? candidate.EventDate) < windowEnd);

        return await SelectMatchFields(query).ToListAsync(cancellationToken);
    }

    public static async Task<IReadOnlyCollection<Event>> LoadAsync(
        SportarrDbContext db,
        IReadOnlyCollection<DateTime> releaseDates,
        IReadOnlyCollection<int> leagueIds,
        CancellationToken cancellationToken = default)
    {
        if (releaseDates.Count == 0 || leagueIds.Count == 0)
            return Array.Empty<Event>();

        var dates = releaseDates
            .SelectMany(date => new[] { date.Date.AddDays(-1), date.Date, date.Date.AddDays(1) })
            .Distinct()
            .ToArray();
        var leagues = leagueIds.Distinct().ToArray();
        var query = db.Events
            .AsNoTracking()
            .Where(candidate => candidate.LeagueId.HasValue
                && leagues.Contains(candidate.LeagueId.Value)
                && dates.Contains((candidate.BroadcastDate ?? candidate.EventDate).Date));

        return await SelectMatchFields(query).ToListAsync(cancellationToken);
    }

    private static IQueryable<Event> SelectMatchFields(IQueryable<Event> query)
    {
        return query.Select(candidate => new Event
            {
                Id = candidate.Id,
                ExternalId = candidate.ExternalId,
                Title = candidate.Title,
                Sport = candidate.Sport,
                LeagueId = candidate.LeagueId,
                HomeTeamId = candidate.HomeTeamId,
                AwayTeamId = candidate.AwayTeamId,
                HomeTeamName = candidate.HomeTeamName,
                AwayTeamName = candidate.AwayTeamName,
                EventDate = candidate.EventDate,
                BroadcastDate = candidate.BroadcastDate,
                BroadcastDateVerified = candidate.BroadcastDateVerified
            });
    }
}
