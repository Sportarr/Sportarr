using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Every wrestling promotion that was not AEW was searched for as WWE, so a
/// TNA or NJPW event asked indexers for releases that cannot exist.
/// </summary>
public class WrestlingQueryTests
{
    private static EventQueryService Service() =>
        new(NullLogger<EventQueryService>.Instance);

    private static Event WrestlingEvent(string leagueName, string title) => new()
    {
        Title = title,
        Sport = "Wrestling",
        EventDate = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Name = leagueName, Sport = "Wrestling" },
    };

    [Theory]
    [InlineData("TNA Wrestling", "TNA Impact", "TNA")]
    [InlineData("New Japan Pro-Wrestling", "NJPW Strong", "NJPW")]
    [InlineData("Ring of Honor", "ROH Honor Club", "ROH")]
    [InlineData("WWE", "WWE RAW", "WWE")]
    [InlineData("All Elite Wrestling", "AEW Dynamite", "AEW")]
    public void Queries_name_the_promotion_that_runs_the_show(string league, string title, string expectedOrg)
    {
        var queries = Service().BuildEventQueries(WrestlingEvent(league, title));

        queries.Should().NotBeEmpty();
        queries.Should().OnlyContain(q => q.StartsWith(expectedOrg, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_unlisted_promotion_falls_back_to_its_league_name_not_wwe()
    {
        var queries = Service().BuildEventQueries(WrestlingEvent("Pro Wrestling NOAH", "Pro Wrestling NOAH Destination"));

        queries.Should().NotBeEmpty();
        queries.Should().NotContain(q => q.StartsWith("WWE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_year_is_not_mistaken_for_a_card_number()
    {
        var evt = new Event
        {
            Title = "PFL 2026 World Tournament 3",
            Sport = "Fighting",
            EventDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "PFL", Sport = "Fighting" },
        };

        var queries = Service().BuildEventQueries(evt);

        queries.Should().NotContain("PFL 2026 ");
        queries.Should().NotBeEmpty();
        queries[0].Should().NotBe("PFL 2026");
    }

    [Fact]
    public void Duplicate_queries_are_not_asked_twice()
    {
        var evt = new Event
        {
            Title = "Belgian Grand Prix",
            Sport = "Motorsport",
            Location = "Belgium",
            Round = "13",
            EventDate = new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "Formula 1", Sport = "Motorsport" },
        };

        var queries = Service().BuildEventQueries(evt);

        queries.Should().OnlyHaveUniqueItems();
    }
}

/// <summary>
/// A special whose name merely contains a weekly show's word is not that
/// show. "Strong Style Evolved" was filed under NJPW Strong and searched by
/// date, so its releases were never found.
/// </summary>
public class WeeklyShowNamingTests
{
    [Theory]
    [InlineData("NJPW Strong", "NJPW", "Strong", true)]
    [InlineData("NJPW Strong Style Evolved 2026", "NJPW", "Strong", false)]
    [InlineData("AEW Dark", "AEW", "Dark", true)]
    [InlineData("AEW Dark Side of the Ring", "AEW", "Dark", false)]
    [InlineData("WWE Armstrong Special", "WWE", "Strong", false)]
    public void Names_the_weekly_show_only_as_a_whole_word_outside_its_specials(
        string title, string org, string show, bool expected)
    {
        EventQueryService.NamesWeeklyShow(title, org, show).Should().Be(expected);
    }
}
