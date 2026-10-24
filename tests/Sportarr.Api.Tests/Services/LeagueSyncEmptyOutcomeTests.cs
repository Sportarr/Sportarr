using FluentAssertions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A league sync that finds nothing used to end as "Synced 0 new events".
/// The league page then waited for events that were never coming. The task
/// row now carries a plain reason.
/// </summary>
public class LeagueSyncEmptyOutcomeTests
{
    [Fact]
    public void SayNothingWhenTheSyncFoundEvents()
    {
        new LeagueEventSyncResult { NewCount = 3, SeasonsTried = 4 }
            .DescribeNothingSynced().Should().BeNull();
        new LeagueEventSyncResult { UpdatedCount = 1, SeasonsTried = 4 }
            .DescribeNothingSynced().Should().BeNull();
        new LeagueEventSyncResult { FailedCount = 2, SeasonsTried = 4 }
            .DescribeNothingSynced().Should().BeNull();
    }

    [Fact]
    public void SeasonsAnsweredButEmptyMeansTheDataSourceHasNoEvents()
    {
        var result = new LeagueEventSyncResult { SeasonsTried = 2 };

        result.DescribeNothingSynced().Should().Be("Sportarr's data source has no events for this league yet. Try Sync again later.");
    }

    [Fact]
    public void HubWithAnEmptySeasonListIsNotCalledOffline()
    {
        var result = new LeagueEventSyncResult
        {
            SeasonListUnavailable = true,
            HubListedNoSeasons = true,
            SeasonsTried = 16,
        };
        for (var i = 0; i < 16; i++) result.SkippedSeasons.Add((2026 - i).ToString());

        result.DescribeNothingSynced().Should().Contain("no seasons or events");
        result.DescribeNothingSynced().Should().NotContain("offline");
    }

    [Fact]
    public void FailedSeasonListMeansTheHubCouldNotBeReached()
    {
        var result = new LeagueEventSyncResult { SeasonListUnavailable = true, SeasonsTried = 16 };

        result.DescribeNothingSynced().Should().Contain("could not get the list of seasons");
    }

    [Fact]
    public void EverySeasonSkippedMeansTheHubDidNotAnswer()
    {
        var result = new LeagueEventSyncResult { SeasonsTried = 2 };
        result.SkippedSeasons.AddRange(new[] { "2025-2027", "2026-2027" });

        result.DescribeNothingSynced().Should().Contain("did not return any events");
    }

    [Fact]
    public void EventsHiddenByWhatTheUserFollowsAreNotCalledMissing()
    {
        var result = new LeagueEventSyncResult { SeasonsTried = 2, FilteredOutCount = 40 };

        result.DescribeNothingSynced().Should().Contain("none match the teams or event types you follow");
    }

    [Fact]
    public void SomeSeasonsAnsweringEmptyStaysTheNoEventsMessage()
    {
        var result = new LeagueEventSyncResult { SeasonsTried = 2 };
        result.SkippedSeasons.Add("2026-2027");

        result.DescribeNothingSynced().Should().Contain("no events for this league yet");
    }

    // The World Test Championship runs on a two-year cycle, so the hub lists
    // it as "2025-2027". These strings must not drop out of the sync window.
    [Fact]
    public void CricketCycleSeasonsStayInTheSyncWindow()
    {
        var year = DateTime.UtcNow.Year;
        var current = $"{year - 1}-{year + 1}";

        LeagueEventSyncService.IsCurrentOrFutureSeason(current).Should().BeTrue();
        SeasonStringFormatter.GetLatestSeasonNotAfter(new[] { "2021-2023", "2023-2025", current }, year)
            .Should().Be(current);
        SeasonStringFormatter.GenerateFutureSeasons(new[] { "2025-2027" }, 2026, 1)
            .Should().Equal("2026-2027", "2027-2028");
    }
}
