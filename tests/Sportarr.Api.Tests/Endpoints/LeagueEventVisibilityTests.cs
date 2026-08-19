using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using FluentAssertions;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Coverage for issue #244: the league events endpoint filtered by monitored
/// teams with no specials bypass, so the postseason games the sync's
/// MonitorFinals / MonitorPlayoffs opt-ins deliberately added were invisible
/// in the UI. The reporter's Super Bowl sat in the database while the event
/// list showed a regular-season game as the latest of the season.
/// </summary>
public class LeagueEventVisibilityTests
{
    private static readonly HashSet<string> MonitoredTeams = new() { "tm-cowboys", "tm-chiefs" };

    private static Event Ev(string title, string? round, string home, string away, bool hasFile = false) => new()
    {
        Title = title,
        Sport = "American Football",
        Round = round,
        Season = "2025",
        HomeTeamExternalId = home,
        AwayTeamExternalId = away,
        HomeTeamName = home,
        AwayTeamName = away,
        EventDate = new DateTime(2026, 2, 8),
        HasFile = hasFile
    };

    private static League NflLeague(bool finals = false, bool playoffs = false, bool preseason = false) => new()
    {
        Name = "NFL",
        Sport = "American Football",
        MonitorFinals = finals,
        MonitorPlayoffs = playoffs,
        MonitorPreseason = preseason
    };

    [Fact]
    public void SuperBowl_IsVisible_WhenMonitorFinalsIsOn()
    {
        // Round 200 is TheSportsDB's final; neither team is monitored.
        var events = new List<Event>
        {
            Ev("New England Patriots vs Seattle Seahawks", "200", "tm-patriots", "tm-seahawks"),
            Ev("Las Vegas Raiders vs Kansas City Chiefs", "18", "tm-raiders", "tm-chiefs")
        };

        var visible = LeagueEndpoints.FilterEventsByMonitoredTeams(events, MonitoredTeams, NflLeague(finals: true));

        visible.Should().Contain(e => e.Round == "200",
            "the sync added the final via the specials bypass, so the UI must show it");
    }

    [Fact]
    public void PlayoffGames_AreVisible_WhenMonitorPlayoffsIsOn()
    {
        var events = new List<Event>
        {
            Ev("Carolina Panthers vs Los Angeles Rams", "160", "tm-panthers", "tm-rams"),
            Ev("Denver Broncos vs Buffalo Bills", "125", "tm-broncos", "tm-bills"),
            Ev("Seattle Seahawks vs Los Angeles Rams", "150", "tm-seahawks", "tm-rams")
        };

        var visible = LeagueEndpoints.FilterEventsByMonitoredTeams(events, MonitoredTeams, NflLeague(playoffs: true));

        visible.Should().HaveCount(3);
    }

    [Fact]
    public void NonTeamGames_StayHidden_WhenNoOptInCoversThem()
    {
        var events = new List<Event>
        {
            Ev("New England Patriots vs Seattle Seahawks", "200", "tm-patriots", "tm-seahawks"),
            Ev("Carolina Panthers vs Los Angeles Rams", "160", "tm-panthers", "tm-rams"),
            Ev("New York Giants vs Dallas Cowboys", "18", "tm-giants", "tm-cowboys")
        };

        var visible = LeagueEndpoints.FilterEventsByMonitoredTeams(events, MonitoredTeams, NflLeague());

        visible.Should().ContainSingle(e => e.Round == "18",
            "with every opt-in off, only monitored-team games show");
    }

    [Fact]
    public void EventsWithFiles_AreAlwaysVisible()
    {
        // The sync's out-of-filter cleanup keeps rows that hold files, so
        // the UI must show them regardless of the filter.
        var events = new List<Event>
        {
            Ev("Carolina Panthers vs Los Angeles Rams", "160", "tm-panthers", "tm-rams", hasFile: true)
        };

        var visible = LeagueEndpoints.FilterEventsByMonitoredTeams(events, MonitoredTeams, NflLeague());

        visible.Should().HaveCount(1);
    }

    [Fact]
    public void FinalsOptIn_DoesNotLeakPlayoffGames()
    {
        var events = new List<Event>
        {
            Ev("New England Patriots vs Seattle Seahawks", "200", "tm-patriots", "tm-seahawks"),
            Ev("Carolina Panthers vs Los Angeles Rams", "160", "tm-panthers", "tm-rams")
        };

        var visible = LeagueEndpoints.FilterEventsByMonitoredTeams(events, MonitoredTeams, NflLeague(finals: true));

        visible.Should().ContainSingle(e => e.Round == "200");
    }
}
