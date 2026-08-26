using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The shared lock was released for the duration of the wait, so every caller
/// for one indexer read the same last-request time, slept through the same
/// delay together and fired at the same moment. The delay was applied once to
/// the whole group instead of once per request.
/// </summary>
public class RateLimitConcurrencyTests
{
    [Fact]
    public async Task Concurrent_callers_for_one_key_are_spaced_apart()
    {
        var service = new RateLimitService(Mock.Of<ILogger<RateLimitService>>());
        // Well clear of the jitter the service adds, so the spacing being
        // measured is the rate limit itself and not the random part.
        var limit = TimeSpan.FromMilliseconds(3000);

        var clock = Stopwatch.StartNew();
        var finished = new long[3];
        await Task.WhenAll(Enumerable.Range(0, 3).Select(async i =>
        {
            await service.WaitAndPulseAsync("indexer.example", "1", limit);
            finished[i] = clock.ElapsedMilliseconds;
        }));

        // Three requests three seconds apart: the first goes at once and the
        // other two each wait their turn, so the last cannot land before six
        // seconds. Sharing one wait would have them all done in three.
        finished.Max().Should().BeGreaterThan(5500);
    }

    [Fact]
    public async Task Different_keys_do_not_wait_for_each_other()
    {
        var service = new RateLimitService(Mock.Of<ILogger<RateLimitService>>());
        var limit = TimeSpan.FromMilliseconds(400);

        // Prime both keys so a second call would have to wait.
        await service.WaitAndPulseAsync("a.example", "1", limit);
        await service.WaitAndPulseAsync("b.example", "1", limit);

        var clock = Stopwatch.StartNew();
        await service.WaitAndPulseAsync("c.example", "1", limit);
        clock.ElapsedMilliseconds.Should().BeLessThan(200);
    }
}
