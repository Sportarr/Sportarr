using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// Guards against the token catalog and the replacement map inside
/// EventQueryService.BuildQueryFromTemplate drifting apart again. Before this
/// existed, the builder supported 19 tokens, the frontend displayed 16, and
/// the backend token endpoint returned only 12 - so a subset of tokens the
/// builder honored could never be discovered or inserted from the UI.
/// </summary>
public class SearchTemplateTokensTests
{
    private static readonly string[] ExpectedTokens =
    {
        "{League}", "{Year}", "{Month}", "{Day}", "{Round}", "{Round:00}", "{Round:0}",
        "{Week}", "{EventTitle}", "{EventName}", "{Stage}", "{Stage:00}", "{Stage:0}",
        "{HomeTeam}", "{AwayTeam}", "{vs}", "{Season}", "{Part}", "{EventType}",
    };

    [Fact]
    public void All_ContainsExactlyTheNineteenExpectedTokens()
    {
        SearchTemplateTokens.All.Select(t => t.Token).Should().BeEquivalentTo(ExpectedTokens);
        SearchTemplateTokens.All.Should().HaveCount(19);
    }

    [Fact]
    public void All_HasNoDuplicateTokens()
    {
        SearchTemplateTokens.All.Select(t => t.Token).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// The catalog's key set and the builder's replacement-map key set must
    /// be identical - not scanned from source text, but compared as actual
    /// data structures - so a token can never be documented without being
    /// honored, or honored without being discoverable.
    /// </summary>
    [Fact]
    public void ReplacementKeys_MatchTheCatalogExactly()
    {
        EventQueryService.SupportedTemplateTokens.Should().BeEquivalentTo(ExpectedTokens);
        EventQueryService.SupportedTemplateTokens.Should().HaveCount(19);

        var catalogKeys = SearchTemplateTokens.All.Select(t => t.Token).ToList();
        EventQueryService.SupportedTemplateTokens.Should().BeEquivalentTo(catalogKeys);
    }

    /// <summary>
    /// Longer formatted keys must be applied before their shorter prefixes,
    /// or {Round:00} could be partially consumed by {Round} and silently
    /// produce a garbage query. Asserting the array order directly (rather
    /// than only the end-to-end substitution result) makes an accidental
    /// reordering fail immediately.
    /// </summary>
    [Fact]
    public void SupportedTemplateTokens_OrdersFormattedRoundAndStageKeysBeforeTheirBaseForm()
    {
        var tokens = EventQueryService.SupportedTemplateTokens.ToList();

        tokens.IndexOf("{Round:00}").Should().BeLessThan(tokens.IndexOf("{Round}"));
        tokens.IndexOf("{Round:0}").Should().BeLessThan(tokens.IndexOf("{Round}"));
        tokens.IndexOf("{Stage:00}").Should().BeLessThan(tokens.IndexOf("{Stage}"));
        tokens.IndexOf("{Stage:0}").Should().BeLessThan(tokens.IndexOf("{Stage}"));
    }

    /// <summary>
    /// Build a fully populated event and run every catalog token through the
    /// builder at once. No supported token may survive into the output -
    /// that would mean the catalog promises a token the builder doesn't
    /// actually substitute.
    /// </summary>
    [Fact]
    public void BuildQueryFromTemplate_SubstitutesEveryCatalogToken()
    {
        var evt = new Event
        {
            Title = "Tour de France Stage 16",
            Sport = "Cycling",
            EventDate = new DateTime(2027, 7, 16, 0, 0, 0, DateTimeKind.Utc),
            Round = "16",
            Season = "2027",
            HomeTeamName = "Team Alpha",
            AwayTeamName = "Team Beta",
            League = new League { Name = "Tour de France", Sport = "Cycling" },
        };

        var template = string.Join(" ", SearchTemplateTokens.All.Select(t => t.Token));
        var service = new EventQueryService(NullLogger<EventQueryService>.Instance);

        var result = service.BuildQueryFromTemplate(template, evt, part: "Prelims");

        result.Should().NotContain("{", "every supported token in the template should have been substituted");
        result.Should().NotContain("}", "every supported token in the template should have been substituted");
    }
}
