using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class EventQueryServiceTeamSportTests
{
    private static EventQueryService CreateService() =>
        new(NullLogger<EventQueryService>.Instance);

    [Fact]
    public void BuildEventQueries_NhlGame_UsesSpacesNotDots()
    {
        var service = CreateService();
        var evt = new Event
        {
            Title = "New Jersey Devils vs New York Rangers",
            Sport = "Ice Hockey",
            EventDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "NHL", Sport = "Ice Hockey" },
        };

        var queries = service.BuildEventQueries(evt);

        // Space-separated is the format trackers accept; dot-separated ("NHL.2026.01")
        // returned nothing on some trackers.
        queries.Should().Contain("NHL 2026 01");
        queries.Should().NotContain(q => q.Contains("NHL.2026"));
    }

    [Fact]
    public void BuildEventQueries_CollegeFootballGame_UsesOneMeasuredQuery()
    {
        var service = CreateService();
        var homeTeam = new Team { Name = "South Florida" };
        var awayTeam = new Team { Name = "Old Dominion" };
        var evt = new Event
        {
            Title = "South Florida vs Old Dominion",
            Sport = "Football",
            EventDate = new DateTime(2025, 12, 17, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "NCAA Division 1", Sport = "Football" },
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
        };

        var queries = service.BuildEventQueries(evt);

        // College football has no recognized league-prefix shorthand (unlike
        // NFL/NBA/etc.), so the query is the event title verbatim - but some
        // indexers title releases in broadcast order rather than Sportarr's
        // home/away designation, so the reversed pairing must also be tried.
        queries.Should().Equal("NCAAF 2025 South Florida Old Dominion");
    }

    [Fact]
    public void BuildEventQueries_NbaGame_UsesOneStableNicknamePair()
    {
        var service = CreateService();
        var spursHome = new Event
        {
            Title = "San Antonio Spurs vs New York Knicks",
            Sport = "Basketball",
            EventDate = new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "NBA", Sport = "Basketball" },
            HomeTeamId = 10,
            AwayTeamId = 20,
            HomeTeamName = "San Antonio Spurs",
            AwayTeamName = "New York Knicks"
        };
        var knicksHome = new Event
        {
            Title = "New York Knicks vs San Antonio Spurs",
            Sport = "Basketball",
            EventDate = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "NBA", Sport = "Basketball" },
            HomeTeamId = 20,
            AwayTeamId = 10,
            HomeTeamName = "New York Knicks",
            AwayTeamName = "San Antonio Spurs"
        };

        service.BuildEventQueries(spursHome).Should().Equal("NBA Spurs Knicks");
        service.BuildEventQueries(knicksHome).Should().Equal("NBA Spurs Knicks");
    }

    [Fact]
    public void BuildMetadataTitleProbe_NbaGame_UsesExactDateFallback()
    {
        var service = CreateService();
        var evt = new Event
        {
            Title = "Oklahoma City Thunder vs Boston Celtics",
            Sport = "Basketball",
            EventDate = new DateTime(2026, 6, 14, 1, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 6, 13),
            League = new League { Name = "NBA", Sport = "Basketball" },
            HomeTeamId = 30,
            AwayTeamId = 40,
            HomeTeamName = "Oklahoma City Thunder",
            AwayTeamName = "Boston Celtics"
        };
        var queries = service.BuildEventQueries(evt);

        queries.Should().Equal("NBA Thunder Celtics");
        service.BuildMetadataTitleProbe(evt, queries).Should().Be("NBA 2026 06 13");
    }

    [Fact]
    public void BuildEventQueries_EnglishPremierLeagueGame_UsesOneStableTeamPair()
    {
        var service = CreateService();
        var evt = new Event
        {
            Title = "Arsenal vs Chelsea",
            Sport = "Soccer",
            EventDate = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "English Premier League", Sport = "Soccer" },
            HomeTeamId = 30,
            AwayTeamId = 40,
            HomeTeamName = "Arsenal",
            AwayTeamName = "Chelsea"
        };

        service.BuildEventQueries(evt).Should().Equal("Arsenal Chelsea");
    }

    [Theory]
    [InlineData("Spanish La Liga", "Real Betis vs Real Madrid", "Real Betis", "Real Madrid", "Real Betis Real Madrid")]
    [InlineData("German Bundesliga", "Hoffenheim vs Borussia Dortmund", "Hoffenheim", "Borussia Dortmund", "Borussia Dortmund Hoffenheim")]
    [InlineData("Italian Serie A", "Juventus vs AC Milan", "Juventus", "AC Milan", "AC Milan Juventus")]
    [InlineData("French Ligue 1", "Paris Saint-Germain vs Monaco", "Paris SG", "Monaco", "Monaco Paris Saint-Germain")]
    public void BuildEventQueries_VerifiedEuropeanFootballLeague_UsesOneFullTitleTeamPair(
        string leagueName,
        string title,
        string homeTeamName,
        string awayTeamName,
        string expected)
    {
        var service = CreateService();
        var evt = new Event
        {
            Title = title,
            Sport = "Soccer",
            EventDate = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = leagueName, Sport = "Soccer" },
            HomeTeamId = 30,
            AwayTeamId = 40,
            HomeTeamName = homeTeamName,
            AwayTeamName = awayTeamName
        };

        service.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Fact]
    public void BuildEventQueries_MlsGame_KeepsSharedSeasonQueries()
    {
        var service = CreateService();
        var evt = new Event
        {
            Title = "Inter Miami vs Atlanta United",
            Sport = "Soccer",
            EventDate = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 9, 5),
            League = new League { Name = "American Major League Soccer", Sport = "Soccer" },
            HomeTeamId = 30,
            AwayTeamId = 40,
            HomeTeamName = "Inter Miami",
            AwayTeamName = "Atlanta United"
        };

        service.BuildEventQueries(evt).Should().Equal("MLS 2026 09", "MLS 2026");
    }

    [Theory]
    [InlineData("FA Cup", "Chelsea vs Manchester City", "Chelsea", "Manchester City", "FA Cup Chelsea Manchester City")]
    [InlineData("UEFA Europa League", "Nottingham Forest vs Porto", "Nottingham Forest", "FC Porto", "Nottingham Forest Porto")]
    [InlineData("FIFA World Cup", "Spain vs Argentina", "Spain", "Argentina", "Argentina Spain")]
    [InlineData("English Womens Super League", "Chelsea Women vs Manchester United WFC", "Chelsea Women", "Manchester United WFC", "WSL Chelsea Manchester United")]
    public void BuildEventQueries_VerifiedCupOrWsl_UsesOneObservedParticipantQuery(
        string leagueName,
        string title,
        string homeTeamName,
        string awayTeamName,
        string expected)
    {
        var service = CreateService();
        var evt = new Event
        {
            Title = title,
            Sport = "Soccer",
            EventDate = new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 9, 13),
            League = new League { Name = leagueName, Sport = "Soccer" },
            HomeTeamId = 30,
            AwayTeamId = 40,
            HomeTeamName = homeTeamName,
            AwayTeamName = awayTeamName
        };

        service.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Fact]
    public void BuildEventQueries_EnglishChampionship_UsesOneReusableLeagueYearQuery()
    {
        var service = CreateService();
        var evt = new Event
        {
            Title = "Sheffield United vs Wolverhampton Wanderers",
            Sport = "Soccer",
            EventDate = new DateTime(2026, 9, 13, 11, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 9, 13),
            League = new League { Name = "English League Championship", Sport = "Soccer" },
            HomeTeamId = 30,
            AwayTeamId = 40,
            HomeTeamName = "Sheffield United",
            AwayTeamName = "Wolverhampton Wanderers"
        };

        service.BuildEventQueries(evt).Should().Equal("EFL Championship 2026");
    }
}
