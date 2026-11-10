using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Team rows exist only for monitored or followed teams, so most games
/// link one team or none. The team-name search must still run for those
/// games, with the same text whichever side is home (issue #397).
/// </summary>
public class TeamPairNameStabilityTests
{
    private static EventQueryService QuerySvc() => new(NullLogger<EventQueryService>.Instance);

    private static Event Game(string league, string sport, string home, string away,
        int? homeId, int? awayId) => new()
    {
        Title = $"{home} vs {away}",
        Sport = sport,
        EventDate = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc),
        HomeTeamName = home,
        AwayTeamName = away,
        HomeTeamId = homeId,
        AwayTeamId = awayId,
        League = new League { Name = league, Sport = sport },
    };

    [Theory]
    [InlineData(1, null)]
    [InlineData(null, 2)]
    [InlineData(null, null)]
    public void A_game_without_two_linked_teams_gets_the_team_name_query(int? homeId, int? awayId)
    {
        var queries = QuerySvc().BuildEventQueries(
            Game("NHL", "Ice Hockey", "Boston Bruins", "Vegas Golden Knights", homeId, awayId));

        queries.Should().Equal("NHL 2026 11", "NHL 2026", "NHL Bruins Knights");
    }

    [Fact]
    public void Swapping_home_and_away_gives_the_same_text_when_unlinked()
    {
        var home = QuerySvc().BuildEventQueries(
            Game("NHL", "Ice Hockey", "Vegas Golden Knights", "Boston Bruins", 7, null));
        var away = QuerySvc().BuildEventQueries(
            Game("NHL", "Ice Hockey", "Boston Bruins", "Vegas Golden Knights", null, 7));

        home.Should().Equal(away);
        home.Should().Contain("NHL Bruins Knights");
    }

    [Fact]
    public void Swapping_unlinked_nba_teams_gives_the_same_text()
    {
        var one = QuerySvc().BuildEventQueries(
            Game("NBA", "Basketball", "Los Angeles Lakers", "Boston Celtics", null, null));
        var other = QuerySvc().BuildEventQueries(
            Game("NBA", "Basketball", "Boston Celtics", "Los Angeles Lakers", null, null));

        one.Should().Equal(other);
        one.Should().Contain("NBA Celtics Lakers");
    }

    [Theory]
    [InlineData("NHL", "Ice Hockey", "Toronto Maple Leafs", "Boston Bruins", 1, 2,
        new[] { "NHL 2026 11", "NHL 2026", "NHL Leafs Bruins" })]
    [InlineData("NHL", "Ice Hockey", "Vegas Golden Knights", "Boston Bruins", 1, 2,
        new[] { "NHL 2026 11", "NHL 2026", "NHL Knights Bruins" })]
    [InlineData("NBA", "Basketball", "Los Angeles Lakers", "Boston Celtics", 9, 3,
        new[] { "NBA Celtics Lakers" })]
    [InlineData("NBA", "Basketball", "Los Angeles Lakers", "Boston Celtics", 3, 9,
        new[] { "NBA Lakers Celtics" })]
    [InlineData("NFL", "American Football", "New York Giants", "Dallas Cowboys", 4, 8,
        new[] { "NFL 2026 11", "NFL 2026", "NFL Giants Cowboys" })]
    public void Linked_games_keep_their_current_queries(string league, string sport, string home,
        string away, int homeId, int awayId, string[] expected)
    {
        QuerySvc().BuildEventQueries(Game(league, sport, home, away, homeId, awayId))
            .Should().Equal(expected);
    }

    [Theory]
    [InlineData("TBD", "Boston Bruins")]
    [InlineData("Boston Bruins", "TBA")]
    [InlineData("Winner of Game 1", "Boston Bruins")]
    [InlineData("Loser of Match 3", "Boston Bruins")]
    [InlineData("Team A", "Team B")]
    [InlineData("To Be Decided", "Boston Bruins")]
    [InlineData("Boston Bruins", "boston bruins ")]
    public void Placeholders_and_same_names_get_no_team_query(string home, string away)
    {
        QuerySvc().BuildEventQueries(Game("NHL", "Ice Hockey", home, away, null, null))
            .Should().Equal("NHL 2026 11", "NHL 2026");
    }

    [Theory]
    [InlineData("Team GB", "Team Canada", "NHL Canada Team GB")]
    [InlineData("Homer Hawks", "Teams United", "NHL Hawks United")]
    public void Real_team_names_that_look_like_placeholders_still_get_the_query(
        string home, string away, string expected)
    {
        QuerySvc().BuildEventQueries(Game("NHL", "Ice Hockey", home, away, null, null))
            .Should().Contain(expected);
    }

    [Fact]
    public void The_nba_dated_probe_follows_the_same_rule()
    {
        var unlinked = Game("NBA", "Basketball", "Los Angeles Lakers", "Boston Celtics", null, 3);
        QuerySvc().BuildMetadataTitleProbe(unlinked, new[] { "NBA Celtics Lakers" })
            .Should().Be("NBA 2026 11 03");

        var placeholder = Game("NBA", "Basketball", "TBD", "Boston Celtics", null, null);
        QuerySvc().BuildMetadataTitleProbe(placeholder, new[] { "NBA 2026 11" })
            .Should().BeNull();

        var linked = Game("NBA", "Basketball", "Los Angeles Lakers", "Boston Celtics", 9, 3);
        QuerySvc().BuildMetadataTitleProbe(linked, new[] { "NBA Celtics Lakers" })
            .Should().Be("NBA 2026 11 03");
    }
}
