using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report follow-up: searching NBA Finals Game 5 surfaced releases for
/// Games 3, 4, and 5 with nothing separating them. Two holes stacked: the
/// parser dropped the Sportscult-style trailing day-month stamp ("... Game 5
/// 13 06 1080pEN60fps ABC") because the year sits at the front of the title
/// and both adjacent-date patterns need it next to the day/month pair, and
/// the matcher's 3-day date grace let neighboring series games through -
/// playoff games between the same two teams run every 2-3 days, so Game 4
/// (June 10) scored a "date within 3 days" bonus on the Game 5 (June 13)
/// event. These tests pin the trailing-date extraction and the exact-day
/// gate for team sports.
/// </summary>
public class PlayoffSeriesDateMatchingTests
{
    private readonly SportsFileNameParser _parser;
    private readonly ReleaseMatchingService _svc;

    public PlayoffSeriesDateMatchingTests()
    {
        _parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _svc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), _parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event FinalsGame5() => new()
    {
        Id = 1,
        Title = "San Antonio Spurs vs New York Knicks",
        Sport = "Basketball",
        HomeTeamName = "San Antonio Spurs",
        AwayTeamName = "New York Knicks",
        EventDate = new DateTime(2026, 6, 13, 0, 30, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "NBA", Sport = "Basketball" }
    };

    // -- Parser: trailing day-month stamp with the year elsewhere in the title --

    [Theory]
    [InlineData("NBA Finals 2026 New York Knicks vs San Antonio Spurs Game 5 13 06 1080pEN60fps ABC", 2026, 6, 13)]
    [InlineData("NBA Finals 2026 Game 4 San Antonio Spurs at New York Knicks 10 06 1080pEN60fps ABC mkv", 2026, 6, 10)]
    [InlineData("NBA Finals 2026 San Antonio Spurs vs New York Knicks Game 3 08 06 1080pEN60fps ABC", 2026, 6, 8)]
    [InlineData("NBA Today 2026 19 06 720pEN60fps ESPN", 2026, 6, 19)]
    [InlineData("WNBA RS 2026 Atlanta Dream vs Indiana Fever 18 06 1080pEN60fps", 2026, 6, 18)]
    // American month-first leak: first group over 12 is impossible as a month
    // after the day-first read, so the pair swaps.
    [InlineData("NHL 2026 Rangers vs Bruins 06 25 1080p WEB", 2026, 6, 25)]
    public void Parse_TrailingDayMonth_CombinesWithDetachedYear(string title, int y, int m, int d)
    {
        var result = _parser.Parse(title);

        result.EventDate.Should().Be(new DateTime(y, m, d),
            "the trailing day-month pair before the quality token is the release's date stamp");
    }

    [Theory]
    // Season span: Jan-Jun fixtures belong to the end-year half of the season.
    [InlineData("EPL 2025-2026 Arsenal vs Chelsea 15 02 1080p", 2026, 2, 15)]
    [InlineData("EPL 2025-2026 Arsenal vs Chelsea 20 09 1080p", 2025, 9, 20)]
    public void Parse_TrailingDayMonth_PicksSeasonHalfYear(string title, int y, int m, int d)
    {
        var result = _parser.Parse(title);

        result.EventDate.Should().Be(new DateTime(y, m, d));
    }

    [Fact]
    public void Parse_PairNotBeforeQualityToken_IsNotADate()
    {
        var result = _parser.Parse("Formula 1 2026 Round 13 06 Austria Race SkyF1HD");

        result.EventDate.Should().BeNull(
            "a two-digit pair in the middle of the title is round/heat noise, not a date stamp");
        result.EventYear.Should().Be(2026);
    }

    [Fact]
    public void Parse_NoYearAnywhere_PairStaysUnused()
    {
        var result = _parser.Parse("Knicks vs Spurs 13 06 1080p");

        result.EventDate.Should().BeNull("without a year there is nothing to anchor the pair to");
    }

    // -- Matcher: exact-day gate for team sports --

    [Fact]
    public void Game5Release_MatchesGame5Event()
    {
        var result = _svc.ValidateRelease(
            Rel("NBA Finals 2026 New York Knicks vs San Antonio Spurs Game 5 13 06 1080pEN60fps ABC"),
            FinalsGame5());

        result.IsHardRejection.Should().BeFalse();
        result.MatchReasons.Should().Contain(r => r.Contains("Date matches"));
    }

    [Fact]
    public void Game4Release_ThreeDaysOff_IsRejectedForGame5Event()
    {
        var result = _svc.ValidateRelease(
            Rel("NBA Finals 2026 Game 4 San Antonio Spurs at New York Knicks 10 06 1080pEN60fps ABC mkv"),
            FinalsGame5());

        result.IsHardRejection.Should().BeTrue(
            "series games run every 2-3 days, so a 3-day-off release is the previous game, not this one");
        result.Rejections.Should().Contain(r => r.Contains("Date mismatch"));
    }

    [Fact]
    public void NonTeamEvent_KeepsThreeDayGrace()
    {
        var evt = new Event
        {
            Id = 2,
            Title = "AEW Dynamite",
            Sport = "Wrestling",
            EventDate = new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Id = 2, Name = "AEW", Sport = "Wrestling" }
        };

        var result = _svc.ValidateRelease(Rel("AEW Dynamite 2026 06 10 1080p WEB h264"), evt);

        result.IsHardRejection.Should().BeFalse(
            "non-team events keep the broadcast-drift grace window");
        result.MatchReasons.Should().Contain(r => r.Contains("Date within"));
    }
}
