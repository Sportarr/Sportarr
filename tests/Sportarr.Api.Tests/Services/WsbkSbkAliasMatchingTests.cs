using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// World Superbike alias coverage: TheSportsDB names the league literally
/// "SBK" while release groups overwhelmingly tag releases "WSBK" (WorldSBK
/// branding). The cross-series guard compared the single matched release
/// pattern against the league name, so a WSBK release had no escape hatch
/// against its own SBK league and was hard rejected as a different sport.
/// The sibling-alias hatch fixes that generally: any pattern mapping to the
/// same series that matches the event's own league clears the guard.
/// </summary>
public class WsbkSbkAliasMatchingTests
{
    private readonly ReleaseMatchingService _svc;

    public WsbkSbkAliasMatchingTests()
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

    private static Event SbkRace() => new()
    {
        Id = 1,
        Title = "Australian Round Race 1",
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "SBK", Sport = "Motorsport" }
    };

    [Theory]
    [InlineData("WSBK.2026.Round01.Australia.Race.1.1080p.WEB.h264-VERUM")]
    [InlineData("WSBK 2026 Round 01 Phillip Island Race One 720p")]
    [InlineData("SBK.2026.Round01.Australia.Race.1.1080p.WEB.h264-GRP")]
    public void WsbkOrSbkRelease_IsNotRejectedAsDifferentSport_AgainstSbkLeague(string title)
    {
        var result = _svc.ValidateRelease(Rel(title), SbkRace());

        result.Rejections.Should().NotContain(r => r.Contains("Different sport") || r.Contains("different sport"));
    }

    [Fact]
    public void MotoGpRelease_IsStillRejected_AgainstSbkLeague()
    {
        var result = _svc.ValidateRelease(Rel("MotoGP.2026.Round01.Qatar.Race.1080p.WEB.h264-GRP"), SbkRace());

        result.IsHardRejection.Should().BeTrue();
    }
}
