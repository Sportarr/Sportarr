using System.Net;
using System.Text.Json.Nodes;
using Sportarr.Api.Services.MotorsportLive;

namespace Sportarr.Api.Tests.Services.MotorsportLive;

/// <summary>
/// Real responses from api.motogp.pulselive.com, captured 2026-11-17. Qatar
/// is finished, Portugal has not started, and the Australian races were
/// cancelled.
/// </summary>
public class MotoGpLiveProviderTests
{
    private const string Calendar = "/motogp/v1/events?seasonYear=2026";

    private static string EventPath(string id) => $"/motogp/v1/events/{id}";

    private static string IdOf(string shortName) =>
        MotorsportFixtures.Json("motogp-events-2026.json").AsArray()
            .Single(e => e!["shortname"]!.GetValue<string>() == shortName && e["kind"]!.GetValue<string>() == "GP")!["id"]!
            .GetValue<string>();

    private static (MotoGpLiveProvider Provider, FixtureHandler Handler) Create(
        string now, Func<string>? qatar = null, Func<string>? portugal = null)
    {
        var handler = new FixtureHandler()
            .Json(Calendar, "motogp-events-2026.json")
            .Json(EventPath(IdOf("QAT")), qatar ?? (() => MotorsportFixtures.Read("motogp-event-2026-qatar.json")))
            .Json(EventPath(IdOf("POR")), portugal ?? (() => MotorsportFixtures.Read("motogp-event-2026-portugal.json")))
            .Json(EventPath(IdOf("AUS")), () =>
            {
                // The calendar entry carries the same broadcasts as the detail page.
                var aus = MotorsportFixtures.Json("motogp-events-2026.json").AsArray()
                    .Single(e => e!["shortname"]!.GetValue<string>() == "AUS");
                return aus!.ToJsonString();
            });
        var time = new ManualTime(DateTimeOffset.Parse(now));
        return (new MotoGpLiveProvider(MotorsportProviders.Fetcher(handler, time), time), handler);
    }

    private static string WithStatus(string fixture, string category, string code, string status)
    {
        var doc = MotorsportFixtures.Json(fixture);
        foreach (var b in doc["broadcasts"]!.AsArray())
        {
            if (b!["category"]!["acronym"]!.GetValue<string>() == category && b["shortname"]!.GetValue<string>() == code)
                b["status"] = status;
        }
        return doc.ToJsonString();
    }

    [Fact]
    public async Task FinishedGrandPrixReadsFinishedWithoutAFinishTime()
    {
        var (provider, _) = Create("2026-11-08T17:30:00Z");

        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.MotoGp, "Qatar GP", "2026-11-08T16:00:00Z"), default);

        Assert.NotNull(state);
        Assert.Equal(MotorsportLivePhase.Finished, state!.Phase);
        Assert.Null(state.FinishedAt);
        Assert.Contains("MGP Grand Prix (RAC)", state.SessionName);
    }

    [Fact]
    public async Task CurrentRaceIsLive()
    {
        var (provider, _) = Create("2026-11-08T16:20:00Z",
            qatar: () => WithStatus("motogp-event-2026-qatar.json", "MGP", "RAC", "CURRENT"));

        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.MotoGp, "Qatar GP", "2026-11-08T16:00:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Live, state!.Phase);
    }

    [Fact]
    public async Task Moto2RaceWithDateOnlyEventTimeMatchesTheMoto2Race()
    {
        // The metadata gives the Moto2 race at midnight on race day.
        var (provider, _) = Create("2026-11-08T15:00:00Z",
            qatar: () => WithStatus("motogp-event-2026-qatar.json", "MT2", "RAC", "CURRENT"));

        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.Moto2, "Grand Prix of Qatar Moto2", "2026-11-08T00:00:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Live, state!.Phase);
        Assert.Contains("MT2 Race (RAC)", state.SessionName);
    }

    [Fact]
    public async Task UpcomingGrandPrixIsNotStarted()
    {
        var (provider, _) = Create("2026-11-22T12:00:00Z");

        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.MotoGp, "Portugal GP", "2026-11-22T13:00:00Z"), default);

        Assert.Equal(MotorsportLivePhase.NotStarted, state!.Phase);
    }

    [Fact]
    public async Task SprintAnchoredAtQualifyingTimeStillMatchesTheSprint()
    {
        // Qualifying 2 is the nearest session to this anchor. The sprint is
        // the session the event names, so the sprint is used.
        var (provider, _) = Create("2026-11-21T11:20:00Z",
            portugal: () => WithStatus("motogp-event-2026-portugal.json", "MGP", "Q2", "CURRENT"));

        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.MotoGp, "Portugal Sprint Race", "2026-11-21T11:15:00Z"), default);

        Assert.Equal(MotorsportLivePhase.NotStarted, state!.Phase);
        Assert.Contains("(SPR)", state.SessionName);
    }

    [Fact]
    public async Task MissingRaceNeverFallsBackToANearbySessionOfAnotherKind()
    {
        var withoutRace = () =>
        {
            var doc = MotorsportFixtures.Json("motogp-event-2026-portugal.json");
            var broadcasts = doc["broadcasts"]!.AsArray();
            foreach (var b in broadcasts.Where(b => b!["shortname"]!.GetValue<string>() == "RAC").ToList())
                broadcasts.Remove(b);
            return doc.ToJsonString();
        };
        var (provider, _) = Create("2026-11-22T09:05:00Z", portugal: withoutRace);

        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.MotoGp, "Portugal GP", "2026-11-22T09:00:00Z"), default);

        Assert.Null(state);
    }

    [Fact]
    public async Task BarePracticeIsThePracticeSessionNotFreePracticeOne()
    {
        var (provider, _) = Create("2026-11-20T15:10:00Z",
            portugal: () => WithStatus("motogp-event-2026-portugal.json", "MGP", "PR", "CURRENT"));

        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.MotoGp, "Portugal Practice", "2026-11-20T15:00:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Live, state!.Phase);
        Assert.Contains("(PR)", state.SessionName);
    }

    [Fact]
    public async Task RedFlaggedRaceWaitingForRestartIsStillLive()
    {
        string Restart(string secondStatus)
        {
            var doc = MotorsportFixtures.Json("motogp-event-2026-qatar.json");
            var broadcasts = doc["broadcasts"]!.AsArray();
            var race = broadcasts.Single(b => b!["category"]!["acronym"]!.GetValue<string>() == "MGP" &&
                                              b["shortname"]!.GetValue<string>() == "RAC")!;
            var restart = JsonNode.Parse(race.ToJsonString())!;
            restart["id"] = "restart";
            restart["shortname"] = "RAC2";
            restart["status"] = secondStatus;
            broadcasts.Add(restart);
            return doc.ToJsonString();
        }

        var (waiting, _) = Create("2026-11-08T16:40:00Z", qatar: () => Restart("NOT-STARTED"));
        var (done, _) = Create("2026-11-08T17:40:00Z", qatar: () => Restart("FINISHED"));
        var query = MotorsportProviders.Query(MotorsportSeries.MotoGp, "Qatar GP", "2026-11-08T16:00:00Z");

        Assert.Equal(MotorsportLivePhase.Live, (await waiting.GetSessionStateAsync(query, default))!.Phase);
        Assert.Equal(MotorsportLivePhase.Finished, (await done.GetSessionStateAsync(query, default))!.Phase);
    }

    [Fact]
    public async Task CancelledRaceIsUnknown()
    {
        var (provider, _) = Create("2026-10-25T07:00:00Z");

        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.MotoGp, "Australia GP", "2026-10-25T05:30:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Unknown, state!.Phase);
    }

    [Fact]
    public async Task GzipBodyWithoutContentEncodingIsDecoded()
    {
        var handler = new FixtureHandler()
            .Raw(Calendar, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(MotorsportFixtures.Gzip(MotorsportFixtures.Read("motogp-events-2026.json")))
            })
            .Raw(EventPath(IdOf("QAT")), () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(MotorsportFixtures.Gzip(MotorsportFixtures.Read("motogp-event-2026-qatar.json")))
            });
        var time = new ManualTime(DateTimeOffset.Parse("2026-11-08T17:30:00Z"));
        var provider = new MotoGpLiveProvider(MotorsportProviders.Fetcher(handler, time), time);

        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.MotoGp, "Qatar GP", "2026-11-08T16:00:00Z"), default);

        Assert.Equal(MotorsportLivePhase.Finished, state!.Phase);
    }

    [Fact]
    public async Task ServerErrorGivesNoState()
    {
        var handler = new FixtureHandler().Raw(Calendar, () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var time = new ManualTime(DateTimeOffset.Parse("2026-11-08T17:30:00Z"));
        var provider = new MotoGpLiveProvider(MotorsportProviders.Fetcher(handler, time), time);

        Assert.Null(await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.MotoGp, "Qatar GP", "2026-11-08T16:00:00Z"), default));
    }

    [Fact]
    public async Task HungServerTimesOutAndGivesNoState()
    {
        // The fetcher's own 4 second limit ends the request. The test checks
        // that the request was cancelled, not the wall clock time, so a busy
        // test machine cannot fail it.
        var time = new ManualTime(DateTimeOffset.Parse("2026-11-08T17:30:00Z"));
        var handler = new HangingHandler();
        var provider = new MotoGpLiveProvider(MotorsportProviders.Fetcher(handler, time), time);

        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.MotoGp, "Qatar GP", "2026-11-08T16:00:00Z"), default)
            .WaitAsync(TimeSpan.FromMinutes(2));

        Assert.Null(state);
        Assert.True(handler.Cancelled);
    }

    [Fact]
    public async Task MatchedSessionKeepsTheFetchTimeOfItsOwnPage()
    {
        // Portugal is moved so that it overlaps Qatar. The Qatar page is
        // fetched first and cached. The Portugal page is fetched 30 seconds
        // later. The Qatar race must carry the Qatar page time.
        var calendar = () =>
        {
            var doc = MotorsportFixtures.Json("motogp-events-2026.json");
            var por = doc.AsArray().Single(e => e!["shortname"]!.GetValue<string>() == "POR")!;
            por["date_start"] = "2026-11-08T00:00:00+00:00";
            por["date_end"] = "2026-11-09T18:00:00+00:00";
            return doc.ToJsonString();
        };
        var handler = new FixtureHandler()
            .Json(Calendar, calendar)
            .Json(EventPath(IdOf("QAT")), "motogp-event-2026-qatar.json")
            .Json(EventPath(IdOf("POR")), "motogp-event-2026-portugal.json");
        var time = new ManualTime(DateTimeOffset.Parse("2026-11-05T12:00:00Z"));
        var provider = new MotoGpLiveProvider(MotorsportProviders.Fetcher(handler, time), time);
        var qatarFetch = time.Now;

        // Only Qatar is in range for this anchor.
        await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.MotoGp, "Qatar Free Practice 1", "2026-11-05T12:00:00Z"), default);
        time.Now = time.Now.AddSeconds(30);
        var state = await provider.GetSessionStateAsync(
            MotorsportProviders.Query(MotorsportSeries.MotoGp, "Qatar GP", "2026-11-08T16:00:00Z"), default);

        Assert.Contains(EventPath(IdOf("POR")), handler.Requests);
        Assert.Contains("QAT", state!.SessionKey);
        Assert.Equal(qatarFetch, state.FetchedAt);
    }

    [Fact]
    public async Task CalendarIsFetchedOnceAndLivePageIsCachedBriefly()
    {
        var (provider, handler) = Create("2026-11-08T16:20:00Z");
        var query = MotorsportProviders.Query(MotorsportSeries.MotoGp, "Qatar GP", "2026-11-08T16:00:00Z");

        await provider.GetSessionStateAsync(query, default);
        await provider.GetSessionStateAsync(query, default);

        Assert.Equal(1, handler.Requests.Count(r => r == Calendar));
        Assert.Equal(1, handler.Requests.Count(r => r == EventPath(IdOf("QAT"))));
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        public bool Cancelled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
            throw new InvalidOperationException();
        }
    }
}
