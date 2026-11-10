using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

internal static class ImportDateMatchPolicy
{
    public static ImportDateMatchResult Evaluate(Event evt, DateTime parsedDate)
    {
        var eventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
        var daysDifference = (int)Math.Abs((eventDate - parsedDate.Date).TotalDays);

        if (daysDifference == 0)
            return new ImportDateMatchResult(eventDate, daysDifference, 20, false);

        if (evt.BroadcastDateVerified && evt.HomeTeamId.HasValue && evt.AwayTeamId.HasValue)
            return new ImportDateMatchResult(eventDate, daysDifference, 0, true);

        var score = daysDifference switch
        {
            <= 1 => 10,
            <= 3 => 8,
            <= 7 => 5,
            _ => 0
        };

        return new ImportDateMatchResult(eventDate, daysDifference, score, false);
    }

    /// <summary>
    /// Same as the two-argument form, with one extra rule. Release names
    /// often carry the UTC date of a game, and the library dates a verified
    /// game by its broadcast day. A name that carries both team names of the
    /// event may use the UTC date, or a date one day from either date, unless
    /// another game of the same two teams fits the date better.
    /// </summary>
    public static ImportDateMatchResult Evaluate(
        Event evt,
        DateTime parsedDate,
        string releaseTitle,
        IReadOnlyCollection<Event>? sameLeagueEvents)
    {
        var result = Evaluate(evt, parsedDate);
        if (!result.Reject || !CanUseNearbyDate(evt, parsedDate, releaseTitle))
            return result;

        var tier = DateTier(evt, parsedDate);
        var hasBetterGame = sameLeagueEvents != null && sameLeagueEvents.Any(peer =>
            !ReleaseMatchingService.IsSameEvent(evt, peer)
            && ReleaseMatchingService.IsSameEventLeague(evt, peer)
            && ReleaseMatchingService.IsSameTeamPair(evt, peer)
            && DateTier(peer, parsedDate) < tier);
        if (hasBetterGame)
            return result;

        var nearestDifference = (int)Math.Min(
            Math.Abs((result.EventDate - parsedDate.Date).TotalDays),
            Math.Abs((evt.EventDate.Date - parsedDate.Date).TotalDays));
        return new ImportDateMatchResult(result.EventDate, nearestDifference, tier == 1 ? 15 : 10, false);
    }

    /// <summary>
    /// True when the nearby-date rule could accept this game. A caller uses
    /// it to decide whether the other games of the league must be loaded.
    /// </summary>
    public static bool CanUseNearbyDate(Event evt, DateTime parsedDate, string releaseTitle) =>
        evt.BroadcastDateVerified
        && evt.HomeTeamId.HasValue
        && evt.AwayTeamId.HasValue
        && DateTier(evt, parsedDate) is 1 or 2
        && LeagueReleaseNamePolicy.NamesBothEventTeams(releaseTitle, evt);

    // 0 is the broadcast date, 1 is the UTC date, 2 is one day from either.
    // 3 is anything else.
    private static int DateTier(Event evt, DateTime parsedDate)
    {
        var date = parsedDate.Date;
        var broadcast = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
        var utc = evt.EventDate.Date;
        if (broadcast == date) return 0;
        if (utc == date) return 1;
        return Math.Abs((broadcast - date).TotalDays) <= 1 || Math.Abs((utc - date).TotalDays) <= 1 ? 2 : 3;
    }
}

internal readonly record struct ImportDateMatchResult(
    DateTime EventDate,
    int DaysDifference,
    int Score,
    bool Reject);
