using FluentAssertions;
using Sportarr.Api.Models;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class RequestSharingRowTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(IndexerType.Torznab)]
    [InlineData(IndexerType.Newznab)]
    public async Task RowsBeyondTheConcurrencyWindowKeepOffersAndCountActualAttempts(IndexerType protocol)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var saved = Enumerable.Range(1, 6).Select(number => rig.Row("Row " + number, protocol)).ToArray();
        await rig.SaveRowsAsync(saved);
        var rows = await rig.SearchAllAsync().WaitAsync(RequestBudgetBaselineHarness.Deadline);

        rig.Source.Attempts.Should().ContainSingle();
        rows.Select(row => row.IndexerId).Should().Equal(saved.Select(row => (int?)row.Id));
        rows.Select(row => row.Indexer).Should().Equal(saved.Select(row => row.Name));
        rows.Should().HaveCount(6);
        rows[0].Title = "Changed after parsing";
        rows[0].Quality = "Changed after evaluation";
        rows[0].MultiLanguageNames!.Add("French");
        rows.Skip(1).Should().OnlyContain(row => row.Title != "Changed after parsing" && row.Quality != "Changed after evaluation");
        rows.Skip(1).Should().OnlyContain(row => row.MultiLanguageNames!.SequenceEqual(new[] { "English" }));
        foreach (var row in saved)
            (await rig.QueryCountAsync(row.Id)).Should().Be(rig.Source.Attempts.Count(attempt => attempt.RowId == row.Id.ToString()));
    }

    [Fact]
    public async Task SharedTorrentBodyKeepsMinimumSeedersAndCountsActualAttempts()
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var first = rig.Row("Visible row");
        var second = rig.Row("Strict row");
        second.MinimumSeeders = 21;
        await rig.SaveRowsAsync(first, second);
        var rows = await rig.SearchAllAsync().WaitAsync(RequestBudgetBaselineHarness.Deadline);

        rig.Source.Attempts.Should().ContainSingle();
        rows.Should().ContainSingle().Which.IndexerId.Should().Be(first.Id);
        (await rig.SavedRowIdsAsync()).Should().Equal(first.Id, second.Id);
        foreach (var row in new[] { first, second })
            (await rig.QueryCountAsync(row.Id)).Should().Be(rig.Source.Attempts.Count(attempt => attempt.RowId == row.Id.ToString()));
    }

    [Theory]
    [InlineData("categories")]
    [InlineData("delay")]
    [InlineData("protocol")]
    public async Task DistinctRequestOrPacingSettingsKeepSeparateFetches(string difference)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var first = rig.Row("First row");
        var second = rig.Row("Second row");
        if (difference == "categories") second.Categories = new() { "5040" };
        if (difference == "delay") second.RequestDelayMs = 50;
        if (difference == "protocol") second.Type = IndexerType.Newznab;
        await rig.SaveRowsAsync(first, second);
        var rows = await rig.SearchAllAsync().WaitAsync(RequestBudgetBaselineHarness.Deadline);

        rig.Source.Attempts.Should().HaveCount(2);
        rows.Select(row => row.IndexerId).Should().BeEquivalentTo(new int?[] { first.Id, second.Id });
    }

    [Fact]
    public async Task SharedRetryChainKeepsOffersAndCountsActualAttempts()
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        rig.Source.FailuresBeforeSuccess = 2;
        var first = rig.Row("First row", IndexerType.Newznab);
        var second = rig.Row("Second row", IndexerType.Newznab);
        await rig.SaveRowsAsync(first, second);
        var rows = await rig.SearchAllAsync().WaitAsync(RequestBudgetBaselineHarness.Deadline);

        rig.Source.Attempts.Select(attempt => attempt.Status).Should().Equal(503, 503, 200);
        rows.Select(row => row.IndexerId).Should().BeEquivalentTo(new int?[] { first.Id, second.Id });
        foreach (var row in new[] { first, second })
            (await rig.QueryCountAsync(row.Id)).Should().Be(rig.Source.Attempts.Count(attempt => attempt.RowId == row.Id.ToString()));
    }

    [Fact]
    public async Task CompletedBatchDoesNotCacheTheNextSearch()
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var first = rig.Row("First row");
        var second = rig.Row("Second row");
        await rig.SaveRowsAsync(first, second);
        (await rig.SearchAllAsync().WaitAsync(RequestBudgetBaselineHarness.Deadline)).Should().HaveCount(2);
        (await rig.SearchAllAsync().WaitAsync(RequestBudgetBaselineHarness.Deadline)).Should().HaveCount(2);
        rig.Source.Attempts.Should().HaveCount(2);
    }
}
