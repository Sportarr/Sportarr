using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FluentAssertions.Execution;
using Sportarr.Api.Models;
using Xunit.Abstractions;
using PageShape = Sportarr.Api.Tests.SearchValidation.RequestBudgetBaselineHarness.BudgetSource.PaginationShape;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class PaginationTransportTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("no-total", IndexerType.Torznab)]
    [InlineData("no-total", IndexerType.Newznab)]
    [InlineData("short-with-total", IndexerType.Torznab)]
    [InlineData("short-with-total", IndexerType.Newznab)]
    [InlineData("ignored-offset", IndexerType.Torznab)]
    [InlineData("ignored-offset", IndexerType.Newznab)]
    public async Task TraversalRetainsOffersAndBoundsActualRequests(string sourceMode, IndexerType protocol)
    {
        var (shape, offsets, returned, rawGuids) = sourceMode switch
        {
            "no-total" => (PageShape.FullPagesWithoutTotal, new[] { 0, 2, 4 },
                new[] { "offer-1", "offer-2", "offer-3", "offer-4" },
                new[] { "offer-1", "offer-2", "offer-3", "offer-4" }),
            "short-with-total" => (PageShape.ShortPageWithLargerTotal, new[] { 0, 1, 3 },
                new[] { "offer-1", "offer-2", "offer-3", "offer-4", "offer-5" },
                new[] { "offer-1", "offer-2", "offer-3", "offer-4", "offer-5" }),
            "ignored-offset" => (PageShape.RepeatedRawPage, new[] { 0, 2 },
                new[] { "offer-1", "offer-2" },
                new[] { "offer-1", "offer-2", "offer-1", "offer-2" }),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceMode))
        };

        await using var rig = await RequestBudgetBaselineHarness.CreateAsync(output);
        rig.Source.Paged = true;
        rig.Source.PageShape = shape;
        var row = rig.Row("Pagination " + sourceMode, protocol);
        row.QueryLimit = 20;
        await rig.SaveRowsAsync(row);

        var releases = await rig.SearchOneAsync(row, maximum: 6, eventId: "ev-2336155")
            .WaitAsync(RequestBudgetBaselineHarness.Deadline);
        var state = await rig.ReadQuotaStateAsync(row.Id);
        var attempts = rig.Source.Attempts;
        var pages = attempts.Where(attempt => attempt.Mode == "search").ToArray();
        var caps = attempts.Single(attempt => attempt.Mode == "caps");
        var capsXml = XDocument.Parse(caps.ResponseBody);
        XNamespace ns = "http://www.newznab.com/DTD/2010/feeds/attributes/";
        var responses = pages.Select(page => XDocument.Parse(page.ResponseBody).Descendants(ns + "response").Single()).ToArray();

        using (new AssertionScope())
        {
            capsXml.Descendants("limits").Single().Attribute("max")!.Value.Should().Be("2");
            capsXml.Descendants("limits").Single().Attribute("default")!.Value.Should().Be("2");
            attempts.Should().OnlyContain(attempt => attempt.ResponseHash ==
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(attempt.ResponseBody))));
            rig.Source.TotalArrivals.Should().Be(1 + offsets.Length);
            attempts.Should().HaveCount(1 + offsets.Length);
            attempts.Select(attempt => attempt.Mode).Should()
                .Equal(new[] { "caps" }.Concat(Enumerable.Repeat("search", offsets.Length)));
            attempts.Should().OnlyContain(attempt => attempt.Status == 200 && attempt.ResponseSent);
            attempts.Should().OnlyContain(attempt => attempt.RowId == row.Id.ToString());
            pages.Select(page => page.Offset).Should().Equal(offsets);
            pages.Should().OnlyContain(page => page.EventId == "ev-2336155" && page.Query == "budget-fixture");
            pages.SelectMany(page => page.Guids).Should().Equal(rawGuids);
            if (sourceMode == "no-total")
            {
                pages.Select(page => page.Guids.Length).Should().Equal(2, 2, 0);
                responses.Select(response => response.Attribute("total")).Should().OnlyContain(total => total == null);
            }
            else
                responses.Select(response => response.Attribute("total")?.Value).Should()
                    .Equal(Enumerable.Repeat("5", offsets.Length));
            if (sourceMode == "short-with-total")
                pages.Select(page => page.Guids.Length).Should().Equal(1, 2, 2);
            if (sourceMode == "ignored-offset")
            {
                pages.Select(page => page.Guids.Length).Should().Equal(2, 2);
                pages.Select(page => page.ResponseHash).Distinct().Should().ContainSingle();
                responses.Select(response => response.Attribute("offset")?.Value).Should().Equal("0", "0");
            }
            releases.Select(release => release.Guid).Should().Equal(returned);
            releases.Select(release => release.IndexerId).Should().Equal(Enumerable.Repeat<int?>(row.Id, returned.Length));
            releases.Select(release => release.Indexer).Should().Equal(Enumerable.Repeat(row.Name, returned.Length));
            releases.Select(release => release.Protocol).Should()
                .Equal(Enumerable.Repeat(protocol == IndexerType.Newznab ? "Usenet" : "Torrent", returned.Length));
            releases.Select(release => release.Guid).Should().OnlyHaveUniqueItems();
            state.Queries.Should().Be(1 + offsets.Length);
            state.Grabs.Should().Be(0);
            state.QueryFailures.Should().Be(0);
            state.ConnectionErrors.Should().Be(0);
            state.RateLimitedUntil.Should().BeNull();
            rig.Source.Violations.Should().BeEmpty();
        }
    }
}
