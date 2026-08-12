using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class EventQueryServiceMotorsportTests
{
    private static EventQueryService CreateService() =>
        new(NullLogger<EventQueryService>.Instance);

    private static Event F1Event(string? round = null) => new()
    {
        Title = "Austrian Grand Prix - Race",
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 6, 29, 0, 0, 0, DateTimeKind.Utc),
        Round = round,
        League = new League { Name = "Formula 1", Sport = "Motorsport" },
    };

    [Fact]
    public void BuildEventQueries_Formula1_IncludesSpacedName()
    {
        var queries = CreateService().BuildEventQueries(F1Event());

        // The reported bug: only "Formula1" (no space) was searched, which misses the
        // common dotted "Formula.1.2026x11.Austria.Race" releases. The spaced form must
        // now be searched too, and it should come first.
        queries.Should().Contain("Formula 1 2026");
        queries[0].Should().StartWith("Formula 1");
    }

    [Fact]
    public void BuildEventQueries_Formula1_KeepsConcatenatedNameToo()
    {
        var queries = CreateService().BuildEventQueries(F1Event());

        // Concatenated "formula1 ..." releases still exist, so that form must remain.
        queries.Should().Contain("Formula1 2026");
    }

    [Fact]
    public void BuildEventQueries_Formula1WithRound_CoversBothFormsWithRound()
    {
        var queries = CreateService().BuildEventQueries(F1Event(round: "11"));

        queries.Should().Contain("Formula 1 2026 Round11");
        queries.Should().Contain("Formula1 2026 Round11");
    }

    [Fact]
    public void BuildEventQueries_Formula1_AddsTitleLocationForBothForms()
    {
        var queries = CreateService().BuildEventQueries(F1Event());

        // "Austrian" is derived from the title and must be searched in both name forms.
        queries.Should().Contain("Formula 1 2026 Austrian");
        queries.Should().Contain("Formula1 2026 Austrian");
    }

    [Fact]
    public void BuildEventQueries_Formula1_AddsCountryNounForAdjectiveGpNames()
    {
        var evt = new Event
        {
            Title = "Belgian Grand Prix - Race",
            Sport = "Motorsport",
            EventDate = new DateTime(2026, 7, 19, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "Formula 1", Sport = "Motorsport" },
        };

        var queries = CreateService().BuildEventQueries(evt);

        // Race releases use the country noun ("Formula.1.2026x10.Belgium.Race")
        // while the event title carries the adjective. Both must be searched -
        // the adjective query only surfaced qualifying releases (#168).
        queries.Should().Contain("Formula 1 2026 Belgian");
        queries.Should().Contain("Formula 1 2026 Belgium");
        queries.Should().Contain("Formula1 2026 Belgium");
    }

    [Fact]
    public void BuildEventQueries_Formula1_NoCountryDuplicateWhenTitleIsAlreadyANoun()
    {
        var evt = new Event
        {
            Title = "Monaco Grand Prix - Race",
            Sport = "Motorsport",
            EventDate = new DateTime(2026, 5, 24, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "Formula 1", Sport = "Motorsport" },
        };

        var queries = CreateService().BuildEventQueries(evt);

        // Noun-named GPs have no adjective/noun split - exactly one location query per form.
        queries.Count(q => q.StartsWith("Formula 1 2026 Monaco")).Should().Be(1);
    }

    [Fact]
    public void BuildEventQueries_MotoGp_UsesSingleTokenNameOnly()
    {
        var evt = new Event
        {
            Title = "Austrian Grand Prix",
            Sport = "Motorsport",
            EventDate = new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "MotoGP", Sport = "Motorsport" },
        };

        var queries = CreateService().BuildEventQueries(evt);

        // MotoGP is a single token in release names; it must not be split with a space.
        queries.Should().Contain("MotoGP 2026");
        queries.Should().NotContain(q => q.Contains("Moto GP"));
    }
    private static Event MotoGpEvent(string? round = "9", string? location = null) => new()
    {
        Title = "Italian Grand Prix Race",
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc),
        Round = round,
        Location = location,
        League = new League { Name = "MotoGP", Sport = "Motorsport" },
    };

    [Fact]
    public void BuildEventQueries_MotoGP_IncludesDemonymAndCountryVariants()
    {
        var queries = CreateService().BuildEventQueries(MotoGpEvent());

        // Location queries used to be Formula 1 only, so a release named
        // "motogp.2026.italy..." was only reachable through the broad season
        // query, which an indexer can cap or bury (#230).
        queries.Should().Contain("MotoGP 2026 Italian");
        queries.Should().Contain("MotoGP 2026 Italy");
    }

    [Fact]
    public void BuildEventQueries_MotoGP_KeepsRoundQueryAheadOfBroadFallback()
    {
        var queries = CreateService().BuildEventQueries(MotoGpEvent());

        queries.Should().Contain("MotoGP 2026 Round09");
        queries.IndexOf("MotoGP 2026 Round09")
            .Should().BeLessThan(queries.IndexOf("MotoGP 2026"),
                "the targeted round query has to run before the broad season fallback");
    }

    [Fact]
    public void BuildEventQueries_MotoGP_UsesVenueWhenSet()
    {
        var queries = CreateService().BuildEventQueries(MotoGpEvent(location: "Mugello"));

        queries.Should().Contain("MotoGP 2026 Mugello");
    }

    [Fact]
    public void BuildEventQueries_SeriesWithoutLocationOrGrandPrix_AddsNoEmptyQueries()
    {
        var evt = new Event
        {
            Title = "Daytona 500",
            Sport = "Motorsport",
            EventDate = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "NASCAR", Sport = "Motorsport" },
        };

        var queries = CreateService().BuildEventQueries(evt);

        queries.Should().OnlyContain(q => !string.IsNullOrWhiteSpace(q));
        queries.Should().NotContain(q => q.EndsWith("2026 "));
    }
}
