using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Two bugs from a manual "Switzerland vs Colombia" World Cup search on a
/// Russian tracker:
/// 1. Quality was read as HDTV-1080p when the title said "IPTV/2160p/60fps" -
///    the resolution regex did not accept the "/" separator common in these
///    bracketed metadata blocks, so 2160p was missed and the parser defaulted.
/// 2. A correct release ("Швейцария - Колумбия") was hard-rejected as "only one
///    team name found - likely a different matchup" because our Latin team
///    dictionary only recognized one side's foreign-language name.
/// </summary>
public class CyrillicWorldCupMatchingTests
{
    // ---- Quality: slash/comma separated resolutions ----

    [Theory]
    [InlineData("FIFA World Cup 2026 Switzerland Colombia Fox Sports IPTV/2160p/60fps MKV", "HDTV-2160p")]
    [InlineData("Match TB [07.07.2026, IPTV/1080p/50fps, MKV/H.264, RUS]", "HDTV-1080p")]
    [InlineData("Event [Футбол, IPTV/720p/60fps]", "HDTV-720p")]
    [InlineData("Some.Release.WEB-DL/2160p/HDR", "WEBDL-2160p")]
    public void SlashSeparatedResolution_IsParsed(string releaseName, string expected)
    {
        QualityParser.ParseQuality(releaseName).QualityName.Should().Be(expected);
    }

    // ---- Team matching: non-Latin titles ----

    private readonly ReleaseMatchingService _svc;

    public CyrillicWorldCupMatchingTests()
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

    // Switzerland carries a Cyrillic alias so it matches; Colombia does not,
    // reproducing the "only one side recognized" data gap.
    private static Event SwitzerlandVsColombia(string? homeAlias = "Швейцария", string? awayAlias = null) => new()
    {
        Id = 1,
        Title = "Switzerland vs Colombia",
        Sport = "Soccer",
        HomeTeamName = "Switzerland",
        AwayTeamName = "Colombia",
        HomeTeam = new Team { Name = "Switzerland", Sport = "Soccer", UserAliases = homeAlias },
        AwayTeam = new Team { Name = "Colombia", Sport = "Soccer", UserAliases = awayAlias },
        EventDate = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "FIFA World Cup", Sport = "Soccer" },
    };

    [Fact]
    public void NonLatinTitle_OneTeamRecognized_NotFlaggedAsWrongMatchup()
    {
        // Only one side is recognizable (Switzerland via alias, Colombia has
        // no Cyrillic alias). The old behavior falsely called this a different
        // matchup. It still cannot be auto-confirmed (the date alone cannot
        // tell World Cup games apart on a shared day), but the message must be
        // the accurate one that points at the real fix - team aliases - not
        // the misleading "different matchup" claim.
        var result = _svc.ValidateRelease(
            Rel("Чемпионат Мира 2026 / FIFA World Cup 2026 / 1/8 финала / Швейцария - Колумбия / Fox Sports [07.07.2026, Футбол, IPTV/2160p/60fps, MKV/H.265/HLG HDR, EN]"),
            SwitzerlandVsColombia());

        result.Rejections.Should().NotContain(r => r.Contains("likely a different matchup"));
        result.Rejections.Should().Contain(r => r.Contains("non-Latin release title"));
    }

    [Fact]
    public void NonLatinTitle_BothTeamsAliased_MatchesCleanly()
    {
        // With aliases on both sides the release matches with no team rejection.
        var result = _svc.ValidateRelease(
            Rel("Чемпионат Мира 2026 / Швейцария - Колумбия / Матч ТВ"),
            SwitzerlandVsColombia(homeAlias: "Швейцария", awayAlias: "Колумбия"));

        result.MatchReasons.Should().Contain("Both team names found");
        result.IsHardRejection.Should().BeFalse();
    }

    [Fact]
    public void LatinTitle_OneTeamFound_StillHardRejected()
    {
        // A Latin title where only one team appears is still a wrong matchup.
        var result = _svc.ValidateRelease(
            Rel("Switzerland vs Brazil 2026 1080p WEB h264"),
            SwitzerlandVsColombia(homeAlias: null));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("likely a different matchup"));
    }
}
