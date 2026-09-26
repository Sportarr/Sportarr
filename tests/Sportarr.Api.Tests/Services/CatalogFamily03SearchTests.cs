using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily03SearchTests
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
    public void ObservedSupercrossEventsUseOneSpecificQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return new object[]
        {
            SupercrossEvent("Anaheim 1", "1", new DateTime(2022, 1, 8)),
            "AMA Supercross 2022 Anaheim 1"
        };
        yield return new object[]
        {
            SupercrossEvent("Salt Lake City", "17", new DateTime(2022, 5, 7), "Rice-Eccles Stadium"),
            "AMA Supercross 2022 Salt Lake City"
        };
    }

    [Theory]
    [InlineData(
        "2022 AMA Supercross Rd 1 Anaheim 1 1080p x265 SlickNick0610",
        "Anaheim 1",
        1)]
    [InlineData(
        "2022 AMA Supercross Rd 17 Salt Lake City 1080p x265 SlickNick0610",
        "Salt Lake City",
        17)]
    public void ObservedSupercrossReleaseParsesEventIdentity(
        string title,
        string expectedLocation,
        int expectedRound)
    {
        var parsed = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);

        parsed.Sport.Should().Be("Motorsport");
        parsed.Organization.Should().Be("AMA Supercross");
        parsed.EventTitle.Should().Be(expectedLocation);
        parsed.EventYear.Should().Be(2022);
        parsed.RoundNumber.Should().Be(expectedRound);
        parsed.Location.Should().Be(expectedLocation);
        parsed.Confidence.Should().BeGreaterThanOrEqualTo(60);
    }

    [Theory]
    [MemberData(nameof(ValidReleaseCases))]
    public void ObservedSupercrossReleaseIsViableAcrossRoutes(Event evt, string title)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

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
            SupercrossEvent("Anaheim 1", "1", new DateTime(2022, 1, 8)),
            "2022 AMA Supercross Rd 1 Anaheim 1 1080p x265 SlickNick0610"
        };
        yield return new object[]
        {
            SupercrossEvent("Salt Lake City", "17", new DateTime(2022, 5, 7), "Rice-Eccles Stadium"),
            "2022 AMA Supercross Rd 17 Salt Lake City 1080p x265 SlickNick0610"
        };
    }

    [Theory]
    [InlineData("2022 AMA Supercross Rd 6 Anaheim 3 1080p x265 SlickNick0610")]
    [InlineData("2022 AMA Supercross Rd 4 Anaheim 2 1080p x265 SlickNick0610")]
    public void ObservedWrongSupercrossRoundIsRejectedAcrossRoutes(string title)
    {
        var evt = SupercrossEvent("Anaheim 1", "1", new DateTime(2022, 1, 8));
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

    [Theory]
    [InlineData("2022 AMA Supercross Rd 1 San Diego 1080p x265 SlickNick0610")]
    [InlineData("AMA Supercross Rd 1 Anaheim 1 1080p x265 SlickNick0610")]
    public void IncompleteSupercrossIdentityIsRejectedAcrossRoutes(string title)
    {
        var evt = SupercrossEvent("Anaheim 1", "1", new DateTime(2022, 1, 8));
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

    [Fact]
    public void LibraryFormattedSupercrossFileUsesAuthoritativeEpisodeIdentity()
    {
        const string title = "AMA Supercross Championship - S2022E01 - Anaheim 1.mkv";
        var evt = SupercrossEvent("Anaheim 1", "1", new DateTime(2022, 1, 8));
        evt.EpisodeNumber = 1;

        LibraryScore(
            title,
            "Anaheim 1",
            evt,
            new SportsParseResult { OriginalFilename = title, EventYear = 2022 },
            explicitEpisodeNumber: 1,
            seriesLabel: "AMA Supercross Championship").Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void OverflowingLibraryEpisodeIsRejectedWithoutThrowing()
    {
        const string title = "AMA Supercross Championship - S2022E2147483648 - Anaheim 1.mkv";
        var evt = SupercrossEvent("Anaheim 1", "1", new DateTime(2022, 1, 8));
        evt.EpisodeNumber = 1;

        LibraryScore(
            title,
            "Anaheim 1",
            evt,
            new SportsParseResult { OriginalFilename = title, EventYear = 2022 },
            seriesLabel: "AMA Supercross Championship").Should().Be(0);
    }

    private static Event SupercrossEvent(
        string title,
        string round,
        DateTime date,
        string? location = null) => new()
    {
        Title = title,
        Sport = "Motorsport",
        Season = "2022",
        Round = round,
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Location = location,
        League = new League { Name = "AMA Supercross Championship", Sport = "Motorsport" }
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
        SportsParseResult parsed,
        int? explicitEpisodeNumber = null,
        string? seriesLabel = null) => LibraryImportService.CalculateMatchConfidence(
            eventTitle,
            evt.Title,
            parsed.Organization,
            evt,
            parsed.EventDate,
            parsed.EventYear,
            parsed.RoundNumber,
            parsed.SeasonYearEnd,
            explicitEpisodeNumber,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport,
            seriesLabel: seriesLabel,
            sourceTitle: sourceTitle);
}
