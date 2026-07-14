using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report: importing "FIFA.World.Cup.2026.Quarter.Final.Spain.vs.
/// Belgium.1080p.AP.WEB-DL" auto-matched a WORLD SNOOKER event
/// ("Championship League Invitation Group 1 Day 1") at exactly 40%.
/// Two scorer holes combined: an empty/degenerate parsed title turns the
/// "event contains search" branch into a 40-point freebie for every event
/// (string.Contains("") is true), and the wizard scorer had no sport gate,
/// so a soccer file could land on a snooker event. 40 was also exactly the
/// acceptance floor.
/// </summary>
public class LibraryImportCrossSportMatchingTests
{
    private const string ReportedFile = "FIFA.World.Cup.2026.Quarter.Final.Spain.vs.Belgium.1080p.AP.WEB-DL.H264-AP";

    private static Event SpainVsBelgiumWorldCup() => new()
    {
        Title = "Spain vs Belgium",
        Sport = "Soccer",
        SeasonNumber = 2026,
        Season = "2026",
        EpisodeNumber = 98,
        EventDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "FIFA World Cup", Sport = "Soccer" },
    };

    private static Event SnookerDecoy() => new()
    {
        Title = "Championship League Invitation Group 1 Day 1",
        Sport = "Snooker",
        SeasonNumber = 2025,
        Season = "2025",
        EpisodeNumber = 103,
        EventDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "World Snooker", Sport = "Snooker" },
    };

    private static (int wc, int snooker) ScoreBoth()
    {
        // Drive the real parser so the test exercises whatever title,
        // organization, sport, and year it actually extracts for this file.
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var parsed = parser.Parse(ReportedFile);
        var searchTitle = parsed.EventTitle ?? ReportedFile;

        int Score(Event evt) => LibraryImportService.CalculateMatchConfidence(
            searchTitle: searchTitle,
            eventTitle: evt.Title!,
            organization: parsed.Organization,
            evt: evt,
            parsedDate: parsed.EventDate,
            parsedYear: parsed.EventYear ?? parsed.EventDate?.Year,
            parsedRoundNumber: parsed.RoundNumber,
            seasonYearEnd: parsed.SeasonYearEnd,
            explicitEpisodeNumber: null,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport);

        return (Score(SpainVsBelgiumWorldCup()), Score(SnookerDecoy()));
    }

    [Fact]
    public void WorldCupFile_NeverAutoMatchesSnooker()
    {
        var (wc, snooker) = ScoreBoth();

        // The wizard accepts at >= 40. The snooker decoy must sit below it.
        snooker.Should().BeLessThan(40,
            $"a soccer file must not clear the auto-match floor on a snooker event (wc={wc}, snooker={snooker})");
    }

    [Fact]
    public void WorldCupFile_PrefersTheRealEvent()
    {
        var (wc, snooker) = ScoreBoth();

        wc.Should().BeGreaterThan(snooker,
            $"the FIFA World Cup event must outscore the snooker decoy (wc={wc}, snooker={snooker})");
    }

    [Fact]
    public void EmptySearchTitle_AwardsNoContainsPoints()
    {
        // string.Contains("") is true - an empty parsed title must not hand
        // every event in the library a 40-point contains award.
        var score = LibraryImportService.CalculateMatchConfidence(
            searchTitle: "",
            eventTitle: "Championship League Invitation Group 1 Day 1",
            organization: null,
            evt: SnookerDecoy(),
            parsedDate: null);

        score.Should().Be(0, "an empty search title carries no title signal at all");
    }
}
