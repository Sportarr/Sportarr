using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily75SearchTests
{
    private static readonly EventQueryService Queries = new(NullLogger<EventQueryService>.Instance);

    [Fact]
    public void EredivisieFixturesInOneMonthShareOneProviderQuery()
    {
        var feyenoord = Game("Dutch Eredivisie", "Feyenoord", "Go Ahead Eagles", 16, 8);
        var ajax = Game("Dutch Eredivisie", "Ajax", "Heerenveen", 16, 8);
        var groningen = Game("Dutch Eredivisie", "Groningen", "Twente", 6, 9);

        Queries.BuildEventQueries(feyenoord).Should().Equal("Eredivisie 2026 08");
        Queries.BuildEventQueries(ajax).Should().Equal("Eredivisie 2026 08");
        Queries.BuildEventQueries(groningen).Should().Equal("Eredivisie 2026 09");
    }

    [Fact]
    public void EredivisieQueryUsesTheBroadcastMonth()
    {
        var evt = Game("Dutch Eredivisie", "Ajax", "Heerenveen", 31, 8);
        evt.EventDate = new DateTime(2026, 9, 1, 0, 30, 0, DateTimeKind.Utc);

        Queries.BuildEventQueries(evt).Should().Equal("Eredivisie 2026 08");
    }

    [Fact]
    public void EersteDivisieAndCustomTemplatesKeepTheirOwnQueries()
    {
        var lowerDivision = Game("Dutch Eerste Divisie", "Feyenoord", "Go Ahead Eagles", 16, 8);
        var eredivisie = Game("Dutch Eredivisie", "Feyenoord", "Go Ahead Eagles", 16, 8);

        Queries.BuildEventQueries(lowerDivision).Should().Equal(
            "Feyenoord vs Go Ahead Eagles", "Go Ahead Eagles vs Feyenoord");
        Queries.BuildEventQueries(eredivisie, customTemplate: "{HomeTeam} {AwayTeam}")
            .Should().Equal("Feyenoord Go Ahead Eagles");
    }

    [Theory]
    [InlineData(SearchTermination.CallerCeiling)]
    [InlineData(SearchTermination.PageCeiling)]
    public void CappedMonthlySearchRestoresTeamQueries(SearchTermination termination)
    {
        var evt = Game("Dutch Eredivisie", "Feyenoord", "Go Ahead Eagles", 16, 8);
        var primary = Queries.BuildEventQueries(evt);
        var diagnostic = new IndexerSearchDiagnostic(1, "Fixture", primary[0], termination,
            100, Array.Empty<SearchPageObservation>(), true, termination == SearchTermination.CallerCeiling);

        Queries.BuildOverflowFallbackQueries(evt, primary, new[] { diagnostic }).Should().Equal(
            "Feyenoord vs Go Ahead Eagles", "Go Ahead Eagles vs Feyenoord");
    }

    [Fact]
    public void CompleteMonthlySearchDoesNotSpendTeamQueries()
    {
        var evt = Game("Dutch Eredivisie", "Feyenoord", "Go Ahead Eagles", 16, 8);
        var primary = Queries.BuildEventQueries(evt);
        var diagnostic = new IndexerSearchDiagnostic(1, "Fixture", primary[0], SearchTermination.Exhausted,
            27, Array.Empty<SearchPageObservation>(), true, true);

        Queries.BuildOverflowFallbackQueries(evt, primary, new[] { diagnostic }).Should().BeEmpty();
    }

    [Fact]
    public void AliasQueryDoesNotHideCanonicalOverflowFallback()
    {
        var evt = Game("Dutch Eredivisie", "Feyenoord", "Go Ahead Eagles", 16, 8);
        evt.HomeTeam = new Team { Name = "Feyenoord", UserAliases = "Feijenoord" };
        evt.AwayTeam = new Team { Name = "Go Ahead Eagles", UserAliases = "GA Eagles" };
        var primary = Queries.BuildEventQueries(evt);
        primary.Should().HaveCountGreaterThan(1);
        var diagnostic = new IndexerSearchDiagnostic(1, "Fixture", primary[0], SearchTermination.CallerCeiling,
            100, Array.Empty<SearchPageObservation>(), true, true);

        Queries.BuildOverflowFallbackQueries(evt, primary, new[] { diagnostic }).Should().Equal(
            "Feyenoord vs Go Ahead Eagles", "Go Ahead Eagles vs Feyenoord");
    }

    private static Event Game(string league, string home, string away, int day, int month) => new()
    {
        Title = $"{home} vs {away}",
        Sport = "Soccer",
        Season = "2026-2027",
        EventDate = new DateTime(2026, month, day, 18, 0, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2026, month, day),
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League
        {
            Name = league,
            ExternalId = league == "Dutch Eredivisie" ? "lg-000009" : "lg-000284",
            Sport = "Soccer"
        }
    };
}
