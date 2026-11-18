using Sportarr.Api.Services.MotorsportLive;

namespace Sportarr.Api.Tests.Services.MotorsportLive;

/// <summary>
/// Real pages from www.tsl-timing.com, captured 2026-11-17. Event 264204 is
/// round 11 at Brands Hatch, 16 to 18 October, with all three race results
/// published.
/// </summary>
public class BritishSuperbikeLiveProviderTests
{
    private static BritishSuperbikeLiveProvider Create(Func<string>? eventPage = null)
    {
        var handler = new FixtureHandler()
            .Json("/Results/bsb", "bsb-results-2026.html")
            .Json("/event/264204", eventPage ?? (() => MotorsportFixtures.Read("bsb-event-264204.html")));
        var time = new ManualTime(DateTimeOffset.Parse("2026-10-18T17:00:00Z"));
        return new BritishSuperbikeLiveProvider(MotorsportProviders.Fetcher(handler, time));
    }

    [Fact]
    public void RoundsComeFromTheRacesListOnly()
    {
        var rounds = BritishSuperbikeLiveProvider.ReadRounds(MotorsportFixtures.Read("bsb-results-2026.html"));

        Assert.Equal(11, rounds.Count);
        var last = rounds.Single(r => r.Id == "264204");
        Assert.Equal("Brands Hatch GP", last.Track);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero), last.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero), last.End);
        var oulton = rounds.Single(r => r.Id == "263104");
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero), oulton.Start);
        Assert.Equal(new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero), oulton.End);
    }

    [Fact]
    public void OnlyTheSuperbikeChampionshipIsRead()
    {
        var sessions = BritishSuperbikeLiveProvider.ReadSuperbikeSessions(MotorsportFixtures.Read("bsb-event-264204.html"));

        Assert.Contains("Race 3 Result", sessions);
        Assert.Contains("Qualifying - Q2", sessions);
        Assert.DoesNotContain("Heat 1 Result", sessions);
    }

    [Theory]
    [InlineData("Brands Hatch GP Race 1", "2026-10-17T15:10:00Z")]
    [InlineData("Brands Hatch GP Race 3", "2026-10-18T15:15:00Z")]
    public async Task PublishedResultMeansFinished(string title, string anchor)
    {
        var state = await Create().GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.BritishSuperbike, title, anchor), default);

        Assert.Equal(MotorsportLivePhase.Finished, state!.Phase);
        Assert.Null(state.FinishedAt);
    }

    [Fact]
    public async Task MissingResultIsUnknownButStillMatched()
    {
        var page = MotorsportFixtures.Read("bsb-event-264204.html")
            .Replace(">Race 3 Result<", ">Grid for Race 3 (provisional)<");

        var state = await Create(() => page).GetSessionStateAsync(MotorsportProviders.Query(
            MotorsportSeries.BritishSuperbike, "Brands Hatch GP Race 3", "2026-10-18T15:15:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Unknown, state!.Phase);
        Assert.Equal("bsb:264204:Race 3", state.SessionKey);
    }

    [Theory]
    [InlineData("Race 1 Part 1 Result", MotorsportLivePhase.Unknown)]
    [InlineData("Race 1 Lap Chart Result", MotorsportLivePhase.Unknown)]
    [InlineData("Race 1 Provisional Result", MotorsportLivePhase.Finished)]
    [InlineData("Race 1 Result", MotorsportLivePhase.Finished)]
    public async Task OnlyTheWholeResultNameCounts(string name, MotorsportLivePhase expected)
    {
        var page = MotorsportFixtures.Read("bsb-event-264204.html").Replace(">Race 1 Result<", $">{name}<");

        var state = await Create(() => page).GetSessionStateAsync(MotorsportProviders.Query(
            MotorsportSeries.BritishSuperbike, "Brands Hatch GP Race 1", "2026-10-17T15:10:00Z"), default);

        Assert.Equal(expected, state!.Phase);
    }

    [Fact]
    public async Task QualifyingIsNotReported()
    {
        Assert.Null(await Create().GetSessionStateAsync(MotorsportProviders.Query(
            MotorsportSeries.BritishSuperbike, "Brands Hatch GP Qualifying", "2026-10-17T10:00:00Z"), default));
    }
}
