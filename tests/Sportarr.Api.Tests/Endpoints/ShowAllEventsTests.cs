using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using FluentAssertions;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// The league page normally shows only what the user follows, so a session
/// type or a team they do not follow is invisible even when the event is in
/// the library. showAll drops those filters for one request, so a one-off
/// event can be found and monitored by hand.
/// </summary>
public class ShowAllEventsTests
{
    private static Event Ev(string title, string? home = null, string? away = null) => new()
    {
        Title = title,
        Sport = "Sport",
        Season = "2026",
        EventDate = new DateTime(2026, 8, 15),
        HomeTeamExternalId = home,
        AwayTeamExternalId = away,
    };

    private static League TeamLeague(params string[] monitoredTeamExternalIds) => new()
    {
        Name = "NFL",
        Sport = "American Football",
        MonitoredTeams = monitoredTeamExternalIds
            .Select(id => new LeagueTeam { Monitored = true, Team = new Team { ExternalId = id, Name = id } })
            .ToList(),
    };

    private static League MotorsportLeague(string? monitoredSessionTypes) => new()
    {
        Name = "Formula 1",
        Sport = "Motorsport",
        MonitoredSessionTypes = monitoredSessionTypes,
    };

    [Fact]
    public void ShowAll_RevealsGamesFromTeamsTheUserDoesNotFollow()
    {
        var league = TeamLeague("tm-cowboys");
        var events = new List<Event>
        {
            Ev("New York Giants vs Dallas Cowboys", "tm-giants", "tm-cowboys"),
            Ev("Chicago Bears vs Green Bay Packers", "tm-bears", "tm-packers"),
        };

        LeagueEndpoints.SelectVisibleEvents(events, league, showAll: false)
            .Should().ContainSingle(e => e.HomeTeamExternalId == "tm-giants");

        LeagueEndpoints.SelectVisibleEvents(events, league, showAll: true)
            .Should().HaveCount(2, "the toggle exists to surface the game the filter hides");
    }

    [Fact]
    public void ShowAll_RevealsSessionTypesTheUserDoesNotMonitor()
    {
        var league = MotorsportLeague("Race");
        var events = new List<Event>
        {
            Ev("Dutch Grand Prix Race"),
            Ev("Dutch Grand Prix Practice 1"),
            Ev("Dutch Grand Prix Qualifying"),
        };

        LeagueEndpoints.SelectVisibleEvents(events, league, showAll: false)
            .Should().ContainSingle(e => e.Title.EndsWith("Race"));

        LeagueEndpoints.SelectVisibleEvents(events, league, showAll: true)
            .Should().HaveCount(3);
    }

    [Fact]
    public void ShowAll_RevealsEventsWhenEverySessionIsDeselected()
    {
        // An empty session string means the user cleared them all, which
        // normally shows nothing at all.
        var league = MotorsportLeague("");
        var events = new List<Event> { Ev("Dutch Grand Prix Race"), Ev("Dutch Grand Prix Practice 1") };

        LeagueEndpoints.SelectVisibleEvents(events, league, showAll: false).Should().BeEmpty();
        LeagueEndpoints.SelectVisibleEvents(events, league, showAll: true).Should().HaveCount(2);
    }

    [Fact]
    public void DefaultView_IsUnchangedForALeagueWithNoFilters()
    {
        var league = TeamLeague();
        var events = new List<Event> { Ev("A vs B", "tm-a", "tm-b"), Ev("C vs D", "tm-c", "tm-d") };

        LeagueEndpoints.SelectVisibleEvents(events, league, showAll: false).Should().HaveCount(2);
        LeagueEndpoints.SelectVisibleEvents(events, league, showAll: true).Should().HaveCount(2);
    }

    [Fact]
    public void MotorsportWithNoSessionFilter_ShowsEverythingEitherWay()
    {
        var league = MotorsportLeague(null);
        var events = new List<Event> { Ev("Dutch Grand Prix Race"), Ev("Dutch Grand Prix Practice 1") };

        LeagueEndpoints.SelectVisibleEvents(events, league, showAll: false).Should().HaveCount(2);
        LeagueEndpoints.SelectVisibleEvents(events, league, showAll: true).Should().HaveCount(2);
    }
}
