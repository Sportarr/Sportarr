using FluentAssertions;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(IndexerStatusFixtureCollection.Name)]
public sealed class RugbyBoundaryOverflowRouteTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CappedNextMonthRestoresTheTargetThroughTeamFallback(bool automatic)
    {
        await using var rig = await CombinedProbeCacheHarness.CreateAsync(output, rugbyBoundaryRows: 11);
        rig.StartMeasurement();
        var phase = await rig.RunAsync("capped-next-rugby-month", automatic);
        var searches = phase.Attempts.Where(attempt => attempt.Mode == "search").ToArray();

        searches.Select(attempt => attempt.Query).Should().Equal(
            new[] { "Super League Rugby 2026 08" }
                .Concat(Enumerable.Repeat("Super League Rugby 2026 09", 5))
                .Concat(new[] { "Warrington Wolves vs Hull Kingston Rovers", "Warrington Wolves vs Hull KR" }));
        searches.Skip(1).Take(5).Select(attempt => attempt.Offset).Should().Equal(0, 2, 4, 6, 8);
        searches.Last().Guids.Should().Contain("rugby-boundary-offer-11");
        phase.Found.Should().BeGreaterThan(10);
        if (!automatic) phase.Guids.Should().Contain("rugby-boundary-offer-11");
        rig.Transport.Violations.Should().BeEmpty();
    }
}
