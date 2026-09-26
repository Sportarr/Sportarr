using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily45SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static TheoryData<Event, string[]> FrozenEvents => new()
    {
        { TeamEvent("CFL", "Football", "Calgary Stampeders vs Saskatchewan Roughriders", "Calgary Stampeders", "Saskatchewan Roughriders", "2026", "2026-05-18", "500"), ["Calgary Stampeders vs Saskatchewan Roughriders", "Saskatchewan Roughriders vs Calgary Stampeders"] },
        { TeamEvent("CFL", "Football", "BC Lions vs Montreal Alouettes", "BC Lions", "Montreal Alouettes", "2026", "2026-09-12", "15", "2026-09-13"), ["BC Lions vs Montreal Alouettes", "Montreal Alouettes vs BC Lions"] },
        { TeamEvent("CFU Club Shield", "Soccer", "Dublanc vs St. John's Sports", "Dublanc", "St. John's Sports", "2026", "2026-07-23", "32"), ["Dublanc vs St. John's Sports", "St. John's Sports vs Dublanc"] },
        { TeamEvent("CFU Club Shield", "Soccer", "Mount Pleasant vs Delfines del Este", "Mount Pleasant", "Delfines del Este", "2026", "2026-08-02", "200"), ["Mount Pleasant vs Delfines del Este", "Delfines del Este vs Mount Pleasant"] },
        { NamedEvent("CHIKARA Pro", "Combat", "Young Lions Cup XVI", "2020", "2020-01-18"), ["Young Lions Cup XVI"] },
        { NamedEvent("CHIKARA Pro", "Combat", "Action Arcade #12", "2020", "2020-06-20"), ["Action Arcade #12"] },
        { NamedEvent("CMLL", "Combat", "Sabados De Coliseo", "2026", "2026-01-03", "1"), ["Sabados De Coliseo"] },
        { NamedEvent("CMLL", "Combat", "Domingo Familiar", "2026", "2026-09-13"), ["Domingo Familiar"] }
    };

    public static TheoryData<Event, string> ObservedWrongReleases => new()
    {
        { TeamEvent("CFL", "Football", "Calgary Stampeders vs Saskatchewan Roughriders", "Calgary Stampeders", "Saskatchewan Roughriders", "2026", "2026-05-18", "500"), "CFL Week 21 Calgary Stampeders at Saskatchewan Roughriders" },
        { TeamEvent("CFL", "Football", "Calgary Stampeders vs Saskatchewan Roughriders", "Calgary Stampeders", "Saskatchewan Roughriders", "2026", "2026-05-18", "500"), "CFL Saskatchewan Roughriders at Calgary Stampeders 20/09/24" },
        { TeamEvent("CFL", "Football", "Calgary Stampeders vs Saskatchewan Roughriders", "Calgary Stampeders", "Saskatchewan Roughriders", "2026", "2026-05-18", "500"), "CFL 2017 Week 5 Saskatchewan Roughriders at Calgary Stampeders 720p EN 60fps" },
        { TeamEvent("CFL", "Football", "BC Lions vs Montreal Alouettes", "BC Lions", "Montreal Alouettes", "2026", "2026-09-12", "15", "2026-09-13"), "CFL Montreal Alouettes @ BC Lions 19/10" },
        { TeamEvent("CFL", "Football", "BC Lions vs Montreal Alouettes", "BC Lions", "Montreal Alouettes", "2026", "2026-09-12", "15", "2026-09-13"), "BC Lions at Montreal Alouettes 06/09/24" },
        { NamedEvent("CMLL", "Combat", "Sabados De Coliseo", "2026", "2026-01-03", "1"), "CMLL.2026.05.02.Sabados.De.Coliseo.SPA.1080p.H.264-SHAWN" },
        { NamedEvent("CMLL", "Combat", "Domingo Familiar", "2026", "2026-09-13"), "CMLL.2026.05.03.Domingo.Familiar.SPA.1080p.H.264-SHAWN" }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void UnprovenFamilyQueriesRemainDirectional(Event evt, string[] expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(ObservedWrongReleases))]
    public void ObservedWrongReleaseIsRejectedAcrossRoutes(Event evt, string title)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeFalse();
        score.Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeLessThan(50);
        libraryScore.Should().BeLessThan(40);
    }

    [Theory]
    [InlineData("CFL 2026 Preseason Week 1 Calgary Stampeders vs Saskatchewan Roughriders 2026.05.18")]
    [InlineData("CFL 2026 Preseason Round 1 Calgary Stampeders vs Saskatchewan Roughriders 2026.05.18")]
    public void NumberedPreseasonReleaseWithExactDateRemainsEligibleAcrossRoutes(string title)
    {
        var evt = TeamEvent(
            "CFL",
            "Football",
            "Calgary Stampeders vs Saskatchewan Roughriders",
            "Calgary Stampeders",
            "Saskatchewan Roughriders",
            "2026",
            "2026-05-18",
            "500");
        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        var (parsed, importTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        Math.Min(100, importMatch.Core + importMatch.TieBreak).Should().BeGreaterThanOrEqualTo(
            50,
            $"the import parse was title='{importTitle}', sport='{parsed.Sport}', organization='{parsed.Organization}', date='{parsed.EventDate:yyyy-MM-dd}', round='{parsed.RoundNumber}', core={importMatch.Core}, tie={importMatch.TieBreak}");
        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    [Theory]
    [InlineData("CFL BC Lions vs Montreal Alouettes 2026/9/12", true)]
    [InlineData("CFL BC Lions vs Montreal Alouettes 2026/9/11", false)]
    [InlineData("CFL BC Lions vs Montreal Alouettes 09/15/2026", false)]
    public void FullDateIdentifiesTheFrozenBroadcastDay(string title, bool shouldMatch)
    {
        var evt = TeamEvent(
            "CFL",
            "Football",
            "BC Lions vs Montreal Alouettes",
            "BC Lions",
            "Montreal Alouettes",
            "2026",
            "2026-09-12",
            "15",
            "2026-09-13");

        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().Be(shouldMatch);
        if (shouldMatch)
        {
            Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        }
        else
        {
            Scorer.CalculateMatchScore(title, evt).Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        }
    }

    [Theory]
    [InlineData("EPL 2026 15 01 Arsenal vs Chelsea 1080p", "2026-01-15")]
    [InlineData("CFL BC Lions vs Montreal Alouettes 09/15/2026", "2026-09-15")]
    public void ExistingSwappedDateFormatsRemainParsed(string title, string expectedDate)
    {
        var parsed = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);

        parsed.EventDate.Should().Be(DateTime.Parse(expectedDate, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("Dundee", "Dundee United", "Dundee United vs Celtic")]
    [InlineData("Mali", "Senegal", "Somalia vs Senegal")]
    [InlineData("England", "Germany", "England U21 vs Germany U21")]
    [InlineData("England", "Germany", "England Women vs Germany Women")]
    public void ImportBoostRequiresTwoDistinctParticipantMatches(
        string home,
        string away,
        string releaseTitle)
    {
        var evt = TeamEvent(
            "Fixture League",
            "Soccer",
            $"{home} vs {away}",
            home,
            away,
            "2026",
            "2026-05-18",
            "1");
        var parsed = new SportsParseResult
        {
            OriginalFilename = releaseTitle,
            EventDate = new DateTime(2026, 5, 18)
        };

        var match = ImportMatchingTestHarness.Service().ScoreMatch(
            releaseTitle, evt.Title, null, evt, parsed);

        Math.Min(100, match.Core + match.TieBreak).Should().BeLessThan(60);
    }

    [Fact]
    public void ImportKeepsAnUnloadedShortParticipantNameEligible()
    {
        var evt = TeamEvent(
            "Premier League",
            "Soccer",
            "Everton vs Tottenham Hotspur",
            "Everton",
            "Tottenham Hotspur",
            "2026",
            "2026-05-18",
            "1");
        const string releaseTitle = "Everton vs Tottenham";
        var parsed = new SportsParseResult
        {
            OriginalFilename = releaseTitle,
            EventDate = new DateTime(2026, 5, 18)
        };

        var match = ImportMatchingTestHarness.Service().ScoreMatch(
            releaseTitle, evt.Title, null, evt, parsed);

        Math.Min(100, match.Core + match.TieBreak).Should().BeGreaterThanOrEqualTo(50);
    }

    [Theory]
    [InlineData("Preseason Round 1 England U21 vs Germany U21 2026.05.18")]
    [InlineData("Preseason Round 1 England Women vs Germany Women 2026.05.18")]
    public void PreseasonImportBoostPreservesParticipantCategories(string releaseTitle)
    {
        var evt = TeamEvent(
            "International Friendlies",
            "Soccer",
            "England vs Germany",
            "England",
            "Germany",
            "2026",
            "2026-05-18",
            "500");
        var parsed = new SportsParseResult
        {
            OriginalFilename = releaseTitle,
            EventDate = new DateTime(2026, 5, 18),
            RoundNumber = 1
        };

        var match = ImportMatchingTestHarness.Service().ScoreMatch(
            releaseTitle, evt.Title, null, evt, parsed);

        Math.Min(100, match.Core + match.TieBreak).Should().BeLessThan(60);
    }

    [Theory]
    [InlineData("Preseason Round 1 England U21 vs Germany U21 2026.05.18")]
    [InlineData("Preseason Round 1 England Women vs Germany Women 2026.05.18")]
    public void PreseasonCategoryConflictIsRejectedAcrossRoutes(string title)
    {
        var evt = TeamEvent(
            "International Friendlies",
            "Soccer",
            "England vs Germany",
            "England",
            "Germany",
            "2026",
            "2026-05-18",
            "500");
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeFalse();
        score.Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeLessThan(50);
        libraryScore.Should().BeLessThan(40);
    }

    [Fact]
    public void PreseasonImportBoostRequiresPreseasonMetadata()
    {
        var evt = TeamEvent(
            "CFL",
            "Football",
            "Calgary Stampeders vs Saskatchewan Roughriders",
            "Calgary Stampeders",
            "Saskatchewan Roughriders",
            "2026",
            "2026-05-18",
            "1");
        const string releaseTitle = "Preseason Calgary Stampeders vs Saskatchewan Roughriders";
        var parsed = new SportsParseResult
        {
            OriginalFilename = releaseTitle,
            EventDate = new DateTime(2026, 5, 18)
        };

        var match = ImportMatchingTestHarness.Service().ScoreMatch(
            releaseTitle, evt.Title, null, evt, parsed);

        Math.Min(100, match.Core + match.TieBreak).Should().BeLessThan(90);
    }

    private static Event TeamEvent(
        string leagueName,
        string sport,
        string title,
        string home,
        string away,
        string season,
        string broadcastDate,
        string round,
        string? eventDate = null)
    {
        var evt = NamedEvent(leagueName, sport, title, season, broadcastDate, round);
        if (eventDate != null)
        {
            var parsedEventDate = DateTime.Parse(eventDate, System.Globalization.CultureInfo.InvariantCulture);
            evt.EventDate = DateTime.SpecifyKind(parsedEventDate.AddHours(12), DateTimeKind.Utc);
        }
        evt.HomeTeamId = 1;
        evt.AwayTeamId = 2;
        evt.HomeTeamName = home;
        evt.AwayTeamName = away;
        return evt;
    }

    private static Event NamedEvent(
        string leagueName,
        string sport,
        string title,
        string season,
        string broadcastDate,
        string? round = null)
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
            Round = round,
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
