using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report: 720pier-style titles carry both a season span and a full
/// day-first game date ("NBA 2026-2027 / RS / 20.10.2026 / 76ers @ Knicks").
/// The parser preferred the span and dropped the date, so validation fell back
/// to the year and accepted the 20 October game for the 5 October meeting of
/// the same two teams, replacing that event's file.
/// </summary>
public class SeasonSpanDayFirstDateTests
{
    private const string Oct20Release = "NBA 2026-2027 / RS / 20.10.2026 / Philadelphia 76ers @ New York Knicks [Basketball, WEB-DL HD/1080p/60fps, MKV/H.264, EN/NBC]";
    private const string Oct05Release = "NBA 2026-2027 / PS / 05.10.2026 / New York Knicks @ Philadelphia 76ers [Basketball, WEB-DL HD/720p/60fps, MKV/H.264, EN/NBCSP+]";

    private readonly SportsFileNameParser _parser;
    private readonly ReleaseMatchingService _svc;

    public SeasonSpanDayFirstDateTests()
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

    private static Event KnicksSixers(int month, int day, string home, string away) => new()
    {
        Id = 1,
        Title = $"{home} vs {away}",
        Sport = "Basketball",
        HomeTeamName = home,
        AwayTeamName = away,
        EventDate = new DateTime(2026, month, day, 23, 0, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2026, month, day),
        League = new League { Id = 1, Name = "NBA", Sport = "Basketball" }
    };

    [Theory]
    [InlineData(Oct20Release, 2026, 10, 20)]
    [InlineData(Oct05Release, 2026, 10, 5)]
    [InlineData("NHL 2026-2027 / RS / 20.10.2026 / San Jose Sharks @ Montréal Canadiens [Hockey, WEB-DL HD/720p/60fps, MKV/H.264, EN/TSN2]", 2026, 10, 20)]
    [InlineData("NBA 2025-26 / RS / 14.03.2026 / Washington Wizards @ Boston Celtics [Basketball, WEB-DL HD/720p]", 2026, 3, 14)]
    public void Parse_DayFirstDateBesideSeasonSpan_IsTheEventDate(string title, int y, int m, int d)
    {
        _parser.Parse(title).EventDate.Should().Be(new DateTime(y, m, d));
    }

    [Fact]
    public void Parse_DayFirstDateOutsideSeasonYears_IsIgnored()
    {
        _parser.Parse("NBA 2026-2027 / RS / 20.10.2024 / Philadelphia 76ers @ New York Knicks").EventDate
            .Should().BeNull();
    }

    [Fact]
    public void LaterGame_IsRejectedForEarlierMeetingOfSameTeams()
    {
        var result = _svc.ValidateRelease(Rel(Oct20Release),
            KnicksSixers(10, 5, "Philadelphia 76ers", "New York Knicks"));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Date mismatch"));
    }

    [Fact]
    public void Release_MatchesItsOwnGame()
    {
        _svc.ValidateRelease(Rel(Oct20Release), KnicksSixers(10, 20, "New York Knicks", "Philadelphia 76ers"))
            .IsHardRejection.Should().BeFalse();
        _svc.ValidateRelease(Rel(Oct05Release), KnicksSixers(10, 5, "Philadelphia 76ers", "New York Knicks"))
            .IsHardRejection.Should().BeFalse();
    }
}
