using FluentAssertions;
using FluentAssertions.Execution;
using Sportarr.Api.Models;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class RequestBudgetBaselineTests
{
    private readonly ITestOutputHelper _output;
    public RequestBudgetBaselineTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("identical", 1, "offer-source-a-account-a-default")]
    [InlineData("account", 2, "offer-source-a-account-b-default")]
    [InlineData("path", 2, "offer-source-b-account-a-default")]
    [InlineData("parameters", 2, "offer-source-a-account-a-alternate")]
    public async Task SharedRequestsPreserveSeparateRowEvidence(string difference, int expectedRequests, string secondGuid)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(_output);
        var first = rig.Row("First row");
        var second = rig.Row("Second row");
        second.MultiLanguages = new List<string> { "German" };
        if (difference == "account") second.ApiKey = RequestBudgetBaselineHarness.BudgetSource.KeyB;
        if (difference == "path") second.ApiPath = "/alternate";
        if (difference == "parameters") second.AdditionalParameters = "edition=alternate";
        await rig.SaveRowsAsync(first, second);

        var rows = await rig.SearchAllAsync().WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var savedIds = await rig.SavedRowIdsAsync();

        using (new AssertionScope())
        {
            rig.Source.Attempts.Should().HaveCount(expectedRequests);
            rig.Source.Attempts.Should().OnlyContain(attempt => attempt.Mode == "search" && attempt.Status == 200);
            savedIds.Should().Equal(first.Id, second.Id);
            rows.Should().HaveCount(2);
            rows.Select(row => row.IndexerId).Should().BeEquivalentTo(new int?[] { first.Id, second.Id });
            rows.Where(row => row.IndexerId == first.Id).Select(row => row.Guid).Should().Equal("offer-source-a-account-a-default");
            rows.Where(row => row.IndexerId == second.Id).Select(row => row.Guid).Should().Equal(secondGuid);
            rows.Where(row => row.IndexerId == first.Id).Select(row => row.Indexer).Should().Equal("First row");
            rows.Where(row => row.IndexerId == second.Id).Select(row => row.Indexer).Should().Equal("Second row");
            rows.Where(row => row.IndexerId == first.Id).SelectMany(row => row.MultiLanguageNames ?? new()).Should().Equal("English");
            rows.Where(row => row.IndexerId == second.Id).SelectMany(row => row.MultiLanguageNames ?? new()).Should().Equal("German");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastQuotaSlotIsReservedAcrossConcurrentCallers(bool secondUsesRss)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(_output);
        var row = rig.Row("Budget row", IndexerType.Newznab);
        row.QueryLimit = 1;
        await rig.SaveRowsAsync(row);
        rig.Source.HoldFirst = true;
        var first = rig.SearchOneAsync(row);
        Task<List<ReleaseSearchResult>>? second = null;
        var releaseEvent = "overlap-setup-failed";
        try
        {
            await rig.Source.FirstArrival.WaitAsync(TimeSpan.FromSeconds(10));
            _output.WriteLine("Counter while the first response is held: " + await rig.QueryCountAsync(row.Id));
            rig.Admission.Arm("127.0.0.1", row.Id.ToString());
            second = secondUsesRss ? rig.RssAsync() : rig.SearchOneAsync(row, "budget-competitor");
            Task winner;
            try
            {
                winner = await Task.WhenAny(second, rig.Admission.Entered).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                releaseEvent = "overlap-witness-timeout";
                throw new InvalidOperationException("Fixture overlap was not witnessed before the deadline.");
            }
            if (!rig.Source.FirstResponseIsHeld)
                throw new InvalidOperationException("The first response was not held when overlap was witnessed.");
            releaseEvent = winner == second ? "competitor-completed-while-first-held" : "competitor-entered-real-pacing";
            if (winner == second) await second;
            else await rig.Admission.Entered;
        }
        finally
        {
            _output.WriteLine("First response release event: " + releaseEvent);
            rig.Source.ReleaseFirst();
        }
        Assert.NotNull(second);
        var results = await Task.WhenAll(first, second!).WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var count = await rig.QueryCountAsync(row.Id);

        using (new AssertionScope())
        {
            rig.Source.Attempts.Should().ContainSingle();
            count.Should().Be(1);
            results[0].Select(release => release.Guid).Should().Equal("offer-source-a-account-a-default");
            results[0].Select(release => release.IndexerId).Should().Equal(row.Id);
            results[1].Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(IndexerType.Torznab)]
    [InlineData(IndexerType.Newznab)]
    public async Task PaginationKeepsTheLastCandidateWithinAdvertisedPageLimits(IndexerType protocol)
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(_output);
        rig.Source.Paged = true;
        var row = rig.Row("Paged row", protocol);
        await rig.SaveRowsAsync(row);

        var releases = await rig.SearchOneAsync(row, maximum: 5, eventId: "ev-2336155")
            .WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var count = await rig.QueryCountAsync(row.Id);
        var attempts = rig.Source.Attempts;
        var pages = attempts.Where(attempt => attempt.Mode == "search").ToArray();

        using (new AssertionScope())
        {
            attempts.Count(attempt => attempt.Mode == "caps").Should().Be(1);
            pages.Select(page => page.Offset).Should().Equal(0, 2, 4);
            pages.Should().OnlyContain(page => page.Limit > 0 && page.Limit <= 2);
            pages.Should().OnlyContain(page => page.EventId == "ev-2336155" && page.Status == 200);
            releases.Select(release => release.Guid).Should().Equal("offer-1", "offer-2", "offer-3", "offer-4", "offer-5");
            releases.Where(release => release.Guid == "offer-5").Select(release => release.Title)
                .Should().Equal("Spain.vs.Belgium.2026.09.01.MULTI.1080p.WEB-DL.H264-GROUP");
            releases.Should().OnlyContain(release => release.IndexerId == row.Id && release.Indexer == "Paged row");
            count.Should().Be(4, "the fixture charges one attempt for caps and each of its three pages");
        }
    }

    [Fact]
    public async Task RetriedHttpAttemptsConsumeQuotaAndKeepPacing()
    {
        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(_output);
        rig.Source.FailuresBeforeSuccess = 2;
        var row = rig.Row("Retry row", IndexerType.Newznab);
        row.RequestDelayMs = 3000;
        await rig.SaveRowsAsync(row);

        var releases = await rig.SearchOneAsync(row).WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var count = await rig.QueryCountAsync(row.Id);
        var attempts = rig.Source.Attempts;

        using (new AssertionScope())
        {
            attempts.Select(attempt => attempt.Status).Should().Equal(503, 503, 200);
            attempts.Should().OnlyContain(attempt => attempt.Mode == "search" && attempt.Query == "budget-fixture" && attempt.Offset == 0);
            releases.Select(release => release.Guid).Should().Equal("offer-source-a-account-a-default");
            releases.Should().OnlyContain(release => release.IndexerId == row.Id);
            count.Should().Be(3);
            if (attempts.Length == 3)
            {
                (attempts[1].ArrivalMs - attempts[0].ArrivalMs).Should().BeGreaterThanOrEqualTo(2900);
                (attempts[2].ArrivalMs - attempts[1].ArrivalMs).Should().BeGreaterThanOrEqualTo(2900);
            }
        }
    }
}
