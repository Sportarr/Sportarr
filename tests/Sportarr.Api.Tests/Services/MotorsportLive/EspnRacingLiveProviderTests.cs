using System.Text.Json.Nodes;
using Sportarr.Api.Services.MotorsportLive;

namespace Sportarr.Api.Tests.Services.MotorsportLive;

/// <summary>
/// Real responses from site.api.espn.com, fetched 2026-11-18 with the exact
/// date ranges that the provider sends. Each test serves a capture only for
/// the query it was fetched with, so a wrong query finds nothing.
/// </summary>
public class EspnRacingLiveProviderTests
{
    private const string F1 = "/apis/site/v2/sports/racing/f1/scoreboard";
    private const string Irl = "/apis/site/v2/sports/racing/irl/scoreboard";

    private static (EspnRacingLiveProvider Provider, FixtureHandler Handler) Create(
        string now, string path, string range, Func<string>? body = null)
    {
        var series = path == Irl ? "irl" : "f1";
        var handler = new FixtureHandler().Json($"{path}?dates={range}",
            body ?? (() => MotorsportFixtures.Read($"espn-{series}-range-{range}.json")));
        var time = new ManualTime(DateTimeOffset.Parse(now));
        return (new EspnRacingLiveProvider(MotorsportProviders.Fetcher(handler, time), time), handler);
    }

    private static string Edit(string fixture, string competitionId, Action<JsonNode> change)
    {
        var doc = MotorsportFixtures.Json(fixture);
        foreach (var e in doc["events"]!.AsArray())
        foreach (var c in e!["competitions"]!.AsArray())
        {
            if (c!["id"]!.GetValue<string>() == competitionId)
                change(c["status"]!["type"]!);
        }
        return doc.ToJsonString();
    }

    [Theory]
    [InlineData("2026-11-22T04:00:00Z", "20261121-20261123")]
    [InlineData("2026-10-11T12:00:00Z", "20261010-20261012")]
    [InlineData("2026-08-30T17:00:00Z", "20260829-20260831")]
    public void QueryRangeRunsFromTheDayBeforeToTheDayAfter(string anchor, string expected)
    {
        Assert.Equal(expected, EspnRacingLiveProvider.DateRange(DateTimeOffset.Parse(anchor)));
    }

    [Theory]
    // The race at 04:00 UTC on 22 November is on 21 November in US Eastern
    // time. A one-day query for the 22nd returned nothing.
    [InlineData("Las Vegas Grand Prix", "2026-11-22T04:00:00Z", "20261121-20261123", "espn:401839137")]
    [InlineData("Las Vegas Grand Prix Qualifying", "2026-11-21T04:00:00Z", "20261120-20261122", "espn:401839136")]
    [InlineData("Las Vegas Grand Prix Practice 2", "2026-11-20T04:00:00Z", "20261119-20261121", "espn:401839134")]
    public async Task UpcomingNightSessionsMatchTheirOwnCompetition(string title, string anchor, string range, string key)
    {
        var (provider, handler) = Create("2026-11-18T03:00:00Z", F1, range);

        var state = await provider.GetSessionStateAsync(MotorsportProviders.Query(MotorsportSeries.Formula1, title, anchor), default);

        Assert.Equal($"{F1}?dates={range}", Assert.Single(handler.Requests));
        Assert.Equal(MotorsportLivePhase.NotStarted, state!.Phase);
        Assert.Equal(key, state.SessionKey);
    }

    [Theory]
    [InlineData("Singapore Grand Prix Sprint Qualifying", "2026-10-09T12:30:00Z", "20261008-20261010", "espn:401839114")]
    [InlineData("Singapore Grand Prix Sprint", "2026-10-10T09:00:00Z", "20261009-20261011", "espn:401839115")]
    [InlineData("Singapore Grand Prix", "2026-10-11T12:00:00Z", "20261010-20261012", "espn:401839117")]
    public async Task SprintWeekendSessionsAreFinal(string title, string anchor, string range, string key)
    {
        var (provider, _) = Create("2026-10-11T15:00:00Z", F1, range);

        var state = await provider.GetSessionStateAsync(MotorsportProviders.Query(MotorsportSeries.Formula1, title, anchor), default);

        Assert.Equal(MotorsportLivePhase.Finished, state!.Phase);
        Assert.Equal(key, state.SessionKey);
        Assert.Null(state.FinishedAt);
    }

    [Fact]
    public async Task InStateIsLive()
    {
        var (provider, _) = Create("2026-10-11T13:00:00Z", F1, "20261010-20261012", () => Edit(
            "espn-f1-range-20261010-20261012.json", "401839117", type =>
            {
                type["state"] = "in";
                type["completed"] = false;
            }));

        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.Formula1, "Singapore Grand Prix", "2026-10-11T12:00:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Live, state!.Phase);
    }

    [Fact]
    public async Task CancelledRaceIsUnknown()
    {
        var (provider, _) = Create("2026-10-11T14:00:00Z", F1, "20261010-20261012", () => Edit(
            "espn-f1-range-20261010-20261012.json", "401839117", type => type["name"] = "STATUS_CANCELED"));

        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.Formula1, "Singapore Grand Prix", "2026-10-11T12:00:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Unknown, state!.Phase);
    }

    [Fact]
    public async Task IndyCarRaceWithoutATypeIsTheRace()
    {
        var (provider, handler) = Create("2026-09-06T21:00:00Z", Irl, "20260905-20260907");

        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.IndyCar, "Grand Prix of Monterey", "2026-09-06T18:30:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Finished, state!.Phase);
        Assert.Equal($"{Irl}?dates=20260905-20260907", Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task IndyCarPracticeHasNoCompetitionToMatch()
    {
        // The real board for this range is empty. The race is on the last day,
        // which a range leaves out.
        var (provider, _) = Create("2026-09-05T21:00:00Z", Irl, "20260904-20260906");

        Assert.Null(await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.IndyCar, "Grand Prix of Monterey Practice 1", "2026-09-05T18:30:00Z"), default));
    }

    [Theory]
    [InlineData("Grand Prix of Milwaukee Race 1", "2026-08-29T18:30:00Z", "20260828-20260830", "2026-08-29T18:30:00Z")]
    [InlineData("Grand Prix of Milwaukee Race 2", "2026-08-30T17:00:00Z", "20260829-20260831", "2026-08-30T17:00:00Z")]
    public async Task DoubleheaderRaceWithANumberMatchesItsOwnDay(string title, string anchor, string range, string start)
    {
        var (provider, _) = Create("2026-08-31T00:00:00Z", Irl, range);

        var state = await provider.GetSessionStateAsync(MotorsportProviders.Query(MotorsportSeries.IndyCar, title, anchor), default);

        Assert.Equal(DateTimeOffset.Parse(start), state!.SessionStart);
    }

    [Fact]
    public async Task DoubleheaderWithoutANumberBetweenTheRacesIsNotGuessed()
    {
        // The races are 22.5 hours apart. Halfway between, neither is clearly
        // the nearer one.
        var (provider, _) = Create("2026-08-31T00:00:00Z", Irl, "20260829-20260831");

        Assert.Null(await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.IndyCar, "Grand Prix of Milwaukee", "2026-08-30T05:45:00Z"), default));
    }
}
