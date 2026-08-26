using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Tags decide which indexers a search actually reaches. Keyed on the query
/// text alone, a league would take an answer gathered from another league's
/// indexers and never ask its own, hiding releases that are really there.
/// </summary>
public class SearchResultCacheScopeTests
{
    [Fact]
    public void The_same_query_under_different_tags_gets_different_keys()
    {
        var a = SearchResultCache.ScopeKey("UFC.300", new[] { 1 });
        var b = SearchResultCache.ScopeKey("UFC.300", new[] { 2 });

        a.Should().NotBe(b);
    }

    [Fact]
    public void Tag_order_does_not_change_the_key()
    {
        var a = SearchResultCache.ScopeKey("UFC.300", new[] { 3, 1, 2 });
        var b = SearchResultCache.ScopeKey("UFC.300", new[] { 1, 2, 3 });

        a.Should().Be(b);
    }

    [Fact]
    public void Repeated_tags_do_not_change_the_key()
    {
        var a = SearchResultCache.ScopeKey("UFC.300", new[] { 1, 1, 2 });
        var b = SearchResultCache.ScopeKey("UFC.300", new[] { 1, 2 });

        a.Should().Be(b);
    }

    [Fact]
    public void No_tags_leaves_the_query_as_the_key()
    {
        SearchResultCache.ScopeKey("UFC.300", null).Should().Be("UFC.300");
        SearchResultCache.ScopeKey("UFC.300", System.Array.Empty<int>()).Should().Be("UFC.300");
    }

    [Fact]
    public void An_untagged_league_does_not_share_a_key_with_a_tagged_one()
    {
        var untagged = SearchResultCache.ScopeKey("UFC.300", null);
        var tagged = SearchResultCache.ScopeKey("UFC.300", new[] { 1 });

        untagged.Should().NotBe(tagged);
    }
}
