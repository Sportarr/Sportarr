using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report: RSS sync grabbed the 720pier "Week 05 / 11.10.2026 / Red Zone"
/// release for "Baltimore Ravens vs Tennessee Titans" (Week 4) as an upgrade.
/// The title names neither team, but team validation found both: the short
/// name "BAL" matched as a substring of "Football", and "TEN" normalized to
/// "10" (number words become digits) and matched the "10" of the date.
/// </summary>
public class NflTeamAbbreviationMatchingTests
{
    private readonly ReleaseMatchingService _svc;

    public NflTeamAbbreviationMatchingTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _svc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title, bool isPack = false) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
        IsPack = isPack,
    };

    private static Event RavensTitans() => new()
    {
        Id = 104,
        Title = "Baltimore Ravens vs Tennessee Titans",
        Sport = "American Football",
        HomeTeamName = "Baltimore Ravens",
        AwayTeamName = "Tennessee Titans",
        HomeTeam = new Team { Name = "Baltimore Ravens", ShortName = "BAL", Sport = "Football" },
        AwayTeam = new Team { Name = "Tennessee Titans", ShortName = "TEN", Sport = "Football" },
        Season = "2026",
        Round = "4",
        EventDate = new DateTime(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "NFL", Sport = "American Football" }
    };

    [Fact]
    public void RedZoneRelease_DoesNotMatchAGameByAbbreviationCollisions()
    {
        var result = _svc.ValidateRelease(
            Rel("NFL 2026-2027 / Week 04 / 04.10.2026 / Red Zone [Football, WEB-DL HD/1080p/50fps, MKV/H.264, EN/NFLN]"),
            RavensTitans());

        result.MatchReasons.Should().NotContain("Both team names found");
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void ShortNameInsideAnotherWord_DoesNotCountAsTheTeam()
    {
        var result = _svc.ValidateRelease(
            Rel("NFL 2026 Week 4 Football Night Tennessee Titans 1080p WEB h264-GRP"),
            RavensTitans());

        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Fact]
    public void NumberWordAbbreviation_DoesNotMatchADigit()
    {
        var result = _svc.ValidateRelease(
            Rel("NFL 2026-2027 / Week 04 / 04.10.2026 / Baltimore Ravens Game [Football, WEB-DL HD/1080p, MKV/H.264, EN]"),
            RavensTitans());

        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Fact]
    public void SpelledWeekNumber_DoesNotCountAsTheTitans()
    {
        var result = _svc.ValidateRelease(
            Rel("NFL 2026 Week Ten Baltimore Ravens 1080p WEB h264-GRP"), RavensTitans());

        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Fact]
    public void FusedCodeDoesNotFindTheOtherCodeInsideALongerWord()
    {
        var result = _svc.ValidateRelease(
            Rel("NFL 2026 Week 4 BALvsTENNIS 1080p WEB h264-GRP"), RavensTitans());

        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Fact]
    public void OrdinaryWordEndingInTenDoesNotIdentifyTitans()
    {
        var result = _svc.ValidateRelease(
            Rel("NFL 2026 Week 4 Baltimore Ravens Batten 1080p WEB h264-GRP"), RavensTitans());

        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Fact]
    public void UppercaseWordBeginningWithBalDoesNotIdentifyRavens()
    {
        var result = _svc.ValidateRelease(
            Rel("NFL 2026 Week 4 Tennessee Titans BALATON 1080p WEB h264-GRP"), RavensTitans());

        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShortAlternateAndUserAliases_DoNotMatchInsideFootball(bool userAlias)
    {
        var evt = RavensTitans();
        evt.HomeTeam!.ShortName = string.Empty;
        if (userAlias) evt.HomeTeam.UserAliases = "BAL";
        else evt.HomeTeam.AlternateName = "BAL";

        var result = _svc.ValidateRelease(
            Rel("NFL 2026 Week 4 Football Night Tennessee Titans 1080p WEB h264-GRP"), evt);

        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Theory]
    [InlineData("NFL.2026.10.04.Ten.at.Bal.1080p.WEB.h264-GRP")]
    [InlineData("NFL-2026.10.04_Ten@Bal")]
    [InlineData("NFL 2026 Week 4 TEN vs BAL 720p WEB h264-GRP")]
    [InlineData("NFL 2026 Week 4 TENvsBAL 720p WEB h264-GRP")]
    [InlineData("NFL 2026 Week 4 BalvsTen 720p WEB h264-GRP")]
    public void SpelledAbbreviations_StillMatchBothTeams(string title)
    {
        var result = _svc.ValidateRelease(Rel(title), RavensTitans());

        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void WeekPack_ForAnotherWeek_IsRejectedOnRound()
    {
        var result = _svc.ValidateRelease(
            Rel("NFL 2026-2027 / Week 05 / 11.10.2026 / Red Zone [Football, WEB-DL HD/1080p/50fps, MKV/H.264, EN/NFLN]", isPack: true),
            RavensTitans());

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain("Round mismatch: release is round 5, event is round 4");
    }

    [Fact]
    public void WeekPack_ForTheSameWeek_IsNotRejectedOnRound()
    {
        var evt = RavensTitans();
        evt.Round = "5";

        var result = _svc.ValidateRelease(
            Rel("NFL 2026-2027 / Week 05 / 11.10.2026 / Red Zone [Football, WEB-DL HD/1080p/50fps, MKV/H.264, EN/NFLN]"),
            evt);

        result.Rejections.Should().NotContain(r => r.StartsWith("Round mismatch"));
    }

    [Fact]
    public void FullSeasonWeekRange_IsNotRejectedAsTheFirstWeek()
    {
        var result = _svc.ValidateRelease(
            Rel("NFL 2026 Week 1-18 Complete Season 1080p WEB h264-GRP"), RavensTitans());

        result.Rejections.Should().NotContain(r => r.StartsWith("Round mismatch"));
    }

    [Theory]
    [InlineData("Week 1 - Week 18")]
    [InlineData("Week 1 to 18")]
    [InlineData("Round 1 - Round 18")]
    [InlineData("Matchday 1 - Matchday 18")]
    public void SpelledWeekRange_IncludingTheEvent_IsNotRejected(string range)
    {
        var result = _svc.ValidateRelease(
            Rel($"NFL 2026 {range} Complete Season 1080p WEB h264-GRP"), RavensTitans());

        result.Rejections.Should().NotContain(r => r.StartsWith("Round mismatch"));
    }

    [Fact]
    public void WeekRangeOutsideTheEventRound_IsRejected()
    {
        var result = _svc.ValidateRelease(
            Rel("NFL 2026 Week 5-8 Complete Pack 1080p WEB h264-GRP", isPack: true), RavensTitans());

        result.Rejections.Should().Contain(r => r.StartsWith("Round mismatch"));
    }

    [Theory]
    [InlineData("Round 5 - 12 Games")]
    [InlineData("Round 5 - 12.Games")]
    [InlineData("Round 5 - 12.10.2026")]
    public void RoundWithCountOrDate_IsNotTreatedAsARange(string round)
    {
        var evt = RavensTitans();
        evt.Round = "12";
        var result = _svc.ValidateRelease(
            Rel($"NFL 2026 {round} 1080p WEB h264-GRP", isPack: true), evt);

        result.Rejections.Should().Contain(r => r.StartsWith("Round mismatch"));
    }

    [Theory]
    [InlineData("Week 5 - 11 Oct 2026")]
    [InlineData("Week 5 - 11 Sept 2026")]
    [InlineData("Week 4 - 6 Hours")]
    public void WeekFollowedByDateOrDuration_IsNotAWeekRange(string title)
    {
        var evt = RavensTitans();
        evt.Round = title.StartsWith("Week 5", StringComparison.Ordinal) ? "8" : "5";

        var result = _svc.ValidateRelease(Rel($"NFL 2026 {title} 1080p WEB h264-GRP", isPack: true), evt);

        result.Rejections.Should().Contain(r => r.StartsWith("Round mismatch"));
    }

    [Fact]
    public void WeekRangeFollowedByCodecBitDepth_StillIncludesTheEvent()
    {
        var result = _svc.ValidateRelease(
            Rel("NFL 2026 Week 1-18.10bit Complete Season 1080p WEB h264-GRP", isPack: true),
            RavensTitans());

        result.Rejections.Should().NotContain(r => r.StartsWith("Round mismatch"));
    }

    [Theory]
    [InlineData("Week 5 - 7.500GB")]
    [InlineData("Week 5 - 7.5GB")]
    [InlineData("Week 5 - 7,5GB")]
    [InlineData("Week 5 - 12.500 GB")]
    public void WeekFollowedByFileSize_IsNotAWeekRange(string title)
    {
        var evt = RavensTitans();
        evt.Round = "6";

        var result = _svc.ValidateRelease(Rel($"NFL 2026 {title} 1080p WEB h264-GRP", isPack: true), evt);

        result.Rejections.Should().Contain(r => r.StartsWith("Round mismatch"));
    }
}
