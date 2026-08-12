using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #231: British Superbike is in the metadata but had no
/// handling of its own, so searches used the full sponsored league name, the
/// parser read nothing out of a BSB release, and the two races of a round
/// were indistinguishable. BSB stays separate from World Superbike, which
/// shares the word "superbike" and nothing else.
/// </summary>
public class BritishSuperbikeTests
{
    private readonly SportsFileNameParser _parser =
        new(Mock.Of<ILogger<SportsFileNameParser>>());

    private static EventQueryService QuerySvc() => new(NullLogger<EventQueryService>.Instance);

    // The reporter's exact release title.
    private const string RaceOneRelease =
        "BSB 2026 Round01 Oulton Park International Race One TNT WEB-DL 1080p H264 DDP5 1 English-MWR";

    private static Event BsbEvent(string leagueName = "Bennetts British Superbike") => new()
    {
        Title = "Oulton Park Race One",
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
        Round = "1",
        Location = "Oulton Park",
        League = new League { Name = leagueName, Sport = "Motorsport" },
    };

    [Fact]
    public void Parse_BsbRelease_ExtractsOrganisationRoundAndSession()
    {
        var result = _parser.Parse(RaceOneRelease);

        result.Sport.Should().Be("Motorsport");
        result.Organization.Should().Be("BSB");
        result.RoundNumber.Should().Be(1, "the padded Round01 form is what releases use");
        result.Session.Should().Be("Race 1");
    }

    [Theory]
    [InlineData("BSB 2026 Round01 Oulton Park International Race One TNT WEB-DL 1080p", "Race 1")]
    [InlineData("BSB 2026 Round01 Oulton Park International Race Two TNT WEB-DL 1080p", "Race 2")]
    [InlineData("BSB.2026.Round03.Donington.Park.Qualifying.1080p.WEB-DL", "Qualifying")]
    public void Parse_NumberedRaces_AreToldApart(string release, string expectedSession)
    {
        _parser.Parse(release).Session.Should().Be(expectedSession);
    }

    [Fact]
    public void Parse_WholeDayCoverage_DoesNotClaimARace()
    {
        // A "Day One" upload covers the whole day. Reading it as race one
        // would import it over a specific race.
        var result = _parser.Parse("BSB 2026 Round01 Oulton Park Day One TNT WEB-DL 1080p");

        result.Organization.Should().Be("BSB");
        result.Session.Should().NotBe("Race 1");
        result.Session.Should().NotBe("Race");
    }

    [Theory]
    [InlineData("Bennetts British Superbike")]
    [InlineData("British Superbike Championship")]
    [InlineData("BSB")]
    public void BuildEventQueries_SearchesTheAbbreviationReleasesUse(string leagueName)
    {
        var queries = QuerySvc().BuildEventQueries(BsbEvent(leagueName));

        queries.Should().Contain("BSB 2026 Round01");
        queries.Should().Contain("BSB 2026");
        queries.Should().NotContain(q => q.Contains("Bennetts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildEventQueries_BsbIsNotSearchedAsWorldSuperbike()
    {
        var queries = QuerySvc().BuildEventQueries(BsbEvent());

        queries.Should().NotContain(q => q.Contains("WSBK", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildEventQueries_WorldSuperbikeStillResolvesToWsbk()
    {
        var evt = BsbEvent("World Superbike");
        var queries = QuerySvc().BuildEventQueries(evt);

        queries.Should().Contain(q => q.StartsWith("WSBK"),
            "adding BSB must not steal World Superbike, which shares the word superbike");
    }

    [Theory]
    [InlineData("BSB 2026 Round01 Oulton Park Race One 1080p", "Race 1")]
    [InlineData("BSB 2026 Round01 Oulton Park Race Two 1080p", "Race 2")]
    public void DetectPart_NumberedRaces_MapToTheirOwnSession(string release, string expectedPart)
    {
        var detector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());

        var part = detector.DetectPart(release, "Motorsport", leagueName: "British Superbike");

        part?.SegmentName.Should().Be(expectedPart);
    }
}
