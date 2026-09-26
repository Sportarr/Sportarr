using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily93AthleticsSearchTests
{
    private static readonly EventQueryService Queries = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Fact]
    public void EuropeanFinalsUseOneLeagueYearQueryPerEvent()
    {
        Queries.BuildEventQueries(WomensShotPut()).Should().Equal("European Athletics Championships 2026");
        Queries.BuildEventQueries(MensRelay()).Should().Equal("European Athletics Championships 2026");
    }

    [Fact]
    public void CustomTemplateOverridesEuropeanFinalQuery()
    {
        Queries.BuildEventQueries(WomensShotPut(), customTemplate: "{EventTitle}")
            .Should().Equal("Womens Shot Put Final");
    }

    [Fact]
    public void ObservedWomensShotPutFinalIsEligible()
    {
        const string title = "European Athletics Championships Birmingham 2026 Women Shot Put Final 10 08 720pEN50fps ES";
        var evt = WomensShotPut();

        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void ObservedWomensShotPutFinalCanBeAssignedDuringImport()
    {
        const string title = "European Athletics Championships Birmingham 2026 Women Shot Put Final 10 08 720pEN50fps ES";
        var evt = WomensShotPut();
        var (parsed, eventTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(eventTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryImportService.CalculateMatchConfidence(
            eventTitle, evt.Title, parsed.Organization, evt, parsed.EventDate,
            parsed.EventYear ?? parsed.EventDate?.Year, parsed.RoundNumber, parsed.SeasonYearEnd,
            parsedLocation: parsed.Location, parsedSport: parsed.Sport, sourceTitle: title);

        importScore.Should().BeGreaterThanOrEqualTo(50);
        libraryScore.Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void ObservedDayOneSessionCannotBeApprovedForShotPutFinal()
    {
        const string title = "European Athletics Championships Birmingham 2026 Day 1 Evening Session 10 08 720pEN50fps ES";
        var evt = WomensShotPut();

        Matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt)
            .Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Theory]
    [InlineData("Birmingham 2026 European Athletics Championships Shot Put Men Final 11 08 720pEN50fps ES")]
    [InlineData("European Athletics Championships Birmingham 2026 Men Shot Put Final 10 08 720pEN50fps ES")]
    [InlineData("European Athletics Championships Birmingham 2026 Women Shot Put Qualification 10 08 720pEN50fps ES")]
    [InlineData("European Athletics Championships Birmingham 2026 Women Discus Final 10 08 720pEN50fps ES")]
    [InlineData("European U18 Athletics Championships 2026 Women Shot Put Final 10 08 720pEN50fps ES")]
    public void OtherFinalsAndSessionsCannotFillWomensShotPut(string title)
    {
        var evt = WomensShotPut();

        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeFalse();
        var (parsed, eventTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(eventTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryImportService.CalculateMatchConfidence(
            eventTitle, evt.Title, parsed.Organization, evt, parsed.EventDate,
            parsed.EventYear ?? parsed.EventDate?.Year, parsed.RoundNumber, parsed.SeasonYearEnd,
            parsedLocation: parsed.Location, parsedSport: parsed.Sport, sourceTitle: title);

        importScore.Should().BeLessThan(50);
        libraryScore.Should().BeLessThan(40);
    }

    [Theory]
    [InlineData("2026-08-11")]
    [InlineData("2026-10-08")]
    public void SameDisciplineOnAnotherDateCannotFillTheEvent(string dateText)
    {
        const string title = "European Athletics Championships Birmingham 2026 Women Shot Put Final 10 08 720pEN50fps ES";
        var date = DateTime.Parse(dateText, System.Globalization.CultureInfo.InvariantCulture);
        var evt = Event("Womens Shot Put Final", date, DateTime.SpecifyKind(date.AddHours(18), DateTimeKind.Utc));
        var (parsed, eventTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(eventTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryImportService.CalculateMatchConfidence(
            eventTitle, evt.Title, parsed.Organization, evt, parsed.EventDate,
            parsed.EventYear ?? parsed.EventDate?.Year, parsed.RoundNumber, parsed.SeasonYearEnd,
            parsedLocation: parsed.Location, parsedSport: parsed.Sport, sourceTitle: title);

        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeFalse();
        importScore.Should().BeLessThan(50);
        libraryScore.Should().BeLessThan(40);
    }

    private static Event WomensShotPut() => Event(
        "Womens Shot Put Final", new DateTime(2026, 8, 10), new DateTime(2026, 8, 10, 18, 3, 0, DateTimeKind.Utc));

    private static Event MensRelay() => Event(
        "Mens 4 x 400 metres Relay Final", new DateTime(2026, 8, 16), new DateTime(2026, 8, 16, 20, 48, 0, DateTimeKind.Utc));

    private static Event Event(string title, DateTime broadcastDate, DateTime eventDate) => new()
    {
        Title = title,
        Sport = "Athletics",
        Season = "2026",
        Round = "200",
        EventDate = eventDate,
        BroadcastDate = broadcastDate,
        BroadcastDateVerified = true,
        League = new League { Name = "European Athletics Championships", Sport = "Athletics" }
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/release",
        Indexer = "Fixture"
    };

    private static (SportsParseResult Parsed, string EventTitle) ParseForImport(string title)
    {
        var sports = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);
        var media = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(title);
        var eventTitle = sports.Confidence >= 60 && !string.IsNullOrWhiteSpace(sports.EventTitle)
            ? sports.EventTitle
            : media.EventTitle;
        return (sports, eventTitle ?? string.Empty);
    }
}
