using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using System.Text.Json;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class MixedSourceCacheTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HealthySourceIsReusedWhileUnavailableSourceRecovers(bool automatic)
    {
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false);
        var unavailable = await AddUnavailableSourceAsync(rig);
        rig.StartMeasurement();
        var cold = await rig.RunAsync("mixed-cold", automatic);
        var repeated = await rig.RunAsync("mixed-repeat", automatic);
        await SetAvailableAsync(rig, unavailable.Id);
        var recovered = await rig.RunAsync("mixed-recovered", automatic);
        var warm = await rig.RunAsync("mixed-warm", automatic);

        using var assertions = new AssertionScope();
        cold.Found.Should().Be(5);
        SourceAttempts(cold).Count().Should().Be(4);
        repeated.Found.Should().Be(5);
        SourceAttempts(repeated).Should().BeEmpty("the unavailable source must not force healthy sources to search again");
        repeated.State.Queries.Should().Be(cold.State.Queries);
        repeated.State.LastSuccess.Should().Be(cold.State.LastSuccess);
        cold.CacheEntries.Should().OnlyContain(entry => entry.Guids == null);
        repeated.CacheEntries.Should().OnlyContain(entry => entry.Guids == null);
        IsComplete(cold).Should().BeFalse();
        IsComplete(repeated).Should().BeFalse();
        recovered.Found.Should().Be(10);
        SourceAttempts(recovered).Count().Should().Be(4);
        SourceAttempts(recovered).Should().OnlyContain(attempt => attempt.RowId == unavailable.Id.ToString());
        recovered.State.Queries.Should().Be(cold.State.Queries);
        warm.Found.Should().Be(10);
        IsComplete(recovered).Should().BeTrue();
        IsComplete(warm).Should().BeTrue();
        SourceAttempts(warm).Should().BeEmpty();
        if (!automatic) recovered.Guids.Should().Contain("recovered-offer-5");
        rig.Transport.Violations.Should().BeEmpty();
        (await rig.QueueCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ForceRefreshRequeriesHealthySourceDuringOutage()
    {
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false);
        await AddUnavailableSourceAsync(rig);
        rig.StartMeasurement();
        var cold = await rig.RunAsync("force-cold", false);
        var repeat = await rig.RunAsync("force-repeat", false);
        var forced = await rig.RunAsync("force-refresh", false, forceRefresh: true);
        using var assertions = new AssertionScope();
        SourceAttempts(repeat).Should().BeEmpty();
        SourceAttempts(forced).Select(attempt => attempt.Mode).Should().Equal("search", "search", "search");
        forced.Guids.Should().Equal(cold.Guids);
        rig.Transport.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task DirectSearchRemainsUncachedWithoutAnAggregateCacheConsumer()
    {
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false);
        await AddUnavailableSourceAsync(rig);
        var service = rig.Services.GetRequiredService<IndexerSearchService>();
        var first = await service.SearchAllIndexersAsync("cache-primary", sportarrId: rig.Event.ExternalId);
        var second = await service.SearchAllIndexersAsync("cache-primary", sportarrId: rig.Event.ExternalId);
        first.Should().HaveCount(3);
        second.Should().HaveCount(3);
        rig.Transport.Attempts.Count(attempt => attempt.Mode == "search").Should().Be(4);
        rig.Transport.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task CurrentQualityPolicyReevaluatesHealthyEvidenceWithoutNewSearches()
    {
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false);
        await AddUnavailableSourceAsync(rig);
        rig.StartMeasurement();
        var cold = await rig.RunAsync("policy-cold", false);
        rig.Profile.Items = new List<QualityItem> { new() { Name = "WEBDL-1080p", Quality = 3, Allowed = false } };
        await rig.Db.SaveChangesAsync();
        var rejected = await rig.RunAsync("policy-rejected", false);
        rig.Profile.Items = new List<QualityItem> { new() { Name = "WEBDL-1080p", Quality = 3, Allowed = true } };
        await rig.Db.SaveChangesAsync();
        var allowed = await rig.RunAsync("policy-allowed", false);
        using var assertions = new AssertionScope();
        Rows(cold).Should().HaveCount(5).And.OnlyContain(row => row.Approved);
        Rows(rejected).Should().HaveCount(5).And.OnlyContain(row => !row.Approved && row.Rejections.Count > 0);
        Rows(allowed).Should().HaveCount(5).And.OnlyContain(row => row.Approved && row.Rejections.Count == 0);
        SourceAttempts(rejected).Should().BeEmpty();
        SourceAttempts(allowed).Should().BeEmpty();
        rig.Transport.Violations.Should().BeEmpty();
    }

    private static List<ReleaseSearchResult> Rows(QuotaIncompleteCacheHarness.Phase phase)
    {
        using var body = JsonDocument.Parse(phase.Body);
        return body.RootElement.GetProperty("results").Deserialize<List<ReleaseSearchResult>>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static bool IsComplete(QuotaIncompleteCacheHarness.Phase phase)
    {
        using var body = JsonDocument.Parse(phase.Body);
        return body.RootElement.EnumerateObject().Single(property =>
            property.Name.Equals("searchComplete", StringComparison.OrdinalIgnoreCase)).Value.GetBoolean();
    }

    [Fact]
    public async Task SourceConfigChangesAndCacheClearInvalidateHealthyResults()
    {
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false);
        var unavailable = await AddUnavailableSourceAsync(rig);
        rig.StartMeasurement();
        await rig.RunAsync("identity-cold", false);
        unavailable.Name = "Renamed unavailable source";
        await rig.Db.SaveChangesAsync();
        var otherChange = await rig.RunAsync("identity-other-row", false);
        rig.Indexer.MultiLanguages = new List<string> { "French" };
        await rig.Db.SaveChangesAsync();
        var ownChange = await rig.RunAsync("identity-own-row", false);
        rig.Services.GetRequiredService<SearchResultCache>().Clear();
        var cleared = await rig.RunAsync("identity-clear", false);
        using var assertions = new AssertionScope();
        SourceAttempts(otherChange).Should().BeEmpty();
        SourceAttempts(ownChange).Count().Should().Be(3);
        SourceAttempts(cleared).Count().Should().Be(3);
        rig.Transport.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task AutomaticAnswerCannotSatisfyManualBreadthAndDisabledRowsStayExcluded()
    {
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false);
        await AddUnavailableSourceAsync(rig);
        rig.StartMeasurement();
        var automatic = await rig.RunAsync("mode-automatic", true);
        var manual = await rig.RunAsync("mode-manual", false);
        rig.Indexer.Enabled = false;
        await rig.Db.SaveChangesAsync();
        var disabled = await rig.RunAsync("mode-disabled", false);
        using var assertions = new AssertionScope();
        SourceAttempts(automatic).Where(a => a.Mode == "search").Should().OnlyContain(a => a.Request.Contains("cat=5060"));
        SourceAttempts(manual).Count().Should().Be(3);
        SourceAttempts(manual).Should().OnlyContain(a => !a.Request.Contains("cat="));
        disabled.Found.Should().Be(0);
        SourceAttempts(disabled).Should().BeEmpty();
        rig.Transport.Violations.Should().BeEmpty();
    }

    internal static async Task<Indexer> AddUnavailableSourceAsync(QuotaIncompleteCacheHarness rig)
    {
        rig.Indexer.QueryLimit = 100;
        var row = new Indexer
        {
            Name = "Recovering source", Type = IndexerType.Newznab, Url = rig.Transport.Url,
            ApiPath = "/api", ApiKey = "fixture", AdditionalParameters = "recovered=1",
            QueryLimit = 100, RequestDelayMs = 20, Categories = new List<string> { "5060" },
            Enabled = true, EnableAutomaticSearch = true, EnableInteractiveSearch = true, EnableRss = false
        };
        rig.Db.Indexers.Add(row);
        await rig.Db.SaveChangesAsync();
        rig.Db.IndexerStatuses.Add(new IndexerStatus
        {
            IndexerId = row.Id, RateLimitedUntil = DateTime.UtcNow.AddHours(1),
            HourResetTime = DateTime.UtcNow.AddHours(1)
        });
        await rig.Db.SaveChangesAsync();
        return row;
    }

    internal static async Task SetAvailableAsync(QuotaIncompleteCacheHarness rig, int id)
    {
        await using var db = await rig.Services.GetRequiredService<IDbContextFactory<SportarrDbContext>>().CreateDbContextAsync();
        var status = await db.IndexerStatuses.SingleAsync(row => row.IndexerId == id);
        status.RateLimitedUntil = null;
        await db.SaveChangesAsync();
    }

    private static IEnumerable<QuotaIncompleteCacheHarness.Attempt> SourceAttempts(QuotaIncompleteCacheHarness.Phase phase) =>
        phase.Attempts.Where(attempt => attempt.Mode != "descriptor");
}
