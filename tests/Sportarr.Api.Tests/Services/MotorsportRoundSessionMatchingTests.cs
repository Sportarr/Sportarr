using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for a Qualifying release being imported as the race, three minutes
/// after the race started:
///
/// <code>
/// [RSS Sync] ✓ Grabbed: IndyCar.Series.2026.Round15.WashingtonDC.Qualifying...
///                       for Freedom 250 Grand Prix of Washington, D.C.
/// </code>
///
/// ValidateRelease's session gate only runs once
/// EventPartDetector.DetectMotorsportSessionType can name the EVENT's session,
/// and that reads MotorsportSessionsByLeague — which held only Formula 1,
/// F1 Academy, British Superbike and MotoGP. Every other motorsport league
/// returned null and skipped the gate entirely.
///
/// Two halves, tested here:
/// 1. IndyCar, NASCAR and Formula E now have session tables (which also
///    un-hides the league's session-type selector, since the UI reads the same
///    table via GetMotorsportSessionTypes).
/// 2. A NASCAR race event is titled by sponsor alone ("Coke Zero Sugar 400"),
///    so no table can name its session. When the league models sessions as
///    separate events and this event names none, the event IS the round, and a
///    release that declares practice or qualifying is not it.
/// </summary>
public class MotorsportRoundSessionMatchingTests
{
    private readonly ReleaseMatchingService _svc;

    public MotorsportRoundSessionMatchingTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _svc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static Event MotorsportEvent(string title, string league, DateTime date) => new()
    {
        Id = 1,
        Title = title,
        Sport = "Motorsport",
        EventDate = date,
        Round = "15",
        League = new League { Id = 1, Name = league, Sport = "Motorsport" }
    };

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private const string QualifyingRelease =
        "IndyCar.Series.2026.Round15.WashingtonDC.Qualifying.STAN.WEB-DL.1080p.H264.English-MWR";
    private const string RaceRelease =
        "IndyCar.Series.2026.Round15.WashingtonDC.Race.STAN.WEB-DL.1080p.H264.English-MWR";

    // --- Event-side session detection, per league ---

    [Theory]
    [InlineData("Freedom 250 Grand Prix of Washington, D.C.", "IndyCar Series", "Race")]
    [InlineData("Freedom 250 Grand Prix of Washington, D.C. - Qualifying", "IndyCar Series", "Qualifying")]
    [InlineData("Freedom 250 Grand Prix of Washington, D.C. - Practice 2", "IndyCar Series", "Practice 2")]
    [InlineData("Snap On Milwaukee 250 Race #1", "IndyCar Series", "Race 1")]
    [InlineData("Snap On Milwaukee 250 Race #2 - Qualifying", "IndyCar Series", "Qualifying")]
    [InlineData("Daytona 500 - Qualifying", "NASCAR Cup Series", "Qualifying")]
    [InlineData("Daytona 500 - Final Practice", "NASCAR Xfinity Series", "Practice 1")]
    [InlineData("Tokyo ePrix", "Formula E", "Race")]
    [InlineData("Tokyo ePrix - Free Practice 3", "Formula E", "Practice 3")]
    [InlineData("London E Prix - Race 2", "Formula E", "Race 2")]
    public void EventTitles_ResolveToTheirSession(string title, string league, string expected)
    {
        EventPartDetector.DetectMotorsportSessionType(title, league).Should().Be(expected);
    }

    [Fact]
    public void SessionTypeSelector_IsPopulatedForTheNewLeagues()
    {
        // Same table drives the UI: an empty list hides the selector, which is
        // why MonitoredSessionTypes could not be set on these leagues at all.
        EventPartDetector.GetMotorsportSessionTypes("IndyCar Series").Should().Contain("Race");
        EventPartDetector.GetMotorsportSessionTypes("NASCAR Cup Series").Should().Contain("Qualifying");
        EventPartDetector.GetMotorsportSessionTypes("Formula E").Should().Contain("Practice 1");
    }

    [Fact]
    public void FormulaOne_SessionDetectionIsUnchanged()
    {
        EventPartDetector.DetectMotorsportSessionType("Monaco Grand Prix", "Formula 1").Should().Be("Race");
        EventPartDetector.DetectMotorsportSessionType("Monaco Grand Prix - Sprint Qualifying", "Formula 1")
            .Should().Be("Sprint Qualifying");
        EventPartDetector.DetectMotorsportSessionFromFilename("Formula1.2025.Abu.Dhabi.FP1.1080p-GROUP")
            .Should().Be("Practice 1");
    }

    // --- The grab that started this ---

    [Fact]
    public void QualifyingRelease_IsHardRejectedAgainstTheRaceEvent()
    {
        var result = _svc.ValidateRelease(
            Rel(QualifyingRelease),
            MotorsportEvent("Freedom 250 Grand Prix of Washington, D.C.", "IndyCar Series",
                new DateTime(2026, 8, 23, 17, 0, 0, DateTimeKind.Utc)));

        result.IsHardRejection.Should().BeTrue();
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void RaceRelease_StillMatchesTheRaceEvent()
    {
        var result = _svc.ValidateRelease(
            Rel(RaceRelease),
            MotorsportEvent("Freedom 250 Grand Prix of Washington, D.C.", "IndyCar Series",
                new DateTime(2026, 8, 23, 17, 0, 0, DateTimeKind.Utc)));

        result.IsHardRejection.Should().BeFalse();
    }

    // --- The sponsor-titled round, which no table can name ---

    [Fact]
    public void SponsorTitledRound_HasNoDetectableSession()
    {
        // Documents why the fallback below is needed rather than more patterns:
        // there is nothing in "Coke Zero Sugar 400" to match on.
        EventPartDetector.DetectMotorsportSessionType("Coke Zero Sugar 400", "NASCAR Cup Series")
            .Should().BeNull();
    }

    [Fact]
    public void PracticeRelease_IsHardRejectedAgainstASponsorTitledRound()
    {
        var result = _svc.ValidateRelease(
            Rel("NASCAR.Cup.Series.2026.Daytona.Practice.1080p.WEB-DL.H264-MWR"),
            MotorsportEvent("Coke Zero Sugar 400", "NASCAR Cup Series",
                new DateTime(2026, 8, 29, 14, 30, 0, DateTimeKind.Utc)));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("the event is the round itself"));
    }

    [Fact]
    public void RaceRelease_IsNotRejectedAgainstASponsorTitledRound()
    {
        var result = _svc.ValidateRelease(
            Rel("NASCAR.Cup.Series.2026.Daytona.Race.1080p.WEB-DL.H264-MWR"),
            MotorsportEvent("Coke Zero Sugar 400", "NASCAR Cup Series",
                new DateTime(2026, 8, 29, 14, 30, 0, DateTimeKind.Utc)));

        result.Rejections.Should().NotContain(r => r.Contains("the event is the round itself"));
    }

    [Fact]
    public void LeagueWithoutASessionTable_IsLeftPermissive()
    {
        // No table means sessions may not be separate events at all; rejecting
        // there would be a guess, so behaviour is unchanged for those leagues.
        var result = _svc.ValidateRelease(
            Rel("WEC.2026.Fuji.Qualifying.1080p.WEB-DL.H264-MWR"),
            MotorsportEvent("6 Hours of Fuji", "FIA World Endurance Championship",
                new DateTime(2026, 9, 27, 2, 0, 0, DateTimeKind.Utc)));

        result.Rejections.Should().NotContain(r => r.Contains("the event is the round itself"));
    }
}
