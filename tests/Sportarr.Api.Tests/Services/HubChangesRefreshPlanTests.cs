using System.Net;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public sealed class HubChangesRefreshPlanTests
{
    private static Dictionary<string, HubChangesRefreshOrder> Orders(params (string League, string[] Seasons, bool SmartWalk)[] items)
    {
        var orders = new Dictionary<string, HubChangesRefreshOrder>(StringComparer.OrdinalIgnoreCase);
        foreach (var (league, seasons, smartWalk) in items)
        {
            HubChangesRefreshPlan.Merge(orders, league, seasons, smartWalk);
        }
        return orders;
    }

    [Fact]
    public void MergeAccumulatesSeasonsAndKeepsASmartWalk()
    {
        var orders = Orders(("nfl", new[] { "2024" }, false));

        HubChangesRefreshPlan.Merge(orders, "NFL", new[] { "2025", "2024" }, true);
        HubChangesRefreshPlan.Merge(orders, "nfl", Array.Empty<string>(), false);

        var order = Assert.Single(orders).Value;
        Assert.Equal(new[] { "2024", "2025" }, order.Seasons.OrderBy(s => s));
        Assert.True(order.SmartWalk);
    }

    [Fact]
    public void ARefreshedLeagueLeavesThePendingSet()
    {
        var orders = Orders(("nfl", new[] { "2025" }, false), ("nba", new[] { "2025" }, false));
        var results = new Dictionary<string, HubChangesRefreshResult>
        {
            ["nfl"] = HubChangesRefreshResult.Done,
            ["nba"] = HubChangesRefreshResult.Done,
        };

        var (pending, dropped) = HubChangesRefreshPlan.Settle(orders, results);

        Assert.Empty(pending);
        Assert.Empty(dropped);
    }

    [Fact]
    public void AFailedLeagueStaysPendingWithItsWorkAndCountsTheAttempt()
    {
        var orders = Orders(("nfl", new[] { "2019" }, true));

        var (pending, _) = HubChangesRefreshPlan.Settle(orders,
            new Dictionary<string, HubChangesRefreshResult> { ["nfl"] = HubChangesRefreshResult.Failed });

        var order = Assert.Single(pending).Value;
        Assert.Equal(new[] { "2019" }, order.Seasons);
        Assert.True(order.SmartWalk);
        Assert.Equal(1, order.Attempts);
    }

    [Fact]
    public void ARateLimitedOrUnattemptedLeagueKeepsItsOrderWithoutCountingAnAttempt()
    {
        var orders = Orders(("nfl", new[] { "2025" }, false), ("nba", new[] { "2025" }, false));
        orders["nba"].Attempts = 3;

        var (pending, _) = HubChangesRefreshPlan.Settle(orders,
            new Dictionary<string, HubChangesRefreshResult> { ["nfl"] = HubChangesRefreshResult.RateLimited });

        Assert.Equal(2, pending.Count);
        Assert.Equal(0, pending["nfl"].Attempts);
        Assert.Equal(3, pending["nba"].Attempts);
    }

    [Fact]
    public void ALeagueThatKeepsFailingIsDroppedAfterTheLastAttempt()
    {
        var orders = Orders(("nfl", new[] { "2025" }, false));
        orders["nfl"].Attempts = HubChangesRefreshPlan.MaxAttempts - 1;

        var (pending, dropped) = HubChangesRefreshPlan.Settle(orders,
            new Dictionary<string, HubChangesRefreshResult> { ["nfl"] = HubChangesRefreshResult.Failed });

        Assert.Empty(pending);
        Assert.Equal(new[] { "nfl" }, dropped);
    }

    [Fact]
    public void PendingOrdersSurviveARestart()
    {
        var orders = Orders(("nfl", new[] { "2019", "2025" }, true), ("nba", Array.Empty<string>(), true));
        orders["nfl"].Attempts = 2;

        var loaded = HubChangesRefreshPlan.Load(HubChangesRefreshPlan.Save(orders));

        Assert.Equal(2, loaded.Count);
        Assert.Equal(new[] { "2019", "2025" }, loaded["NFL"].Seasons.OrderBy(s => s));
        Assert.True(loaded["nfl"].SmartWalk);
        Assert.Equal(2, loaded["nfl"].Attempts);
        Assert.Empty(loaded["nba"].Seasons);
    }

    [Fact]
    public void NothingPendingIsStoredAsNullAndBadJsonLoadsAsNothing()
    {
        Assert.Null(HubChangesRefreshPlan.Save(new Dictionary<string, HubChangesRefreshOrder>()));
        Assert.Empty(HubChangesRefreshPlan.Load(null));
        Assert.Empty(HubChangesRefreshPlan.Load(""));
        Assert.Empty(HubChangesRefreshPlan.Load("{not json"));
        Assert.Empty(HubChangesRefreshPlan.Load("[1,2]"));
    }

    [Fact]
    public void AHubRateLimitIsRecognisedAnywhereInTheFailureChain()
    {
        var limited = new HttpRequestException("Rate limited by Sportarr API (429)", null, HttpStatusCode.TooManyRequests);

        Assert.True(HubChangesRefreshPlan.IsRateLimited(limited));
        Assert.True(HubChangesRefreshPlan.IsRateLimited(new InvalidOperationException("sync failed", limited)));
        Assert.True(HubChangesRefreshPlan.IsRateLimited(new AggregateException(new TimeoutException(), limited)));
        Assert.False(HubChangesRefreshPlan.IsRateLimited(new HttpRequestException("boom", null, HttpStatusCode.BadGateway)));
        Assert.False(HubChangesRefreshPlan.IsRateLimited(new TimeoutException()));
        Assert.False(HubChangesRefreshPlan.IsRateLimited(null));
    }
}

public sealed class HubChangesRefreshPlanNarrowTests
{
    private static HubChangesRefreshOrder Order(string[] seasons, bool smartWalk, bool fullHistory = false)
    {
        var orders = new Dictionary<string, HubChangesRefreshOrder>(StringComparer.OrdinalIgnoreCase);
        HubChangesRefreshPlan.Merge(orders, "nfl", seasons, smartWalk, fullHistory);
        return orders["nfl"];
    }

    [Fact]
    public void ACompleteRefreshLeavesNothingToRetry()
    {
        var order = Order(new[] { "2019", "2025" }, true);

        Assert.False(HubChangesRefreshPlan.Narrow(order, Array.Empty<string>(), walkIncomplete: false));
    }

    [Fact]
    public void OnlyTheSeasonsTheHubDidNotAnswerForStayPending()
    {
        var order = Order(new[] { "2019", "2025" }, true);

        Assert.True(HubChangesRefreshPlan.Narrow(order, new[] { "2019" }, walkIncomplete: false));
        Assert.Equal(new[] { "2019" }, order.Seasons);
        Assert.False(order.SmartWalk);
    }

    [Fact]
    public void AnIncompleteWalkKeepsTheWalkAndItsHistoryDepth()
    {
        var order = Order(Array.Empty<string>(), true, fullHistory: true);

        Assert.True(HubChangesRefreshPlan.Narrow(order, Array.Empty<string>(), walkIncomplete: true));
        Assert.True(order.SmartWalk);
        Assert.True(order.FullHistory);
    }

    [Fact]
    public void AFullHistoryOrderSurvivesARestart()
    {
        var orders = new Dictionary<string, HubChangesRefreshOrder>(StringComparer.OrdinalIgnoreCase);
        HubChangesRefreshPlan.Merge(orders, "nfl", Array.Empty<string>(), false, fullHistory: true);

        var loaded = HubChangesRefreshPlan.Load(HubChangesRefreshPlan.Save(orders));

        Assert.True(loaded["nfl"].SmartWalk);
        Assert.True(loaded["nfl"].FullHistory);
    }
}

public sealed class HubChangesRefreshPlanEdgeTests
{
    [Fact]
    public void SkippedSeasonsAndAnIncompleteWalkAreBothKept()
    {
        var orders = new Dictionary<string, HubChangesRefreshOrder>(StringComparer.OrdinalIgnoreCase);
        HubChangesRefreshPlan.Merge(orders, "nfl", new[] { "2019", "2025" }, true);

        Assert.True(HubChangesRefreshPlan.Narrow(orders["nfl"], new[] { "2025", "1999" }, walkIncomplete: true));
        Assert.Equal(new[] { "2025" }, orders["nfl"].Seasons);
        Assert.True(orders["nfl"].SmartWalk);
    }

    [Fact]
    public void StoredJsonWithoutTheHistoryFieldLoadsAsAShallowWalk()
    {
        var loaded = HubChangesRefreshPlan.Load("""{"nfl":{"Seasons":["2025"],"SmartWalk":true,"Attempts":2}}""");

        Assert.True(loaded["nfl"].SmartWalk);
        Assert.False(loaded["nfl"].FullHistory);
        Assert.Equal(2, loaded["nfl"].Attempts);
    }

    [Fact]
    public void NewWorkResetsTheAttemptCountAndRepeatedWorkDoesNot()
    {
        var orders = new Dictionary<string, HubChangesRefreshOrder>(StringComparer.OrdinalIgnoreCase);
        HubChangesRefreshPlan.Merge(orders, "nfl", new[] { "2019" }, false);
        orders["nfl"].Attempts = 5;

        HubChangesRefreshPlan.Merge(orders, "nfl", new[] { "2019" }, false);
        Assert.Equal(5, orders["nfl"].Attempts);

        HubChangesRefreshPlan.Merge(orders, "nfl", new[] { "2025" }, false);
        Assert.Equal(0, orders["nfl"].Attempts);

        orders["nfl"].Attempts = 5;
        HubChangesRefreshPlan.Merge(orders, "nfl", Array.Empty<string>(), true);
        Assert.Equal(0, orders["nfl"].Attempts);
    }
}
