using System.Text.Json.Nodes;
using Sportarr.Api.Services.MotorsportLive;

namespace Sportarr.Api.Tests.Services.MotorsportLive;

/// <summary>
/// Real responses from api.pulselive.worldsbk.com, captured 2026-11-17. At
/// Magny-Cours the SBK Race 1 was red-flagged and restarted. At Estoril the
/// SSP Race 2 was.
/// </summary>
public class WorldSbkLiveProviderTests
{
    private const string Rounds = "/wsbk-events/v1/seasons/2026/rounds";

    private static WorldSbkLiveProvider Create(string now, Func<string>? france = null, Func<string>? estoril = null)
    {
        var handler = new FixtureHandler()
            .Json(Rounds, "wsbk-rounds-2026.json")
            .Json($"{Rounds}/FRA/sessions", france ?? (() => MotorsportFixtures.Read("wsbk-sessions-2026-FRA.json")))
            .Json($"{Rounds}/EST/sessions", estoril ?? (() => MotorsportFixtures.Read("wsbk-sessions-2026-EST.json")))
            .Json($"{Rounds}/JER/sessions", "wsbk-sessions-2026-JER.json");
        var time = new ManualTime(DateTimeOffset.Parse(now));
        return new WorldSbkLiveProvider(MotorsportProviders.Fetcher(handler, time), time);
    }

    private static string Edit(string fixture, Action<JsonArray> change)
    {
        var doc = MotorsportFixtures.Json(fixture);
        change(doc["data"]!.AsArray());
        return doc.ToJsonString();
    }

    private static JsonNode Session(JsonArray data, string id) =>
        data.Single(s => s!["id"]!.GetValue<string>() == id)!;

    [Fact]
    public async Task RedFlaggedRaceFinishesWithTheRestartNotTheStoppedPart()
    {
        var provider = Create("2026-09-05T15:00:00Z");

        var state = await provider.GetSessionStateAsync(MotorsportProviders.Query(
            MotorsportSeries.WorldSbk, "Acerbis French Round Race 1", "2026-09-05T13:30:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Finished, state!.Phase);
        // The stopped part ended at 13:47:22. The restart ran to 14:30.
        Assert.Equal(DateTimeOffset.Parse("2026-09-05T14:30:00Z"), state.FinishedAt);
        Assert.Equal("FRA SBK Race 1", state.SessionName);
    }

    [Fact]
    public async Task StoppedPartFinishedWhileRestartWaitsIsLive()
    {
        var provider = Create("2026-09-05T13:55:00Z", france: () => Edit("wsbk-sessions-2026-FRA.json", data =>
        {
            var restart = Session(data, "2026-FRA-SBK-101");
            restart["attributes"]!["status"] = "NOT-STARTED";
            restart["attributes"]!["effective_end_date_utc"] = null;
        }));

        var state = await provider.GetSessionStateAsync(MotorsportProviders.Query(
            MotorsportSeries.WorldSbk, "Acerbis French Round Race 1", "2026-09-05T13:30:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Live, state!.Phase);
    }

    [Theory]
    [InlineData("2026-09-05T13:57:00Z", MotorsportLivePhase.Live)]
    [InlineData("2026-09-05T14:40:00Z", MotorsportLivePhase.Unknown)]
    public async Task OnlyTheStoppedPartListedCountsAsLiveForALimitedTime(string now, MotorsportLivePhase expected)
    {
        var provider = Create(now, france: () => Edit("wsbk-sessions-2026-FRA.json",
            data => data.Remove(Session(data, "2026-FRA-SBK-101"))));

        var state = await provider.GetSessionStateAsync(MotorsportProviders.Query(
            MotorsportSeries.WorldSbk, "Acerbis French Round Race 1", "2026-09-05T13:30:00Z"), default);

        Assert.Equal(expected, state!.Phase);
    }

    [Fact]
    public async Task CurrentRaceIsLive()
    {
        var provider = Create("2026-09-06T12:20:00Z", france: () => Edit("wsbk-sessions-2026-FRA.json",
            data => Session(data, "2026-FRA-SBK-003")["attributes"]!["status"] = "CURRENT"));

        var state = await provider.GetSessionStateAsync(MotorsportProviders.Query(
            MotorsportSeries.WorldSbk, "Acerbis French Round Race 2", "2026-09-06T12:00:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Live, state!.Phase);
        Assert.Null(state.FinishedAt);
    }

    [Fact]
    public async Task SuperpoleRaceIsNotRaceTwo()
    {
        var provider = Create("2026-09-06T09:20:00Z", france: () => Edit("wsbk-sessions-2026-FRA.json",
            data => Session(data, "2026-FRA-SBK-002")["attributes"]!["status"] = "CURRENT"));

        var superpole = await provider.GetSessionStateAsync(MotorsportProviders.Query(
            MotorsportSeries.WorldSbk, "Acerbis French Round Superpole Race", "2026-09-06T09:10:00Z"), default);
        var race2 = await provider.GetSessionStateAsync(MotorsportProviders.Query(
            MotorsportSeries.WorldSbk, "Acerbis French Round Race 2", "2026-09-06T09:10:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Live, superpole!.Phase);
        Assert.Equal(MotorsportLivePhase.Finished, race2!.Phase);
        Assert.Equal("FRA SBK Race 2", race2.SessionName);
    }

    [Fact]
    public async Task SupersportRaceNumberComesFromTheTitleAndUsesItsOwnCategory()
    {
        // The metadata gives this race at midnight. The title gives the number.
        var provider = Create("2026-10-11T16:00:00Z");

        var state = await provider.GetSessionStateAsync(MotorsportProviders.Query(
            MotorsportSeries.WorldSsp, "Tissot Estoril Round Race 2", "2026-10-11T00:00:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Finished, state!.Phase);
        Assert.Equal("EST SSP Race 2", state.SessionName);
        Assert.Equal(DateTimeOffset.Parse("2026-10-11T13:46:39Z"), state.FinishedAt);
    }

    [Fact]
    public async Task NormalRaceFinishTimeIsTheEffectiveEnd()
    {
        var provider = Create("2026-10-18T16:00:00Z");

        var state = await provider.GetSessionStateAsync(MotorsportProviders.Query(
            MotorsportSeries.WorldSbk, "Pirelli Spanish Round Race 2", "2026-10-18T13:30:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Finished, state!.Phase);
        Assert.Equal(DateTimeOffset.Parse("2026-10-18T14:04:39Z"), state.FinishedAt);
    }

    [Fact]
    public async Task DateOutsideEveryRoundGivesNoState()
    {
        var provider = Create("2026-11-15T12:00:00Z");

        Assert.Null(await provider.GetSessionStateAsync(MotorsportProviders.Query(
            MotorsportSeries.WorldSbk, "Some Round Race 1", "2026-11-15T12:00:00Z"), default));
    }

    [Fact]
    public async Task HtmlErrorPageGivesAnException()
    {
        // The status service turns this into an unknown state.
        var handler = new FixtureHandler().Json(Rounds, () => "<html>blocked</html>");
        var time = new ManualTime(DateTimeOffset.Parse("2026-09-05T15:00:00Z"));
        var provider = new WorldSbkLiveProvider(MotorsportProviders.Fetcher(handler, time), time);

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.WorldSbk, "Acerbis French Round Race 1", "2026-09-05T13:30:00Z"), default));
    }
}
