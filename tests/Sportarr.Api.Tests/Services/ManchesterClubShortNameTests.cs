using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #450: release groups write "Man City" and "Man Utd" for
/// the two Manchester clubs, and the alias table had no entry for either.
/// A release naming Manchester City as "Man City" was hard rejected with
/// "Only one team name found" because only the opponent matched.
/// </summary>
public class ManchesterClubShortNameTests
{
    private readonly ReleaseMatchingService _matching = new(
        Mock.Of<ILogger<ReleaseMatchingService>>(),
        new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
        new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()));

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event Fixture(string home, string away, DateTime date) => new()
    {
        Id = 1,
        Title = $"{home} vs {away}",
        Sport = "Soccer",
        EventDate = date,
        HomeTeamName = home,
        AwayTeamName = away,
        Season = "2025-2026",
        Monitored = true,
        League = new League { Id = 1, Name = "UEFA Champions League", Sport = "Soccer" }
    };

    [Theory]
    [InlineData("UEFA.Champions.League.2026.03.17.Man.City.vs.Real.Madrid.720p.WEB.h264-ULTRAS")]
    [InlineData("UEFA Champions League 2026 03 17 Man City vs Real Madrid 1080p WEB h264")]
    [InlineData("UEFA.Champions.League.2026.03.17.Manchester.City.vs.Real.Madrid.720p.WEB.h264-ULTRAS")]
    public void ManCity_ReleaseMatchesManchesterCityEvent(string title)
    {
        var evt = Fixture("Manchester City", "Real Madrid", new DateTime(2026, 3, 17, 20, 0, 0, DateTimeKind.Utc));

        var result = _matching.ValidateRelease(Rel(title), evt);

        result.Rejections.Should().NotContain(reason => reason.Contains("Only one team name found"));
        result.IsHardRejection.Should().BeFalse();
    }

    [Theory]
    [InlineData("Premier.League.2026.03.21.Man.Utd.vs.Arsenal.720p.WEB.h264-ULTRAS")]
    [InlineData("Premier League 2026 03 21 Man United vs Arsenal 1080p WEB h264")]
    public void ManUtd_ReleaseMatchesManchesterUnitedEvent(string title)
    {
        var evt = Fixture("Manchester United", "Arsenal", new DateTime(2026, 3, 21, 15, 0, 0, DateTimeKind.Utc));

        var result = _matching.ValidateRelease(Rel(title), evt);

        result.Rejections.Should().NotContain(reason => reason.Contains("Only one team name found"));
        result.IsHardRejection.Should().BeFalse();
    }

    [Fact]
    public void ManCity_ReleaseDoesNotMatchManchesterUnitedEvent()
    {
        var evt = Fixture("Manchester United", "Real Madrid", new DateTime(2026, 3, 17, 20, 0, 0, DateTimeKind.Utc));

        var result = _matching.ValidateRelease(
            Rel("UEFA.Champions.League.2026.03.17.Man.City.vs.Real.Madrid.720p.WEB.h264-ULTRAS"), evt);

        result.IsMatch.Should().BeFalse("a Man City release is a different matchup from a Man United event");
    }
}
