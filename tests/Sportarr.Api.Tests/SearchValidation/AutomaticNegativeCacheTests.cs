using System.Security.Cryptography;
using System.Text;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class AutomaticNegativeCacheTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AutomaticSearchFindsAndCachesReleaseAfterTwoEmptyQueries()
    {
        await using var rig = await AutomaticNegativeCacheHarness.CreateAsync(output);
        Assert.Null(rig.Event.League!.SearchQueryTemplate);
        var laterQuery = rig.Queries.Skip(2).First(query => query.Contains("Formula1"));
        rig.UseMatchingRelease(laterQuery);
        rig.StartMeasurement();

        var first = await rig.RunAsync("later-query-only", automatic: true);
        var repeat = await rig.RunAsync("later-query-cache-repeat", automatic: true);

        Assert.Contains(first.Attempts, attempt => attempt.Mode == "search" && attempt.Query == laterQuery);
        Assert.Equal(rig.Transport.Releases[0].Title, first.Selected);
        Assert.Equal(rig.Indexer.Name, first.SelectedIndexer);
        Assert.Equal(1, first.Found);
        Assert.Equal(new[] { "matching-release" }, Assert.Single(first.CacheEntries).Guids);
        Assert.Equal(first.Selected, repeat.Selected);
        Assert.DoesNotContain(repeat.Attempts, attempt => attempt.Mode == "search");
        Assert.Equal(rig.Queries, first.Attempts.Where(attempt => attempt.Mode == "search").Select(attempt => attempt.Query));
        Assert.All(first.Attempts.Where(attempt => attempt.Mode == "search" && attempt.Query != laterQuery),
            attempt => Assert.Empty(attempt.Guids));
        Assert.Equal(0, await rig.QueueCountAsync());
        Assert.Empty(rig.Transport.Violations);
    }

    [Fact]
    public async Task AutomaticCacheDoesNotReuseReleaseFromDisabledIndexer()
    {
        await using var rig = await AutomaticNegativeCacheHarness.CreateAsync(output);
        rig.UseMatchingRelease();
        rig.StartMeasurement();
        var first = await rig.RunAsync("initial-source", automatic: true);
        var replacement = await rig.ReplaceIndexerAsync();
        var second = await rig.RunAsync("replacement-source", automatic: true);

        Assert.Equal(rig.Indexer.Name, first.SelectedIndexer);
        Assert.Equal(replacement.Name, second.SelectedIndexer);
        Assert.NotEmpty(second.Attempts);
        Assert.All(second.Attempts.Where(attempt => attempt.Mode == "search"),
            attempt => Assert.Equal(replacement.Id.ToString(), attempt.RowId));
    }

    [Fact]
    public async Task RepeatedDefaultPlanCachesAllQueriesAndOneMetadataProbe()
    {
        await using var rig = await AutomaticNegativeCacheHarness.CreateAsync(output);
        Assert.Null(rig.Event.League!.SearchQueryTemplate);
        Assert.True(rig.Queries.Length > 2);
        var contract = await rig.ContractHashAsync();
        var initial = await rig.ReadStateAsync();
        Assert.Equal(0, initial.Queries);
        Assert.Null(initial.LastSuccess);
        rig.StartMeasurement();
        var first = await rig.RunAsync("complete-empty-plan", automatic: true);
        var repeat = await rig.RunAsync("negative-cache-repeat", automatic: true);

        Assert.Equal(Enumerable.Repeat("search", rig.Queries.Length + 1), first.Attempts.Select(attempt => attempt.Mode));
        Assert.Equal(rig.Queries.Append(rig.Event.Title), first.Attempts.Select(attempt => attempt.Query));
        Assert.Equal(Enumerable.Repeat(0, rig.Queries.Length + 1), first.Attempts.Select(attempt => attempt.Offset));
        Assert.All(first.Attempts, attempt =>
        {
            Assert.Equal(200, attempt.Status);
            Assert.True(attempt.ResponseSent);
            Assert.Empty(attempt.Guids);
            Assert.Equal(rig.Indexer.Id.ToString(), attempt.RowId);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(attempt.ResponseBody))), attempt.ResponseSha256);
        });
        Assert.Empty(repeat.Attempts);
        Assert.Equal(rig.Queries.Length + 1, rig.Transport.Attempts.Length);
        Assert.Equal(rig.Queries.Length + 1, first.State.Queries);
        Assert.Equal(first.State, repeat.State);
        Assert.NotNull(first.State.LastSuccess);
        Assert.Equal(0, first.State.QueryFailures);
        Assert.Equal(0, first.State.ConnectionErrors);
        Assert.Equal(0, first.State.Grabs);
        Assert.Null(first.State.RateLimitedUntil);
        Assert.Equal(initial.ResetAt, first.State.ResetAt);
        Assert.Equal(0, first.Found);
        Assert.Equal(0, repeat.Found);
        Assert.Null(first.Selected);
        Assert.Null(repeat.Selected);
        var cached = Assert.Single(first.CacheEntries);
        Assert.NotNull(cached.Guids);
        Assert.Empty(cached.Guids!);
        Assert.InRange(cached.LifetimeSeconds!.Value, 60 - (int)Math.Ceiling((first.EndMilliseconds - first.StartMilliseconds) / 1000), 60);
        var cachedAgain = Assert.Single(repeat.CacheEntries);
        Assert.Equal(cached.Key, cachedAgain.Key);
        Assert.NotNull(cachedAgain.Guids);
        Assert.Empty(cachedAgain.Guids!);
        Assert.Equal(cached.LifetimeSeconds, cachedAgain.LifetimeSeconds);
        Assert.Equal(contract, first.ContractHash);
        Assert.Equal(contract, repeat.ContractHash);
        Assert.True(repeat.EndMilliseconds < 45_000);
        Assert.True(repeat.StartMilliseconds >= first.EndMilliseconds);
        Assert.Equal(0, await rig.QueueCountAsync());
        Assert.Empty(rig.Transport.Violations);
    }
}
