using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Shortened-cut guard: condensed games and All-22/coaches film are the same
/// match (same teams, same date) cut down, so team/date matching alone sees
/// them as valid full-game releases. Before this guard a 1080p condensed cut
/// could be grabbed for a missing event, or quality-upgrade over - and delete -
/// an existing 720p full game or DVR recording. They must hard-reject exactly
/// like highlights and recaps do.
/// </summary>
public class CondensedCutRejectionTests
{
    private readonly ReleaseMatchingService _svc;

    public CondensedCutRejectionTests()
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

    private static Event NflEvent() => new()
    {
        Id = 1,
        Title = "Kansas City Chiefs vs Baltimore Ravens",
        Sport = "American Football",
        EventDate = new DateTime(2026, 9, 10, 0, 20, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "NFL", Sport = "American Football" }
    };

    [Theory]
    [InlineData("NFL.2026.09.10.Chiefs.vs.Ravens.Condensed.1080p.WEB.h264-GRP")]
    [InlineData("NFL 2026-09-10 Chiefs vs Ravens CONDENSED GAME 720p")]
    [InlineData("NFL.2026.09.10.Chiefs.vs.Ravens.All-22.Coaches.Film.1080p")]
    [InlineData("NFL.2026.09.10.Chiefs.vs.Ravens.All.22.1080p.WEB")]
    [InlineData("NFL.2026.09.10.Chiefs.vs.Ravens.Coaches.Film.1080p.WEB")]
    [InlineData("NFL.2026.09.10.Chiefs.vs.Ravens.Coaches.Tape.720p")]
    public void ShortenedCutRelease_IsHardRejected(string title)
    {
        var result = _svc.ValidateRelease(Rel(title), NflEvent());

        result.IsHardRejection.Should().BeTrue();
        result.IsMatch.Should().BeFalse();
        result.Rejections.Should().Contain(r => r.Contains("Non-event content"));
    }

    [Theory]
    [InlineData("NFL.2026.09.10.Chiefs.vs.Ravens.1080p.WEB.h264-GRP")]
    [InlineData("NFL 2026-09-10 Chiefs vs Ravens FULL GAME 720p HDTV")]
    public void FullGameRelease_IsNotRejectedAsShortenedCut(string title)
    {
        var result = _svc.ValidateRelease(Rel(title), NflEvent());

        result.Rejections.Should().NotContain(r => r.Contains("Non-event content"));
    }
}
