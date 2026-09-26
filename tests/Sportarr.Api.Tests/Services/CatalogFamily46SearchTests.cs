using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily46SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static TheoryData<Event, string> FrozenEvents => new()
    {
        { TeamEvent("CONCACAF Caribbean Cup", "Portmore United vs Salcedo", "Portmore United", "Salcedo", "2026", "2026-08-04", "1"), "Portmore United Salcedo" },
        { TeamEvent("CONCACAF Caribbean Cup", "Salcedo vs Cibao", "Salcedo", "Cibao", "2026", "2026-09-10", "4"), "Cibao Salcedo" },
        { TeamEvent("CONCACAF Central American Cup", "Verdes vs Motagua", "Verdes", "Motagua", "2026", "2026-07-28", "1", "2026-07-29"), "Motagua Verdes" },
        { TeamEvent("CONCACAF Central American Cup", "Firpo vs CD Olimpia", "Firpo", "CD Olimpia", "2026", "2026-09-10", null, "2026-09-11"), "CD Olimpia Firpo" },
        { TeamEvent("CONCACAF Champions Cup", "Forge vs Tigres UANL", "Forge", "Tigres", "2026", "2026-02-03", "32", "2026-02-04"), "Forge Tigres" },
        { TeamEvent("CONCACAF Champions Cup", "Toluca vs Tigres UANL", "Toluca", "Tigres", "2026", "2026-05-30", "200", "2026-05-31"), "Tigres Toluca" },
        { TeamEvent("CONCACAF Gold Cup", "Mexico vs Dominican Republic", "Mexico", "Dominican Republic", "2025", "2025-06-14", "1", "2025-06-15"), "Dominican Republic Mexico" },
        { TeamEvent("CONCACAF Gold Cup", "USA vs Mexico", "USA", "Mexico", "2025", "2025-07-06", "200"), "Mexico USA" },
        { TeamEvent("CONCACAF Gold Cup Qualifying", "Cuba vs Trinidad and Tobago", "Cuba", "Trinidad and Tobago", "2025", "2025-03-21", "2025"), "Cuba Trinidad and Tobago" },
        { TeamEvent("CONCACAF Gold Cup Qualifying", "Honduras vs Bermuda", "Honduras", "Bermuda", "2025", "2025-03-26", "2025"), "Bermuda Honduras" }
    };

    public static TheoryData<Event, string> ObservedValidReleases => new()
    {
        { TeamEvent("CONCACAF Caribbean Cup", "Portmore United vs Salcedo", "Portmore United", "Salcedo", "2026", "2026-08-04", "1"), "CONCACAF Caribbean Cup 2026 Portmore United FC vs Salcedo FC 04 08 1080p60fps EN Paramount" },
        { TeamEvent("CONCACAF Caribbean Cup", "Salcedo vs Cibao", "Salcedo", "Cibao", "2026", "2026-09-10", "4"), "CONCACAF Caribbean Cup 2026 Salcedo FC vs Cibao FC 10 09 1080p60fps EN Paramount" },
        { TeamEvent("CONCACAF Central American Cup", "Firpo vs CD Olimpia", "Firpo", "CD Olimpia", "2026", "2026-09-10", null, "2026-09-11"), "CONCACAF Central American Cup 2026 CD Luis Ángel Firpo vs CD Olimpia 11 09 1080p60fps EN Paramount" },
        { TeamEvent("CONCACAF Champions Cup", "Toluca vs Tigres UANL", "Toluca", "Tigres", "2026", "2026-05-30", "200", "2026-05-31"), "Concacaf Champions Cup FINAL: Deportivo Toluca FC (MEX) vs Tigres UANL (MEX) 1080p" },
        { TeamEvent("CONCACAF Champions Cup", "Toluca vs Tigres UANL", "Toluca", "Tigres", "2026", "2026-05-30", "200", "2026-05-31"), "Concacaf Champions Cup FINAL: Deportivo Toluca FC (MEX) vs Tigres UANL (MEX) 1080p DD5.1" },
        { TeamEvent("CONCACAF Champions Cup", "Toluca vs Tigres UANL", "Toluca", "Tigres", "2026", "2026-05-30", "200", "2026-05-31"), "Concacaf Champions Cup FINAL: Deportivo Toluca FC (MEX) vs Tigres UANL (MEX) 1080p DD.5.1" },
        { TeamEvent("CONCACAF Champions Cup", "Toluca vs Tigres UANL", "Toluca", "Tigres", "2026", "2026-05-30", "200", "2026-05-31"), "Concacaf Champions Cup FINAL: Deportivo Toluca FC (MEX) vs Tigres UANL (MEX) 1080p DD 5.1" },
        { TeamEvent("CONCACAF Champions Cup", "Toluca vs Tigres UANL", "Toluca", "Tigres", "2026", "2026-05-30", "200", "2026-05-31"), "Concacaf Champions Cup FINAL: Deportivo Toluca FC (MEX) vs Tigres UANL (MEX) 1080p AAC.5.1" },
        { TeamEvent("CONCACAF Champions Cup", "Toluca vs Tigres UANL", "Toluca", "Tigres", "2026", "2026-05-30", "200", "2026-05-31"), "Concacaf Champions Cup FINAL: Deportivo Toluca FC (MEX) vs Tigres UANL (MEX) 1080p DD5.1 60fps" },
        { TeamEvent("CONCACAF Champions Cup", "Toluca vs Tigres UANL", "Toluca", "Tigres", "2026", "2026-05-30", "200", "2026-05-31"), "Concacaf Champions Cup FINAL: Deportivo Toluca FC (MEX) vs Tigres UANL (MEX) 1080p DTS-HD/MA 5.1" },
        { TeamEvent("CONCACAF Champions Cup", "Toluca vs Tigres UANL", "Toluca", "Tigres", "2026", "2026-05-30", "200", "2026-05-31"), "Concacaf Champions Cup Final 2026 Toluca vs Tigres 30 05 720pEN60fps FS1" },
        { TeamEvent("CONCACAF Gold Cup", "Mexico vs Dominican Republic", "Mexico", "Dominican Republic", "2025", "2025-06-14", "1", "2025-06-15"), "Concacaf Gold Cup 2025 Mexico vs Dominican Republic 14 06 1080pEN25fps WEB DL" },
        { TeamEvent("CONCACAF Gold Cup", "USA vs Mexico", "USA", "Mexico", "2025", "2025-07-06", "200"), "Concacaf Gold Cup Final 2025 USA vs Mexico 06 07 720pEN60fps Fox" },
        { TeamEvent("CONCACAF Gold Cup", "USA vs Mexico", "USA", "Mexico", "2025", "2025-07-06", "200"), "CONCACAF Gold Cup 2025 07 06 Final USA vs Mexico inc Trophy Presentation 1080p EN TSN" },
        { TeamEvent("CONCACAF Gold Cup Qualifying", "Honduras vs Bermuda", "Honduras", "Bermuda", "2025", "2025-03-26", "2025"), "Concacaf Gold Cup Prelims 2025 Honduras vs Bermuda 25 03 720pEN60fps FS2" },
        { TeamEvent("CONCACAF Gold Cup", "USA vs Mexico", "USA", "Mexico", "2025", "2025-07-06", "200"), "CONCACAF Gold Cup 2025 Final USA v Mexico FOX 480p H264 English IMD" }
    };

    public static TheoryData<string> CandidateOnlyWrongReleases => new()
    {
        { "Cruising.with.Susan.Calman.S03E01.Mexico.and.USA.720p.MY5.WEB-DL.AAC2.0.H.264-HiNGS" },
        { "Cruising.with.Susan.Calman.S03E01.Mexico.and.USA.1080p.MY5.WEB-DL.AAC2.0.H.264-HiNGS" },
        { "ITV.On.Assignment.2023.USA.Mexico.and.France.1080p.HDTV.x265.AAC.MVGroup.org" },
        { "Full.Circle.with.Michael.Palin-S01E10MexicoandWestern.USAandAlaska-1997.688x512.AC3" },
        { "S01E05 Mexico and USA 1080i nl subs" },
        { "U 20 Women's World Cup Mexico v USA Round of 16 11/09/2024 FIFA+ (English) 1080p50fps BlackDevil" },
        { "USA USMNT @ Mexico 2013 2014 World Cup Qualifying full game English 720p" },
        { "FIFA 2018 WCQ 2016 11 11 usa mexico spa 720p mkv" }
    };

    public static TheoryData<string> RetainedWrongReleases => new()
    {
        { "CONCACAF Nations League 2024 03 24 Mexico vs USA 720p x264 AAC SPANISH TUDN" },
        { "Concacaf Nations League Championship 2021 USA vs Mexico 06/06 576pEN60fps CBSS" },
        { "Concacaf U20 Championshp F 2026 USA vs Mexico 09 08 720pEN60fps FS2" },
        { "FIBA Basketball World Cup Qualifiers 2026 USA vs Mexico 01 03 1080pEN25fps" },
        { "Women's International Soccer 2024 Mexico vs USA 13 07 720pSPA30fps NBC" },
        { "Volleyball WC 2022 08 26 Mexico vs USA 720p 25fps ENG" },
        { "Wolrd Baseball Classic 2023 Mexico vs USA 12 03 720pEN60fps FS1" }
    };

    public static TheoryData<Event, string> AdversarialWrongReleases => new()
    {
        {
            TeamEvent("CONCACAF Gold Cup", "USA vs Mexico", "USA", "Mexico", "2025", "2025-07-06", "200"),
            "CONCACAF Gold Cup Final 2025 USA vs Mexico 2025.06.14"
        },
        {
            TeamEvent("CONCACAF Central American Cup", "Firpo vs CD Olimpia", "Firpo", "CD Olimpia", "2026", "2026-09-10", null, "2026-09-11"),
            "CONCACAF Central American Cup 2026.10.09 CD Luis Ángel Firpo vs CD Olimpia 1080p"
        },
        {
            TeamEvent("CONCACAF Caribbean Cup", "Salcedo vs Cibao", "Salcedo", "Cibao", "2026", "2026-09-10", "4"),
            "CONCACAF Caribbean Cup Salcedo FC vs Cibao FC 10/09/24"
        }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void VerifiedConcacafFamilyUsesOneOrderIndependentParticipantQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(ObservedValidReleases))]
    public void ObservedValidReleaseIsAcceptedAcrossRoutes(Event evt, string title)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeTrue(
            "confidence was {0}; matches were {1}; rejections were {2}",
            validation.Confidence,
            string.Join(", ", validation.MatchReasons),
            string.Join(", ", validation.Rejections));
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(
            50,
            $"the import parse was title='{importTitle}', sport='{parsed.Sport}', organization='{parsed.Organization}', date='{parsed.EventDate:yyyy-MM-dd}', round='{parsed.RoundNumber}', core={importMatch.Core}, tie={importMatch.TieBreak}");
        libraryScore.Should().BeGreaterThanOrEqualTo(40);
    }

    [Theory]
    [MemberData(nameof(CandidateOnlyWrongReleases))]
    [MemberData(nameof(RetainedWrongReleases))]
    public void ObservedWrongReleaseIsRejectedAcrossRoutes(string title)
    {
        var evt = TeamEvent(
            "CONCACAF Gold Cup",
            "USA vs Mexico",
            "USA",
            "Mexico",
            "2025",
            "2025-07-06",
            "200");
        AssertRejectedAcrossRoutes(evt, title);
    }

    [Theory]
    [MemberData(nameof(AdversarialWrongReleases))]
    public void ExplicitWrongDateIsRejectedAcrossRoutes(Event evt, string title)
    {
        AssertRejectedAcrossRoutes(evt, title);
    }

    private static void AssertRejectedAcrossRoutes(Event evt, string title)
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

    private static Event TeamEvent(
        string leagueName,
        string title,
        string home,
        string away,
        string season,
        string broadcastDate,
        string? round,
        string? eventDate = null)
    {
        var localDate = DateTime.Parse(broadcastDate, System.Globalization.CultureInfo.InvariantCulture);
        var utcDate = eventDate == null
            ? localDate
            : DateTime.Parse(eventDate, System.Globalization.CultureInfo.InvariantCulture);
        return new Event
        {
            Title = title,
            Sport = "Soccer",
            Season = season,
            EventDate = DateTime.SpecifyKind(utcDate.AddHours(12), DateTimeKind.Utc),
            BroadcastDate = localDate,
            BroadcastDateVerified = true,
            Round = round,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = home,
            AwayTeamName = away,
            League = new League { Name = leagueName, Sport = "Soccer" }
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
