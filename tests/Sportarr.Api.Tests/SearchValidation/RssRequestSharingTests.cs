using FluentAssertions;
using Sportarr.Api.Models;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class RssRequestSharingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(IndexerType.Torznab)]
    [InlineData(IndexerType.Newznab)]
    public async Task ExactDuplicateRowsShareOneRequestAndKeepEveryRow(IndexerType protocol)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var rows = Enumerable.Range(1, 6).Select(number => rig.Row("RSS row " + number, protocol)).ToArray();
        await rig.SaveRowsAsync(rows);

        var releases = await rig.RssAsync().WaitAsync(RequestBudgetBaselineHarness.Deadline);

        var attempts = rig.Source.Attempts.Where(attempt => attempt.Mode == "rss").ToArray();
        attempts.Should().ContainSingle();
        releases.Should().HaveCount(rows.Length);
        releases.Select(release => release.IndexerId).Should().BeEquivalentTo(rows.Select(row => (int?)row.Id));
        releases.Select(release => release.Indexer).Should().BeEquivalentTo(rows.Select(row => row.Name));
        foreach (var row in rows)
            (await rig.QueryCountAsync(row.Id)).Should().Be(attempts.Count(attempt => attempt.RowId == row.Id.ToString()));
    }

    [Fact]
    public async Task SharedBodyStillAppliesEachTorznabRowsSeederPolicy()
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var visible = rig.Row("Visible RSS row", IndexerType.Torznab);
        var strict = rig.Row("Strict RSS row", IndexerType.Torznab);
        strict.MinimumSeeders = 21;
        await rig.SaveRowsAsync(visible, strict);

        var releases = await rig.RssAsync().WaitAsync(RequestBudgetBaselineHarness.Deadline);

        rig.Source.Attempts.Where(attempt => attempt.Mode == "rss").Should().ContainSingle();
        releases.Should().ContainSingle();
        releases[0].IndexerId.Should().Be(visible.Id);
    }

    [Fact]
    public async Task DuplicateGroupsBeyondTheBatchKeyLimitStillShareTheirRequest()
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        rig.Source.MaxArrivals = 66;
        var rows = Enumerable.Range(0, 65).SelectMany(group =>
        {
            var first = rig.Row("RSS group " + group + " first");
            var second = rig.Row("RSS group " + group + " second");
            first.Categories = new() { (5000 + group).ToString() };
            second.Categories = new() { (5000 + group).ToString() };
            return new[] { first, second };
        }).ToArray();
        await rig.SaveRowsAsync(rows);

        var releases = await rig.RssAsync().WaitAsync(RequestBudgetBaselineHarness.Deadline);

        rig.Source.Attempts.Where(attempt => attempt.Mode == "rss").Should().HaveCount(65);
        releases.Should().HaveCount(130);
    }

    [Fact]
    public async Task DeniedDuplicateRowLetsAnEligibleRowFetchTheSharedFeed()
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var denied = rig.Row("Denied RSS row", IndexerType.Newznab);
        var eligible = rig.Row("Eligible RSS row", IndexerType.Newznab);
        denied.QueryLimit = 1;
        eligible.QueryLimit = 1;
        await rig.SaveRowsAsync(denied, eligible);
        await rig.SeedQuotaWindowAsync(denied.Id, 1, 0, DateTime.UtcNow.AddHours(1));

        var releases = await rig.RssAsync().WaitAsync(RequestBudgetBaselineHarness.Deadline);

        rig.Source.Attempts.Where(attempt => attempt.Mode == "rss").Should().ContainSingle()
            .Which.RowId.Should().Be(eligible.Id.ToString());
        releases.Should().ContainSingle().Which.IndexerId.Should().Be(eligible.Id);
    }

    [Theory]
    [InlineData(IndexerType.Torznab)]
    [InlineData(IndexerType.Newznab)]
    public async Task SharedRateLimitUpdatesEveryDuplicateRowsCooldown(IndexerType protocol)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        rig.Source.ForcedStatus = 429;
        rig.Source.RetryAfter = "60";
        var rows = Enumerable.Range(1, 3).Select(number => rig.Row("RSS row " + number, protocol)).ToArray();
        await rig.SaveRowsAsync(rows);

        var releases = await rig.RssAsync().WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var states = await Task.WhenAll(rows.Select(row => rig.ReadQuotaStateAsync(row.Id)));

        rig.Source.Attempts.Where(attempt => attempt.Mode == "rss").Should().ContainSingle();
        releases.Should().BeEmpty();
        states.Sum(state => state.Queries).Should().Be(1);
        states.Should().OnlyContain(state => state.RateLimitedUntil != null);
    }
}
