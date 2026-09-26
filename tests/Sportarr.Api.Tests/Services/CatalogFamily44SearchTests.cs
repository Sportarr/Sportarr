using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily44SearchTests
{
    private const string ObservedWrongRelease =
        "sur.les.traces.de.marco.polo.e03.de.l.iran.en.afghanistan.doc.french.hdtv.x264-abitbol";

    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static TheoryData<Event, string[]> FrozenEvents => new()
    {
        { TeamEvent("CAFA Nations Cup", "Soccer", "Iran vs Afghanistan", "Iran", "Afghanistan", "2025", "2025-08-29"), ["Iran vs Afghanistan", "Afghanistan vs Iran"] },
        { TeamEvent("CAFA Nations Cup", "Soccer", "Iran vs Uzbekistan", "Uzbekistan", "Iran", "2025", "2025-09-08"), ["Iran vs Uzbekistan"] },
        { TeamEvent("CECAFA Club Cup", "Soccer", "Jamus vs Al-Hilal Omdurman", "Al-Hilal Omdurman", "Jamus", "2027", "2026-08-07"), ["Jamus vs Al-Hilal Omdurman"] },
        { TeamEvent("CECAFA Club Cup", "Soccer", "Rayon Sports vs Gor Mahia", "Rayon Sports", "Gor Mahia", "2027", "2026-08-07"), ["Rayon Sports vs Gor Mahia", "Gor Mahia vs Rayon Sports"] },
        { TeamEvent("CEHL", "Hockey", "IHC Leuven vs Mechelen Golden Sharks", "IHC Leuven", "Mechelen Golden Sharks", "2023-2024", "2023-12-16"), ["IHC Leuven vs Mechelen Golden Sharks", "Mechelen Golden Sharks vs IHC Leuven"] },
        { TeamEvent("CEHL", "Hockey", "Bulldogs Liège vs EHC Neuwied", "Bulldogs Liège", "EHC Neuwied", "2023-2024", "2024-03-30"), ["Bulldogs Liège vs EHC Neuwied", "EHC Neuwied vs Bulldogs Liège"] },
        { TeamEvent("CEV Champions League", "Volleyball", "Olympiacos Volleyball vs Guaguas Las Palmas", "Olympiacos Volleyball", "Guaguas Las Palmas", "2025-2026", "2025-11-06"), ["Olympiacos Volleyball vs Guaguas Las Palmas"] },
        { TeamEvent("CEV Champions League", "Volleyball", "Volley Perugia vs Aluron CMC Warta Zawiercie", "Volley Perugia", "Aluron CMC Warta Zawiercie", "2025-2026", "2026-05-17"), ["Volley Perugia vs Aluron CMC Warta Zawiercie"] }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void UnprovenFamilyQueriesRemainDirectional(Event evt, string[] expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Fact]
    public void ObservedDocumentaryIsRejectedAcrossRoutes()
    {
        var evt = TeamEvent(
            "CAFA Nations Cup",
            "Soccer",
            "Iran vs Afghanistan",
            "Iran",
            "Afghanistan",
            "2025",
            "2025-08-29");
        var validation = Matcher.ValidateRelease(Release(ObservedWrongRelease), evt);
        var score = Scorer.CalculateMatchScore(ObservedWrongRelease, evt);
        var (parsed, importTitle) = ParseForImport(ObservedWrongRelease);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryScore(ObservedWrongRelease, importTitle, evt, parsed);

        validation.IsMatch.Should().BeFalse();
        score.Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeLessThan(50);
        libraryScore.Should().BeLessThan(40);
    }

    private static Event TeamEvent(
        string leagueName,
        string sport,
        string title,
        string home,
        string away,
        string season,
        string broadcastDate)
    {
        var date = DateTime.Parse(broadcastDate, System.Globalization.CultureInfo.InvariantCulture);
        return new Event
        {
            Title = title,
            Sport = sport,
            Season = season,
            EventDate = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = home,
            AwayTeamName = away,
            League = new League { Name = leagueName, Sport = sport }
        };
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
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

    private static int LibraryScore(string sourceTitle, string eventTitle, Event evt, SportsParseResult parsed) =>
        LibraryImportService.CalculateMatchConfidence(
            eventTitle,
            evt.Title,
            parsed.Organization,
            evt,
            parsed.EventDate,
            parsed.EventYear ?? parsed.EventDate?.Year,
            parsed.RoundNumber,
            parsed.SeasonYearEnd,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport,
            sourceTitle: sourceTitle);
}
