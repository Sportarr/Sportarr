using Sportarr.Api.Services;
using Sportarr.Api.Models;
using Sportarr.Api.Helpers;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #244: "Always monitor finals and championships" only
/// carried an event past the monitored-teams filter. The monitoring decision
/// itself ignored the toggles for every monitor type except SpecialsOnly, so
/// under MonitorType.Future an already-played Super Bowl was added to the
/// library but arrived unmonitored, and saving the league unmonitored the
/// postseason games the toggle was supposed to protect. The reporter's trace
/// showed exactly that: the sync admitted the games with the bypass note,
/// then the save pass reported "0 now monitored, 2069 now unmonitored".
/// </summary>
public class AlwaysMonitorSpecialsTests
{
    // The reach defaults to All here because that is what these cases were
    // written against, and it is what a league that predates the setting
    // carries. How far the toggles reach is covered on its own below.
    private static League Nfl(MonitorType type, bool finals = true, bool playoffs = true,
        MonitorType reach = MonitorType.All) => new()
    {
        Name = "NFL",
        Sport = "American Football",
        Monitored = true,
        MonitorType = type,
        MonitorFinals = finals,
        MonitorPlayoffs = playoffs,
        SpecialEventsMonitorType = reach,
    };

    private static readonly IReadOnlySet<int> NoCupStages = new HashSet<int>();

    private static bool Monitor(League league, string round, DateTime date) =>
        LeagueEventSyncService.ShouldMonitorEvent(
            league, date, "2025", "2026", "2026", round, "Patriots vs Seahawks", NoCupStages);

    [Fact]
    public void PastSuperBowl_UnderFuture_IsStillMonitored()
    {
        var monitored = Monitor(Nfl(MonitorType.Future), round: "200", date: DateTime.UtcNow.AddMonths(-6));

        monitored.Should().BeTrue("the finals toggle says always, not only when the type filter agrees");
    }

    [Fact]
    public void PastPlayoffGame_UnderFuture_IsStillMonitored()
    {
        Monitor(Nfl(MonitorType.Future), "160", DateTime.UtcNow.AddMonths(-7))
            .Should().BeTrue("round 160 is a wildcard game and MonitorPlayoffs is on");
    }

    [Fact]
    public void PastRegularSeasonGame_UnderFuture_StaysUnmonitored()
    {
        Monitor(Nfl(MonitorType.Future), "7", DateTime.UtcNow.AddMonths(-6))
            .Should().BeFalse("the toggles cover specials, not the whole season");
    }

    [Fact]
    public void TogglesOff_ChangeNothing()
    {
        Monitor(Nfl(MonitorType.Future, finals: false, playoffs: false), "200", DateTime.UtcNow.AddMonths(-6))
            .Should().BeFalse();
    }

    [Fact]
    public void MonitorTypeNone_StaysAbsolute()
    {
        Monitor(Nfl(MonitorType.None), "200", DateTime.UtcNow.AddMonths(-6))
            .Should().BeFalse("None means monitor nothing; SpecialsOnly exists for specials without a season");
    }
}
