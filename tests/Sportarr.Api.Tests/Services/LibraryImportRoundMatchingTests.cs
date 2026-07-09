using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Library-import match scoring for motorsport. Field report: importing a
/// folder, "Formula1.2025.Round09.Spain.Qualifying..." matched
/// "Chinese GP Free Practice 1" because that session was chronologically the
/// 9th episode of the season. The scorer was comparing the filename's
/// championship ROUND against the event's EPISODE number - unrelated counters
/// for a multi-session sport - which both rewarded the wrong event and
/// penalised the correct one. It must compare round against the event's own
/// Round field.
/// </summary>
public class LibraryImportRoundMatchingTests
{
    // The two reported files, with the wrong event they were matching to and
    // the correct event they should match.
    private static Event SpainQualifyingRound9() => new()
    {
        Title = "Spanish Grand Prix - Qualifying",
        Sport = "Motorsport",
        Round = "9",
        SeasonNumber = 2025,
        EpisodeNumber = 43, // qualifying of round 9 - far from the round number
        EventDate = new DateTime(2025, 5, 31, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "Formula 1", Sport = "Motorsport" },
    };

    private static Event ChinaFp1Episode9() => new()
    {
        Title = "Chinese Grand Prix - Free Practice 1",
        Sport = "Motorsport",
        Round = "2",
        SeasonNumber = 2025,
        EpisodeNumber = 9, // the coincidental chronological episode 9
        EventDate = new DateTime(2025, 3, 21, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "Formula 1", Sport = "Motorsport" },
    };

    private static int Score(Event evt) => LibraryImportService.CalculateMatchConfidence(
        searchTitle: "Spain Qualifying",
        eventTitle: evt.Title!,
        organization: "Formula 1",
        evt: evt,
        parsedDate: null,
        parsedYear: 2025,
        parsedRoundNumber: 9,
        seasonYearEnd: null,
        explicitEpisodeNumber: null,
        parsedLocation: "Spain");

    [Fact]
    public void Round9File_ScoresCorrectEventAboveTheEpisode9Decoy()
    {
        var correct = Score(SpainQualifyingRound9());
        var decoy = Score(ChinaFp1Episode9());

        correct.Should().BeGreaterThan(decoy,
            "round 9 Spain Qualifying must beat the event that merely happens to be episode 9");
    }

    [Fact]
    public void CorrectRoundEvent_GetsTheRoundBonus()
    {
        // Round 9 file vs round 9 event: full round bonus, no episode confusion.
        Score(SpainQualifyingRound9()).Should().BeGreaterThanOrEqualTo(40,
            "an exact round match should comfortably clear the acceptance threshold");
    }

    [Fact]
    public void WrongRoundEvent_IsPenalised_NotRewarded()
    {
        // The China FP1 decoy is round 2, not round 9: it must be penalised for
        // the round mismatch rather than rewarded for episode==round coincidence.
        Score(ChinaFp1Episode9()).Should().BeLessThan(40,
            "a round-2 event should not clear acceptance for a round-9 file");
    }

    [Fact]
    public void SecondReportedFile_HungaryRound14_BeatsJapanEpisode14()
    {
        int ScoreFor(Event evt) => LibraryImportService.CalculateMatchConfidence(
            "Hungary Qualifying", evt.Title!, "Formula 1", evt,
            parsedDate: null, parsedYear: 2025, parsedRoundNumber: 14,
            seasonYearEnd: null, explicitEpisodeNumber: null, parsedLocation: "Hungary");

        var hungaryRound14 = new Event
        {
            Title = "Hungarian Grand Prix - Qualifying", Sport = "Motorsport", Round = "14",
            SeasonNumber = 2025, EpisodeNumber = 68,
            EventDate = new DateTime(2025, 8, 2, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "Formula 1", Sport = "Motorsport" },
        };
        var japanFp1Episode14 = new Event
        {
            Title = "Japanese Grand Prix - Free Practice 1", Sport = "Motorsport", Round = "4",
            SeasonNumber = 2025, EpisodeNumber = 14,
            EventDate = new DateTime(2025, 4, 4, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "Formula 1", Sport = "Motorsport" },
        };

        ScoreFor(hungaryRound14).Should().BeGreaterThan(ScoreFor(japanFp1Episode14));
    }

    [Fact]
    public void NoRoundData_FallsBackButDoesNotOverpower()
    {
        // Event with no Round field: the episode==round fallback still applies
        // (single-session series legitimately use it) but at a light weight, so
        // it can't recreate the original bug's dominance.
        var noRound = new Event
        {
            Title = "Some Event", Sport = "Motorsport", Round = null,
            SeasonNumber = 2025, EpisodeNumber = 9,
            EventDate = new DateTime(2025, 3, 21, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "Formula 1", Sport = "Motorsport" },
        };

        // Still scores something (fallback + year + org) but nowhere near the
        // +50 the exact-round match gives the correct event.
        Score(noRound).Should().BeLessThan(Score(SpainQualifyingRound9()));
    }
}
