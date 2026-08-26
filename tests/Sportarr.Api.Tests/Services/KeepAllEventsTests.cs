using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// KeepAllEvents lets a league hold every event, including games with none
/// of the monitored teams in them, so a one-off game can be found and
/// monitored by hand. Those extra events must arrive UNMONITORED. If they
/// did not, turning the setting on would start searching for every game in
/// the league, which is the opposite of what a team selection is for.
/// </summary>
public class KeepAllEventsTests
{
    private static readonly IReadOnlySet<int> NoCupStages = new HashSet<int>();

    private static Event Game(string home, string away, string? round = "18") => new()
    {
        Title = $"{home} vs {away}",
        Sport = "American Football",
        Round = round,
        Season = "2026",
        EventDate = new DateTime(2026, 9, 12),
        HomeTeamExternalId = home,
        AwayTeamExternalId = away,
    };

    private static League Nfl(bool finals = false, bool playoffs = false) => new()
    {
        Name = "NFL",
        Sport = "American Football",
        Monitored = true,
        MonitorType = MonitorType.All,
        KeepAllEvents = true,
        MonitorFinals = finals,
        MonitorPlayoffs = playoffs,
    };

    [Fact]
    public void GameWithNoFollowedTeam_IsNotMonitored()
    {
        var teams = new HashSet<string> { "tm-cowboys" };

        LeagueEventSyncService.IsInsideTeamSelection(
            Game("tm-bears", "tm-packers"), Nfl(), teams, NoCupStages)
            .Should().BeFalse("keeping the game must not mean searching for it");
    }

    [Fact]
    public void GameWithAFollowedTeam_StaysMonitored()
    {
        var teams = new HashSet<string> { "tm-cowboys" };

        LeagueEventSyncService.IsInsideTeamSelection(
            Game("tm-giants", "tm-cowboys"), Nfl(), teams, NoCupStages)
            .Should().BeTrue();
    }

    [Fact]
    public void LeagueWithNoTeamSelection_MonitorsEverythingAsBefore()
    {
        LeagueEventSyncService.IsInsideTeamSelection(
            Game("tm-bears", "tm-packers"), Nfl(), new HashSet<string>(), NoCupStages)
            .Should().BeTrue("without a team selection nothing was ever filtered");
    }

    [Fact]
    public void SpecialsOptIns_StillMonitorTheirEvents()
    {
        var teams = new HashSet<string> { "tm-cowboys" };

        // Round 200 is the final, and the league opted into finals.
        LeagueEventSyncService.IsInsideTeamSelection(
            Game("tm-patriots", "tm-seahawks", "200"), Nfl(finals: true), teams, NoCupStages)
            .Should().BeTrue("the finals opt-in already covers this game");

        // Same game, no opt-in.
        LeagueEventSyncService.IsInsideTeamSelection(
            Game("tm-patriots", "tm-seahawks", "200"), Nfl(), teams, NoCupStages)
            .Should().BeFalse();
    }

    [Fact]
    public void SavingLeagueSettings_MustNotMonitorRetainedGames()
    {
        // The league PUT recalculates monitoring across every stored event.
        // Before KeepAllEvents those out-of-team games did not exist, so the
        // loop had no team test and would have monitored the whole league,
        // starting a search for every game. This is the gate it now shares
        // with the sync.
        var teams = new HashSet<string> { "tm-cowboys" };
        var league = Nfl();
        league.MonitorType = MonitorType.All;

        var retained = Game("tm-bears", "tm-packers");

        var shouldMonitor = league.Monitored
            && LeagueEventSyncService.IsInsideTeamSelection(retained, league, teams, NoCupStages);

        shouldMonitor.Should().BeFalse("saving a setting must not arm the games the user only wanted to keep");
    }

    [Fact]
    public void PlayoffOptIn_IsSeparateFromFinals()
    {
        var teams = new HashSet<string> { "tm-cowboys" };

        LeagueEventSyncService.IsInsideTeamSelection(
            Game("tm-bears", "tm-packers", "160"), Nfl(playoffs: true), teams, NoCupStages)
            .Should().BeTrue();

        LeagueEventSyncService.IsInsideTeamSelection(
            Game("tm-bears", "tm-packers", "160"), Nfl(finals: true), teams, NoCupStages)
            .Should().BeFalse("finals and postseason are separate opt-ins");
    }
}
