using System.Reflection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Covers ShouldMonitorEvent's MonitorType.LatestSeason branch, which used
/// to be a literal duplicate of CurrentSeason ("Same as CurrentSeason for
/// now"). LatestSeason should track the most recent season the sync loop
/// actually found data for (latestSeasonWithData), not "now" (currentSeason) -
/// the two diverge specifically during an off-season gap.
/// </summary>
public class LeagueEventSyncServiceMonitorTypeTests
{
    private static bool InvokeShouldMonitorEvent(
        MonitorType monitorType, DateTime eventDate, string? eventSeason, string currentSeason, string latestSeasonWithData)
    {
        var method = typeof(LeagueEventSyncService).GetMethod("ShouldMonitorEvent", BindingFlags.NonPublic | BindingFlags.Static)!;
        var league = new League { Name = "Test League", Sport = "Football", MonitorType = monitorType };
        var cupStageSizes = new HashSet<int>();

        return (bool)method.Invoke(null, new object?[] { league, eventDate, eventSeason, currentSeason, latestSeasonWithData, null, null, cupStageSizes })!;
    }

    [Fact]
    public void LatestSeason_DuringOffSeasonGap_MatchesLastCompletedSeason_NotCurrentCalendarYear()
    {
        // currentSeason has already flipped to "2027" (calendar year rolled
        // over) but the hub has no 2027 events yet - latestSeasonWithData
        // correctly stays on "2026", the last season with real data.
        var matches = InvokeShouldMonitorEvent(
            MonitorType.LatestSeason,
            eventDate: new DateTime(2026, 11, 1),
            eventSeason: "2026",
            currentSeason: "2027",
            latestSeasonWithData: "2026");

        matches.Should().BeTrue();
    }

    [Fact]
    public void LatestSeason_EventInEmptyCurrentSeason_DoesNotMatch()
    {
        var matches = InvokeShouldMonitorEvent(
            MonitorType.LatestSeason,
            eventDate: new DateTime(2027, 1, 1),
            eventSeason: "2027",
            currentSeason: "2027",
            latestSeasonWithData: "2026");

        matches.Should().BeFalse();
    }

    [Fact]
    public void CurrentSeason_UnaffectedByLatestSeasonWithData_StillMatchesCalendarCurrentSeason()
    {
        // CurrentSeason must keep its original behavior regardless of what
        // latestSeasonWithData resolves to - only LatestSeason's semantics changed.
        var matches = InvokeShouldMonitorEvent(
            MonitorType.CurrentSeason,
            eventDate: new DateTime(2027, 1, 1),
            eventSeason: "2027",
            currentSeason: "2027",
            latestSeasonWithData: "2026");

        matches.Should().BeTrue();
    }

    [Fact]
    public void LatestSeason_NoOffSeasonGap_BehavesSameAsCurrentSeason()
    {
        var matches = InvokeShouldMonitorEvent(
            MonitorType.LatestSeason,
            eventDate: new DateTime(2026, 6, 1),
            eventSeason: "2026",
            currentSeason: "2026",
            latestSeasonWithData: "2026");

        matches.Should().BeTrue();
    }
}
