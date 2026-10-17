using FluentAssertions;
using FluentAssertions.Execution;
using Sportarr.Api.Models;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class RequestQuotaPartialPageTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LocalQuotaDenialOnNextPageRetainsFetchedOffersWithoutProviderSuccess()
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        rig.Source.Paged = true;
        var row = rig.Row("Quota page retention", IndexerType.Newznab);
        row.QueryLimit = 2;
        await rig.SaveRowsAsync(row);
        var beforeHealth = await rig.ReadQueryHealthAsync(row.Id);

        var releases = await rig.SearchOneAsync(row, maximum: 5, eventId: "ev-2336155")
            .WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var state = await rig.ReadQuotaStateAsync(row.Id);
        var health = await rig.ReadQueryHealthAsync(row.Id);
        var attempts = rig.Source.Attempts;
        var pages = attempts.Where(attempt => attempt.Mode == "search").ToArray();

        using (new AssertionScope())
        {
            rig.Source.TotalArrivals.Should().Be(2);
            attempts.Select(attempt => attempt.Mode).Should().Equal("caps", "search");
            attempts.Should().OnlyContain(attempt => attempt.Status == 200 && attempt.ResponseSent);
            attempts.Should().OnlyContain(attempt => attempt.RowId == row.Id.ToString());
            pages.Should().ContainSingle();
            pages.Select(page => page.Offset).Should().Equal(0);
            pages.Select(page => page.EventId).Should().Equal("ev-2336155");
            pages.SelectMany(page => page.Guids).Should().Equal("offer-1", "offer-2");
            rig.Source.Violations.Should().BeEmpty();
            state.Queries.Should().Be(2);
            state.Grabs.Should().Be(0);
            state.QueryFailures.Should().Be(0);
            state.ConnectionErrors.Should().Be(0);
            state.RateLimitedUntil.Should().BeNull();
            beforeHealth.LastSuccess.Should().BeNull();
            health.Should().Be(beforeHealth);
            health.LastSuccess.Should().BeNull();
            health.LastQueryFailure.Should().BeNull();
            health.QueryDisabledUntil.Should().BeNull();
            health.LastConnectionError.Should().BeNull();

            releases.Select(release => release.Guid).Should().Equal("offer-1", "offer-2");
            releases.Select(release => release.IndexerId).Should().Equal(row.Id, row.Id);
            releases.Select(release => release.Indexer).Should().Equal(row.Name, row.Name);
            releases.Select(release => release.Protocol).Should().Equal("Usenet", "Usenet");
        }
    }
}
