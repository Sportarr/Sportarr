using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class MixedSourceCacheExpiryTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(600)]
    [InlineData(120)]
    public async Task ManualSearchUsesConfiguredLifetimeAndRefreshStillFetches(int duration)
    {
        var clock = new ManualClock();
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false, cacheClock: clock);
        var configService = rig.Services.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        config.SearchCacheDuration = duration;
        await configService.SaveConfigAsync(config);
        rig.Indexer.QueryLimit = 30;
        await rig.Db.SaveChangesAsync();
        rig.Transport.RequestCeiling = 20;
        rig.StartMeasurement();

        var cold = await rig.RunAsync("configured-cold", automatic: false);
        clock.Advance(duration - 1);
        var warm = await rig.RunAsync("configured-warm", automatic: false);
        clock.Advance(2);
        var expired = await rig.RunAsync("configured-expired", automatic: false);
        var refreshed = await rig.RunAsync("configured-refresh", automatic: false, forceRefresh: true);

        using var assertions = new AssertionScope();
        cold.Found.Should().Be(5);
        cold.Attempts.Count(attempt => attempt.Mode == "search").Should().Be(3);
        warm.Attempts.Should().BeEmpty();
        expired.Attempts.Count(attempt => attempt.Mode == "search").Should().Be(3);
        refreshed.Attempts.Count(attempt => attempt.Mode == "search").Should().Be(3);
        warm.Guids.Should().Equal(cold.Guids);
        expired.Guids.Should().Equal(cold.Guids);
        refreshed.Guids.Should().Equal(cold.Guids);
        rig.Transport.Violations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RecoveryDoesNotExtendHealthyEvidenceLifetime(bool automatic, bool empty)
    {
        var clock = new ManualClock();
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false, cacheClock: clock);
        var unavailable = await MixedSourceCacheTests.AddUnavailableSourceAsync(rig);
        rig.Transport.RequestCeiling = 20;
        if (empty)
        {
            rig.Indexer.AdditionalParameters = "empty=1";
            await rig.Db.SaveChangesAsync();
        }
        rig.StartMeasurement();
        await rig.RunAsync("expiry-cold", automatic);
        clock.Advance(empty ? 50 : 290);
        await MixedSourceCacheTests.SetAvailableAsync(rig, unavailable.Id);
        var recovered = await rig.RunAsync("expiry-recovered", automatic);
        var warm = await rig.RunAsync("expiry-warm", automatic);
        clock.Advance(11);
        var expired = await rig.RunAsync("expiry-expired", automatic);
        using var assertions = new AssertionScope();
        recovered.Found.Should().Be(empty ? 5 : 10);
        recovered.CacheEntries.Should().OnlyContain(entry => entry.LifetimeSeconds > 0 && entry.LifetimeSeconds <= 10);
        warm.Attempts.Should().NotContain(attempt => attempt.Mode != "descriptor");
        expired.Attempts.Should().Contain(attempt => attempt.Mode == "search" && attempt.RowId == rig.Indexer.Id.ToString());
        expired.Found.Should().Be(empty ? 5 : 10);
        rig.Transport.Violations.Should().BeEmpty();
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now += TimeSpan.FromSeconds(seconds);
    }
}
