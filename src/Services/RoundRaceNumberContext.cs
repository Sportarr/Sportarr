using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

internal static class RoundRaceNumberContext
{
    /// <summary>
    /// The race numbers of the event's Supercars round, or null for any
    /// other event. A release can name a race by its place in the round.
    /// </summary>
    public static async Task<List<int>?> LoadAsync(SportarrDbContext db, Event evt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(evt.Round) ||
            evt.League?.Name.Contains("Supercars", StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        var titles = await db.Events
            .AsNoTracking()
            .Where(e => e.LeagueId == evt.LeagueId && e.Season == evt.Season && e.Round == evt.Round)
            .Select(e => e.Title)
            .ToListAsync(cancellationToken);
        return ReleaseMatchingService.RaceNumbersInTitles(titles);
    }
}
