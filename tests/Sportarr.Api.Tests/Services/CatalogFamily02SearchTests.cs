using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily02SearchTests
{
    private static readonly EventQueryService QueryService =
        new(NullLogger<EventQueryService>.Instance);

    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    private static readonly ReleaseMatchScorer Scorer = new();

    [Theory]
    [MemberData(nameof(QueryCases))]
    public void ObservedCatalogFamiliesUseOneSpecificQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return new object[]
        {
            TeamEvent(
                "All-Ireland Senior Football Championship",
                "Gaelic",
                "Kerry GAA Football",
                "Mayo GAA Football",
                "2026",
                "200",
                new DateTime(2026, 7, 26)),
            "GAA Football All Ireland 2026 Kerry Mayo"
        };
        yield return new object[]
        {
            TeamEvent(
                "EHF Champions League",
                "Handball",
                "Wisła Płock",
                "Sporting CP Handball",
                "2025-2026",
                "160",
                new DateTime(2026, 4, 9)),
            "EHF Champions League 2026 Wisla Plock Sporting CP"
        };
        yield return new object[]
        {
            TeamEvent(
                "EHF Champions League",
                "Handball",
                "SC Pick Szeged",
                "SC Magdeburg",
                "2025-2026",
                "125",
                new DateTime(2026, 4, 29)),
            "EHF Champions League 2026 Szeged Magdeburg"
        };
        yield return new object[]
        {
            TeamEvent(
                "World Mens Curling Championship",
                "Wintersports",
                "Sweden Curling",
                "Canada Curling",
                "2026",
                "200",
                new DateTime(2026, 4, 4)),
            "Curling World Championship 2026 Canada Sweden"
        };
        yield return new object[]
        {
            TeamEvent(
                "World Mens Curling Championship",
                "Wintersports",
                "Scotland Curling",
                "Canada Curling",
                "2026",
                "150",
                new DateTime(2026, 4, 3)),
            "Curling World Championship 2026 Scotland Canada"
        };
        yield return new object[]
        {
            TeamEvent(
                "World Mens Curling Championship",
                "Wintersports",
                "USA Curling",
                "Italy Curling",
                "2026",
                "7",
                new DateTime(2026, 4, 2)),
            "Curling World Championship 2026 USA Italy"
        };
        yield return new object[]
        {
            TeamEvent(
                "World Mens Curling Championship",
                "Wintersports",
                "Italy Curling",
                "Poland Curling",
                "2026",
                "5",
                new DateTime(2026, 3, 31)),
            "Curling World Championship 2026 Italy Poland"
        };
    }

    [Theory]
    [MemberData(nameof(ValidReleaseCases))]
    public void ObservedCatalogReleaseIsViableAcrossRoutes(Event evt, string title)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        parsed.EventTitle.Should().NotBeNull(
            "the production parser must preserve the observed event identity");
        validation.IsMatch.Should().BeTrue(
            "confidence was {0}; matches were {1}; rejections were {2}",
            validation.Confidence,
            string.Join(", ", validation.MatchReasons),
            string.Join(", ", validation.Rejections));
        validation.IsHardRejection.Should().BeFalse();
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    public static IEnumerable<object[]> ValidReleaseCases()
    {
        yield return new object[]
        {
            TeamEvent("All-Ireland Senior Football Championship", "Gaelic", "Kerry GAA Football", "Mayo GAA Football", "2026", "200", new DateTime(2026, 7, 26)),
            "2026 GAA Football All Ireland Senior Championship Final   Kerry vs Mayo (RTE2)"
        };
        yield return new object[]
        {
            TeamEvent("EHF Champions League", "Handball", "Wisła Płock", "Sporting CP Handball", "2025-2026", "160", new DateTime(2026, 4, 9)),
            "EHF Champions League 2026 Wisla Plock vs Sporting CP 09 04 720pEN50fps DAZN"
        };
        yield return new object[]
        {
            TeamEvent("EHF Champions League", "Handball", "SC Pick Szeged", "SC Magdeburg", "2025-2026", "125", new DateTime(2026, 4, 29)),
            "Handball EHF Championsh League 2026 Szeged vs Magdeburg 30 04 720pEN50fps DAZN"
        };
        yield return new object[]
        {
            TeamEvent("EHF Champions League", "Handball", "SC Pick Szeged", "SC Magdeburg", "2025-2026", "125", new DateTime(2026, 4, 29)),
            "Handball EHF Championsh League 2026 Szeged vs Magdeburg 30 04 2026 720pEN50fps DAZN"
        };
        yield return new object[]
        {
            TeamEvent("World Mens Curling Championship", "Wintersports", "Sweden Curling", "Canada Curling", "2026", "200", new DateTime(2026, 4, 4)),
            "Curling World Championships Final 2026 Canada vs Sweden 04 04 720pEN50fps ES"
        };
        yield return new object[]
        {
            TeamEvent("World Mens Curling Championship", "Wintersports", "Scotland Curling", "Canada Curling", "2026", "150", new DateTime(2026, 4, 3)),
            "Curling World Championships 2026 Semi Final Scotland vs Canada 04 04 720pEN50fps ES"
        };
        yield return new object[]
        {
            TeamEvent("World Mens Curling Championship", "Wintersports", "USA Curling", "Italy Curling", "2026", "7", new DateTime(2026, 4, 2)),
            "Curling World Championships 2026 USA vs Italy Round Robin 02 04 720pEN50fps ES"
        };
        yield return new object[]
        {
            TeamEvent("World Mens Curling Championship", "Wintersports", "Italy Curling", "Poland Curling", "2026", "5", new DateTime(2026, 3, 31)),
            "Curling World Championships 2026 RR Italy vs Poland 31 03 720pEN50fps ES"
        };
    }

    [Theory]
    [MemberData(nameof(WrongReleaseCases))]
    public void ObservedWrongEventIsRejectedAcrossRoutes(Event evt, string title)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeLessOrEqualTo(0);
        LibraryScore(title, importTitle, evt, parsed).Should().BeLessThan(40);
    }

    public static IEnumerable<object[]> WrongReleaseCases()
    {
        var swedenCanada = TeamEvent(
            "World Mens Curling Championship",
            "Wintersports",
            "Sweden Curling",
            "Canada Curling",
            "2026",
            "200",
            new DateTime(2026, 4, 4));
        yield return new object[] { swedenCanada, "Sochi.Winter.Olympics.2014.Curling.Womens.Gold.Medal.Game.Canada.Vs.Sweden.720p.HDTV.x264-W4F" };
        yield return new object[] { swedenCanada, "Winter.Olympics.2014.Curling.Mens.Round.Robin.Canada.vs.Sweden.HDTV.x264-2HD" };
        yield return new object[] { swedenCanada, "Winter Olympic Games Milano Cortina 2026 Women Curling Semi Final Canada vs Sweden 20 02 720pEN50fps ES" };
        yield return new object[] { swedenCanada, "Winter Olympic Games Milano Cortina 2026 Men Curling Canada vs Sweden Round Robin 13 02 720pEN50fps ES" };
        yield return new object[] { swedenCanada, "WInter Olympic Games Milano Cortina 2026 Curling Canada vs Sweden Mixed Doubles Round Robin 08 02 720pEN50fps ES" };
        yield return new object[] { swedenCanada, "Curling World Chapionship Final 2022 Canada vs Sweden Full Highlights 10 04 720pEN50fps ES" };
        yield return new object[] { swedenCanada, "Curling 2021 Mens World Championship Round Robin Canada vs Sweden 540p WEB DL h264 OsC TSN ENG" };

        var switzerlandUsaQualification = TeamEvent(
            "World Mens Curling Championship",
            "Wintersports",
            "Switzerland Curling",
            "USA Curling",
            "2026",
            "160",
            new DateTime(2026, 4, 3));
        yield return new object[]
        {
            switzerlandUsaQualification,
            "Curling World Championships 2026 RR Switzerland vs USA 02 04 720pEN50fps ES"
        };
    }

    [Fact]
    public void ObservedDateDriftDoesNotSkipFrozenCompetingFixture()
    {
        var qualification = TeamEvent(
            "World Mens Curling Championship",
            "Wintersports",
            "Switzerland Curling",
            "USA Curling",
            "2026",
            "160",
            new DateTime(2026, 4, 3));
        var roundRobin = TeamEvent(
            "World Mens Curling Championship",
            "Wintersports",
            "Switzerland Curling",
            "USA Curling",
            "2026",
            "7",
            new DateTime(2026, 4, 2));
        var title = "Curling World Championships 2026 RR Switzerland vs USA 02 04 720pEN50fps ES";

        var validation = Matcher.ValidateRelease(
            Release(title),
            qualification,
            datePeers: new[] { qualification, roundRobin });

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void ObservedDateDriftStillChecksAnExactDatePeer()
    {
        var target = TeamEvent(
            "EHF Champions League",
            "Handball",
            "SC Pick Szeged",
            "SC Magdeburg",
            "2025-2026",
            "125",
            new DateTime(2026, 4, 29));
        var peer = TeamEvent(
            "EHF Champions League",
            "Handball",
            "SC Pick Szeged",
            "SC Magdeburg",
            "2025-2026",
            "126",
            new DateTime(2026, 4, 30));
        var release = Release(
            "Handball EHF Championsh League 2026 Szeged vs Magdeburg 30 04 2026 720pEN50fps DAZN");

        var withoutPeers = Matcher.ValidateRelease(release, target);
        var withPeer = Matcher.ValidateRelease(release, target, datePeers: new[] { target, peer });

        withoutPeers.IsMatch.Should().BeTrue();
        withoutPeers.IsHardRejection.Should().BeFalse();
        withPeer.IsMatch.Should().BeFalse();
        withPeer.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void YearOnlyEHFTeamPairCannotIdentifyEitherLeg()
    {
        var first = TeamEvent(
            "EHF Champions League", "Handball", "SC Pick Szeged", "SC Magdeburg",
            "2025-2026", "125", new DateTime(2026, 4, 29));
        var second = TeamEvent(
            "EHF Champions League", "Handball", "SC Magdeburg", "SC Pick Szeged",
            "2025-2026", "125", new DateTime(2026, 5, 7));
        var title = "EHF Champions League 2026 Szeged vs Magdeburg 1080p";

        foreach (var evt in new[] { first, second })
        {
            Scorer.CalculateMatchScore(title, evt)
                .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
            Matcher.ValidateRelease(Release(title), evt, datePeers: new[] { first, second })
                .IsMatch.Should().BeFalse();
        }
    }

    [Fact]
    public void EHFWrittenMonthDateRemainsViableAcrossRoutes()
    {
        var evt = TeamEvent("EHF Champions League", "Handball", "Wisła Płock", "Sporting CP Handball",
            "2025-2026", "160", new DateTime(2026, 4, 9));
        var title = "EHF Champions League 2026 Wisla Plock vs Sporting CP 9 April 2026 720p";
        var parsed = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);

        parsed.EventDate.Should().Be(new DateTime(2026, 4, 9));
        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(
            ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("11 Sep 2025")]
    [InlineData("11 Sept 2025")]
    [InlineData("11 September 2025")]
    [InlineData("Sep 11, 2025")]
    [InlineData("Sept 11, 2025")]
    [InlineData("September 11, 2025")]
    public void EHFSeptemberMonthNamesRemainViable(string writtenDate)
    {
        var evt = TeamEvent("EHF Champions League", "Handball", "SC Pick Szeged", "Wisła Płock",
            "2025-2026", "1", new DateTime(2025, 9, 11));
        var title = $"EHF Champions League 2025 Szeged vs Wisla Plock {writtenDate} 720p";
        var parsed = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);

        parsed.EventDate.Should().Be(new DateTime(2025, 9, 11));
        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(
            ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void EHFLibraryEpisodeIdentifiesItsDatedFixtureWithoutACalendarDate()
    {
        var evt = TeamEvent("EHF Champions League", "Handball", "SC Pick Szeged", "Wisła Płock",
            "2025-2026", "1", new DateTime(2025, 9, 11));
        evt.SeasonNumber = 2025;
        evt.EpisodeNumber = 1;
        var title = "EHF Champions League - S2025E01 - SC Pick Szeged vs Wisła Płock - 1080p";

        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(
            ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void EHFLibraryEpisodeUsesSeasonYearForANextYearFixture()
    {
        var evt = TeamEvent("EHF Champions League", "Handball", "Wisła Płock", "Sporting CP Handball",
            "2025-2026", "160", new DateTime(2026, 4, 9));
        evt.SeasonNumber = 2025;
        evt.EpisodeNumber = 100;
        var title = "EHF Champions League - S2025E100 - Wisła Płock vs Sporting CP Handball - 1080p";

        var match = Matcher.ValidateRelease(Release(title), evt);
        match.IsMatch.Should().BeTrue(string.Join("; ", match.Rejections));
        Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(
            ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void EHFLibraryEpisodeWithAConflictingDateCannotMatch()
    {
        var evt = TeamEvent("EHF Champions League", "Handball", "Wisła Płock", "Sporting CP Handball",
            "2025-2026", "160", new DateTime(2026, 4, 9));
        evt.SeasonNumber = 2025;
        evt.EpisodeNumber = 100;
        var title = "EHF Champions League - S2025E100 - Wisła Płock vs Sporting CP Handball - 1080p 16 04 2026";

        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeFalse();
        Scorer.CalculateMatchScore(title, evt).Should().BeLessThan(
            ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void EHFDateAfterQualityStillIdentifiesItsFixture()
    {
        var evt = TeamEvent("EHF Champions League", "Handball", "Wisła Płock", "Sporting CP Handball",
            "2025-2026", "160", new DateTime(2026, 4, 9));
        var title = "EHF Champions League 2026 Wisla Plock vs Sporting CP 720p DAZN 09 04 2026";

        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(
            ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void EHFSeasonSpanTitleReachesTheNextYearFixtureAcrossRoutes()
    {
        var evt = TeamEvent("EHF Champions League", "Handball", "Wisła Płock", "Sporting CP Handball",
            "2025-2026", "160", new DateTime(2026, 4, 9));
        var title = "EHF Champions League 2025-2026 Wisla Plock vs Sporting CP 720p DAZN 09 04";
        var (parsed, importTitle) = ParseForImport(title);

        parsed.EventDate.Should().Be(new DateTime(2026, 4, 9));
        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(
            ReleaseMatchScorer.AutoGrabMatchScore);
        ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core
            .Should().BeGreaterThanOrEqualTo(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void EHFShortSeasonSpanReachesTheNextYearFixtureAcrossRoutes()
    {
        var evt = TeamEvent("EHF Champions League", "Handball", "Wisła Płock", "Sporting CP Handball",
            "2025-2026", "160", new DateTime(2026, 4, 9));
        var title = "EHF Champions League 2025-26 Wisla Plock vs Sporting CP 09 04 720p";
        var (parsed, importTitle) = ParseForImport(title);

        parsed.EventDate.Should().Be(new DateTime(2026, 4, 9));
        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(
            ReleaseMatchScorer.AutoGrabMatchScore);
        ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core
            .Should().BeGreaterThanOrEqualTo(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void EHFSeasonStartYearWithWrittenNextYearDateRemainsViable()
    {
        var evt = TeamEvent("EHF Champions League", "Handball", "Wisła Płock", "Sporting CP Handball",
            "2025-2026", "160", new DateTime(2026, 4, 9));
        var title = "EHF Champions League 2025 Wisla Plock vs Sporting CP 9 April 2026 720p";
        var parsed = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);

        parsed.EventDate.Should().Be(new DateTime(2026, 4, 9));
        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(
            ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void EHFLibraryEpisodeWithConflictingTrailingDayMonthCannotScore()
    {
        var evt = TeamEvent("EHF Champions League", "Handball", "Wisła Płock", "Sporting CP Handball",
            "2025-2026", "160", new DateTime(2026, 4, 9));
        evt.EpisodeNumber = 100;
        var title = "EHF Champions League - S2025E100 - Wisła Płock vs Sporting CP Handball - 1080p DAZN 16 04";

        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeFalse();
        Scorer.CalculateMatchScore(title, evt).Should().BeLessThan(
            ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void EHFLibraryEpisodeDayMonthAfterQualityUsesTheFixtureYearAcrossRoutes()
    {
        var evt = TeamEvent("EHF Champions League", "Handball", "Wisła Płock", "Sporting CP Handball",
            "2025-2026", "160", new DateTime(2026, 4, 9));
        evt.SeasonNumber = 2025;
        evt.EpisodeNumber = 100;
        var title = "EHF Champions League - S2025E100 - Wisła Płock vs Sporting CP Handball - 1080p DAZN 09 04";
        var (parsed, importTitle) = ParseForImport(title);

        parsed.EventDate.Should().Be(new DateTime(2026, 4, 9));
        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(
            ReleaseMatchScorer.AutoGrabMatchScore);
        ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core
            .Should().BeGreaterThanOrEqualTo(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    private static Event TeamEvent(
        string league,
        string sport,
        string home,
        string away,
        string season,
        string round,
        DateTime date) => new()
    {
        Title = $"{home} vs {away}",
        Sport = sport,
        Season = season,
        SeasonNumber = int.TryParse(season[..4], out var seasonYear) ? seasonYear : null,
        Round = round,
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = league, Sport = sport }
    };

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

    private static int LibraryScore(
        string sourceTitle,
        string eventTitle,
        Event evt,
        SportsParseResult parsed) => LibraryImportService.CalculateMatchConfidence(
            eventTitle,
            evt.Title,
            parsed.Organization,
            evt,
            parsed.EventDate,
            parsed.EventYear,
            parsed.RoundNumber,
            parsed.SeasonYearEnd,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport,
            sourceTitle: sourceTitle);
}
