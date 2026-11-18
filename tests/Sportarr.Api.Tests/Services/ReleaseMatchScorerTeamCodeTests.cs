using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Release groups often name the two teams by short codes ("MI vs CSK").
/// A search that picks its own release needs the auto-grab score, so codes
/// that name both teams in a matchup, with a date or a match number of the
/// event, must reach it. A code never outscores a known abbreviation. A code
/// that is a plain word, that two teams share, or that belongs to another
/// team must not help.
/// </summary>
public class ReleaseMatchScorerTeamCodeTests
{
    private static readonly League Ipl = new() { Id = 10, Name = "Indian Premier League", Sport = "Cricket" };
    private static readonly League Nba = new() { Id = 11, Name = "NBA", Sport = "Basketball" };
    private static readonly League Nfl = new() { Id = 12, Name = "NFL", Sport = "American Football" };
    private static readonly League Nhl = new() { Id = 14, Name = "NHL", Sport = "Ice Hockey" };
    private static readonly League Epl = new() { Id = 13, Name = "English Premier League", Sport = "Soccer" };

    private readonly ReleaseMatchScorer _scorer = new();
    private readonly ReleaseMatchingService _matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    private static Event Game(
        League league, string home, string away, DateTime date, string? round = null,
        string? homeCode = null, string? awayCode = null, bool withTeamRows = false)
    {
        var rows = withTeamRows || homeCode != null || awayCode != null;
        return new Event
        {
            Id = 1,
            Title = home + " vs " + away,
            Sport = league.Sport,
            Season = date.Year.ToString(),
            Round = round,
            EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = home,
            AwayTeamName = away,
            HomeTeam = rows ? new Team { Id = 1, Name = home, Sport = league.Sport, ShortName = homeCode } : null,
            AwayTeam = rows ? new Team { Id = 2, Name = away, Sport = league.Sport, ShortName = awayCode } : null,
            League = league,
        };
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture",
    };

    private static Event MumbaiChennai(bool withTeamRows = false) => withTeamRows
        ? Game(Ipl, "Mumbai Indians", "Chennai Super Kings", new DateTime(2024, 4, 14), "29", "MI", "CSK")
        : Game(Ipl, "Mumbai Indians", "Chennai Super Kings", new DateTime(2024, 4, 14), "29");

    [Theory]
    [InlineData("IPL.2024.Match.29.MI.vs.CSK.720p", false)]
    [InlineData("IPL.2024.Match.29.MI.vs.CSK.720p", true)]
    [InlineData("IPL.2024.Match.29.Mumbai.Indians.vs.CSK.720p", false)]
    [InlineData("IPL.2024.Match.29.MI.vs.Chennai.Super.Kings.720p", false)]
    [InlineData("IPL.2024.04.14.MI.vs.CSK.720p", false)]
    public void IplReleaseNamedByCodes_WithItsMatchNumberOrDate_ReachesTheAutoGrabScore(string title, bool withTeamRows)
    {
        _scorer.CalculateMatchScore(title, MumbaiChennai(withTeamRows))
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void CodesNeverOutscoreFullNames()
    {
        var evt = MumbaiChennai();
        var fullNames = _scorer.CalculateMatchScore(
            "IPL.2024.Match.29.Mumbai.Indians.vs.Chennai.Super.Kings.720p", evt);

        _scorer.CalculateMatchScore("IPL.2024.Match.29.MI.vs.CSK.720p", evt).Should().BeLessThan(fullNames);
    }

    [Fact]
    public void CodesOfAnotherMeetingOfTheSamePair_ScoreLow()
    {
        var matchThree = Game(Ipl, "Mumbai Indians", "Chennai Super Kings", new DateTime(2025, 3, 23), "3");

        _scorer.CalculateMatchScore("IPL.2025.Match.38.MI.vs.CSK.720p", matchThree)
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        _scorer.CalculateMatchScore("IPL.2025.MI.vs.CSK.720p", matchThree)
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("IPL.2024.Match.29.MI.vs.CSK.720p")]
    [InlineData("IPL.2024.MI.vs.Punjab.Kings.720p")]
    public void SameDaySameLeague_CodesOfAnotherPair_ScoreLow(string title)
    {
        var evt = Game(Ipl, "Delhi Capitals", "Punjab Kings", new DateTime(2024, 4, 14), "30");

        _scorer.CalculateMatchScore(title, evt).Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Fact]
    public void CodeSharedByTwoTeamsOfTheLeague_DoesNotLiftTheScore()
    {
        // Deccan Chargers and Delhi Capitals both use "DC".
        var evt = Game(Ipl, "Deccan Chargers", "Mumbai Indians", new DateTime(2010, 3, 20), "10", homeCode: "DC");

        _scorer.CalculateMatchScore("IPL.2010.Match.10.DC.vs.MI.720p", evt)
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        _scorer.CalculateMatchScore("IPL.2010.Match.10.Deccan.Chargers.vs.Mumbai.Indians.720p", evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void DefunctTeamWithoutCodes_IsNotVetoedByACodeOfANewerTeam()
    {
        // The table knows "DC" only as Delhi Capitals. Deccan Chargers has no codes.
        var evt = Game(Ipl, "Deccan Chargers", "Mumbai Indians", new DateTime(2010, 3, 20), "10");

        _scorer.CalculateMatchScore("IPL.2010.Match.10.DC.vs.MI.720p", evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Fact]
    public void CodeThatOnlyOneTeamUses_StillLifts()
    {
        var evt = Game(Ipl, "Delhi Capitals", "Mumbai Indians", new DateTime(2024, 3, 31), "9");

        _scorer.CalculateMatchScore("IPL.2024.Match.9.DC.vs.MI.720p", evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("ipl.2024.match.29.mi.vs.csk.720p")]
    [InlineData("IPL.2024.Match.29.MI.Highlights.720p")]
    [InlineData("IPL.2024.Match.29.CSK.Fan.Zone.MI.720p")]
    public void CodesWithoutAMatchup_DoNotLift(string title)
    {
        _scorer.CalculateMatchScore(title, MumbaiChennai())
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    private static Event RamsSeahawks() =>
        Game(Nfl, "Los Angeles Rams", "Seattle Seahawks", new DateTime(2024, 10, 6));

    [Theory]
    [InlineData("NFL.2024.10.06.SEA.Pregame.Live.at.LA.720p")]
    [InlineData("NFL.2024.10.06.SEA.vs.LAFC.Watch.Party.720p")]
    [InlineData("NFL.2024.10.06.SEA.vs.PLAYOFF.LAB.720p")]
    [InlineData("2024.10.06.SEA.vs.LA.720p")]
    public void TwoLetterCode_AsALocationOrInsideAWord_DoesNotLift(string title)
    {
        _scorer.CalculateMatchScore(title, RamsSeahawks())
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void TwoLetterCode_InAMatchupWithTheLeagueNamed_Lifts()
    {
        _scorer.CalculateMatchScore("NFL.2024.10.06.SEA.vs.LA.720p", RamsSeahawks())
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    private static Event LakersCeltics() =>
        Game(Nba, "Los Angeles Lakers", "Boston Celtics", new DateTime(2024, 10, 22));

    [Fact]
    public void NbaReleaseNamedByCodes_WithTheDate_ReachesTheAutoGrabScore()
    {
        _scorer.CalculateMatchScore("NBA.2024.10.22.LAL.vs.BOS.720p", LakersCeltics())
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void CodesWithoutADateOrMatchNumber_DoNotReachTheAutoGrabScore()
    {
        _scorer.CalculateMatchScore("LAL.vs.BOS.2024.720p", LakersCeltics())
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        _scorer.CalculateMatchScore("NYR.vs.NYI.2024.720p",
                Game(Nhl, "New York Rangers", "New York Islanders", new DateTime(2024, 10, 22)))
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        _scorer.CalculateMatchScore("MUN.vs.LIV.2024.720p", UnitedLiverpool())
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void CodesOfAnotherTeamInTheLeague_Reject()
    {
        _scorer.CalculateMatchScore("NBA.2024.10.22.LAL.vs.LAC.720p", LakersCeltics())
            .Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Fact]
    public void ForeignCodeInTheMatchup_IsNotExcusedByAnEventCodeInTheGroupTag()
    {
        _scorer.CalculateMatchScore("NBA.2024.10.22.LAL.vs.MIA.720p-BOS", LakersCeltics())
            .Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Fact]
    public void CodePairThatAnotherLeagueAlsoHas_DoesNotLiftWithoutTheLeagueName()
    {
        // Toronto and Boston also play each other in the NHL and in MLB.
        var evt = Game(Nba, "Toronto Raptors", "Boston Celtics", new DateTime(2024, 10, 22));

        _scorer.CalculateMatchScore("TOR.vs.BOS.2024.720p", evt)
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void CodePairOfAnotherLeague_DoesNotLift()
    {
        var evt = Game(Nba, "Toronto Raptors", "Boston Celtics", new DateTime(2024, 10, 22));

        _scorer.CalculateMatchScore("NHL.2024.10.22.TOR.vs.BOS.720p", evt)
            .Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
    }

    private static Event UnitedLiverpool() => Game(
        Epl, "Manchester United", "Liverpool", new DateTime(2024, 10, 20), homeCode: "MUN", awayCode: "LIV");

    [Fact]
    public void SoccerReleaseNamedByStoredShortNames_ReachesTheAutoGrabScore()
    {
        _scorer.CalculateMatchScore("Premier.League.2024.10.20.MUN.vs.LIV.720p", UnitedLiverpool())
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("Premier.League.2024.10.20.MCI.vs.LIV.720p")]
    [InlineData("Premier.League.2024.10.20.Manchester.City.vs.LIV.720p")]
    public void SoccerReleaseOfAnotherClub_DoesNotLift(string title)
    {
        _scorer.CalculateMatchScore(title, UnitedLiverpool())
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("IPL.2024.Match.29.MI.vs.CSK.720p")]
    [InlineData("IPL.2024.Match.29.Mumbai.Indians.vs.CSK.720p")]
    public void Validator_AcceptsCodesThatTheScorerAccepts(string title)
    {
        var result = _matcher.ValidateRelease(Release(title), MumbaiChennai());

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.IsHardRejection.Should().BeFalse();
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void Validator_DoesNotTakeCodesWithoutAMatchNumberOrDate()
    {
        var result = _matcher.ValidateRelease(Release("IPL.2024.MI.vs.CSK.720p"), MumbaiChennai());

        result.MatchReasons.Should().NotContain("Both team names found");
    }

    [Fact]
    public void Validator_RejectsCodesOfAnotherPair()
    {
        var evt = Game(Ipl, "Delhi Capitals", "Punjab Kings", new DateTime(2024, 4, 14), "30");

        _matcher.ValidateRelease(Release("IPL.2024.Match.30.MI.vs.CSK.720p"), evt).IsMatch.Should().BeFalse();
    }

    // A league abbreviation must never make a title name another league.
    [Theory]
    [InlineData("NRL.2024.Season.Complete.720p", "NRL Women's Premiership", "Rugby")]
    [InlineData("NRL.Womens.2024.Round.1.720p", "National Rugby League", "Rugby")]
    [InlineData("FIFA.CWC.2025.06.14.Al.Ahly.vs.Inter.Miami.720p", "ICC Cricket World Cup", "Cricket")]
    [InlineData("FIFA.CWC.2025.06.14.Al.Ahly.vs.Inter.Miami.720p", "Cricket World Cup", "Cricket")]
    [InlineData("Womens.IPL.2024.Match.1.720p", "Indian Premier League", "Cricket")]
    [InlineData("Womens.BBL.2024.Match.1.720p", "Big Bash League", "Cricket")]
    public void LeagueAbbreviation_DoesNotNameAnotherLeague(string title, string leagueName, string sport)
    {
        ReleaseMatchingService.TitleNamesLeague(title, new League { Name = leagueName, Sport = sport })
            .Should().BeFalse();
    }

    [Fact]
    public void FifaClubWorldCupRelease_IsNotTakenForTheCricketWorldCup()
    {
        var clubWorldCup = new League { Id = 20, Name = "FIFA Club World Cup", Sport = "Soccer" };
        var cricketWorldCup = new League { Id = 21, Name = "ICC Cricket World Cup", Sport = "Cricket" };
        var evt = Game(clubWorldCup, "Al Ahly", "Inter Miami", new DateTime(2025, 6, 14));

        var result = _matcher.ValidateRelease(
            Release("FIFA.CWC.2025.06.14.Al.Ahly.vs.Inter.Miami.720p"), evt,
            knownLeagues: new[] { clubWorldCup, cricketWorldCup });

        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
    }

    // Codes alone with no league name can be a game of a league without a
    // table: Seattle v Vancouver is also an MLS game.
    [Theory]
    [InlineData("SEA.vs.VAN.2024.10.22.720p", "NHL", "Ice Hockey", "Seattle Kraken", "Vancouver Canucks", 2024, 10, 22)]
    [InlineData("CIN.vs.COL.2024.05.18.720p", "MLB", "Baseball", "Cincinnati Reds", "Colorado Rockies", 2024, 5, 18)]
    public void CodesOnBothSides_WithoutTheLeagueName_DoNotLift(
        string title, string leagueName, string sport, string home, string away, int y, int m, int d)
    {
        var evt = Game(new League { Id = 30, Name = leagueName, Sport = sport }, home, away, new DateTime(y, m, d));

        _scorer.CalculateMatchScore(title, evt).Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        _matcher.ValidateRelease(Release(title), evt).MatchReasons.Should().NotContain("Both team names found");
    }

    [Fact]
    public void CodesOfAnotherPair_WithoutAMatchNumber_ScoreLow()
    {
        var evt = Game(Ipl, "Delhi Capitals", "Punjab Kings", new DateTime(2024, 4, 14), "30");

        _scorer.CalculateMatchScore("IPL.2024.MI.vs.CSK.720p", evt)
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void SecondGameOfAPack_DoesNotVetoTheFirst()
    {
        _scorer.CalculateMatchScore("NBA.2024.10.22.Lakers.vs.Celtics.and.NYK.vs.MIA.720p", LakersCeltics())
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void MatchNumberTie_IsOnlyForCricket()
    {
        var weekFive = Game(Nfl, "Los Angeles Rams", "Seattle Seahawks", new DateTime(2024, 10, 6), "5");

        _scorer.CalculateMatchScore("NFL.2024.M5.SEA.vs.LA.720p", weekFive)
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("IPL.2024.Match.29.Part.1.MI.vs.CSK.720p")]
    [InlineData("IPL.2024.Match.29.of.74.MI.vs.CSK.720p")]
    public void MatchNumberBeforeAPartOrSeasonCount_StillTiesTheCodes(string title)
    {
        _scorer.CalculateMatchScore(title, MumbaiChennai())
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    // Two codes of other teams that face each other, with no sign of either
    // event team, are another game of the same day.
    [Fact]
    public void ForeignCodePair_WithNoEventTeamInTheTitle_ScoresLow()
    {
        var evt = Game(Ipl, "Mumbai Indians", "Royal Challengers Bangalore", new DateTime(2024, 4, 11), "25");

        _scorer.CalculateMatchScore("IPL.2024.04.11.DC.vs.PBKS.720p", evt)
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    // A pack that names this game by codes keeps it, whatever the order.
    [Theory]
    [InlineData("IPL.2024.04.11.MI.vs.RCB.and.DC.vs.PBKS.720p")]
    [InlineData("IPL.2024.04.11.DC.vs.PBKS.and.MI.vs.RCB.720p")]
    [InlineData("IPL.2024.Match.25.MI.vs.RCB.Next.DC.vs.PBKS.720p")]
    public void IplCodePack_StillGrabsThisGame(string title)
    {
        var evt = Game(Ipl, "Mumbai Indians", "Royal Challengers Bangalore", new DateTime(2024, 4, 11), "25");

        _scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void CodePack_StillGrabsItsFirstGame()
    {
        _scorer.CalculateMatchScore("NBA.2024.10.22.LAL.vs.BOS.and.NYK.vs.MIA.720p", LakersCeltics())
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void CodeTables_ListEachTeamNameInOneLeagueOnly()
    {
        TeamShortCodeData.NamesInMoreThanOneLeague().Should().BeEmpty();
    }
}
