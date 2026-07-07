using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// AFL matching coverage from the field reports: TheSportsDB canonical team
/// names are "&lt;Place&gt; Football Club" while releases use the bare place
/// ("St Kilda v West Coast"), place+nickname ("Carlton Blues V Western
/// Bulldogs" in KAYO web releases), or abbreviations ("GWS Giants") - every
/// automatic AFL search was rejected with "Team names not found in release".
/// Covers the generic club-suffix strip, the AFL variation data, and the new
/// user-defined alias field.
/// </summary>
public class AflTeamNameMatchingTests
{
    private readonly ReleaseMatchingService _svc;

    public AflTeamNameMatchingTests()
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

    private static Event AflEvent(string home, string away, Team? homeTeam = null, Team? awayTeam = null) => new()
    {
        Id = 1,
        Title = $"{home} vs {away}",
        Sport = "Australian Football",
        HomeTeamName = home,
        AwayTeamName = away,
        HomeTeam = homeTeam,
        AwayTeam = awayTeam,
        EventDate = new DateTime(2026, 5, 16, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "Australian AFL", Sport = "Australian Football", AlternateName = "AFL" }
    };

    [Fact]
    public void KayoNicknameRelease_MatchesFootballClubNames()
    {
        // The reporter's exact release shape: KAYO attaches nicknames the
        // TSDB names don't carry, and the TSDB names carry a "Football Club"
        // suffix no release uses.
        var result = _svc.ValidateRelease(
            Rel("AFL 2026 Round 10 Game 6 Carlton Blues V Western Bulldogs 1080p KAYO WEB-DL 50FPS DD5.1 H264"),
            AflEvent("Carlton Football Club", "Western Bulldogs"));

        result.Rejections.Should().NotContain(r => r.Contains("Team names"));
    }

    [Fact]
    public void BareClubNameRelease_MatchesFootballClubNames()
    {
        // Scene shape: "AFL 2026 Round 7-St Kilda v West Coast".
        var result = _svc.ValidateRelease(
            Rel("AFL 2026 Round 7 St Kilda v West Coast 1080p HDTV H264"),
            AflEvent("St Kilda Football Club", "West Coast Eagles"));

        result.Rejections.Should().NotContain(r => r.Contains("Team names"));
    }

    [Fact]
    public void GwsAbbreviation_MatchesGreaterWesternSydneyGiants()
    {
        var result = _svc.ValidateRelease(
            Rel("AFL 2026 Round 10 Game 9 West Coast Eagles V GWS Giants 1080p KAYO WEB-DL 50FPS DD5.1 H264"),
            AflEvent("West Coast Eagles", "Greater Western Sydney Giants"));

        result.Rejections.Should().NotContain(r => r.Contains("Team names"));
    }

    [Fact]
    public void UserAlias_MatchesReleaseNameUpstreamDataLacks()
    {
        // A user-taught alias on the Team row must match exactly like the
        // synced alternates do.
        var home = new Team { Id = 10, Name = "Manchester City", UserAliases = "ManCity" };
        var away = new Team { Id = 11, Name = "Liverpool" };

        var evt = new Event
        {
            Id = 2,
            Title = "Manchester City vs Liverpool",
            Sport = "Soccer",
            HomeTeamName = "Manchester City",
            AwayTeamName = "Liverpool",
            HomeTeam = home,
            AwayTeam = away,
            EventDate = new DateTime(2026, 5, 16, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Id = 2, Name = "English Premier League", Sport = "Soccer", AlternateName = "EPL" }
        };

        var result = _svc.ValidateRelease(
            Rel("EPL 2026 05 16 ManCity vs Liverpool 1080p WEB H264"),
            evt);

        result.Rejections.Should().NotContain(r => r.Contains("Team names"));
    }
}
