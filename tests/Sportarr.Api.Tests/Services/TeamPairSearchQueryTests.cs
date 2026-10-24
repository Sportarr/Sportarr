using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A broad team sport query such as "NHL 2026 10" returns whatever an
/// indexer lists first. An indexer that sorts newest first can fill its page
/// with other games, so the wanted game never appears. Each team sport also
/// searches the two team names.
/// </summary>
public class TeamPairSearchQueryTests
{
    private static EventQueryService QuerySvc() => new(NullLogger<EventQueryService>.Instance);

    private static Event Game(string league, string sport, string home, string away,
        int homeId = 1, int awayId = 2) => new()
    {
        Title = $"{home} vs {away}",
        Sport = sport,
        EventDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc),
        HomeTeamName = home,
        AwayTeamName = away,
        HomeTeamId = homeId,
        AwayTeamId = awayId,
        League = new League { Name = league, Sport = sport },
    };

    [Fact]
    public void Nhl_SearchesTheTwoTeamNamesAfterTheBroadQueries()
    {
        var queries = QuerySvc().BuildEventQueries(
            Game("NHL", "Ice Hockey", "Toronto Maple Leafs", "Boston Bruins"));

        queries.Should().Equal("NHL 2026 10", "NHL 2026", "NHL Leafs Bruins");
    }

    [Theory]
    [InlineData("NFL", "American Football", "Dallas Cowboys", "New York Giants", "NFL Cowboys Giants")]
    [InlineData("MLB", "Baseball", "Boston Red Sox", "New York Yankees", "MLB Sox Yankees")]
    public void OtherTeamSports_GetTheSameQuery(string league, string sport, string home, string away, string expected)
    {
        var queries = QuerySvc().BuildEventQueries(Game(league, sport, home, away));

        queries.Should().Contain(expected);
        queries.Should().HaveCount(3);
    }

    [Fact]
    public void TeamsEndingInTheSameWord_KeepTheirFullNames()
    {
        var queries = QuerySvc().BuildEventQueries(
            Game("MLS", "Soccer", "Inter Miami", "Austin FC", 1, 2));
        queries.Should().Contain("MLS Miami Austin FC");

        var sameWord = QuerySvc().BuildEventQueries(
            Game("MLS", "Soccer", "Atlanta United", "Minnesota United", 3, 4));
        sameWord.Should().Contain("MLS Atlanta United Minnesota United");

        EventQueryService.BuildTeamPairQuery("NHL", "Seattle Kraken", "Utah Kraken")
            .Should().Be("NHL Seattle Kraken Utah Kraken");
    }

    [Fact]
    public void ShortEndingsKeepTheWordBeforeThem()
    {
        EventQueryService.BuildTeamPairQuery("MLS", "Columbus Crew 2", "Austin FC II")
            .Should().Be("MLS Crew 2 FC II");
    }

    [Fact]
    public void NoPairQuery_WhenTheTeamsAreMissingOrTheSame()
    {
        var noTeams = Game("NHL", "Ice Hockey", "Toronto Maple Leafs", "Boston Bruins");
        noTeams.HomeTeamId = null;
        QuerySvc().BuildEventQueries(noTeams).Should().Equal("NHL 2026 10", "NHL 2026");

        var sameTeam = Game("NHL", "Ice Hockey", "Toronto Maple Leafs", "Toronto Maple Leafs", 5, 5);
        QuerySvc().BuildEventQueries(sameTeam).Should().Equal("NHL 2026 10", "NHL 2026");
    }
}
