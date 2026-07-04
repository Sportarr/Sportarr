using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// End-to-end matching coverage for issue #92: a dot-separated cross-year season
/// tag ("NBA.2025.2026.Team.Vs.Team...", the dominant scene-release convention)
/// was never recognized as a season span at all - the season-span regex only
/// accepted a hyphen or slash separator - so the release fell back to
/// year-only extraction, grabbed just the first year (2025), and was hard
/// rejected against a genuine 2025-2026 season game actually played in 2026.
/// Uses the reporter's exact release title.
/// </summary>
public class SeasonSpanMatchingTests
{
    private readonly ReleaseMatchingService _svc;

    public SeasonSpanMatchingTests()
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

    private static Event SpursAt76ers(int eventYear) => new()
    {
        Id = 1,
        Title = "Philadelphia 76ers vs San Antonio Spurs",
        Sport = "Basketball",
        HomeTeamName = "Philadelphia 76ers",
        AwayTeamName = "San Antonio Spurs",
        EventDate = new DateTime(eventYear, 3, 3, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "NBA", Sport = "Basketball" }
    };

    [Fact]
    public void DotSeparatedSeasonSpan_MatchesEventInTheSecondSeasonYear()
    {
        // The reporter's exact release title, hard-rejected before the fix
        // ("Year mismatch: release is 2025, event is 2026") despite both team
        // names and the league matching.
        var result = _svc.ValidateRelease(
            Rel("NBA.2025.2026.San.Antonio.Spurs.Vs.Philadelphia.76ers.03.03.2026.VFF.1080p.WEBRip.AAC.2.0.AVC-Macolive"),
            SpursAt76ers(2026));

        result.IsHardRejection.Should().BeFalse();
        result.Rejections.Should().NotContain(r => r.Contains("Year mismatch"));
    }

    [Fact]
    public void DotSeparatedSeasonSpan_MatchesEventInTheFirstSeasonYear()
    {
        // Same release, event played in the season's starting year instead -
        // both ends of a 2025-2026 season span must be accepted.
        var result = _svc.ValidateRelease(
            Rel("NBA.2025.2026.San.Antonio.Spurs.Vs.Philadelphia.76ers.03.03.2026.VFF.1080p.WEBRip.AAC.2.0.AVC-Macolive"),
            SpursAt76ers(2025));

        result.IsHardRejection.Should().BeFalse();
        result.Rejections.Should().NotContain(r => r.Contains("Year mismatch"));
    }

    [Fact]
    public void DotSeparatedSeasonSpan_StillHardRejectsAYearOutsideTheSpan()
    {
        // A 2025-2026 season release must not match a 2027 game.
        var result = _svc.ValidateRelease(
            Rel("NBA.2025.2026.San.Antonio.Spurs.Vs.Philadelphia.76ers.03.03.2026.VFF.1080p.WEBRip.AAC.2.0.AVC-Macolive"),
            SpursAt76ers(2027));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Year mismatch"));
    }
}
