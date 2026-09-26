using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily84SearchTests
{
    private const string Target =
        "Super League Rugby 2026 Warrington Wolves vs Hull KR 20 09 720pEN60fps FSP";

    private static readonly EventQueryService Queries = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    [Fact]
    public void SuperLeagueFixturesUseOneSharedMonthQuery()
    {
        Queries.BuildEventQueries(Warrington()).Should().Equal("Super League Rugby 2026 09");
        Queries.BuildEventQueries(York()).Should().Equal("Super League Rugby 2026 02");
    }

    [Theory]
    [InlineData(2026, 8, 30, "Super League Rugby 2026 08")]
    [InlineData(2026, 8, 31, "Super League Rugby 2026 08|Super League Rugby 2026 09")]
    [InlineData(2026, 12, 31, "Super League Rugby 2026 12|Super League Rugby 2027 01")]
    public void MonthBoundaryQueriesCoverObservedNextDayTitles(
        int year, int month, int day, string expected)
    {
        var evt = Warrington();
        evt.EventDate = new DateTime(year, month, day, 16, 30, 0, DateTimeKind.Utc);
        evt.BroadcastDate = evt.EventDate.Date;

        Queries.BuildEventQueries(evt).Should().Equal(expected.Split('|'));
        Queries.BuildEventQueries(evt, customTemplate: "{HomeTeam} {AwayTeam}")
            .Should().Equal("Warrington Wolves Hull Kingston Rovers");
    }

    [Fact]
    public void CustomTemplateAndNeighborLeagueKeepTheirPlans()
    {
        Queries.BuildEventQueries(Warrington(), customTemplate: "{HomeTeam} {AwayTeam}")
            .Should().Equal("Warrington Wolves Hull Kingston Rovers");

        var championship = Warrington();
        championship.League = new League { Name = "English RFL Championship", Sport = "Rugby" };
        Queries.BuildEventQueries(championship).Should().Equal(
            "Warrington Wolves vs Hull Kingston Rovers",
            "Hull Kingston Rovers vs Warrington Wolves");
    }

    [Theory]
    [InlineData(SearchTermination.CallerCeiling)]
    [InlineData(SearchTermination.PageCeiling)]
    public void CappedMonthQueryRestoresPreciseTeamQueries(SearchTermination termination)
    {
        var evt = Warrington();
        var primary = Queries.BuildEventQueries(evt);
        var diagnostic = new IndexerSearchDiagnostic(1, "Fixture", primary[0], termination,
            100, Array.Empty<SearchPageObservation>(), true, true);

        Queries.BuildOverflowFallbackQueries(evt, primary, new[] { diagnostic }).Should().Equal(
            "Warrington Wolves vs Hull Kingston Rovers", "Warrington Wolves vs Hull KR");
    }

    [Fact]
    public void CompleteMonthQueryDoesNotSpendFallbackQueries()
    {
        var evt = Warrington();
        var primary = Queries.BuildEventQueries(evt);
        var diagnostic = new IndexerSearchDiagnostic(1, "Fixture", primary[0], SearchTermination.Exhausted,
            7, Array.Empty<SearchPageObservation>(), true, true);

        Queries.BuildOverflowFallbackQueries(evt, primary, new[] { diagnostic }).Should().BeEmpty();
    }

    [Fact]
    public void CappedNextMonthQueryRestoresPreciseTeamQueries()
    {
        var evt = Warrington();
        evt.EventDate = new DateTime(2026, 8, 31, 16, 30, 0, DateTimeKind.Utc);
        evt.BroadcastDate = evt.EventDate.Date;
        var primary = Queries.BuildEventQueries(evt);
        var diagnostics = new[]
        {
            new IndexerSearchDiagnostic(1, "Fixture", primary[0], SearchTermination.Exhausted,
                0, Array.Empty<SearchPageObservation>(), true, true),
            new IndexerSearchDiagnostic(1, "Fixture", "Super League Rugby 2026 09",
                SearchTermination.PageCeiling, 100, Array.Empty<SearchPageObservation>(), true, true)
        };

        Queries.BuildOverflowFallbackQueries(evt, primary, diagnostics).Should().Equal(
            "Warrington Wolves vs Hull Kingston Rovers", "Warrington Wolves vs Hull KR");
    }

    [Fact]
    public void NextMonthTitleCanMatchTheLastDayFixture()
    {
        var evt = Warrington();
        evt.EventDate = new DateTime(2026, 8, 31, 16, 30, 0, DateTimeKind.Utc);
        evt.BroadcastDate = evt.EventDate.Date;
        const string title =
            "Super League Rugby 2026 Warrington Wolves vs Hull KR 01 09 720pEN60fps FSP";

        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        new ReleaseMatchScorer().CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void ObservedReleaseMatchesOfficialFixtureDespiteNextDayTitle()
    {
        var evt = Warrington();
        var result = Matcher.ValidateRelease(Release(Target), evt);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        new ReleaseMatchScorer().CalculateMatchScore(Target, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        EventDateMatchContext.ShouldLoadPeers(evt).Should().BeTrue();
    }

    [Fact]
    public void NeighboringSameTeamFixtureBlocksDateDrift()
    {
        var evt = Warrington();
        var peer = Warrington();
        peer.Id = 2;
        peer.ExternalId = "ev-peer";
        peer.EventDate = new DateTime(2026, 9, 20, 16, 30, 0, DateTimeKind.Utc);
        peer.BroadcastDate = new DateTime(2026, 9, 20);

        Matcher.ValidateRelease(Release(Target), evt, datePeers: new[] { peer })
            .IsMatch.Should().BeFalse();
    }

    [Theory]
    [InlineData("Super League Rugby 2026 Wakefield Trinity vs Leigh Leopards 19 09 720pEN60fps FSP")]
    [InlineData("Super League Rugby 2026 Leight Leopards vs St Helen Saints 05 09 720pEN60fps FSP")]
    [InlineData("Super League Rugby 2026 Touluse vs Hull FC 04 09 720pEN60fps FSP")]
    [InlineData("Super League Rugby 2026 Bradford Bulls vs Castleford Tigers 03 09 720pEN60fps FSP")]
    [InlineData("Super League Rugby 2026 Castleford Tigers vs Hull KR  09 08 720pEN60fps FSP")]
    [InlineData("Super League Rugby 2026 York Knicks vs Hull FC 09 07 720pEN60fps FSP")]
    [InlineData("Super League Rugby 2026 Hull KR vs Warrington Wolves 18 08 720pEN60fps FSP")]
    [InlineData("Super League Rugby 2025 Hull KR vs Warrington Wolves 20 09 720pEN60fps FSP")]
    public void AdjacentOrDifferentFixtureIsNotEligible(string title)
    {
        var evt = Warrington();
        var result = Matcher.ValidateRelease(Release(title), evt);

        (result.IsMatch && new ReleaseMatchScorer().CalculateMatchScore(title, evt)
            >= ReleaseMatchScorer.AutoGrabMatchScore).Should().BeFalse();
    }

    private static Event Warrington() => new()
    {
        Id = 1,
        ExternalId = "ev-2616201",
        Title = "Warrington Wolves vs Hull Kingston Rovers",
        Sport = "Rugby",
        Season = "2026",
        EventDate = new DateTime(2026, 9, 19, 16, 30, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2026, 9, 19),
        BroadcastDateVerified = true,
        LeagueId = 1,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = "Warrington Wolves",
        AwayTeamName = "Hull Kingston Rovers",
        League = new League { Id = 1, ExternalId = "lg-000054",
            Name = "English Rugby League Super League", Sport = "Rugby" }
    };

    private static Event York() => new()
    {
        Id = 3,
        ExternalId = "ev-374175",
        Title = "York City Knights vs Hull Kingston Rovers",
        Sport = "Rugby",
        Season = "2026",
        EventDate = new DateTime(2026, 2, 12, 20, 0, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2026, 2, 12),
        BroadcastDateVerified = true,
        LeagueId = 1,
        HomeTeamId = 3,
        AwayTeamId = 2,
        HomeTeamName = "York City Knights",
        AwayTeamName = "Hull Kingston Rovers",
        League = new League { Id = 1, ExternalId = "lg-000054",
            Name = "English Rugby League Super League", Sport = "Rugby" }
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/rugby.nzb",
        Indexer = "Frozen provider"
    };
}
