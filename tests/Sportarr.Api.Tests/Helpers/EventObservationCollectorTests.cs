using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// An unmatched release is reported only when it names a monitored league
/// with a canonical id and carries a date near now. Each gate is pinned.
/// </summary>
public class EventObservationCollectorTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    private static League League(int id, string name, string? externalId = "lg-000032", string? alternate = null, string sport = "American Football") => new()
    {
        Id = id,
        Name = name,
        Sport = sport,
        ExternalId = externalId,
        AlternateName = alternate,
    };

    private static SportsParseResult Parsed(string? organization = "NFL", DateTime? date = null, string? leagueToken = null, string? sport = null) => new()
    {
        OriginalFilename = "release",
        Organization = organization,
        Sport = sport,
        EventDate = date ?? new DateTime(2026, 10, 26),
        SportarrLeagueId = leagueToken,
    };

    private static readonly List<League> Nfl = new() { League(1, "NFL") };

    [Fact]
    public void ReportsALeagueMatchedByName()
    {
        var observation = EventObservationCollector.FromUnmatchedRelease(
            " NFL.2026.10.26.Bills.vs.Chiefs.1080p ", Parsed(), Nfl, Now);

        observation.Should().NotBeNull();
        observation!.LeagueId.Should().Be("lg-000032");
        observation.ReleaseTitle.Should().Be("NFL.2026.10.26.Bills.vs.Chiefs.1080p");
        observation.ObservedDate.Should().Be(new DateTime(2026, 10, 26));
        observation.DedupeKey.Should().Be("lg-000032|nfl.2026.10.26.bills.vs.chiefs.1080p");
    }

    [Fact]
    public void ReportsALeagueNamedByACanonicalToken()
    {
        var leagues = new List<League> { League(1, "National Football League", "lg-000032") };

        var observation = EventObservationCollector.FromUnmatchedRelease(
            "sportarr-lg-000032 week 8", Parsed(organization: null, leagueToken: "lg-000032"), leagues, Now);

        observation!.LeagueId.Should().Be("lg-000032");
    }

    [Fact]
    public void ReportsALeagueMatchedByAlternateName()
    {
        var leagues = new List<League> { League(1, "English Premier League", "lg-000001", alternate: "EPL") };

        var observation = EventObservationCollector.FromUnmatchedRelease(
            "EPL.2026.10.26.Arsenal.vs.Spurs", Parsed(organization: "EPL"), leagues, Now);

        observation!.LeagueId.Should().Be("lg-000001");
    }

    [Fact]
    public void SkipsAReleaseThatNamesNoMonitoredLeague()
    {
        EventObservationCollector.FromUnmatchedRelease("NBA.2026.10.26.Lakers", Parsed(organization: "NBA"), Nfl, Now)
            .Should().BeNull();
        EventObservationCollector.FromUnmatchedRelease("Bills.vs.Chiefs", Parsed(organization: null), Nfl, Now)
            .Should().BeNull();
    }

    [Fact]
    public void SkipsWhenTwoMonitoredLeaguesShareTheLabel()
    {
        var leagues = new List<League> { League(1, "NFL", "lg-000032"), League(2, "NFL Preseason", "lg-000900", alternate: "NFL") };

        EventObservationCollector.FromUnmatchedRelease("NFL.2026.10.26.Bills.vs.Chiefs", Parsed(), leagues, Now)
            .Should().BeNull();
    }

    [Fact]
    public void SkipsALeagueWithoutACanonicalId()
    {
        var leagues = new List<League> { League(1, "NFL", externalId: null) };

        EventObservationCollector.FromUnmatchedRelease("NFL.2026.10.26.Bills.vs.Chiefs", Parsed(), leagues, Now)
            .Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-09-26")]
    [InlineData("2026-11-04")]
    public void SkipsReleasesWithNoDateOrADateFarFromNow(string? date)
    {
        var parsed = Parsed(date: date == null ? null : DateTime.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        if (date == null)
            parsed.EventDate = null;

        EventObservationCollector.FromUnmatchedRelease("NFL.Bills.vs.Chiefs", parsed, Nfl, Now)
            .Should().BeNull();
    }

    [Theory]
    [InlineData("2026-09-27")]
    [InlineData("2026-11-03")]
    public void KeepsReleasesAtTheEdgeOfTheWindow(string date)
    {
        EventObservationCollector.FromUnmatchedRelease("NFL.Bills.vs.Chiefs", Parsed(date: DateTime.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), Nfl, Now)
            .Should().NotBeNull();
    }

    [Fact]
    public void SkipsEmptyTitlesAndMissingParses()
    {
        EventObservationCollector.FromUnmatchedRelease("  ", Parsed(), Nfl, Now).Should().BeNull();
        EventObservationCollector.FromUnmatchedRelease("NFL.Bills.vs.Chiefs", null, Nfl, Now).Should().BeNull();
    }

    [Fact]
    public void ReportsAReleaseThatTheRealParserReads()
    {
        const string title = "NFL.2026.10.26.Buffalo.Bills.vs.Kansas.City.Chiefs.1080p.WEB.h264";
        var parsed = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()).Parse(title);

        var observation = EventObservationCollector.FromUnmatchedRelease(title, parsed, Nfl, Now);

        observation!.LeagueId.Should().Be("lg-000032");
    }

    [Theory]
    [InlineData("Champions League", "AFC Champions League", "Soccer")]
    [InlineData("Premier League", "Indian Premier League", "Cricket")]
    [InlineData("Boxing", "Matchroom Boxing", "Fighting")]
    public void SkipsALeagueThatOnlyContainsTheLabel(string organization, string leagueName, string sport)
    {
        var leagues = new List<League> { League(1, leagueName, "lg-000500", sport: sport) };

        EventObservationCollector.FromUnmatchedRelease("Release.2026.10.26", Parsed(organization: organization), leagues, Now)
            .Should().BeNull();
    }

    [Fact]
    public void SkipsALeagueOfAnotherSport()
    {
        var leagues = new List<League> { League(1, "Premier League", "lg-000600", sport: "Cricket") };

        EventObservationCollector.FromUnmatchedRelease(
                "EPL.2026.10.26.Arsenal.vs.Spurs", Parsed(organization: "Premier League", sport: "Soccer"), leagues, Now)
            .Should().BeNull();
    }

    [Fact]
    public void CutsLongTitlesToTheServerLimit()
    {
        var title = "NFL.2026.10.26." + new string('x', 400);

        var observation = EventObservationCollector.FromUnmatchedRelease(title, Parsed(), Nfl, Now);

        observation!.ReleaseTitle.Should().HaveLength(EventObservationCollector.MaxTitleLength);
    }

    [Theory]
    [InlineData("2026-10-25")]
    [InlineData("2026-10-26")]
    [InlineData("2026-10-27")]
    public void SkipsADayOnWhichTheLeagueAlreadyHasAnEvent(string eventDay)
    {
        var known = new HashSet<(int, DateTime)>
        {
            (1, DateTime.ParseExact(eventDay, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
        };

        EventObservationCollector.FromUnmatchedRelease("NFL.2026.10.26.Bills.vs.Chiefs", Parsed(), Nfl, Now, known)
            .Should().BeNull();
    }

    [Fact]
    public void KeepsADayTwoDaysFromTheNearestEvent()
    {
        var known = new HashSet<(int, DateTime)> { (1, new DateTime(2026, 10, 24)), (2, new DateTime(2026, 10, 26)) };

        EventObservationCollector.FromUnmatchedRelease("NFL.2026.10.26.Bills.vs.Chiefs", Parsed(), Nfl, Now, known)
            .Should().NotBeNull();
    }

    [Fact]
    public void ReportsAFightReleaseUnderACombatLeague()
    {
        const string title = "UFC.310.2026.10.26.Pantoja.vs.Asakura.1080p.WEB.h264";
        var parsed = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()).Parse(title);
        var leagues = new List<League> { League(1, "UFC", "lg-000700", alternate: "Ultimate Fighting Championship", sport: "Combat") };

        var observation = EventObservationCollector.FromUnmatchedRelease(title, parsed, leagues, Now);

        parsed.Sport.Should().Be("Fighting");
        observation!.LeagueId.Should().Be("lg-000700");
    }
}
