using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily47SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static TheoryData<Event, string> FrozenEvents => new()
    {
        { TeamEvent("CONCACAF Nations League", "Anguilla vs Turks and Caicos Islands", "Anguilla", "Turks and Caicos Islands", "2024-2025", "2024-09-04", "1"), "Anguilla Turks and Caicos Islands" },
        { TeamEvent("CONCACAF Nations League", "Mexico vs Panama", "Mexico", "Panama", "2024-2025", "2025-03-23", "200", "2025-03-24"), "Mexico Panama" },
        { TeamEvent("CONCACAF Series", "St Martin vs Barbados", "St Martin", "Barbados", "2026-2027", "2026-03-26", null), "Barbados St Martin" },
        { TeamEvent("CONCACAF Series", "Dominica vs St Maarten", "Dominica", "St Maarten", "2026-2027", "2026-03-30", null), "Dominica St Maarten" },
        { TeamEvent("CONCACAF W Champions Cup", "Alianza Women vs Washington Spirit", "Alianza Women", "Washington Spirit", "2025-2026", "2025-08-20", "1"), "Alianza Women Washington Spirit" },
        { TeamEvent("CONCACAF W Champions Cup", "América Femenil vs Washington Spirit", "CF América Femenil", "Washington Spirit", "2025-2026", "2026-05-23", "200", "2026-05-24"), "CF America Femenil Washington Spirit" },
        { TeamEvent("CONCACAF W Gold Cup", "Mexico Women vs Argentina Women", "Mexico Women", "Argentina Women", "2024", "2024-02-21", "1"), "Argentina Women Mexico Women" },
        { TeamEvent("CONCACAF W Gold Cup", "USA Women vs Brazil Women", "USA Women", "Brazil Women", "2024", "2024-03-11", "200"), "Brazil Women USA Women" },
        { TeamEvent("CONMEBOL Liga de Naciones Femenina", "Venezuela Women vs Chile Women", "Venezuela Women", "Chile Women", "2025-2026", "2025-10-24", "1"), "Chile Women Venezuela Women" },
        { TeamEvent("CONMEBOL Liga de Naciones Femenina", "Ecuador Women vs Argentina Women", "Ecuador Women", "Argentina Women", "2025-2026", "2026-06-09", "9"), "Argentina Women Ecuador Women" }
    };

    public static TheoryData<Event, string> ValidReleases => new()
    {
        {
            TeamEvent("CONCACAF Nations League", "Anguilla vs Turks and Caicos Islands", "Anguilla", "Turks and Caicos Islands", "2024-2025", "2024-09-04", "1"),
            "CONCACAF.Nations.League.2024.09.04.Anguilla.vs.Turks.and.Caicos.Islands.720p.WEB.h264-ULTRAS"
        },
        {
            TeamEvent("CONCACAF Nations League", "Mexico vs Panama", "Mexico", "Panama", "2024-2025", "2025-03-23", "200", "2025-03-24"),
            "Concacaf Nations League Finals Final Mexico vs Panama 23/03/2025 Concacaf TV (English) 1080p30fps H264 BlackDeviL WebDL"
        },
        {
            TeamEvent("CONCACAF W Gold Cup", "USA Women vs Brazil Women", "USA Women", "Brazil Women", "2024", "2024-03-11", "200"),
            "CONCACAF Gold Cup Final USA Womens v Brazil Womens Full Match Replay 10/03/2024 1080pEng30fps"
        },
        {
            TeamEvent("CONCACAF Series", "St Martin vs Barbados", "St Martin", "Barbados", "2026-2027", "2026-03-26", null),
            "CONCACAF Series 2026 St Martin vs Barbados 26 03 1080p"
        },
        {
            TeamEvent("CONCACAF W Champions Cup", "Alianza Women vs Washington Spirit", "Alianza Women", "Washington Spirit", "2025-2026", "2025-08-20", "1"),
            "CONCACAF Womens Champions Cup 2025 Alianza Women vs Washington Spirit 20 08 1080p"
        },
        {
            TeamEvent("CONCACAF W Champions Cup", "América Femenil vs Washington Spirit", "CF América Femenil", "Washington Spirit", "2025-2026", "2026-05-23", "200", "2026-05-24"),
            "CONCACAF Womens Champions Cup Final CF América Femenil vs Washington Spirit 23/05/2026 1080p"
        },
        {
            TeamEvent("CONMEBOL Liga de Naciones Femenina", "Venezuela Women vs Chile Women", "Venezuela Women", "Chile Women", "2025-2026", "2025-10-24", "1"),
            "CONMEBOL Liga de Naciones Femenina 2025 Venezuela Women vs Chile Women 24 10 1080p"
        }
    };

    public static TheoryData<Event, string> ObservedWrongReleases => new()
    {
        { NationsFinal(), "CONCACAF Gold Cup 2013 Mexico vs. Panama 1080i MPA 2.0 H264-TrollHD" },
        { NationsFinal(), "CONCACAF Gold Cup 2023 07 16 Final Mexico vs Panama including Trophy Celebration 720p x264 AAC EN PREMIER" },
        { NationsFinal(), "Concacaf Gold Cup 2023 Mexico vs Panama 16 07 720pEN60fps FS1" },
        { NationsFinal(), "CONCACAF Gold Cup Final 2023 16 07 Mexico vs Panama 720pEN60fps FS1" },
        { NationsFinal(), "Concacaf Nations League Semifinal Panama vs Mexico Full Match Reply 21/03/2024 1080pEng30fps" },
        { NationsFinal(), "Concacaf U20 Championships 2026 QF Mexico vs Panama 05 08 720pEN60fps FS2" },
        { NationsFinal(), "CONCACAF World Cup Qualifying Final Round 2022 02 02 Mexico vs Panama International broadcast unedited ENG 720p60 H264" },
        { NationsFinal(), "CONCACAF World Cup Qualifying Final Round 2022 02 02 Mexico vs Panama USA broadcast unedited SPA 1080p60 H264" },
        { NationsFinal(), "Deadly.60.On.A.Mission.S01E03.Mexico.And.Panama.1080p.WEB-DL.DDP2.0.H.264-squalor" },
        { NationsFinal(), "FIBA World Cup Quali 2019 Panama vs Mexico 25/02 720pEN60fps" },
        { NationsFinal(), "WCQ CONCACAF 2021 09 08 Panama vs Mexico SPANISH 1080p HDTV x264 Telemundo" },
        { WomensGoldFinal(), "FIBA Women's AmeriCup 2019 2019 Brazil vs USA 25/09 720pEN60fps" },
        { WomensGoldFinal(), "FIBA Women's Americup 2021 Semi Final Brazil vs USA 18/06 720pEN60fps" },
        { WomensGoldFinal(), "FIBA Womens Nations League 2019 Brazil vs USA 06/06 720pEN60fps NBCSN" },
        { WomensGoldFinal(), "FIFA U20 Women's World Cup 2026 R16 Brazil vs USA 15 09 720pEN60fps FS2" },
        { WomensGoldFinal(), "Olympic Games Paris 2024 Women's Rugby Seven USA vs Brazil 28 07 720pEN25fps EuroSport" },
        { WomensGoldFinal(), "Olympic Games Rio 2016 Women's Beach Volleyball 3rd/4th place game Brazil vs USA 17/08 576p CZE 25fps" },
        { WomensGoldFinal(), "Olympic Games Rio 2016 Women's Beachvolleyball Semifinal Brazil vs USA 16/08 720p Eng 30fps" },
        { WomensGoldFinal(), "Pan Americans Games 2019 Women's HandBall Brazil vs USA 29/07 720pSPA60fps" },
        { WomensGoldFinal(), "Tokyo Olympics 2020 Women's Beach Volley USA vs Brazil 31 07 720pEN25fps ES" },
        { WomensGoldFinal(), "Tokyo Olympics 2020 Women's Volleyball Final Brazil vs USA 08 08 720pEN50fps ES" },
        { WomensGoldFinal(), "Tokyo.Olympics.2020.2021.08.08.Womens.Volleyball.Gold.Medal.Match.Brazil.Vs.USA.1080p.WEB.H264-DARKSPORT" },
        { WomensGoldFinal(), "Tokyo.Olympics.2020.2021.08.08.Womens.Volleyball.Gold.Medal.Match.Brazil.Vs.USA.480p.x264-mSD" },
        { WomensGoldFinal(), "Tokyo.Olympics.2020.2021.08.08.Womens.Volleyball.Gold.Medal.Match.Brazil.Vs.USA.720p.WEB.H264-DARKSPORT" },
        { WomensGoldFinal(), "Tokyo.Olympics.2020.2021.08.08.Womens.Volleyball.Gold.Medal.Match.Brazil.Vs.USA.XviD-AFG" },
        { WomensGoldFinal(), "USA USWNT vs Brazil 2007 Women's World Cup Full Game ESPN 360p mp4" },
        { WomensGoldFinal(), "USA USWNT vs Brazil 2011 Women's World Cup Quarterfinal Full Game FIFA FEED FOX SPORTS 720p mp4" },
        { WomensGoldFinal(), "Women's Football Gold Medal Brazil v USA 10/08/2024 Discovery+ 1080p50fps BlackDevil MultiLang" },
        { WomensGoldFinal(), "Women's Intenational Soccer 2025 USA vs Brazil 08 04 720pSPA30fps Universo" },
        { WomensGoldFinal(), "Women's International Friendly 2026 Brazil vs USA 07 06 720pSPA30fps PCenVivo" },
        { WomensGoldFinal(), "Women's Nations League Volleyabll 2025 USA vs Brazil 05 06 720pEN60fps BIGN" },
        { WomensGoldFinal(), "World Series of Beach Vollyeball 2013 Womens Gold Medal Match USA vs Brazil 1080i HDTV MPA 2.0 H264-TrollHD" },
        {
            TeamEvent("CONCACAF W Gold Cup", "Mexico Women vs Argentina Women", "Mexico Women", "Argentina Women", "2024", "2024-02-21", "1"),
            "FIFA U20 Women's World Cup 2026 Argentina vs Mexico 11 09 720pEN60fps FS2"
        },
        {
            TeamEvent("CONMEBOL Liga de Naciones Femenina", "Ecuador Women vs Argentina Women", "Ecuador Women", "Argentina Women", "2025-2026", "2026-06-09", "9"),
            "Women's Copa America 2025 Ecuador vs Argentina 24 07 720pEN60fps FS1"
        },
        {
            WomensGoldFinal(),
            "CONCACAF Gold Cup Final USA Womens v Brazil Womens Full Match Replay 12/03/2024 1080pEng30fps"
        },
        {
            ConmebolLibraryEvent(),
            "CONMEBOL Liga de Naciones Femenina S2025E1 Venezuela Women vs Chile Women 2025.11.24 1080p"
        }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void VerifiedFamilyUsesOneOrderIndependentParticipantQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(ValidReleases))]
    public void ValidReleaseIsAcceptedAcrossRoutes(Event evt, string title)
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
        importScore.Should().BeGreaterThanOrEqualTo(50);
        libraryScore.Should().BeGreaterThanOrEqualTo(40);
    }

    [Theory]
    [MemberData(nameof(ObservedWrongReleases))]
    public void ObservedWrongReleaseIsRejectedAcrossRoutes(Event evt, string title)
    {
        AssertRejectedAcrossRoutes(evt, title);
    }

    private static Event NationsFinal() => TeamEvent(
        "CONCACAF Nations League",
        "Mexico vs Panama",
        "Mexico",
        "Panama",
        "2024-2025",
        "2025-03-23",
        "200",
        "2025-03-24");

    private static Event WomensGoldFinal() => TeamEvent(
        "CONCACAF W Gold Cup",
        "USA Women vs Brazil Women",
        "USA Women",
        "Brazil Women",
        "2024",
        "2024-03-11",
        "200");

    private static Event ConmebolLibraryEvent()
    {
        var evt = TeamEvent(
            "CONMEBOL Liga de Naciones Femenina",
            "Venezuela Women vs Chile Women",
            "Venezuela Women",
            "Chile Women",
            "2025-2026",
            "2025-10-24",
            "1");
        evt.SeasonNumber = 2025;
        evt.EpisodeNumber = 1;
        return evt;
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
