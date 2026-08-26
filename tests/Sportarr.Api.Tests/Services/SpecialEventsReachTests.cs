using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The special-event toggles carry an event past the season rule as well as
/// past the team filter. That reach had no limit, so switching on finals and
/// championships monitored the championship of every season the league has
/// ever had, back to the 1960s for the NFL, which is not what someone picking
/// it expects (issue #254).
///
/// It now takes the same choices the league itself has. A league that predates
/// the setting carries All, so nothing changes for anyone until they say so.
/// </summary>
public class SpecialEventsReachTests
{
    private static readonly IReadOnlySet<int> NoCupStages = new HashSet<int>();

    private static League Nfl(MonitorType type, MonitorType reach) => new()
    {
        Name = "NFL",
        Sport = "American Football",
        Monitored = true,
        MonitorType = type,
        MonitorFinals = true,
        MonitorPlayoffs = true,
        SpecialEventsMonitorType = reach,
    };

    private static bool Monitor(League league, string round, DateTime date, string season = "2022") =>
        LeagueEventSyncService.ShouldMonitorEvent(
            league, date, season, "2026", "2026", round, "Eagles vs Chiefs", NoCupStages);

    // Round 200 is a final. The date is years back, like Super Bowl LVII.
    private static readonly DateTime LongAgo = DateTime.UtcNow.AddYears(-3);

    [Fact]
    public void A_reach_of_all_seasons_keeps_an_old_championship()
    {
        Monitor(Nfl(MonitorType.Future, MonitorType.All), "200", LongAgo)
            .Should().BeTrue("all seasons is what the toggle meant before it could be narrowed");
    }

    [Fact]
    public void A_reach_of_future_leaves_an_old_championship_alone()
    {
        Monitor(Nfl(MonitorType.Future, MonitorType.Future), "200", LongAgo)
            .Should().BeFalse("a championship from three years ago is not a future event");
    }

    [Fact]
    public void A_reach_of_future_still_takes_a_championship_yet_to_come()
    {
        Monitor(Nfl(MonitorType.Future, MonitorType.Future), "200", DateTime.UtcNow.AddMonths(2), season: "2026")
            .Should().BeTrue("the toggle still carries it past the team filter and the season rule agrees");
    }

    [Fact]
    public void A_reach_of_recent_takes_a_championship_from_last_week()
    {
        Monitor(Nfl(MonitorType.Future, MonitorType.Recent), "200", DateTime.UtcNow.AddDays(-7))
            .Should().BeTrue();
    }

    [Fact]
    public void A_reach_of_recent_leaves_one_from_years_ago()
    {
        Monitor(Nfl(MonitorType.Future, MonitorType.Recent), "200", LongAgo)
            .Should().BeFalse();
    }

    [Fact]
    public void The_reach_does_not_widen_an_ordinary_game()
    {
        // Round 7 is a regular-season fixture, so no toggle applies to it and
        // the reach has nothing to say either.
        Monitor(Nfl(MonitorType.Future, MonitorType.All), "7", LongAgo)
            .Should().BeFalse();
    }

    [Fact]
    public void Special_events_only_answers_to_the_same_reach()
    {
        // Choosing to monitor only special events asks the same two questions,
        // which kinds and how far back, so it reads the same way as any other
        // league.
        Monitor(Nfl(MonitorType.SpecialsOnly, MonitorType.All), "200", LongAgo)
            .Should().BeTrue();

        Monitor(Nfl(MonitorType.SpecialsOnly, MonitorType.Future), "200", LongAgo)
            .Should().BeFalse();
    }

    [Fact]
    public void Monitoring_nothing_still_means_nothing()
    {
        Monitor(Nfl(MonitorType.None, MonitorType.All), "200", LongAgo)
            .Should().BeFalse("None is absolute and no toggle argues with it");
    }
}
