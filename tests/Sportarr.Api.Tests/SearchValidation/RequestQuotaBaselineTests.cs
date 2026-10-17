using FluentAssertions;
using FluentAssertions.Execution;
using Sportarr.Api.Models;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class RequestQuotaBaselineTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RetryAfterTheLastSlotDoesNotReachTheSource()
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var row = rig.Row("Last retry slot", IndexerType.Newznab);
        row.QueryLimit = 1;
        rig.Source.FailuresBeforeSuccess = 1;
        await rig.SaveRowsAsync(row);

        var releases = await rig.SearchOneAsync(row).WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var state = await rig.ReadQuotaStateAsync(row.Id);

        using (new AssertionScope())
        {
            rig.Source.Attempts.Select(attempt => attempt.Status).Should().Equal(503);
            releases.Should().BeEmpty();
            state.Queries.Should().Be(1);
            state.QueryFailures.Should().Be(0);
            state.ConnectionErrors.Should().Be(0);
            state.RateLimitedUntil.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(IndexerType.Torznab)]
    [InlineData(IndexerType.Newznab)]
    public async Task CapabilitiesSpendOneSlotAndCachedCapabilitiesSpendNone(IndexerType protocol)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var row = rig.Row("Caps accounting", protocol);
        row.QueryLimit = 3;
        await rig.SaveRowsAsync(row);

        var first = await rig.SearchOneAsync(row, eventId: "ev-2336155").WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var firstState = await rig.ReadQuotaStateAsync(row.Id);
        var second = await rig.SearchOneAsync(row, "budget-competitor", eventId: "ev-2336155")
            .WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var finalState = await rig.ReadQuotaStateAsync(row.Id);

        using (new AssertionScope())
        {
            rig.Source.Attempts.Select(attempt => attempt.Mode).Should().Equal("caps", "search", "search");
            rig.Source.Attempts.Should().OnlyContain(attempt => attempt.Status == 200);
            first.Select(release => release.Guid).Should().Equal("offer-source-a-account-a-default");
            second.Select(release => release.Guid).Should().Equal("offer-source-a-account-a-default");
            firstState.Queries.Should().Be(2);
            finalState.Queries.Should().Be(3);
            finalState.QueryFailures.Should().Be(0);
            finalState.ConnectionErrors.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(IndexerType.Torznab)]
    [InlineData(IndexerType.Newznab)]
    public async Task SuccessfulCapabilitiesCanConsumeTheLastSlot(IndexerType protocol)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var row = rig.Row("Caps last slot", protocol);
        row.QueryLimit = 1;
        await rig.SaveRowsAsync(row);

        var releases = await rig.SearchOneAsync(row, eventId: "ev-2336155").WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var state = await rig.ReadQuotaStateAsync(row.Id);

        using (new AssertionScope())
        {
            rig.Source.Attempts.Select(attempt => attempt.Mode).Should().Equal("caps");
            rig.Source.Attempts.Select(attempt => attempt.Status).Should().Equal(200);
            releases.Should().BeEmpty();
            state.Queries.Should().Be(1);
            state.QueryFailures.Should().Be(0);
            state.ConnectionErrors.Should().Be(0);
            state.RateLimitedUntil.Should().BeNull();
        }
    }

    [Fact]
    public async Task DeniedCapabilitiesRetryDoesNotBecomeAnUnmarkedSearch()
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var row = rig.Row("Caps retry denial", IndexerType.Newznab);
        row.QueryLimit = 1;
        rig.Source.CapsFailuresBeforeSuccess = 1;
        await rig.SaveRowsAsync(row);

        var releases = await rig.SearchOneAsync(row, eventId: "ev-2336155").WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var state = await rig.ReadQuotaStateAsync(row.Id);
        var firstAttempts = rig.Source.Attempts;
        await rig.SeedQuotaWindowAsync(row.Id, 1, 0, DateTime.UtcNow.AddMinutes(-5));
        var nextWindow = await rig.SearchOneAsync(row, "budget-competitor", eventId: "ev-2336155")
            .WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var nextState = await rig.ReadQuotaStateAsync(row.Id);

        using (new AssertionScope())
        {
            firstAttempts.Select(attempt => attempt.Mode).Should().Equal("caps");
            firstAttempts.Select(attempt => attempt.Status).Should().Equal(503);
            rig.Source.Attempts.Select(attempt => attempt.Mode).Should().Equal("caps", "caps");
            rig.Source.Attempts.Select(attempt => attempt.Status).Should().Equal(503, 200);
            nextWindow.Should().BeEmpty();
            nextState.Queries.Should().Be(1);
            nextState.QueryFailures.Should().Be(0);
            nextState.ConnectionErrors.Should().Be(0);
            nextState.RateLimitedUntil.Should().BeNull();
            releases.Should().BeEmpty();
            state.Queries.Should().Be(1);
            state.QueryFailures.Should().Be(0);
            state.ConnectionErrors.Should().Be(0);
            state.RateLimitedUntil.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(null, 1, 2)]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 1)]
    public async Task SequentialSearchesRespectLimitsWithoutLogicalDoubleCounting(
        int? queryLimit, int firstCount, int finalCount)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var row = rig.Row("Limit control", IndexerType.Newznab);
        row.QueryLimit = queryLimit;
        await rig.SaveRowsAsync(row);

        var first = await rig.SearchOneAsync(row).WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var firstState = await rig.ReadQuotaStateAsync(row.Id);
        var second = await rig.SearchOneAsync(row, "budget-competitor").WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var finalState = await rig.ReadQuotaStateAsync(row.Id);

        using (new AssertionScope())
        {
            rig.Source.Attempts.Should().HaveCount(finalCount);
            if (finalCount > 0)
                rig.Source.Attempts.Should().OnlyContain(attempt => attempt.Mode == "search" && attempt.Status == 200);
            first.Should().HaveCount(firstCount);
            second.Should().HaveCount(finalCount - firstCount);
            first.Select(release => release.Guid).Should().Equal(firstCount == 1
                ? new[] { "offer-source-a-account-a-default" } : Array.Empty<string>());
            firstState.Queries.Should().Be(firstCount);
            finalState.Queries.Should().Be(finalCount);
            finalState.QueryFailures.Should().Be(0);
            finalState.ConnectionErrors.Should().Be(0);
            finalState.RateLimitedUntil.Should().BeNull();
        }
    }

    [Fact]
    public async Task ExpiredWindowResetsBeforeOneNewAttemptIsReserved()
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var row = rig.Row("Rollover control", IndexerType.Newznab);
        row.QueryLimit = 1;
        await rig.SaveRowsAsync(row);
        await rig.SeedQuotaWindowAsync(row.Id, 1, 7, DateTime.UtcNow.AddMinutes(-5));
        var before = DateTime.UtcNow;

        var first = await rig.SearchOneAsync(row).WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var firstState = await rig.ReadQuotaStateAsync(row.Id);
        var second = await rig.SearchOneAsync(row, "budget-competitor").WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var finalState = await rig.ReadQuotaStateAsync(row.Id);

        using (new AssertionScope())
        {
            rig.Source.Attempts.Should().ContainSingle().Which.Status.Should().Be(200);
            first.Select(release => release.Guid).Should().Equal("offer-source-a-account-a-default");
            second.Should().BeEmpty();
            firstState.Queries.Should().Be(1);
            finalState.Queries.Should().Be(1);
            firstState.Grabs.Should().Be(0);
            finalState.Grabs.Should().Be(0);
            firstState.ResetAt.Should().NotBeNull();
            firstState.ResetAt!.Value.Should().BeAfter(before.AddMinutes(45));
            firstState.ResetAt.Value.Should().BeBefore(DateTime.UtcNow.AddHours(2));
            finalState.ResetAt.Should().Be(firstState.ResetAt);
        }
    }
}
