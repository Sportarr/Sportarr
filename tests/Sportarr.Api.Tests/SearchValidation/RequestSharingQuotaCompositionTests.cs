using System.Net;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class RequestSharingQuotaCompositionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchModeQueriesOnlyRowsEnabledForThatMode(bool interactiveSearch)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var interactive = rig.Row("Interactive row");
        interactive.EnableAutomaticSearch = false;
        interactive.EnableInteractiveSearch = true;
        var automatic = rig.Row("Automatic row");
        automatic.EnableAutomaticSearch = true;
        automatic.EnableInteractiveSearch = false;
        var feedOnly = rig.Row("Feed row", IndexerType.Rss);
        feedOnly.EnableAutomaticSearch = false;
        feedOnly.EnableInteractiveSearch = false;
        await rig.SaveRowsAsync(interactive, automatic, feedOnly);

        var releases = await rig.SearchAllAsync(interactiveSearch).WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var expected = interactiveSearch ? interactive : automatic;

        using (new AssertionScope())
        {
            releases.Should().ContainSingle().Which.IndexerId.Should().Be(expected.Id);
            rig.Source.Attempts.Should().ContainSingle();
            rig.Source.Attempts.Should().OnlyContain(attempt => attempt.RowId == expected.Id.ToString());
            rig.Source.Attempts.Should().OnlyContain(attempt => attempt.Mode == "search");
            rig.Source.Violations.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AlreadyDeniedOwnerAllowsOneReplacementForTwoFollowers()
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        var rows = Enumerable.Range(1, 3).Select(number => rig.Row("Composition row " + number, IndexerType.Newznab)).ToArray();
        foreach (var row in rows) row.QueryLimit = 1;
        await rig.SaveRowsAsync(rows);
        await rig.SeedQuotaWindowAsync(rows[0].Id, 1, 0, DateTime.UtcNow.AddHours(1));
        using var batch = new IndexerSearchRequestBatch();
        using var http = rig.CompositionClient();
        var url = rig.Source.Url + "/api?t=search&apikey=" + Uri.EscapeDataString(RequestBudgetBaselineHarness.BudgetSource.KeyA)
            + "&q=budget-fixture&limit=5&extended=1";
        async Task<SendObservation> SendAsync(Indexer row)
        {
            using var request = IndexerQueryRequest.Create(row, url, new IndexerQueryContext(row.Id));
            try
            {
                using var response = await batch.SendAsync(http, nameof(IndexerType.Newznab), row.RequestDelayMs,
                    request, HttpCompletionOption.ResponseHeadersRead);
                return new(row.Id, (int)response.StatusCode, await response.Content.ReadAsStringAsync(), null, null);
            }
            catch (IndexerQueryAdmissionException failure)
            {
                return new(row.Id, null, null, failure.Kind.ToString(), failure.IndexerId);
            }
        }
        var owner = await SendAsync(rows[0]).WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var followers = await Task.WhenAll(SendAsync(rows[1]), SendAsync(rows[2])).WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var states = await Task.WhenAll(rows.Select(row => rig.ReadQuotaStateAsync(row.Id)));
        output.WriteLine(JsonSerializer.Serialize(new { Owner = owner, Followers = followers, States = states }));
        using (new AssertionScope())
        {
            owner.Failure.Should().Be(nameof(QueryAdmissionFailure.Denied));
            owner.FailureRow.Should().Be(rows[0].Id);
            owner.Status.Should().BeNull();
            followers.Should().OnlyContain(result => result.Status == 200 && result.Failure == null);
            followers.Select(result => result.Body).Distinct().Should().ContainSingle();
            followers.Should().OnlyContain(result => result.Body != null && result.Body.Contains("offer-source-a-account-a-default"));
            rig.Source.Attempts.Should().ContainSingle();
            rig.Source.Attempts.Should().OnlyContain(attempt => attempt.ResponseSent && attempt.Status == 200 && attempt.RowId != rows[0].Id.ToString());
            states[0].Queries.Should().Be(1);
            for (var index = 1; index < rows.Length; index++)
                states[index].Queries.Should().Be(rig.Source.Attempts.Count(attempt => attempt.RowId == rows[index].Id.ToString()));
            states.Sum(state => state.Queries).Should().Be(2);
            states.Should().OnlyContain(state => state.QueryFailures == 0 && state.ConnectionErrors == 0 && state.RateLimitedUntil == null);
            rig.Source.Violations.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(IndexerType.Newznab)]
    [InlineData(IndexerType.Torznab)]
    public async Task RetryTimeOwnerDenialRetainsItsOutcomeAndCoalescesEligibleFollowers(IndexerType protocol)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        rig.Source.FailuresBeforeSuccess = 1;
        var rows = Enumerable.Range(1, 3).Select(number => rig.Row("Composition row " + number, protocol)).ToArray();
        foreach (var row in rows) row.QueryLimit = 1;
        await rig.SaveRowsAsync(rows);
        var outcome = await rig.CompositionSearchAsync(maximum: 1).WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var states = await Task.WhenAll(rows.Select(row => rig.ReadQuotaStateAsync(row.Id)));
        var health = await Task.WhenAll(rows.Select(row => rig.ReadQueryHealthAsync(row.Id)));
        var attempts = rig.Source.Attempts;
        var deniedOwner = attempts.FirstOrDefault()?.RowId;
        using (new AssertionScope())
        {
            attempts.Select(attempt => attempt.Status).Should().Equal(503, 200);
            attempts.Select(attempt => attempt.RowId).Distinct().Should().HaveCount(2);
            attempts.Should().OnlyContain(attempt => attempt.Mode == "search" && attempt.ResponseSent);
            outcome.SatisfiesRequest.Should().BeFalse();
            outcome.Diagnostics.Should().HaveCount(3);
            outcome.Releases.Should().HaveCount(2);
            outcome.Releases.Should().OnlyContain(release => release.Guid == "offer-source-a-account-a-default" && release.IndexerId.ToString() != deniedOwner);
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                states[index].Queries.Should().Be(attempts.Count(attempt => attempt.RowId == row.Id.ToString()));
                var diagnostic = outcome.Diagnostics.Where(item => item.IndexerId == row.Id).ToArray();
                diagnostic.Should().ContainSingle();
                if (row.Id.ToString() == deniedOwner)
                {
                    diagnostic.Should().OnlyContain(item => item.Termination == SearchTermination.LocalDenied && !item.SatisfiesRequest);
                    health[index].LastSuccess.Should().BeNull();
                }
                else
                {
                    diagnostic.Should().OnlyContain(item => item.SatisfiesRequest);
                    health[index].LastSuccess.Should().NotBeNull();
                    outcome.Releases.Where(release => release.IndexerId == row.Id).Select(release => release.Indexer).Should().Equal(row.Name);
                }
                health[index].LastQueryFailure.Should().BeNull();
                health[index].QueryDisabledUntil.Should().BeNull();
            }
            states.Sum(state => state.Queries).Should().Be(2);
            states.Should().OnlyContain(state => state.QueryFailures == 0 && state.ConnectionErrors == 0 && state.RateLimitedUntil == null);
            rig.Source.Violations.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(IndexerType.Newznab)]
    [InlineData(IndexerType.Torznab)]
    public async Task SharedRetryChargesActualOwnerAttemptsAndKeepsEveryRowsHealth(IndexerType protocol)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        rig.Source.FailuresBeforeSuccess = 2;
        var rows = Enumerable.Range(1, 3).Select(number => rig.Row("Composition row " + number, protocol)).ToArray();
        foreach (var row in rows) row.QueryLimit = 10;
        await rig.SaveRowsAsync(rows);
        var outcome = await rig.CompositionSearchAsync(maximum: 1).WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var states = await Task.WhenAll(rows.Select(row => rig.ReadQuotaStateAsync(row.Id)));
        var health = await Task.WhenAll(rows.Select(row => rig.ReadQueryHealthAsync(row.Id)));
        using (new AssertionScope())
        {
            rig.Source.Attempts.Select(attempt => attempt.Status).Should().Equal(503, 503, 200);
            rig.Source.Attempts.Select(attempt => attempt.RowId).Distinct().Should().ContainSingle();
            rig.Source.Attempts.Should().OnlyContain(attempt => attempt.Mode == "search" && attempt.ResponseSent);
            outcome.SatisfiesRequest.Should().BeTrue();
            outcome.Diagnostics.Should().HaveCount(3).And.OnlyContain(item => item.SatisfiesRequest);
            outcome.Releases.Select(release => release.IndexerId).Should().BeEquivalentTo(rows.Select(row => (int?)row.Id));
            outcome.Releases.Should().OnlyContain(release => release.Guid == "offer-source-a-account-a-default");
            for (var index = 0; index < rows.Length; index++)
            {
                states[index].Queries.Should().Be(rig.Source.Attempts.Count(attempt => attempt.RowId == rows[index].Id.ToString()));
                health[index].LastSuccess.Should().NotBeNull();
                health[index].LastQueryFailure.Should().BeNull();
                outcome.Releases.Where(release => release.IndexerId == rows[index].Id).Select(release => release.Indexer).Should().Equal(rows[index].Name);
            }
            states.Sum(state => state.Queries).Should().Be(3);
            states.Should().OnlyContain(state => state.QueryFailures == 0 && state.ConnectionErrors == 0 && state.RateLimitedUntil == null);
            rig.Source.Violations.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(IndexerType.Newznab)]
    [InlineData(IndexerType.Torznab)]
    public async Task ExactPagesShareBodiesAndPreserveEveryRowsFiveOffers(IndexerType protocol)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        rig.Source.Paged = true;
        var rows = Enumerable.Range(1, 3).Select(number => rig.Row("Composition row " + number, protocol)).ToArray();
        foreach (var row in rows) row.QueryLimit = 10;
        await rig.SaveRowsAsync(rows);
        var outcome = await rig.CompositionSearchAsync(eventId: "ev-2336155").WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var states = await Task.WhenAll(rows.Select(row => rig.ReadQuotaStateAsync(row.Id)));
        var attempts = rig.Source.Attempts;
        var pages = attempts.Where(attempt => attempt.Mode == "search").ToArray();
        using (new AssertionScope())
        {
            attempts.Should().HaveCount(4).And.OnlyContain(attempt => attempt.Status == 200 && attempt.ResponseSent);
            attempts.Count(attempt => attempt.Mode == "caps").Should().Be(1);
            pages.Select(page => page.Offset).OrderBy(offset => offset).Should().Equal(0, 2, 4);
            pages.OrderBy(page => page.Offset).Select(page => page.Limit).Should().Equal(2, 2, 1);
            pages.Should().OnlyContain(page => page.EventId == "ev-2336155" && page.Query == "budget-fixture");
            outcome.SatisfiesRequest.Should().BeTrue();
            outcome.Diagnostics.Should().HaveCount(3).And.OnlyContain(item => item.SatisfiesRequest);
            outcome.Releases.Should().HaveCount(15);
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                outcome.Releases.Where(release => release.IndexerId == row.Id).Select(release => release.Guid).OrderBy(guid => guid)
                    .Should().Equal(Enumerable.Range(1, 5).Select(number => "offer-" + number));
                outcome.Releases.Where(release => release.IndexerId == row.Id).Should().OnlyContain(release => release.Indexer == row.Name && release.Protocol == (protocol == IndexerType.Newznab ? "Usenet" : "Torrent"));
                states[index].Queries.Should().Be(attempts.Count(attempt => attempt.RowId == row.Id.ToString()));
            }
            states.Sum(state => state.Queries).Should().Be(4);
            states.Should().OnlyContain(state => state.QueryFailures == 0 && state.ConnectionErrors == 0 && state.RateLimitedUntil == null);
            rig.Source.Violations.Should().BeEmpty();
        }
    }

    private sealed record SendObservation(int RowId, int? Status, string? Body, string? Failure, int? FailureRow);
}
