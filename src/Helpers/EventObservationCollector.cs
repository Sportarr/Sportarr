using System;
using System.Collections.Generic;
using System.Linq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Decides whether an unmatched release is worth reporting. A report needs
/// a monitored league with a canonical id and an event date close to now.
/// Anything else is noise the service cannot act on.
/// </summary>
public static class EventObservationCollector
{
    public static readonly TimeSpan LookBack = TimeSpan.FromDays(30);
    public static readonly TimeSpan LookAhead = TimeSpan.FromDays(7);
    public const int MaxPerBatch = 20;

    // The metadata service stores at most this many characters of a title.
    public const int MaxTitleLength = 300;

    public static EventObservation? FromUnmatchedRelease(
        string? releaseTitle,
        SportsParseResult? parsed,
        IReadOnlyCollection<League> monitoredLeagues,
        DateTime nowUtc,
        IReadOnlySet<(int LeagueId, DateTime Day)>? knownEventDays = null)
    {
        var title = releaseTitle?.Trim();
        if (string.IsNullOrEmpty(title) || parsed == null || !parsed.EventDate.HasValue)
            return null;

        var observed = parsed.EventDate.Value.Date;
        if (observed < nowUtc.Date - LookBack || observed > nowUtc.Date + LookAhead)
            return null;

        var league = ResolveLeague(parsed, monitoredLeagues);
        if (league == null || string.IsNullOrWhiteSpace(league.ExternalId))
            return null;

        // Release names and event rows can disagree on the day by time zone.
        if (knownEventDays != null && Enumerable.Range(-1, 3)
                .Any(offset => knownEventDays.Contains((league.Id, observed.AddDays(offset)))))
            return null;

        if (title.Length > MaxTitleLength)
            title = title[..MaxTitleLength];

        return new EventObservation(league.ExternalId, title, observed);
    }

    private static League? ResolveLeague(SportsParseResult parsed, IReadOnlyCollection<League> monitoredLeagues)
    {
        if (!string.IsNullOrWhiteSpace(parsed.SportarrLeagueId))
        {
            return monitoredLeagues.FirstOrDefault(league =>
                string.Equals(league.ExternalId, parsed.SportarrLeagueId, StringComparison.OrdinalIgnoreCase));
        }

        if (string.IsNullOrWhiteSpace(parsed.Organization))
            return null;

        // A loose label match files a UEFA release under a monitored AFC
        // league. Only an exact alias in the same sport counts here.
        var matches = monitoredLeagues
            .Where(league => SameSport(parsed.Sport, league.Sport)
                && ReleaseMatchingService.SeriesLabelEqualsLeagueAlias(parsed.Organization, league))
            .Take(2)
            .ToList();
        // Two monitored leagues sharing a label means the report could be
        // filed under the wrong one. Skip rather than guess.
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool SameSport(string? parsedSport, string? leagueSport)
    {
        if (string.IsNullOrWhiteSpace(parsedSport) || string.IsNullOrWhiteSpace(leagueSport))
            return true;
        // The parser says "Fighting" where the league row says "Combat".
        return LeagueSportRules.AreEquivalentSports(parsedSport, leagueSport);
    }
}
