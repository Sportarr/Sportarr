using System.Net;
using System.Text.Json;

namespace Sportarr.Api.Services;

/// <summary>
/// One league's outstanding refresh work from the hub change feed.
/// </summary>
public sealed class HubChangesRefreshOrder
{
    public HashSet<string> Seasons { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool SmartWalk { get; set; }

    /// <summary>
    /// The smart walk must cover full history. Set for leagues a resync
    /// could not reconcile.
    /// </summary>
    public bool FullHistory { get; set; }
    public int Attempts { get; set; }
}

public enum HubChangesRefreshResult
{
    Done,
    Failed,
    RateLimited,
}

/// <summary>
/// Decides what the changes poller still owes after a cycle.
///
/// The cursor always moves past the pages the poller read. A league whose
/// refresh failed is kept as a pending order and retried next cycle. The
/// old rule held the cursor instead, so one failing league made every
/// cycle read the same pages again and refresh every other league again.
/// With the hub's per-address rate limit that fed on itself.
/// </summary>
public static class HubChangesRefreshPlan
{
    /// <summary>
    /// Cycles a failing league is retried before it is dropped. One cycle
    /// is 15 minutes, so this is about a day. The daily auto-sync still
    /// refreshes its current seasons after that.
    /// </summary>
    public const int MaxAttempts = 96;

    private sealed record StoredOrder(string[] Seasons, bool SmartWalk, bool FullHistory, int Attempts);

    public static Dictionary<string, HubChangesRefreshOrder> Load(string? json)
    {
        var orders = new Dictionary<string, HubChangesRefreshOrder>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return orders;
        }

        Dictionary<string, StoredOrder>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<Dictionary<string, StoredOrder>>(json);
        }
        catch (JsonException)
        {
            return orders;
        }

        foreach (var (league, item) in stored ?? new())
        {
            if (string.IsNullOrEmpty(league) || item == null)
            {
                continue;
            }
            Merge(orders, league, item.Seasons ?? Array.Empty<string>(), item.SmartWalk, item.FullHistory);
            orders[league].Attempts = Math.Max(0, item.Attempts);
        }
        return orders;
    }

    public static string? Save(IReadOnlyDictionary<string, HubChangesRefreshOrder> orders)
    {
        if (orders.Count == 0)
        {
            return null;
        }
        var stored = orders.ToDictionary(
            pair => pair.Key,
            pair => new StoredOrder(pair.Value.Seasons.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                pair.Value.SmartWalk, pair.Value.FullHistory, pair.Value.Attempts));
        return JsonSerializer.Serialize(stored);
    }

    /// <summary>
    /// Adds work for a league. Seasons accumulate and a smart walk, once
    /// requested, stays requested.
    /// </summary>
    public static void Merge(Dictionary<string, HubChangesRefreshOrder> orders, string league,
        IEnumerable<string> seasons, bool smartWalk, bool fullHistory = false)
    {
        if (!orders.TryGetValue(league, out var order))
        {
            order = new HubChangesRefreshOrder();
            orders[league] = order;
        }
        var grew = false;
        foreach (var season in seasons)
        {
            if (!string.IsNullOrEmpty(season))
            {
                grew |= order.Seasons.Add(season);
            }
        }
        var walk = smartWalk || fullHistory;
        grew |= walk && !order.SmartWalk;
        grew |= fullHistory && !order.FullHistory;
        order.SmartWalk |= walk;
        order.FullHistory |= fullHistory;
        if (grew)
        {
            // New work gets its own retry budget.
            order.Attempts = 0;
        }
    }

    /// <summary>
    /// Shrinks an order to the part a refresh did not apply. Returns false
    /// when nothing is left, so the league is done.
    /// </summary>
    public static bool Narrow(HubChangesRefreshOrder order, IEnumerable<string> skippedSeasons, bool walkIncomplete)
    {
        var skipped = new HashSet<string>(skippedSeasons, StringComparer.OrdinalIgnoreCase);
        order.Seasons.IntersectWith(skipped);
        if (!walkIncomplete)
        {
            order.SmartWalk = false;
            order.FullHistory = false;
        }
        return order.Seasons.Count > 0 || order.SmartWalk;
    }

    /// <summary>
    /// True when the hub answered 429 anywhere in the failure chain.
    /// </summary>
    public static bool IsRateLimited(Exception? exception)
    {
        var seen = 0;
        while (exception != null && seen++ < 16)
        {
            if (exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
            {
                return true;
            }
            if (exception is AggregateException aggregate)
            {
                return aggregate.InnerExceptions.Any(IsRateLimited);
            }
            exception = exception.InnerException;
        }
        return false;
    }

    /// <summary>
    /// Returns the orders that stay pending and the leagues given up on.
    /// A league without a result was not attempted and keeps its order.
    /// </summary>
    public static (Dictionary<string, HubChangesRefreshOrder> Pending, List<string> Dropped) Settle(
        IReadOnlyDictionary<string, HubChangesRefreshOrder> orders,
        IReadOnlyDictionary<string, HubChangesRefreshResult> results)
    {
        var pending = new Dictionary<string, HubChangesRefreshOrder>(StringComparer.OrdinalIgnoreCase);
        var dropped = new List<string>();
        foreach (var (league, order) in orders)
        {
            if (!results.TryGetValue(league, out var result))
            {
                pending[league] = order;
                continue;
            }
            switch (result)
            {
                case HubChangesRefreshResult.Done:
                    break;
                case HubChangesRefreshResult.RateLimited:
                    pending[league] = order;
                    break;
                case HubChangesRefreshResult.Failed:
                    order.Attempts++;
                    if (order.Attempts >= MaxAttempts)
                    {
                        dropped.Add(league);
                    }
                    else
                    {
                        pending[league] = order;
                    }
                    break;
            }
        }
        return (pending, dropped);
    }
}
