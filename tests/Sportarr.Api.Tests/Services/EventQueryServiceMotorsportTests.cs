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
}
