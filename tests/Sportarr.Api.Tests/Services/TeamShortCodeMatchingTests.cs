using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Releases of the major US leagues often name a game by the official team codes
/// and a date ("NBA 2026 10 20 OKC SAS NBC"). The match needs both codes, the
/// date, and the league of the event. These tests use the titles from the field
/// report and the cases that must stay rejected.
/// </summary>
public class TeamShortCodeMatchingTests
{
    private static readonly League Nba = new() { Id = 1, Name = "NBA", Sport = "Basketball" };
    private static readonly League Nhl = new() { Id = 2, Name = "NHL", Sport = "Ice Hockey" };
    private static readonly League Nfl = new() { Id = 3, Name = "NFL", Sport = "American Football" };
    private static readonly League Mlb = new() { Id = 4, Name = "MLB", Sport = "Baseball" };
    private static readonly League[] KnownLeagues = { Nba, Nhl, Nfl, Mlb };

    private readonly ReleaseMatchingService _svc;

    public TeamShortCodeMatchingTests()
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

    // The opponent of a followed team has no team row, so the team objects stay null.
    private static Event Game(League league, string home, string away, DateTime utc, bool withTeamRows = false)
    {
        return new Event
        {
            Id = 1,
            Title = home + " vs " + away,
            Sport = league.Sport,
            HomeTeamName = home,
            AwayTeamName = away,
            HomeTeam = withTeamRows ? new Team { Name = home, Sport = league.Sport } : null,
            AwayTeam = withTeamRows ? new Team { Name = away, Sport = league.Sport } : null,
            Season = "2026",
            EventDate = utc,
            League = league,
        };
    }

    private ReleaseMatchResult Validate(string title, Event evt, bool withKnownLeagues = true) =>
        _svc.ValidateRelease(Rel(title), evt, knownLeagues: withKnownLeagues ? KnownLeagues : null);

    [Theory]
    [InlineData("NBA 2026 10 20 OKC SAS NBC", "Oklahoma City Thunder", "San Antonio Spurs", 2026, 10, 20)]
    [InlineData("NBA_20260403_MIN @ PHI_1080p60_NBCS-PHI.mp4", "Philadelphia 76ers", "Minnesota Timberwolves", 2026, 4, 3)]
    [InlineData("NBA_20260330_DET @ OKC_1080p60_Peacock.mkv", "Oklahoma City Thunder", "Detroit Pistons", 2026, 3, 30)]
    [InlineData("NBA 2026 10 20 PHO UTAH NBC", "Phoenix Suns", "Utah Jazz", 2026, 10, 20)]
    [InlineData("NBA 2026 10 20 BRK WSH NBC", "Brooklyn Nets", "Washington Wizards", 2026, 10, 20)]
    [InlineData("NBA 2026 10 20 CHO PHX NBC", "Charlotte Hornets", "Phoenix Suns", 2026, 10, 20)]
    public void NbaReleasesNamedByCodes_MatchTheRightGame(string title, string home, string away, int y, int m, int d)
    {
        foreach (var withTeamRows in new[] { false, true })
        {
            var evt = Game(Nba, home, away, new DateTime(y, m, d, 1, 0, 0, DateTimeKind.Utc), withTeamRows);

            var result = Validate(title, evt);

            result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
            result.MatchReasons.Should().Contain("Both team names found");
        }
    }

    [Theory]
    [InlineData("NHL 2026 10 20 BOS TOR NBC", "Toronto Maple Leafs", "Boston Bruins")]
    [InlineData("NHL_20261020_WSH @ PHI_720p60_NHLN.mkv", "Philadelphia Flyers", "Washington Capitals")]
    public void NhlReleasesNamedByCodes_MatchTheRightGame(string title, string home, string away)
    {
        var evt = Game(Nhl, home, away, new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc));

        var result = Validate(title, evt);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void NflReleaseNamedByCodes_MatchesTheRightGame()
    {
        var evt = Game(Nfl, "Philadelphia Eagles", "Minnesota Vikings",
            new DateTime(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc));

        var result = Validate("NFL.2026.10.04.MIN.at.PHI.1080p.WEB.h264-GRP", evt);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void MlbReleaseNamedByCodes_MatchesTheRightGame()
    {
        var evt = Game(Mlb, "Texas Rangers", "Seattle Mariners",
            new DateTime(2026, 6, 13, 23, 0, 0, DateTimeKind.Utc));

        var result = Validate("MLB 2026 06 13 SEA TEX 720p WEB h264-GRP", evt);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void LocalGameDate_OneDayBeforeTheUtcDate_StillMatches()
    {
        // A 7:30 pm Eastern game on April 3 starts on April 4 in UTC.
        var evt = Game(Nba, "Philadelphia 76ers", "Minnesota Timberwolves",
            new DateTime(2026, 4, 4, 0, 30, 0, DateTimeKind.Utc));

        var result = Validate("NBA_20260403_MIN @ PHI_1080p60_NBCS-PHI.mp4", evt);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void FullNamesWithNonLatinTags_MatchWhenBothTeamsAndDateAgree()
    {
        var evt = Game(Nba, "Miami Heat", "Atlanta Hawks", new DateTime(2026, 4, 12, 23, 0, 0, DateTimeKind.Utc));

        var result = Validate(
            "NBA 2025-2026 / RS / 12.04.2026 / Atlanta Hawks @ Miami Heat [Баскетбол, WEB-DL HD/1080p/60fps]", evt);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void OnlyOneCodePresent_IsRejected()
    {
        var evt = Game(Nba, "Oklahoma City Thunder", "San Antonio Spurs",
            new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc));

        var result = Validate("NBA 2026 10 20 OKC NBC", evt);

        result.IsMatch.Should().BeFalse();
        result.Rejections.Should().Contain("Only one team name found - likely a different matchup");
    }

    [Fact]
    public void CodeOfATeamFromAnotherGame_IsRejected()
    {
        var evt = Game(Nba, "Oklahoma City Thunder", "San Antonio Spurs",
            new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc));

        var result = Validate("NBA 2026 10 20 OKC DEN NBC", evt);

        result.IsMatch.Should().BeFalse();
        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Theory]
    [InlineData(2026, 10, 25)]
    [InlineData(2026, 10, 18)]
    public void DateThatDoesNotMatchTheEvent_IsRejected(int y, int m, int d)
    {
        var evt = Game(Nba, "Oklahoma City Thunder", "San Antonio Spurs",
            new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc));

        var result = Validate($"NBA {y} {m:00} {d:00} OKC SAS NBC", evt);

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.StartsWith("Date mismatch"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CodesSharedWithAnotherLeague_DoNotMatchAGameOfThisLeague(bool withKnownLeagues)
    {
        // Toronto and Boston play in the NBA and in the NHL. The title is a hockey game.
        var evt = Game(Nba, "Toronto Raptors", "Boston Celtics",
            new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc));

        var result = Validate("NHL 2026 10 20 TOR BOS NBC", evt, withKnownLeagues);

        result.IsMatch.Should().BeFalse();
        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OfficialCodesSharedWithAnotherLeague_DoNotMatchAFootballGame(bool withKnownLeagues)
    {
        var evt = Game(Nfl, "Philadelphia Eagles", "Minnesota Vikings",
            new DateTime(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc));

        var result = Validate("NHL 2026 10 04 MIN PHI 720p WEB h264-GRP", evt, withKnownLeagues);

        result.IsMatch.Should().BeFalse();
        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Fact]
    public void ShortCodesAreNotTakenWithoutALeagueName()
    {
        var evt = Game(Nfl, "Philadelphia Eagles", "Minnesota Vikings",
            new DateTime(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc));

        var result = Validate("2026 10 04 MIN PHI 720p WEB h264-GRP", evt);

        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Fact]
    public void ShortCodesAreNotTakenWithoutADate()
    {
        var evt = Game(Nfl, "Philadelphia Eagles", "Minnesota Vikings",
            new DateTime(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc));

        var result = Validate("NFL MIN PHI 720p WEB h264-GRP", evt);

        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Fact]
    public void PlainWordsThatSpellACode_DoNotCountAsTeams()
    {
        var evt = Game(Nfl, "Washington Commanders", "Minnesota Vikings",
            new DateTime(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc));

        var result = Validate("NFL 2026 10 04 what was the min score highlights 720p WEB h264-GRP", evt);

        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Fact]
    public void EventWithoutTeams_IsNotMatchedByCodes()
    {
        var evt = new Event
        {
            Id = 2,
            Title = "Grand Prix of Texas",
            Sport = "Motorsport",
            Season = "2026",
            EventDate = new DateTime(2026, 10, 20, 18, 0, 0, DateTimeKind.Utc),
            League = new League { Id = 9, Name = "Formula 1", Sport = "Motorsport" },
        };

        var result = Validate("NBA 2026 10 20 OKC SAS NBC", evt);

        result.IsMatch.Should().BeFalse();
        result.MatchReasons.Should().NotContain("Both team names found");
    }

    // The hub has separate leagues for these competitions. Their names hold "NBA" or "MLB"
    // as a word, and releases name them by that word alone.
    [Theory]
    [InlineData("NBA Summer League", "NBA 2026 07 12 MIA UTA", "Miami Heat", "Utah Jazz")]
    [InlineData("NBA Summer League", "NBA 2026 07 12 Heat vs Jazz", "Miami Heat", "Utah Jazz")]
    [InlineData("NBA Cup", "NBA 2026 07 12 ATL BOS 1080p", "Atlanta Hawks", "Boston Celtics")]
    [InlineData("NBA G League", "NBA 2026 07 12 ATL BOS 1080p", "Atlanta Hawks", "Boston Celtics")]
    [InlineData("MLB Spring Training", "MLB 2026 07 12 SEA TEX 720p", "Seattle Mariners", "Texas Rangers")]
    public void SubLeagueNamedOnlyByTheMajorLeagueWord_StillMatches(
        string leagueName, string title, string home, string away)
    {
        var sport = leagueName.StartsWith("MLB") ? "Baseball" : "Basketball";
        var subLeague = new League { Id = 20, Name = leagueName, Sport = sport };
        var known = new[] { Nba, Mlb, subLeague };
        var evt = Game(subLeague, home, away, new DateTime(2026, 7, 12, 1, 0, 0, DateTimeKind.Utc));

        var result = _svc.ValidateRelease(Rel(title), evt, knownLeagues: known);

        result.MatchReasons.Should().Contain("Both team names found", string.Join("; ", result.Rejections));
    }

    [Fact]
    public void SubLeagueEvent_StillRejectsAReleaseOfAnotherMajorLeague()
    {
        var cup = new League { Id = 20, Name = "NBA Cup", Sport = "Basketball" };
        var evt = Game(cup, "Toronto Raptors", "Boston Celtics", new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc));

        var result = _svc.ValidateRelease(Rel("NHL 2026 10 20 TOR BOS NBC"), evt, knownLeagues: new[] { Nba, Nhl, cup });

        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Fact]
    public void NicknamesStayInUse_WhenTheTitleNamesAnotherLeague()
    {
        var evt = Game(Nba, "Miami Heat", "Utah Jazz", new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc));

        var result = Validate("NHL 2026 10 20 Heat vs Jazz NBC", evt);

        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void StoredShortName_IsIgnoredOnceTheTitleNamesAnotherLeague()
    {
        var evt = Game(Nba, "Toronto Raptors", "Boston Celtics", new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc), withTeamRows: true);
        evt.HomeTeam!.ShortName = "TOR";
        evt.AwayTeam!.ShortName = "BOS";

        var result = Validate("NHL 2026 10 20 TOR BOS NBC", evt);

        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Fact]
    public void UserAliases_SurviveWhenShortCodesAreNotTrusted()
    {
        var evt = Game(Nba, "Toronto Raptors", "Boston Celtics", new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc), withTeamRows: true);
        evt.HomeTeam!.UserAliases = "TOR";
        evt.AwayTeam!.UserAliases = "BOS";

        var result = Validate("NHL 2026 10 20 TOR BOS NBC", evt);

        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void NbaEventWithoutALeagueWord_StillMatchesSharedCodesThroughTheOldPath()
    {
        var evt = Game(Nba, "Toronto Raptors", "Boston Celtics", new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc));

        var result = Validate("2026 10 20 TOR BOS 1080p WEB h264-GRP", evt);

        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void MlbReleaseWithTwoLetterOfficialCode_MatchesTheRightGame()
    {
        var evt = Game(Mlb, "Kansas City Royals", "Seattle Mariners",
            new DateTime(2026, 6, 13, 23, 0, 0, DateTimeKind.Utc));

        var result = Validate("MLB 2026 06 13 SEA @ KC 720p WEB h264-GRP", evt);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Theory]
    [InlineData("NHL 2026 10 20 NJ @ TB 720p WEB h264-GRP", "Tampa Bay Lightning", "New Jersey Devils")]
    [InlineData("NHL 2026 10 20 SJ @ LA 720p WEB h264-GRP", "Los Angeles Kings", "San Jose Sharks")]
    public void NhlReleaseWithTwoLetterOfficialCodes_MatchesTheRightGame(string title, string home, string away)
    {
        var evt = Game(Nhl, home, away, new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc));

        var result = Validate(title, evt);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void NflReleaseWithTwoLetterOfficialCodes_MatchesTheRightGame()
    {
        var evt = Game(Nfl, "Kansas City Chiefs", "Green Bay Packers",
            new DateTime(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc));

        var result = Validate("NFL 2026 10 04 GB @ KC 720p WEB h264-GRP", evt);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void TwoLetterCodes_NeedTheLeagueNameAndUpperCase()
    {
        var evt = Game(Mlb, "Kansas City Royals", "Seattle Mariners",
            new DateTime(2026, 6, 13, 23, 0, 0, DateTimeKind.Utc));

        Validate("2026 06 13 SEA @ KC 720p WEB h264-GRP", evt)
            .MatchReasons.Should().NotContain("Both team names found");
        Validate("MLB 2026 06 13 sea @ kc 720p WEB h264-GRP", evt)
            .MatchReasons.Should().NotContain("Both team names found");
    }
}
