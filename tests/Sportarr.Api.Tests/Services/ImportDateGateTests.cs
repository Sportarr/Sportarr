using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report follow-up: "Spain vs Argentina 19.07.2026.mkv" was suggested
/// onto "Spain vs Saudi Arabia" from June 21 - wrong opponent, four weeks
/// off. Three holes stacked: the parser only understood year-first dates so
/// 19.07.2026 was never extracted, the import scorer treated dates as a
/// bonus rather than an anchor, and "vs" counted as a matching word, handing
/// any two matchup titles free similarity. Year (25) + Spain/vs overlap (15)
/// landed exactly on the 40-point acceptance floor.
/// </summary>
public class ImportDateGateTests
{
    private const string ReportedFile = "Spain vs Argentina 19.07.2026";

    private static Event SpainVsSaudiArabia() => new()
    {
        Title = "Spain vs Saudi Arabia",
        Sport = "Soccer",
        SeasonNumber = 2026,
        Season = "2026",
        EpisodeNumber = 37,
        EventDate = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "FIFA World Cup", Sport = "Soccer" },
    };

    private static Event SpainVsArgentinaFinal() => new()
    {
        Title = "Spain vs Argentina",
        Sport = "Soccer",
        SeasonNumber = 2026,
        Season = "2026",
        EpisodeNumber = 104,
        EventDate = new DateTime(2026, 7, 19, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "FIFA World Cup", Sport = "Soccer" },
    };

    private static int Score(Event evt)
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var parsed = parser.Parse(ReportedFile);

        return LibraryImportService.CalculateMatchConfidence(
            searchTitle: parsed.EventTitle ?? ReportedFile,
            eventTitle: evt.Title,
            organization: parsed.Organization,
            evt: evt,
            parsedDate: parsed.EventDate,
            parsedYear: parsed.EventYear ?? parsed.EventDate?.Year,
            parsedRoundNumber: parsed.RoundNumber,
            seasonYearEnd: parsed.SeasonYearEnd,
            explicitEpisodeNumber: null,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport);
    }

    [Fact]
    public void Parser_ReadsDayFirstEuropeanDates()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var parsed = parser.Parse(ReportedFile);

        parsed.EventDate.Should().Be(new DateTime(2026, 7, 19));
    }

    [Fact]
    public void DatedFile_NeverMatchesAnEventWeeksAway()
    {
        Score(SpainVsSaudiArabia()).Should().Be(0,
            "a July 19 file cannot be a June 21 event regardless of title overlap");
    }

    [Fact]
    public void DatedFile_PrefersTheEventOnItsDate()
    {
        Score(SpainVsArgentinaFinal()).Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void UndatedMatchupTitle_CannotRideVsToTheFloor()
    {
        // Without a parsed date the gate can't fire; the connector-word fix
        // must keep "Spain vs <anyone>" from reaching 40 on "vs" + year.
        var score = LibraryImportService.CalculateMatchConfidence(
            searchTitle: "Spain vs Argentina",
            eventTitle: "Spain vs Saudi Arabia",
            organization: null,
            evt: SpainVsSaudiArabia(),
            parsedDate: null,
            parsedYear: 2026,
            parsedRoundNumber: null,
            seasonYearEnd: null,
            explicitEpisodeNumber: null,
            parsedLocation: null,
            parsedSport: "Soccer");

        score.Should().BeLessThan(40);
    }
}
