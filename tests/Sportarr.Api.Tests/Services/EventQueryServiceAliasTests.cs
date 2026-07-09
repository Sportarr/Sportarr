using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// User-defined team aliases must influence SEARCHING, not just matching.
/// Field case: Cyrillic aliases on Portugal/Spain matched a rutracker
/// release perfectly once it was in the result list, but no query ever
/// contained the Cyrillic names, so the tracker never returned it - the
/// title has no Latin team names to hit. Queries now include alias-slot
/// variants for both the custom-template path and the built-in team-sport
/// path.
/// </summary>
public class EventQueryServiceAliasTests
{
    private static EventQueryService CreateService() =>
        new(NullLogger<EventQueryService>.Instance);

    private static Event WorldCupEvent(string? homeAliases = null, string? awayAliases = null) => new()
    {
        Title = "Portugal vs Spain",
        Sport = "Soccer",
        EventDate = new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "FIFA World Cup", Sport = "Soccer" },
        HomeTeam = new Team { Name = "Portugal", Sport = "Soccer", UserAliases = homeAliases },
        AwayTeam = new Team { Name = "Spain", Sport = "Soccer", UserAliases = awayAliases },
    };

    [Fact]
    public void Template_WithAliases_EmitsAliasVariantAfterCanonical()
    {
        var service = CreateService();
        var evt = WorldCupEvent("Португалия", "Испания");

        var queries = service.BuildEventQueries(evt, customTemplate: "{HomeTeam} {AwayTeam} {Year}");

        queries.Should().HaveCount(2);
        queries[0].Should().Be("Portugal Spain 2026");
        queries[1].Should().Be("Португалия Испания 2026");
    }

    [Fact]
    public void Template_WithoutAliases_EmitsSingleCanonicalQuery()
    {
        var service = CreateService();
        var evt = WorldCupEvent();

        var queries = service.BuildEventQueries(evt, customTemplate: "{HomeTeam} {AwayTeam} {Year}");

        queries.Should().ContainSingle().Which.Should().Be("Portugal Spain 2026");
    }

    [Fact]
    public void TeamSport_UnmappedLeague_AddsLeagueYearAliasQuery()
    {
        var service = CreateService();
        var evt = WorldCupEvent("Португалия", "Испания");

        var queries = service.BuildEventQueries(evt);

        // The exact query shape the reporter typed by hand and which
        // returned + matched the release on rutracker.
        queries.Should().Contain("FIFA World Cup 2026 Португалия Испания");
        // Canonical behavior unchanged: normalized title stays first.
        queries[0].Should().Be("Portugal vs Spain");
    }

    [Fact]
    public void TeamSport_MappedLeague_AddsPrefixedAliasQuery()
    {
        var service = CreateService();
        var evt = new Event
        {
            Title = "New Jersey Devils vs New York Rangers",
            Sport = "Ice Hockey",
            EventDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "NHL", Sport = "Ice Hockey" },
            HomeTeam = new Team { Name = "New Jersey Devils", Sport = "Ice Hockey", UserAliases = "Девилз" },
            AwayTeam = new Team { Name = "New York Rangers", Sport = "Ice Hockey", UserAliases = "Рейнджерс" },
        };

        var queries = service.BuildEventQueries(evt);

        queries.Should().Contain("NHL 2026 01");
        queries.Should().Contain("NHL 2026 Девилз Рейнджерс");
    }

    [Fact]
    public void AliasSlots_PairPositionally_AndFallBackToCanonical()
    {
        var service = CreateService();
        // Home has two aliases, away has one: slot 2 pairs the second home
        // alias with the canonical away name.
        var evt = WorldCupEvent("Португалия, Portugalsko", "Испания");

        var queries = service.BuildEventQueries(evt, customTemplate: "{HomeTeam} {AwayTeam} {Year}");

        queries.Should().Contain("Португалия Испания 2026");
        queries.Should().Contain("Portugalsko Spain 2026");
    }

    [Fact]
    public void OneSidedAlias_StillEmitsMixedVariant()
    {
        var service = CreateService();
        var evt = WorldCupEvent(homeAliases: "Португалия");

        var queries = service.BuildEventQueries(evt, customTemplate: "{HomeTeam} {AwayTeam} {Year}");

        queries.Should().Contain("Португалия Spain 2026");
    }
}
