using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// From a user report: a league monitored for future events only kept sending
/// "EPL 2017 15 01 Everton vs Manchester City 720p HDTV x264-VERUM" to the
/// download client for a current-season Everton vs Manchester City fixture.
/// The title carries an unmistakable 2017 season marker, so date/year
/// validation must hard-reject it against a 2026 event.
/// </summary>
public class EplOldSeasonRegrabTests
{
    private readonly ReleaseMatchingService _svc;

    public EplOldSeasonRegrabTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _svc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event EvertonVsManCity2026() => new()
    {
        Id = 1,
        Title = "Everton vs Manchester City",
        Sport = "Soccer",
        HomeTeamName = "Everton",
        AwayTeamName = "Manchester City",
        HomeTeam = new Team { Name = "Everton", Sport = "Soccer" },
        AwayTeam = new Team { Name = "Manchester City", Sport = "Soccer" },
        EventDate = new DateTime(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "English Premier League", Sport = "Soccer" },
    };

    [Fact]
    public void OldSeasonRelease_IsHardRejectedAgainstCurrentFixture()
    {
        var result = _svc.ValidateRelease(
            Rel("EPL 2017 15 01 Everton vs Manchester City 720p HDTV x264-VERUM"),
            EvertonVsManCity2026());

        result.IsHardRejection.Should().BeTrue(
            "a release stamped 2017 must never be grabbed for a 2026 fixture");
    }

    [Fact]
    public void CurrentSeasonRelease_StillMatches()
    {
        var result = _svc.ValidateRelease(
            Rel("EPL 2026 12 05 Everton vs Manchester City 1080p HDTV H264-DARKSPORT"),
            EvertonVsManCity2026());

        result.IsHardRejection.Should().BeFalse();
    }
}
