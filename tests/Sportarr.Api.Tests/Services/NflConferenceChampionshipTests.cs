using FluentAssertions;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class NflConferenceChampionshipTests
{
    private static readonly IReadOnlySet<int> NoCupStages = new HashSet<int>();
    private static readonly HashSet<string> FollowedTeams = new() { "tm-cowboys" };

    private static League Nfl(bool finals, bool playoffs, string name = "NFL") => new()
    {
        Name = name,
        Sport = "American Football",
        Monitored = true,
        MonitorType = MonitorType.All,
        SpecialEventsMonitorType = MonitorType.All,
        KeepAllEvents = true,
        MonitorFinals = finals,
        MonitorPlayoffs = playoffs,
        MonitoredTeams = new List<LeagueTeam>
        {
            new() { Monitored = true, Team = new Team { ExternalId = "tm-cowboys", Name = "Dallas Cowboys" } },
        },
    };

    private static Event Game(string round, string title = "New England Patriots vs Indianapolis Colts", string season = "2003") => new()
    {
        Title = title,
        Season = season,
        Round = round,
        HasLaterSeasonFinal = true,
        Sport = "American Football",
        HomeTeamExternalId = "tm-home",
        AwayTeamExternalId = "tm-away",
        EventDate = season == "2007" ? new DateTime(2008, 1, 20) : new DateTime(2004, 1, 18),
    };

    [Theory]
    [InlineData("2003", "New England Patriots vs Indianapolis Colts")]
    [InlineData("2003", "Philadelphia Eagles vs Carolina Panthers")]
    [InlineData("2007", "New England Patriots vs San Diego Chargers")]
    [InlineData("2007", "Green Bay Packers vs New York Giants")]
    public void ReportedConferenceGamesFollowPostseasonInsteadOfFinals(string season, string title)
    {
        var game = Game("180", title, season);

        LeagueEventSyncService.IsInsideTeamSelection(game, Nfl(true, false), FollowedTeams, NoCupStages)
            .Should().BeFalse();
        LeagueEventSyncService.IsInsideTeamSelection(game, Nfl(false, true), FollowedTeams, NoCupStages)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("NFL", "Playoff Final")]
    [InlineData("NFL", "Play-off Final")]
    [InlineData("NFL", "Conference Final")]
    [InlineData("National Football League", "180")]
    [InlineData("nfl", " 180 ")]
    public void EquivalentLeagueAndRoundNamesUseTheSameTier(string leagueName, string round)
    {
        LeagueEventSyncService.IsInsideTeamSelection(Game(round), Nfl(true, false, leagueName), FollowedTeams, NoCupStages)
            .Should().BeFalse();
        LeagueEventSyncService.IsInsideTeamSelection(Game(round), Nfl(false, true, leagueName), FollowedTeams, NoCupStages)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("200", "New England Patriots vs Carolina Panthers")]
    [InlineData("180", "Super Bowl XXXVIII")]
    [InlineData("Playoff Final", "Super Bowl XLII")]
    public void SuperBowlStillFollowsFinals(string round, string title)
    {
        LeagueEventSyncService.IsInsideTeamSelection(Game(round, title), Nfl(true, false), FollowedTeams, NoCupStages)
            .Should().BeTrue();
        LeagueEventSyncService.IsInsideTeamSelection(Game(round, title), Nfl(false, true), FollowedTeams, NoCupStages)
            .Should().BeFalse();
    }

    [Fact]
    public void AFollowedTeamsConferenceGameNeedsPostseasonSelected()
    {
        var game = Game("180");
        game.HomeTeamExternalId = "tm-cowboys";

        LeagueEventSyncService.IsInsideTeamSelection(game, Nfl(false, false), FollowedTeams, NoCupStages)
            .Should().BeFalse();

        var league = Nfl(false, false);
        league.PlayoffTeamMode = SpecialEventTeamMode.SelectedTeams;
        LeagueEventSyncService.IsInsideTeamSelection(game, league, FollowedTeams, NoCupStages)
            .Should().BeTrue();
    }

    [Fact]
    public void SpecialEventsOnlyUsesTheCorrectTier()
    {
        var league = Nfl(true, false);
        league.MonitorType = MonitorType.SpecialsOnly;
        var game = Game("180");

        LeagueEventSyncService.ShouldMonitorEvent(league, game.EventDate, game.Season, "2026", "2026",
            game.Round, game.Title, NoCupStages, game.HasLaterSeasonFinal).Should().BeFalse();

        league.MonitorFinals = false;
        league.MonitorPlayoffs = true;
        LeagueEventSyncService.ShouldMonitorEvent(league, game.EventDate, game.Season, "2026", "2026",
            game.Round, game.Title, NoCupStages, game.HasLaterSeasonFinal).Should().BeTrue();
    }

    [Fact]
    public void FilteredLeagueViewUsesTheCorrectTier()
    {
        var league = Nfl(true, false);
        league.KeepAllEvents = false;
        var games = new List<Event> { Game("180") };

        LeagueEndpoints.SelectVisibleEvents(games, league, showAll: false).Should().BeEmpty();

        league.MonitorFinals = false;
        league.MonitorPlayoffs = true;
        LeagueEndpoints.SelectVisibleEvents(games, league, showAll: false).Should().ContainSingle();
    }

    [Fact]
    public void OtherLeaguesKeepPlayoffFinalsInTheFinalsTier()
    {
        var league = Nfl(true, false, "NBA");
        var final = Game("180");
        final.HasLaterSeasonFinal = false;
        league.Sport = "Basketball";

        LeagueEventSyncService.IsInsideTeamSelection(final, league, FollowedTeams, NoCupStages)
            .Should().BeTrue();
        SpecialEventClassifier.Classify("Playoff Final", "Team A vs Team B")
            .Should().Be(SpecialEventClassifier.SpecialTier.Final);
    }
}
