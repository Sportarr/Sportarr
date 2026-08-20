using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A league can carry several custom search templates, one per line, because
/// release groups name the same event differently and one template cannot
/// cover them all. Storage is the same single text field, so a league that
/// already has one template keeps working untouched.
/// </summary>
public class MultipleSearchTemplatesTests
{
    private readonly EventQueryService _service = new(NullLogger<EventQueryService>.Instance);

    private static Event F1Event() => new()
    {
        Title = "Dutch Grand Prix Race",
        Sport = "Motorsport",
        Season = "2026",
        Round = "15",
        EventDate = new DateTime(2026, 8, 30),
        League = new League { Name = "Formula 1", Sport = "Motorsport" },
    };

    [Fact]
    public void EachTemplate_ProducesItsOwnQuery()
    {
        var queries = _service.BuildEventQueries(F1Event(), null,
            "formula1 {Year} round{Round} SkyF1HD\nF1 {Year} R{Round:0} 1080p");

        queries.Should().HaveCount(2);
        queries[0].Should().Be("formula1 2026 round15 SkyF1HD");
        queries[1].Should().Be("F1 2026 R15 1080p");
    }

    [Fact]
    public void FirstTemplate_StaysThePrimaryQuery()
    {
        // The first query decides the search cache key, so order is the
        // user's stated preference.
        var queries = _service.BuildEventQueries(F1Event(), null,
            "primary {Year}\nsecondary {Year}");

        queries.First().Should().Be("primary 2026");
    }

    [Fact]
    public void SingleTemplate_BehavesExactlyAsBefore()
    {
        var queries = _service.BuildEventQueries(F1Event(), null, "formula1 {Year} round{Round}");

        queries.Should().ContainSingle().Which.Should().Be("formula1 2026 round15");
    }

    [Fact]
    public void BlankLinesAndDuplicates_AreIgnored()
    {
        var queries = _service.BuildEventQueries(F1Event(), null,
            "  formula1 {Year}  \n\n   \nformula1 {Year}\nF1 {Year}\n");

        queries.Should().HaveCount(2);
        queries.Should().Contain("formula1 2026").And.Contain("F1 2026");
    }

    [Fact]
    public void TemplateCount_IsCapped()
    {
        // Every template is a query per event against every indexer, so a
        // pasted wall of text must not turn one search into hundreds.
        var many = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"group{i} {{Year}}"));

        _service.BuildEventQueries(F1Event(), null, many)
            .Should().HaveCount(SearchTemplateList.MaxTemplates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void NoTemplate_FallsBackToTheBuiltInQueries(string? stored)
    {
        SearchTemplateList.Parse(stored).Should().BeEmpty();

        // The built-in motorsport logic still runs.
        _service.BuildEventQueries(F1Event(), null, stored).Should().NotBeEmpty();
    }

    [Fact]
    public void Normalize_CollapsesToStorageForm()
    {
        SearchTemplateList.Normalize("  a {Year} \n\n b {Year}\na {Year}\n").Should().Be("a {Year}\nb {Year}");
        SearchTemplateList.Normalize("   ").Should().BeNull();
        SearchTemplateList.Normalize(null).Should().BeNull();
    }
}
