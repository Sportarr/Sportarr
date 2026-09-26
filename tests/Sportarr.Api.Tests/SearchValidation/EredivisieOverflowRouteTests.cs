using FluentAssertions;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(IndexerStatusFixtureCollection.Name)]
public sealed class EredivisieOverflowRouteTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteMonthUsesOneSearch(bool automatic)
    {
        await using var rig = await CombinedProbeCacheHarness.CreateAsync(output, eredivisieRows: 1);
        rig.StartMeasurement();
        var phase = await rig.RunAsync("complete-eredivisie-month", automatic);

        phase.Attempts.Where(attempt => attempt.Mode == "search")
            .Select(attempt => attempt.Query).Should().Equal("Eredivisie 2026 08");
        phase.Found.Should().BeGreaterThan(0);
        if (!automatic) phase.Guids.Should().Contain("eredivisie-offer-1");
        rig.Transport.Violations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PageCeilingRestoresTheTargetThroughTeamFallback(bool automatic)
    {
        await using var rig = await CombinedProbeCacheHarness.CreateAsync(output, eredivisieRows: 11);
        rig.StartMeasurement();
        var phase = await rig.RunAsync("capped-eredivisie-month", automatic);
        var searches = phase.Attempts.Where(attempt => attempt.Mode == "search").ToArray();

        searches.Select(attempt => attempt.Query).Should().Equal(
            Enumerable.Repeat("Eredivisie 2026 08", 5)
                .Concat(new[] { "Feyenoord vs Go Ahead Eagles", "Go Ahead Eagles vs Feyenoord" }));
        searches.Take(5).Select(attempt => attempt.Offset).Should().Equal(0, 2, 4, 6, 8);
        searches.Last().Guids.Should().Contain("eredivisie-offer-11");
        phase.Found.Should().BeGreaterThan(10);
        if (!automatic) phase.Guids.Should().Contain("eredivisie-offer-11");
        rig.Transport.Violations.Should().BeEmpty();
    }
}
