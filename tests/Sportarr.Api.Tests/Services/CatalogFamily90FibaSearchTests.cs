using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily90FibaSearchTests
{
    private static readonly EventQueryService Queries = new(NullLogger<EventQueryService>.Instance);

    [Theory]
    [InlineData("Panama Basketball", "Puerto Rico Basketball", "2025-08-22")]
    [InlineData("Argentina Basketball", "Brazil Basketball", "2025-09-01")]
    public void AmeriCupGameUsesOneCompetitionYearQuery(string home, string away, string date)
    {
        Queries.BuildEventQueries(Game(home, away, date))
            .Should().Equal("FIBA AmeriCup 2025");
    }

    [Fact]
    public void AmeriCupKeepsUserTeamAliasQueries()
    {
        var game = Game("Argentina Basketball", "Brazil Basketball", "2025-09-01");
        game.HomeTeam = new Team { Name = "Argentina Basketball", UserAliases = "Argentina" };
        game.AwayTeam = new Team { Name = "Brazil Basketball", UserAliases = "Brazil" };

        Queries.BuildEventQueries(game).Should().Equal(
            "FIBA AmeriCup 2025", "FIBA AmeriCup 2025 Argentina Brazil");
    }

    [Fact]
    public void AmeriCupKeepsUserTemplateOverride()
    {
        Queries.BuildEventQueries(Game("Argentina Basketball", "Brazil Basketball", "2025-09-01"),
                customTemplate: "{EventTitle}")
            .Should().Equal("Argentina Basketball vs Brazil Basketball");
    }

    [Theory]
    [InlineData(SearchTermination.CallerCeiling)]
    [InlineData(SearchTermination.PageCeiling)]
    public void CappedAmeriCupSearchUsesOnePrecisePairFallback(SearchTermination termination)
    {
        var evt = Game("Argentina Basketball", "Brazil Basketball", "2025-09-01");
        var primary = Queries.BuildEventQueries(evt);
        var diagnostic = new IndexerSearchDiagnostic(1, "Fixture", primary[0], termination,
            100, Array.Empty<SearchPageObservation>(), true, true);

        Queries.BuildOverflowFallbackQueries(evt, primary, new[] { diagnostic })
            .Should().Equal("FIBA AmeriCup 2025 Argentina Brazil");
    }

    [Fact]
    public void CompleteAmeriCupSearchDoesNotUsePairFallback()
    {
        var evt = Game("Argentina Basketball", "Brazil Basketball", "2025-09-01");
        var primary = Queries.BuildEventQueries(evt);
        var diagnostic = new IndexerSearchDiagnostic(1, "Fixture", primary[0], SearchTermination.Exhausted,
            10, Array.Empty<SearchPageObservation>(), true, true);

        Queries.BuildOverflowFallbackQueries(evt, primary, new[] { diagnostic }).Should().BeEmpty();
    }

    [Fact]
    public void UserTemplateDoesNotTriggerAmeriCupOverflowFallback()
    {
        var evt = Game("Argentina Basketball", "Brazil Basketball", "2025-09-01");
        var primary = Queries.BuildEventQueries(evt, customTemplate: "{EventTitle}");
        var diagnostic = new IndexerSearchDiagnostic(1, "Fixture", primary[0], SearchTermination.CallerCeiling,
            100, Array.Empty<SearchPageObservation>(), true, true);

        Queries.BuildOverflowFallbackQueries(evt, primary, new[] { diagnostic }, "{EventTitle}")
            .Should().BeEmpty();
    }

    private static Event Game(string home, string away, string date) => new()
    {
        Title = $"{home} vs {away}",
        Sport = "Basketball",
        Season = "2025",
        EventDate = DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
        BroadcastDate = DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        LeagueId = 1,
        League = new League { Name = "FIBA AmeriCup", Sport = "Basketball", ExternalId = "lg-000487" }
    };
}
