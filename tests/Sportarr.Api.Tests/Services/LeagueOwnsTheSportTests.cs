using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The league decides the sport. Measured on a real library, 19,952 events
/// carried a sport that disagreed with the league they belong to: NFL and NCAA
/// events read "Football" under leagues reading "American Football", NHL read
/// "Hockey" under "Ice Hockey", and UFC, ONE and Boxing read "Combat" under
/// "Fighting". Import matching compares a parsed sport against this field, so
/// each of those releases was penalised against its own event.
/// </summary>
public class LeagueOwnsTheSportTests
{
    private static readonly SportsFileNameParser Parser =
        new(NullLogger<SportsFileNameParser>.Instance);

    // league name, league sport, the drifted value events used to carry
    [Theory]
    [InlineData("NFL", "American Football", "Football")]
    [InlineData("NCAA Division 1", "American Football", "Football")]
    [InlineData("NHL", "Ice Hockey", "Hockey")]
    [InlineData("UFC", "Fighting", "Combat")]
    [InlineData("ONE", "Fighting", "Combat")]
    [InlineData("Boxing", "Fighting", "Combat")]
    public void ScoringUsesTheLeaguesSportNotTheEventsOwn(
        string leagueName, string leagueSport, string driftedEventSport)
    {
        var svc = ImportMatchingTestHarness.Service();
        var release = $"{leagueName} 2026 06 15 Some Event 1080p WEB";
        var parsed = Parser.Parse(release);

        var drifted = ImportMatchingTestHarness.Event(
            "Some Event", driftedEventSport, leagueName, leagueSport);
        var aligned = ImportMatchingTestHarness.Event(
            "Some Event", leagueSport, leagueName, leagueSport);

        var driftedScore = svc.CalculateMatchConfidence("Some Event", drifted.Title, null, drifted, parsed);
        var alignedScore = svc.CalculateMatchConfidence("Some Event", aligned.Title, null, aligned, parsed);

        // Whatever the event row says, the league decides, so both score the same.
        Assert.Equal(alignedScore, driftedScore);
    }
}
