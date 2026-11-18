using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services.MotorsportLive;

namespace Sportarr.Api.Tests.Services.MotorsportLive;

public class MotorsportSeriesMapTests
{
    // League names and alternate names as the metadata service gives them.
    [Theory]
    [InlineData("MotoGP", null, MotorsportSeries.MotoGp)]
    [InlineData("Moto2", null, MotorsportSeries.Moto2)]
    [InlineData("Moto3", null, MotorsportSeries.Moto3)]
    [InlineData("SBK", "World Superbike, SBK, WSB, WSBK", MotorsportSeries.WorldSbk)]
    [InlineData("Superbike World Championship", null, MotorsportSeries.WorldSbk)]
    [InlineData("My bikes", "WorldSBK", MotorsportSeries.WorldSbk)]
    [InlineData("WorldSSP", "Supersport World Championship", MotorsportSeries.WorldSsp)]
    [InlineData("Sportbike World Championship", null, MotorsportSeries.WorldSpb)]
    [InlineData("Formula 1", "F1, Formula One, Formula1, Formula 1, Formula-1", MotorsportSeries.Formula1)]
    [InlineData("IndyCar Series", "NTT IndyCar Series", MotorsportSeries.IndyCar)]
    [InlineData("British Superbike Championship", null, MotorsportSeries.BritishSuperbike)]
    [InlineData("ZYN British Superbike Championship", null, MotorsportSeries.BritishSuperbike)]
    [InlineData("Bennetts British Superbikes", null, MotorsportSeries.BritishSuperbike)]
    public void KnownLeaguesMapToTheirSeries(string name, string? alternate, MotorsportSeries expected)
    {
        Assert.Equal(expected, MotorsportSeriesMap.Resolve(name, alternate));
    }

    [Theory]
    [InlineData("MotoE World Championship")]
    [InlineData("F1 Academy")]
    [InlineData("DTM")]
    [InlineData("Formula E")]
    [InlineData("Quattro Group British Supersport Championship")]
    [InlineData("British Superbike Academy")]
    [InlineData("NASCAR Cup Series")]
    public void OtherLeaguesAreNotMapped(string name)
    {
        Assert.Null(MotorsportSeriesMap.Resolve(name, null));
    }

    // Titles from the metadata service for the 2026 season.
    [Theory]
    [InlineData(MotorsportSeries.MotoGp, "Qatar GP", MotorsportSessionKind.Race, null)]
    [InlineData(MotorsportSeries.MotoGp, "Qatar Sprint Race", MotorsportSessionKind.Sprint, null)]
    [InlineData(MotorsportSeries.MotoGp, "Qatar Qualifying 2", MotorsportSessionKind.Qualifying, 2)]
    [InlineData(MotorsportSeries.MotoGp, "Qatar Free Practice 1", MotorsportSessionKind.Practice, 1)]
    [InlineData(MotorsportSeries.MotoGp, "Qatar Practice", MotorsportSessionKind.Practice, null)]
    [InlineData(MotorsportSeries.Moto2, "Grand Prix of Qatar Moto2", MotorsportSessionKind.Race, null)]
    [InlineData(MotorsportSeries.Moto2, "Repsol Grand Prix of Portugal", MotorsportSessionKind.Race, null)]
    [InlineData(MotorsportSeries.Moto3, "Grande Prémio de Portugal Moto3", MotorsportSessionKind.Race, null)]
    [InlineData(MotorsportSeries.WorldSbk, "Tissot Estoril Round Race 1", MotorsportSessionKind.Race, 1)]
    [InlineData(MotorsportSeries.WorldSbk, "Tissot Estoril Round Superpole Race", MotorsportSessionKind.SuperpoleRace, null)]
    [InlineData(MotorsportSeries.WorldSsp, "Tissot Estoril Round Race 2", MotorsportSessionKind.Race, 2)]
    [InlineData(MotorsportSeries.WorldSsp, "Tissot Estoril Round Superpole", MotorsportSessionKind.Superpole, null)]
    [InlineData(MotorsportSeries.WorldSsp, "Tissot Estoril Round Warm Up 1", MotorsportSessionKind.WarmUp, 1)]
    [InlineData(MotorsportSeries.WorldSsp, "Tissot Estoril Round Free Practice", MotorsportSessionKind.Practice, null)]
    [InlineData(MotorsportSeries.Formula1, "Las Vegas Grand Prix", MotorsportSessionKind.Race, null)]
    [InlineData(MotorsportSeries.Formula1, "Singapore Grand Prix Sprint Qualifying", MotorsportSessionKind.SprintQualifying, null)]
    [InlineData(MotorsportSeries.Formula1, "Singapore Grand Prix Sprint", MotorsportSessionKind.Sprint, null)]
    [InlineData(MotorsportSeries.Formula1, "São Paulo Grand Prix Practice 3", MotorsportSessionKind.Practice, 3)]
    [InlineData(MotorsportSeries.IndyCar, "Grand Prix of Monterey", MotorsportSessionKind.Race, null)]
    [InlineData(MotorsportSeries.BritishSuperbike, "Brands Hatch GP Race 2", MotorsportSessionKind.Race, 2)]
    public void TitlesNameTheirSession(MotorsportSeries series, string title, MotorsportSessionKind kind, int? number)
    {
        var query = MotorsportSeriesMap.DescribeSession(series, title, DateTimeOffset.UtcNow, null);

        Assert.NotNull(query);
        Assert.Equal(kind, query!.Kind);
        Assert.Equal(number, query.Number);
    }

    [Theory]
    [InlineData(MotorsportSeries.MotoGp, "Qatar Moto2 Race")]
    [InlineData(MotorsportSeries.MotoGp, "Qatar Moto3 Race")]
    [InlineData(MotorsportSeries.MotoGp, "Qatar MotoE Race 1")]
    [InlineData(MotorsportSeries.Moto2, "Grand Prix of Qatar Moto3")]
    [InlineData(MotorsportSeries.WorldSbk, "Tissot Estoril Round WorldSSP Race 1")]
    [InlineData(MotorsportSeries.WorldSbk, "Tissot Estoril Round Sportbike Race 2")]
    [InlineData(MotorsportSeries.WorldSsp, "Tissot Estoril Round Superbike Race 1")]
    [InlineData(MotorsportSeries.Formula1, "Las Vegas Grand Prix F1 Academy Race")]
    [InlineData(MotorsportSeries.Formula1, "Monaco Formula 2 Sprint")]
    [InlineData(MotorsportSeries.IndyCar, "Grand Prix of Monterey Indy NXT")]
    [InlineData(MotorsportSeries.BritishSuperbike, "Brands Hatch GP Supersport Race 1")]
    public void TitleNamingAnotherClassIsNotMatched(MotorsportSeries series, string title)
    {
        Assert.Null(MotorsportSeriesMap.DescribeSession(series, title, DateTimeOffset.UtcNow, null));
    }

    [Theory]
    [InlineData(MotorsportSeries.Moto2, "Grand Prix of Qatar Moto2")]
    [InlineData(MotorsportSeries.MotoGp, "Qatar MotoGP Sprint Race")]
    [InlineData(MotorsportSeries.BritishSuperbike, "Brands Hatch Superbike Race 1")]
    [InlineData(MotorsportSeries.Formula1, "Formula 1 Las Vegas Grand Prix")]
    public void TitleNamingItsOwnClassIsMatched(MotorsportSeries series, string title)
    {
        Assert.NotNull(MotorsportSeriesMap.DescribeSession(series, title, DateTimeOffset.UtcNow, null));
    }

    [Theory]
    [InlineData(MotorsportSeries.MotoGp, "Sepang Test Day 1")]
    [InlineData(MotorsportSeries.MotoGp, "Qatar After the Flag")]
    [InlineData(MotorsportSeries.Formula1, "Las Vegas Grand Prix Press Conference")]
    public void ShowsAndTestsNameNoSession(MotorsportSeries series, string title)
    {
        Assert.Null(MotorsportSeriesMap.DescribeSession(series, title, DateTimeOffset.UtcNow, null));
    }
}

public class MotorsportLiveStatusServiceTests
{
    private sealed class ScriptedProvider : IMotorsportLiveProvider
    {
        public MotorsportSourceState? Next { get; set; }
        public Exception? Throw { get; set; }
        public int Calls { get; private set; }
        public string Source => "test-source";
        public bool Supports(MotorsportSeries series) => true;

        public Task<MotorsportSourceState?> GetSessionStateAsync(MotorsportSessionQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw != null)
                throw Throw;
            return Task.FromResult(Next);
        }
    }

    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-08T16:00:00Z");

    private static (MotorsportLiveStatusService Service, ScriptedProvider Provider, ManualTime Time) Create()
    {
        var provider = new ScriptedProvider();
        var time = new ManualTime(T0);
        return (new MotorsportLiveStatusService(new[] { provider }, NullLogger<MotorsportLiveStatusService>.Instance, time),
            provider, time);
    }

    private static Event Race(string league = "MotoGP") => new()
    {
        Title = "Qatar GP", Sport = "Motorsport", EventDate = T0.UtcDateTime,
        League = new League { Name = league, Sport = "Motorsport" }
    };

    private static DvrRecording Recording(DateTimeOffset actualStart) => new()
    {
        Id = 1, Title = "Qatar GP", ScheduledStart = T0.UtcDateTime, ScheduledEnd = T0.AddHours(1).UtcDateTime,
        ActualStart = actualStart.UtcDateTime, Status = DvrRecordingStatus.Recording
    };

    private static MotorsportSourceState State(MotorsportLivePhase phase, DateTimeOffset fetchedAt,
        DateTimeOffset? finishedAt = null, DateTimeOffset? sessionStart = null) =>
        new(phase, "QAT:MGP:Race:", "QAT MGP Grand Prix (RAC)", finishedAt, fetchedAt, sessionStart);

    [Theory]
    [InlineData(18, 30, MotorsportLivePhase.Live)]
    // After the wait, a short race is unknown. It never becomes a finish.
    [InlineData(18, 70, MotorsportLivePhase.Unknown)]
    // 30 minutes is short for a Grand Prix of about 41 minutes.
    [InlineData(35, 40, MotorsportLivePhase.Live)]
    [InlineData(41, 45, MotorsportLivePhase.Finished)]
    public async Task RaceThatStopsShortWaitsForARestart(int finishMinutes, int nowMinutes, MotorsportLivePhase expected)
    {
        // The Grand Prix runs about 41 minutes. A finish after 18 minutes is a
        // red flag, and the restart can come later as a new session.
        // The race is seen before it starts, so its live state marks the start.
        var (service, provider, time) = Create();
        provider.Next = State(MotorsportLivePhase.NotStarted, T0, sessionStart: T0);
        await service.GetStatusAsync(Race(), Recording(T0), default);
        provider.Next = State(MotorsportLivePhase.Live, T0.AddMinutes(5), sessionStart: T0);
        await service.GetStatusAsync(Race(), Recording(T0), default);
        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(nowMinutes), T0.AddMinutes(finishMinutes), T0);
        time.Now = T0.AddMinutes(nowMinutes);

        var status = await service.GetStatusAsync(Race(), Recording(T0), default);

        Assert.Equal(expected, status!.Phase);
    }

    [Fact]
    public async Task RedFlagAfterADelayedStartCountsFromTheFirstLiveState()
    {
        // The race was due at T0 but went live 20 minutes late. A red flag
        // came 28 minutes later. From the scheduled start that is 48 minutes,
        // which looks like a full race.
        var (service, provider, time) = Create();
        var recording = Recording(T0);
        provider.Next = State(MotorsportLivePhase.NotStarted, T0.AddMinutes(10), sessionStart: T0);
        await service.GetStatusAsync(Race(), recording, default);
        provider.Next = State(MotorsportLivePhase.Live, T0.AddMinutes(20), sessionStart: T0);
        await service.GetStatusAsync(Race(), recording, default);
        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(50), T0.AddMinutes(48), T0);
        time.Now = T0.AddMinutes(50);

        var status = await service.GetStatusAsync(Race(), recording, default);

        Assert.Equal(MotorsportLivePhase.Live, status!.Phase);
    }

    [Fact]
    public async Task SourceGapCannotHideARedFlag()
    {
        // Live from the start, a red flag at 20 minutes, the source fails
        // from 18 to 34 minutes, and the first finished state comes at 34.
        // From the start to 34 looks like a full race, but the race may have
        // stopped at 18.
        var (service, provider, time) = Create();
        var recording = Recording(T0);
        provider.Next = State(MotorsportLivePhase.NotStarted, T0.AddMinutes(-1), sessionStart: T0);
        await service.GetStatusAsync(Race(), recording, default);
        for (var minute = 0; minute <= 18; minute += 2)
        {
            provider.Next = State(MotorsportLivePhase.Live, T0.AddMinutes(minute), sessionStart: T0);
            time.Now = T0.AddMinutes(minute);
            await service.GetStatusAsync(Race(), recording, default);
        }
        provider.Throw = new HttpRequestException("source down");
        time.Now = T0.AddMinutes(26);
        Assert.Equal(MotorsportLivePhase.Unknown, (await service.GetStatusAsync(Race(), recording, default))!.Phase);
        provider.Throw = null;

        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(34), sessionStart: T0);
        time.Now = T0.AddMinutes(34);
        var first = await service.GetStatusAsync(Race(), recording, default);
        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(36), sessionStart: T0);
        time.Now = T0.AddMinutes(36);
        var second = await service.GetStatusAsync(Race(), recording, default);

        Assert.Equal(MotorsportLivePhase.Live, first!.Phase);
        Assert.Equal(MotorsportLivePhase.Live, second!.Phase);
        Assert.Null(second.FinishedAt);
    }

    [Theory]
    [InlineData("2026-11-08T16:00:00", false)]
    [InlineData("2026-11-08T00:00:00", true)]
    public async Task BritishSuperbikesUsesTheEventTimeForTheDelayedBroadcastCheck(string eventDate, bool during)
    {
        // The results give no session time. A recording 40 minutes after the
        // event time is a delayed broadcast. A date with no time skips the check.
        var provider = new ScriptedProvider();
        var time = new ManualTime(T0.AddMinutes(80));
        var service = new MotorsportLiveStatusService(new[] { provider },
            NullLogger<MotorsportLiveStatusService>.Instance, time);
        var evt = new Event
        {
            Title = "Brands Hatch GP Race 1", Sport = "Motorsport", EventDate = DateTime.Parse(eventDate),
            League = new League { Name = "British Superbike Championship", Sport = "Motorsport" }
        };
        var recording = Recording(T0.AddMinutes(40));
        recording.ScheduledStart = T0.AddMinutes(40).UtcDateTime;
        provider.Next = new(MotorsportLivePhase.Unknown, "bsb:1:Race 1", "Race 1", null, T0.AddMinutes(45));
        await service.GetStatusAsync(evt, recording, default);
        provider.Next = new(MotorsportLivePhase.Finished, "bsb:1:Race 1", "Race 1", null, T0.AddMinutes(80));

        var status = await service.GetStatusAsync(evt, recording, default);

        Assert.Equal(MotorsportLivePhase.Finished, status!.Phase);
        Assert.Equal(during, status.FinishedDuringRecording);
    }

    [Fact]
    public async Task FirstLookInMidRaceDoesNotMakeAnOverrunLookShort()
    {
        // With Early Finish Guard off, the first look comes at the scheduled
        // end. The race ran 50 minutes from its start, which is a full race.
        var (service, provider, time) = Create();
        var recording = Recording(T0);
        provider.Next = State(MotorsportLivePhase.Live, T0.AddMinutes(40), sessionStart: T0);
        time.Now = T0.AddMinutes(40);
        await service.GetStatusAsync(Race(), recording, default);
        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(50), sessionStart: T0);
        time.Now = T0.AddMinutes(50);

        var status = await service.GetStatusAsync(Race(), recording, default);

        Assert.Equal(MotorsportLivePhase.Finished, status!.Phase);
    }

    [Fact]
    public async Task RestartAfterTheWaitFinishesNormally()
    {
        var (service, provider, time) = Create();
        var recording = Recording(T0);
        provider.Next = State(MotorsportLivePhase.Live, T0.AddMinutes(2), sessionStart: T0);
        await service.GetStatusAsync(Race(), recording, default);
        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(70), T0.AddMinutes(15), T0);
        time.Now = T0.AddMinutes(70);
        Assert.Equal(MotorsportLivePhase.Unknown, (await service.GetStatusAsync(Race(), recording, default))!.Phase);

        provider.Next = State(MotorsportLivePhase.Live, T0.AddMinutes(75), sessionStart: T0);
        time.Now = T0.AddMinutes(75);
        Assert.Equal(MotorsportLivePhase.Live, (await service.GetStatusAsync(Race(), recording, default))!.Phase);

        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(100), T0.AddMinutes(98), T0);
        time.Now = T0.AddMinutes(100);
        Assert.Equal(MotorsportLivePhase.Finished, (await service.GetStatusAsync(Race(), recording, default))!.Phase);
    }

    [Fact]
    public async Task RecordingThatStartsLongAfterTheSessionIsADelayedBroadcast()
    {
        var (service, provider, time) = Create();
        var delayed = Recording(T0.AddMinutes(30));
        delayed.ScheduledStart = T0.AddMinutes(30).UtcDateTime;
        provider.Next = State(MotorsportLivePhase.Live, T0.AddMinutes(31), sessionStart: T0);
        await service.GetStatusAsync(Race(), delayed, default);
        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(75), T0.AddMinutes(72), T0);
        time.Now = T0.AddMinutes(75);

        var status = await service.GetStatusAsync(Race(), delayed, default);

        Assert.Equal(MotorsportLivePhase.Finished, status!.Phase);
        Assert.False(status.FinishedDuringRecording);
    }

    [Fact]
    public async Task UnmappedLeagueGetsNoStatusAndNoRequest()
    {
        var (service, provider, _) = Create();

        Assert.Null(await service.GetStatusAsync(Race("Premier League"), Recording(T0), default));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task FinishWithoutTimeUsesFirstSeenAndNeedsASecondFetch()
    {
        var (service, provider, time) = Create();
        var recording = Recording(T0.AddMinutes(-10));

        // The race is seen live from its start, so 45 minutes is a full race.
        provider.Next = State(MotorsportLivePhase.Live, T0);
        Assert.Equal(MotorsportLivePhase.Live, (await service.GetStatusAsync(Race(), recording, default))!.Phase);
        provider.Next = State(MotorsportLivePhase.Live, T0.AddMinutes(40));
        time.Now = T0.AddMinutes(40);
        Assert.Equal(MotorsportLivePhase.Live, (await service.GetStatusAsync(Race(), recording, default))!.Phase);

        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(45));
        time.Now = T0.AddMinutes(45);
        var first = await service.GetStatusAsync(Race(), recording, default);
        Assert.Equal(MotorsportLivePhase.Finished, first!.Phase);
        Assert.Equal(T0.AddMinutes(45), first.FinishedAt);
        Assert.False(first.FinishedAtReported);
        Assert.False(first.Confirmed);
        Assert.True(first.FinishedDuringRecording);

        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(46.5));
        time.Now = T0.AddMinutes(46.5);
        var second = await service.GetStatusAsync(Race(), recording, default);
        Assert.True(second!.Confirmed);
        Assert.Equal(T0.AddMinutes(45), second.FinishedAt);
    }

    [Fact]
    public async Task FinishSeenOnlyBeforeAnyUnfinishedStateIsNotDuringTheRecording()
    {
        // A delayed broadcast starts after the live race is over.
        var (service, provider, time) = Create();
        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(5));
        time.Now = T0.AddMinutes(5);

        var status = await service.GetStatusAsync(Race(), Recording(T0), default);

        Assert.Equal(MotorsportLivePhase.Finished, status!.Phase);
        Assert.False(status.FinishedDuringRecording);
    }

    [Theory]
    [InlineData(-30, false)]
    [InlineData(20, true)]
    public async Task ReportedFinishTimeIsComparedWithTheRecordingStart(int finishMinutes, bool during)
    {
        var (service, provider, time) = Create();
        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(30), T0.AddMinutes(finishMinutes));
        time.Now = T0.AddMinutes(30);

        var status = await service.GetStatusAsync(Race(), Recording(T0), default);

        Assert.True(status!.FinishedAtReported);
        Assert.Equal(T0.AddMinutes(finishMinutes), status.FinishedAt);
        Assert.Equal(during, status.FinishedDuringRecording);
    }

    [Fact]
    public async Task ProviderErrorIsUnknown()
    {
        var (service, provider, _) = Create();
        provider.Throw = new HttpRequestException("boom");

        var status = await service.GetStatusAsync(Race(), Recording(T0), default);

        Assert.Equal(MotorsportLivePhase.Unknown, status!.Phase);
    }

    [Fact]
    public async Task UnfinishedStateResetsTheFinishConfirmation()
    {
        var (service, provider, time) = Create();
        var recording = Recording(T0);
        provider.Next = State(MotorsportLivePhase.Live, T0.AddMinutes(10));
        await service.GetStatusAsync(Race(), recording, default);
        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(50));
        await service.GetStatusAsync(Race(), recording, default);
        provider.Next = State(MotorsportLivePhase.Live, T0.AddMinutes(52));
        await service.GetStatusAsync(Race(), recording, default);
        provider.Next = State(MotorsportLivePhase.Finished, T0.AddMinutes(60));
        time.Now = T0.AddMinutes(60);

        var status = await service.GetStatusAsync(Race(), recording, default);

        Assert.Equal(MotorsportLivePhase.Finished, status!.Phase);
        Assert.False(status.Confirmed);
        Assert.Equal(T0.AddMinutes(60), status.FinishedAt);
    }
}
