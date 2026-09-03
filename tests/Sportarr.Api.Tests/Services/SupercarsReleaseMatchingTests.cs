using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #257. Supercars releases are named two ways and neither matches the
/// event titles the metadata gives us. Every title below was taken from a real
/// indexer response, not invented.
///
///   Supercars 2026 Round09 Ipswich Race 3 ...   round + race within the round
///   Supercars 2025 Race 25 Ipswich 10 08 ...    race counted across the season
///   Supercars 2024 Adelaide 500 Race 23 ...     venue event + season race
///
/// The library counts races across the season, so round 9 race 3 is race 28.
/// </summary>
public class SupercarsReleaseMatchingTests
{
    private readonly ReleaseMatchingService _matchingSvc;
    private readonly ReleaseMatchScorer _scorer = new();

    public SupercarsReleaseMatchingTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _matchingSvc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event Race(string title, string round, int raceNumber, int year, int month, int day) => new()
    {
        Id = raceNumber,
        Title = title,
        Sport = "Motorsport",
        EventDate = new DateTime(year, month, day, 4, 0, 0, DateTimeKind.Utc),
        Location = "AU",
        Round = round,
        Season = year.ToString(),
        EpisodeNumber = raceNumber,
        League = new League { Id = 1, Name = "Supercars", Sport = "Motorsport" },
    };

    // Round 9 of 2026 holds races 26, 27 and 28, so the release's "Race 3" is race 28.
    private static Event Ipswich2026Race28() =>
        Race("Century Batteries Ipswich Super 440 - Race 28", "9", 28, 2026, 8, 23);

    private static Event Ipswich2025Race25() =>
        Race("Century Batteries Ipswich Super 440 - Race 25", "8", 25, 2025, 8, 10);

    private static Event Adelaide2024Race23() =>
        Race("VAILO Adelaide 500 - Race 23", "12", 23, 2024, 11, 16);

    // Round 9 holds races 26, 27 and 28. The caller reads them from the
    // library, because "Race 3" means nothing without them.
    private static readonly int[] Round9Races = { 26, 27, 28 };

    [Fact]
    public void RoundAndRaceWithinTheRound_MatchesTheSeasonRace()
    {
        var release = Rel("Supercars 2026 Round09 Ipswich Race 3 2160p FoxSports WEB DL DD H265 English");

        var result = _matchingSvc.ValidateRelease(release, Ipswich2026Race28(), roundRaceNumbers: Round9Races);

        result.IsHardRejection.Should().BeFalse("round 9 race 3 is race 28");
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void RoundAndRaceWithinTheRound_DoesNotMatchAnotherRaceOfThatRound()
    {
        var release = Rel("Supercars 2026 Round09 Ipswich Race 1 2160p FoxSports WEB DL DD H265 English");

        var result = _matchingSvc.ValidateRelease(release, Ipswich2026Race28(), roundRaceNumbers: Round9Races);

        result.IsHardRejection.Should().BeTrue("race 1 of round 9 is race 26, a different event");
    }

    [Fact]
    public void SeasonRaceNumber_MatchesTheEventThatCarriesIt()
    {
        var release = Rel("Supercars 2025 Race 25 Ipswich 10 08 1080p EN");

        var result = _matchingSvc.ValidateRelease(release, Ipswich2025Race25());

        result.IsHardRejection.Should().BeFalse();
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void SeasonRaceNumber_DoesNotMatchANeighbouringRace()
    {
        var release = Rel("Supercars 2025 Race 24 Ipswich 09 08 1080p EN");

        _matchingSvc.ValidateRelease(release, Ipswich2025Race25())
            .IsHardRejection.Should().BeTrue("race 24 is its own event");
    }

    [Fact]
    public void AVenueEventNameAndSeasonRaceNumber_Match()
    {
        var release = Rel("Supercars 2024 Adelaide 500 Race 23 16 11 1080p EN");

        var result = _matchingSvc.ValidateRelease(release, Adelaide2024Race23());

        result.IsHardRejection.Should().BeFalse();
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void WithoutTheRoundsRacesAPerRoundReleaseIsNotRejected()
    {
        // A caller that cannot say which races the round holds leaves the
        // question open rather than rejecting a release that may well be right.
        var release = Rel("Supercars 2026 Round09 Ipswich Race 3 2160p FoxSports WEB DL DD H265 English");

        _matchingSvc.ValidateRelease(release, Ipswich2026Race28())
            .IsHardRejection.Should().BeFalse();
    }

    [Fact]
    public void AnEventNameHoldingAThousandIsNotADate()
    {
        // "Bathurst 1000 13 10" read 1000 as the year, retried as 1000-10-13,
        // and hard-rejected every Bathurst release as 374,009 days out.
        var release = Rel("Supercars 2024 Race 20 Bathurst 1000 13 10 1080p EN");
        var evt = Race("Repco Bathurst 1000 - Race 20", "9", 20, 2024, 10, 13);

        var result = _matchingSvc.ValidateRelease(release, evt);

        result.IsHardRejection.Should().BeFalse("the biggest race of the season is not from the year 1000");
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void OneFileHoldingTwoRacesMatchesBoth()
    {
        var release = Rel("Supercars 2026 Races 26 and 27 Ipswich 22 08 1080p EN");

        foreach (var race in new[] { 26, 27 })
        {
            _matchingSvc.ValidateRelease(release, Race($"Century Batteries Ipswich Super 440 - Race {race}", "9", race, 2026, 8, 22))
                .IsHardRejection.Should().BeFalse($"the file holds race {race}");
        }

        _matchingSvc.ValidateRelease(release, Ipswich2026Race28())
            .IsHardRejection.Should().BeTrue("race 28 is not in that file");
    }

    [Fact]
    public void TheRaceNumberScoresTheRightEventHigher()
    {
        const string release = "Supercars 2025 Race 25 Ipswich 10 08 1080p EN";

        var right = _scorer.CalculateMatchScore(release, Ipswich2025Race25());
        var wrong = _scorer.CalculateMatchScore(release, Race(
            "Century Batteries Ipswich Super 440 - Race 24", "8", 24, 2025, 8, 9));

        right.Should().BeGreaterThan(wrong, "the race number is the only thing telling them apart");
    }
}
