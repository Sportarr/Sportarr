using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The series dropped "V8" from its name in 2016 and every release since is
/// filed under Supercars alone. Queries built from the metadata name found
/// nothing at all: measured against a real indexer, "V8Supercars 2026 Round09"
/// returned zero and "Supercars 2026 Round09" returned seven.
/// </summary>
public class EventQueryServiceSupercarsTests
{
    private static EventQueryService CreateService() =>
        new(NullLogger<EventQueryService>.Instance);

    private static Event Race(string title, string round, string leagueName = "V8 Supercars") => new()
    {
        Title = title,
        Sport = "Motorsport",
        Season = "2025",
        Round = round,
        Location = "AU",
        EventDate = new DateTime(2025, 8, 10, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Name = leagueName, Sport = "Motorsport" },
    };

    [Fact]
    public void TheSeriesIsCalledSupercarsWhateverTheLeagueIsNamed()
    {
        var queries = CreateService().BuildEventQueries(Race("Century Batteries Ipswich Super 440 - Race 25", "8"));

        queries.Should().Contain(q => q.StartsWith("Supercars "));
        queries.Should().NotContain(q => q.Contains("V8"), "no release carries V8 since 2016");
    }

    [Fact]
    public void ARenamedLeagueAsksForTheSameThing()
    {
        var renamed = CreateService().BuildEventQueries(
            Race("Century Batteries Ipswich Super 440 - Race 25", "8", leagueName: "Supercars"));
        var old = CreateService().BuildEventQueries(
            Race("Century Batteries Ipswich Super 440 - Race 25", "8"));

        renamed.Should().BeEquivalentTo(old, "the fix must not depend on which name the install carries");
    }

    [Fact]
    public void TheRaceNumberInTheTitleBecomesAQuery()
    {
        var queries = CreateService().BuildEventQueries(Race("Century Batteries Ipswich Super 440 - Race 25", "8"));

        // Releases from these seasons are named "Supercars 2025 Race 25 Ipswich 10 08".
        queries.Should().Contain("Supercars 2025 Race 25");
        queries.Should().Contain("Supercars 2025 Round08");
        queries.Should().Contain("Supercars 2025");
    }

    [Fact]
    public void AnEventWithNoRaceNumberAsksForNoRace()
    {
        var queries = CreateService().BuildEventQueries(Race("Bathurst 1000 Practice", "10"));

        queries.Should().NotContain(q => q.Contains(" Race "));
    }

    [Fact]
    public void ARenameUpstreamReachesTheQueries()
    {
        // The sync now follows the source's name, so an install that carried
        // the old one asks with the new one after its next refresh. Both are
        // covered because a rename lands league by league.
        foreach (var name in new[] { "V8 Supercars", "Supercars" })
        {
            CreateService()
                .BuildEventQueries(Race("Century Batteries Ipswich Super 440 - Race 25", "8", leagueName: name))
                .Should().Contain("Supercars 2025 Race 25");
        }
    }

    [Fact]
    public void ACountryCodeIsNeverAQuery()
    {
        var queries = CreateService().BuildEventQueries(Race("Century Batteries Ipswich Super 440 - Race 25", "8"));

        // "Supercars 2025 AU" returned nothing on a real indexer.
        queries.Should().NotContain("Supercars 2025 AU");
    }

    [Fact]
    public void ARealPlaceNameIsStillWorthAsking()
    {
        var evt = Race("Century Batteries Ipswich Super 440 - Race 25", "8");
        evt.Location = "Ipswich";

        CreateService().BuildEventQueries(evt).Should().Contain("Supercars 2025 Ipswich");
    }
}
