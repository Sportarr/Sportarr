using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A fighting event searches once per part, and the part is not part of the
/// query, so every part asks the indexers the same thing. They all ran at
/// once, all missed the cache before any of them had stored anything, and
/// each went out to every indexer. The log showed one query repeating every
/// two seconds while several indexers rate limited the account.
///
/// The cache always meant to cover this. Its own summary names multi-part
/// events as the reason it exists. What it lacked was a way to say a fetch
/// was already under way.
/// </summary>
public class SearchResultCacheSingleFlightTests
{
    private static SearchResultCache NewCache() =>
        new(NullLogger<SearchResultCache>.Instance);

    private static List<ReleaseSearchResult> OneRelease(string guid) => new()
    {
        new ReleaseSearchResult { Title = "UFC 300 Main Card 1080p", Guid = guid, Indexer = "test", DownloadUrl = "http://example.invalid/" + guid }
    };

    [Fact]
    public async Task Only_one_part_fetches_while_the_others_wait()
    {
        var cache = NewCache();
        const string key = "ufc.300";
        var fetches = 0;

        // The three parts of a card, starting together the way the search
        // service starts them.
        async Task SearchAsPart()
        {
            using var slot = await cache.EnterFillAsync(key);

            if (cache.TryGetCached(key, 300) != null)
            {
                return;
            }

            Interlocked.Increment(ref fetches);
            await Task.Delay(30);
            cache.Store(key, OneRelease("g1"), 300);
        }

        await Task.WhenAll(SearchAsPart(), SearchAsPart(), SearchAsPart());

        Assert.Equal(1, fetches);
    }

    [Fact]
    public async Task The_parts_behind_the_first_one_get_its_results()
    {
        var cache = NewCache();
        const string key = "ufc.301";

        using (var slot = await cache.EnterFillAsync(key))
        {
            cache.Store(key, OneRelease("g2"), 300);
        }

        using var second = await cache.EnterFillAsync(key);
        var cached = cache.TryGetCached(key, 300);

        Assert.NotNull(cached);
        Assert.Single(cached!.RawReleases);
    }

    [Fact]
    public async Task A_released_slot_lets_the_next_caller_straight_through()
    {
        var cache = NewCache();

        using (await cache.EnterFillAsync("nfl.2026")) { }

        // Would hang here if the slot were not released.
        var second = cache.EnterFillAsync("nfl.2026");
        var finished = await Task.WhenAny(second, Task.Delay(2000));

        Assert.Same(second, finished);
        (await second).Dispose();
    }

    /// <summary>
    /// The gates are hash stripes, so one unlucky pair of queries may share
    /// a stripe and take turns. What must hold is that holding one query's
    /// slot does not stall unrelated searches in general, so this passes as
    /// soon as any of several other queries gets through.
    /// </summary>
    [Fact]
    public async Task Holding_one_query_does_not_stall_unrelated_searches()
    {
        var cache = NewCache();

        using var first = await cache.EnterFillAsync("ufc.300");

        for (var i = 0; i < 10; i++)
        {
            var other = cache.EnterFillAsync($"nfl.week-{i}");
            var finished = await Task.WhenAny(other, Task.Delay(500));

            if (ReferenceEquals(other, finished))
            {
                (await other).Dispose();
                return;
            }
        }

        Assert.Fail("ten unrelated queries all waited behind an unrelated fill");
    }

    /// <summary>
    /// The parts of an unreleased event search together and all find
    /// nothing. Refusing to cache the nothing meant every waiter re-ran the
    /// whole search the moment the gate opened, one after another. An empty
    /// answer is kept briefly so the burst coalesces, and only briefly so an
    /// indexer blip cannot hide a real release for long.
    /// </summary>
    [Fact]
    public void An_empty_answer_is_shared_briefly_not_refused()
    {
        var cache = NewCache();

        cache.Store("ufc.401", new List<ReleaseSearchResult>(), 300);

        var cached = cache.TryGetCached("ufc.401", 300);

        Assert.NotNull(cached);
        Assert.Empty(cached!.RawReleases);
        Assert.True(cached.LifetimeSeconds <= SearchResultCache.EmptyResultLifetimeSeconds,
            $"an empty answer was stored for {cached.LifetimeSeconds}s");
    }

    [Fact]
    public void An_expired_empty_answer_is_a_miss_even_inside_the_callers_window()
    {
        var cache = NewCache();

        cache.Store("ufc.402", new List<ReleaseSearchResult>(), 300);
        var cached = cache.TryGetCached("ufc.402", 300)!;
        cached.CachedAt = DateTime.UtcNow.AddSeconds(-(SearchResultCache.EmptyResultLifetimeSeconds + 5));

        Assert.Null(cache.TryGetCached("ufc.402", 300));
    }

    /// <summary>
    /// The time limit alone could not bound this. A backlog sweep caches a
    /// distinct query per event, and every entry holds a full release list,
    /// so a large library filled memory well inside one lifetime.
    /// </summary>
    [Fact]
    public void The_cache_stops_growing_at_its_ceiling()
    {
        var cache = NewCache();

        for (var i = 0; i < 600; i++)
        {
            cache.Store($"query-{i}", OneRelease($"g{i}"), 3600);
        }

        var (entries, _) = cache.GetStats();

        Assert.True(entries <= 500, $"cache held {entries} entries, expected it to stay at or under 500");
    }
}
