using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class IndividualSportReleaseSearchTests
{
    private static readonly EventQueryService QueryService =
        new(NullLogger<EventQueryService>.Instance);

    private static readonly ReleaseMatchingService MatchingService = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    private static readonly ReleaseMatchScorer Scorer = new();

    [Fact]
    public void TennisQuery_KeepsTheExactTournamentAndParticipants()
    {
        var evt = CreateEvent(
            "Australian Open Alcaraz vs Djokovic",
            "Tennis",
            "ATP World Tour",
            new DateTime(2026, 2, 1));

        QueryService.BuildEventQueries(evt)
            .Should().Equal("Australian Open Alcaraz vs Djokovic");
    }

    [Fact]
    public void CyclingQuery_AddsYearAndSplitsJoinedPunctuation()
    {
        var evt = CreateEvent(
            "Giro dItalia Stage 21",
            "Cycling",
            "UCI World Tour",
            new DateTime(2026, 5, 31));

        QueryService.BuildEventQueries(evt)
            .Should().Equal("Giro d Italia Stage 21 2026");
    }

    [Fact]
    public void MastersQuery_CoversCanonicalAndAugustaNamesWithOneRequest()
    {
        var evt = CreateEvent(
            "Masters Tournament Final Round",
            "Golf",
            "PGA Tour",
            new DateTime(2026, 4, 12));

        QueryService.BuildEventQueries(evt)
            .Should().Equal("Masters 2026");
    }

    [Fact]
    public void TennisRelease_WithBothParticipantsAndYear_Matches()
    {
        var evt = CreateEvent(
            "Australian Open Alcaraz vs Djokovic",
            "Tennis",
            "ATP World Tour",
            new DateTime(2026, 2, 1));

        var result = MatchingService.ValidateRelease(
            Release("Australian.Open.2026.Final.Carlos.Alcaraz.vs.Novak.Djokovic.1080p.HMAX.WEB-DL.H.264-playWEB"),
            evt);

        result.IsMatch.Should().BeTrue(
            "confidence was {0}; matches were {1}; rejections were {2}",
            result.Confidence,
            string.Join(", ", result.MatchReasons),
            string.Join(", ", result.Rejections));
        result.IsHardRejection.Should().BeFalse();
        Scorer.CalculateMatchScore(
                "Australian.Open.2026.Final.Carlos.Alcaraz.vs.Novak.Djokovic.1080p.HMAX.WEB-DL.H.264-playWEB",
                evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void TennisRelease_WithOnlyOneExpectedParticipant_DoesNotMatch()
    {
        var evt = CreateEvent(
            "Australian Open Alcaraz vs Djokovic",
            "Tennis",
            "ATP World Tour",
            new DateTime(2026, 2, 1));

        var result = MatchingService.ValidateRelease(
            Release("Australian.Open.2026.Final.Carlos.Alcaraz.vs.Jannik.Sinner.1080p.WEB.H264"),
            evt);

        result.IsMatch.Should().BeFalse();
        Scorer.CalculateMatchScore(
                "Australian.Open.2026.Final.Carlos.Alcaraz.vs.Jannik.Sinner.1080p.WEB.H264",
                evt)
            .Should().Be(0);
    }

    [Fact]
    public void TennisRoundSuffixDoesNotReplaceTheOpponentIdentity()
    {
        var evt = CreateEvent(
            "Australian Open Alcaraz vs Djokovic (Final)",
            "Tennis",
            "ATP World Tour",
            new DateTime(2026, 2, 1));
        const string right = "Australian.Open.2026.Final.Alcaraz.vs.Djokovic.1080p.WEB.H264";
        const string wrong = "Australian.Open.2026.Final.Alcaraz.vs.Sinner.1080p.WEB.H264";

        MatchingService.ValidateRelease(Release(right), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(right, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        MatchingService.ValidateRelease(Release(wrong), evt).IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore(wrong, evt).Should().Be(0);
    }

    [Fact]
    public void TennisRelease_WithSameParticipantsFromDifferentTournament_IsRejected()
    {
        var evt = CreateEvent(
            "Australian Open Alcaraz vs Djokovic",
            "Tennis",
            "ATP World Tour",
            new DateTime(2026, 2, 1));

        var result = MatchingService.ValidateRelease(
            Release("Wimbledon.2026.Alcaraz.vs.Djokovic.1080p.WEB.H264"),
            evt);

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore("Wimbledon.2026.Alcaraz.vs.Djokovic.1080p.WEB.H264", evt)
            .Should().Be(0);
    }

    [Fact]
    public void TennisRelease_WithCompoundOpponentFromDifferentTournament_IsRejected()
    {
        var evt = CreateEvent(
            "Wimbledon Djokovic vs de Minaur",
            "Tennis",
            "ATP World Tour",
            new DateTime(2026, 7, 12));

        var result = MatchingService.ValidateRelease(
            Release("Australian.Open.2026.Djokovic.vs.de.Minaur.1080p.WEB.H264"),
            evt);

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void TennisRelease_WithSharedTourAndDifferentTournamentLocation_IsRejected()
    {
        var evt = CreateEvent(
            "ATP Madrid Alcaraz vs Sinner",
            "Tennis",
            "ATP World Tour",
            new DateTime(2026, 5, 3));

        var result = MatchingService.ValidateRelease(
            Release("ATP.Rome.2026.Alcaraz.vs.Sinner.1080p.WEB.H264"),
            evt);

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Theory]
    [InlineData(
        "Australian Open Alcaraz vs Djokovic",
        "Australian.Championship.2026.Alcaraz.vs.Djokovic.1080p.WEB.H264")]
    [InlineData(
        "US Open Tiafoe vs Shelton",
        "US.Mens.Clay.Court.Championship.2026.Tiafoe.vs.Shelton.1080p.WEB.H264")]
    [InlineData(
        "ATP Masters 1000 Madrid Alcaraz vs Sinner",
        "ATP.Masters.1000.Rome.2026.Alcaraz.vs.Sinner.1080p.WEB.H264")]
    [InlineData(
        "ATP Masters 1000 Madrid Carlos Alcaraz vs Jannik Sinner",
        "ATP.Masters.1000.Rome.2026.Carlos.Alcaraz.vs.Jannik.Sinner.1080p.WEB.H264")]
    public void TennisRelease_WithSharedTournamentPrefixAndDifferentTournament_IsRejected(
        string eventTitle,
        string releaseTitle)
    {
        var evt = CreateEvent(
            eventTitle,
            "Tennis",
            "ATP World Tour",
            new DateTime(2026, 2, 1));

        var result = MatchingService.ValidateRelease(Release(releaseTitle), evt);

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore(releaseTitle, evt).Should().Be(0);
    }

    [Theory]
    [InlineData("Australian.Open.2026.Final.Alcaraz.vs.Djokovic.1080p.WEB.H264")]
    [InlineData("Australian.Open.2026.Final.Djokovic.vs.Alcaraz.1080p.WEB.H264")]
    public void TennisRelease_WithFullEventPlayerNamesAndReleaseSurnames_Matches(string releaseTitle)
    {
        var evt = CreateEvent(
            "Australian Open Carlos Alcaraz vs Novak Djokovic",
            "Tennis",
            "ATP World Tour",
            new DateTime(2026, 2, 1));

        var result = MatchingService.ValidateRelease(Release(releaseTitle), evt);

        result.IsMatch.Should().BeTrue();
        result.IsHardRejection.Should().BeFalse();
        Scorer.CalculateMatchScore(releaseTitle, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void TennisRelease_WithCompoundSurnameAndTournament_Matches()
    {
        var evt = CreateEvent(
            "Wimbledon Alex de Minaur vs Novak Djokovic",
            "Tennis",
            "ATP World Tour",
            new DateTime(2026, 7, 12));
        const string releaseTitle = "Wimbledon.2026.de.Minaur.vs.Djokovic.1080p.WEB.H264";

        var result = MatchingService.ValidateRelease(Release(releaseTitle), evt);

        result.IsMatch.Should().BeTrue(
            "confidence was {0}; matches were {1}; rejections were {2}",
            result.Confidence,
            string.Join(", ", result.MatchReasons),
            string.Join(", ", result.Rejections));
        result.IsHardRejection.Should().BeFalse();
        Scorer.CalculateMatchScore(releaseTitle, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void TennisRelease_ForAtpFinals_Matches()
    {
        var evt = CreateEvent(
            "ATP Finals Sinner vs Zverev",
            "Tennis",
            "ATP World Tour",
            new DateTime(2026, 11, 22));

        var result = MatchingService.ValidateRelease(
            Release("ATP.Finals.2026.Sinner.vs.Zverev.1080p.WEB.H264"),
            evt);

        result.IsMatch.Should().BeTrue();
        result.IsHardRejection.Should().BeFalse();
        Scorer.CalculateMatchScore("ATP.Finals.2026.Sinner.vs.Zverev.1080p.WEB.H264", evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void CyclingRelease_WithMatchingStageAndYear_Matches()
    {
        var evt = CreateEvent(
            "Tour de France Stage 16",
            "Cycling",
            "UCI World Tour",
            new DateTime(2026, 7, 21));

        var result = MatchingService.ValidateRelease(
            Release("Cycling.UCI.World.Tour.2026.Tour.De.France.Men.Elite.Stage.16.1080p.WEB.h264-BILLIE"),
            evt);

        result.IsMatch.Should().BeTrue();
        result.IsHardRejection.Should().BeFalse();
        Scorer.CalculateMatchScore(
                "Cycling.UCI.World.Tour.2026.Tour.De.France.Men.Elite.Stage.16.1080p.WEB.h264-BILLIE",
                evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void CyclingRelease_WithDifferentStage_IsRejected()
    {
        var evt = CreateEvent(
            "Tour de France Stage 16",
            "Cycling",
            "UCI World Tour",
            new DateTime(2026, 7, 21));

        var result = MatchingService.ValidateRelease(
            Release("Tour.de.France.2026.Stage.11.1080p.WEB.H264"),
            evt);

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void CyclingRelease_WithSameStageFromDifferentRace_IsRejected()
    {
        var evt = CreateEvent(
            "Tour de France Stage 16",
            "Cycling",
            "UCI World Tour",
            new DateTime(2026, 7, 21));

        var result = MatchingService.ValidateRelease(
            Release("Tour.de.Suisse.2026.Stage.16.1080p.WEB.H264"),
            evt);

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void MensCyclingEvent_RejectsWomensRaceRelease()
    {
        var evt = CreateEvent(
            "Paris–Roubaix",
            "Cycling",
            "UCI World Tour",
            new DateTime(2026, 4, 12));

        var result = MatchingService.ValidateRelease(
            Release("Cycling.UCI.World.Tour.2026.Paris-Roubaix.Women.Elite.1080p.WEB.h264-BILLIE"),
            evt);

        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void WomensCyclingEventInSharedLeague_AcceptsWomensRelease()
    {
        var evt = CreateEvent(
            "Paris-Roubaix Womens Elite",
            "Cycling",
            "UCI World Tour",
            new DateTime(2026, 4, 11));

        var result = MatchingService.ValidateRelease(
            Release("Cycling.UCI.World.Tour.2026.Paris-Roubaix.Women.Elite.1080p.WEB.h264-BILLIE"),
            evt);

        result.IsHardRejection.Should().BeFalse();
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void MensCyclingEvent_RejectsFemmesRelease()
    {
        var evt = CreateEvent(
            "Tour de France Stage 16",
            "Cycling",
            "UCI World Tour",
            new DateTime(2026, 7, 21));

        MatchingService.ValidateRelease(
                Release("Cycling.UCI.World.Tour.2026.Tour.de.France.Femmes.Stage.16.1080p.WEB.h264-BILLIE"),
                evt)
            .IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void FemmesCyclingEvent_RejectsMensRelease()
    {
        var evt = CreateEvent(
            "Tour de France Femmes Stage 8",
            "Cycling",
            "UCI World Tour",
            new DateTime(2026, 8, 9));

        MatchingService.ValidateRelease(
                Release("Cycling.UCI.World.Tour.2026.Tour.de.France.Men.Elite.Stage.8.1080p.WEB.h264-BILLIE"),
                evt)
            .IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void FemmesCyclingEvent_AcceptsFemmesRelease()
    {
        var evt = CreateEvent(
            "Tour de France Femmes Stage 8",
            "Cycling",
            "UCI World Tour",
            new DateTime(2026, 8, 9));

        var result = MatchingService.ValidateRelease(
            Release("Cycling.UCI.World.Tour.2026.Tour.de.France.Femmes.Stage.8.1080p.WEB.h264-BILLIE"),
            evt);

        result.IsHardRejection.Should().BeFalse();
        result.IsMatch.Should().BeTrue();
    }

    [Theory]
    [InlineData(
        "Tour de France Stage 8",
        "Cycling.UCI.World.Tour.2026.Tour.de.France.Femmes.Stage.8.1080p.WEB.h264-BILLIE")]
    [InlineData(
        "Tour de France Femmes Stage 8",
        "Cycling.UCI.World.Tour.2026.Tour.de.France.Men.Elite.Stage.8.1080p.WEB.h264-BILLIE")]
    public void CyclingCategoryConflict_IsRejectedByBothImportScorers(
        string eventTitle,
        string releaseTitle)
    {
        var evt = CreateEvent(
            eventTitle,
            "Cycling",
            "UCI World Tour",
            new DateTime(2026, 8, 9));
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse(releaseTitle);
        parsed.EventTitle.Should().NotBeNull();

        ImportMatchingTestHarness.Service()
            .ScoreMatch(parsed.EventTitle!, evt.Title, null, evt, parsed).Core
            .Should().BeLessThan(50);
        new ReleaseMatchScorer().CalculateMatchScore(releaseTitle, evt)
            .Should().Be(0);
        LibraryImportService.CalculateMatchConfidence(
                parsed.EventTitle!,
                evt.Title,
                parsed.Organization,
                evt,
                parsed.EventDate,
                parsed.EventYear,
                parsed.RoundNumber,
                parsed.SeasonYearEnd,
                parsedLocation: parsed.Location,
                parsedSport: parsed.Sport,
                sourceTitle: releaseTitle)
            .Should().Be(0);
    }

    [Fact]
    public void FemmesCyclingRelease_RemainsImportableForFemmesEvent()
    {
        const string releaseTitle =
            "Cycling.UCI.World.Tour.2026.Tour.de.France.Femmes.Stage.8.1080p.WEB.h264-BILLIE";
        var evt = CreateEvent(
            "Tour de France Femmes Stage 8",
            "Cycling",
            "UCI World Tour",
            new DateTime(2026, 8, 9));
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse(releaseTitle);
        parsed.EventTitle.Should().NotBeNull();

        ImportMatchingTestHarness.Service()
            .ScoreMatch(parsed.EventTitle!, evt.Title, null, evt, parsed).Core
            .Should().BeGreaterThanOrEqualTo(50);
        LibraryImportService.CalculateMatchConfidence(
                parsed.EventTitle!,
                evt.Title,
                parsed.Organization,
                evt,
                parsed.EventDate,
                parsed.EventYear,
                parsed.RoundNumber,
                parsed.SeasonYearEnd,
                parsedLocation: parsed.Location,
                parsedSport: parsed.Sport,
                sourceTitle: releaseTitle)
            .Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void MensCyclingEvent_AcceptsMensRaceRelease()
    {
        var evt = CreateEvent(
            "Paris–Roubaix",
            "Cycling",
            "UCI World Tour",
            new DateTime(2026, 4, 12));

        var result = MatchingService.ValidateRelease(
            Release("Cycling.UCI.World.Tour.2026.Paris-Roubaix.Men.Elite.1080p.WEB.h264-BILLIE"),
            evt);

        result.IsMatch.Should().BeTrue(
            "confidence was {0}; matches were {1}; rejections were {2}",
            result.Confidence,
            string.Join(", ", result.MatchReasons),
            string.Join(", ", result.Rejections));
        result.IsHardRejection.Should().BeFalse();
    }

    [Fact]
    public void MastersDayFourRelease_MatchesFinalRound()
    {
        var evt = CreateEvent(
            "Masters Tournament Final Round",
            "Golf",
            "PGA Tour",
            new DateTime(2026, 4, 12));

        var result = MatchingService.ValidateRelease(
            Release("The Augusta Masters 2026 Day 4 Full replay 1080p"),
            evt);

        result.IsMatch.Should().BeTrue();
        result.IsHardRejection.Should().BeFalse();
        Scorer.CalculateMatchScore("The Augusta Masters 2026 Day 4 Full replay 1080p", evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void CanonicalMastersTournamentRelease_MatchesFinalRound()
    {
        var evt = CreateEvent(
            "Masters Tournament Final Round",
            "Golf",
            "PGA Tour",
            new DateTime(2026, 4, 12));
        const string releaseTitle = "Masters.Tournament.2026.Final.Round.1080p.WEB.H264";

        var result = MatchingService.ValidateRelease(Release(releaseTitle), evt);

        result.IsMatch.Should().BeTrue(
            "confidence was {0}; matches were {1}; rejections were {2}",
            result.Confidence,
            string.Join(", ", result.MatchReasons),
            string.Join(", ", result.Rejections));
        result.IsHardRejection.Should().BeFalse();
        Scorer.CalculateMatchScore(releaseTitle, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void MastersFirstRoundRelease_IsRejectedForFinalRound()
    {
        var evt = CreateEvent(
            "Masters Tournament Final Round",
            "Golf",
            "PGA Tour",
            new DateTime(2026, 4, 12));

        var result = MatchingService.ValidateRelease(
            Release("The Masters 2026 First Round Holes 4 5 6 09 04 720p"),
            evt);

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void DifferentMastersTournamentFinalRound_IsRejected()
    {
        var evt = CreateEvent(
            "Masters Tournament Final Round",
            "Golf",
            "PGA Tour",
            new DateTime(2026, 4, 12));

        var result = MatchingService.ValidateRelease(
            Release("British Masters 2026 Final Round 1080p WEB H264"),
            evt);

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void PgaChampionshipFinalRoundRelease_Matches()
    {
        var evt = CreateEvent(
            "PGA Championship Final Round",
            "Golf",
            "PGA Tour",
            new DateTime(2026, 5, 17));

        var result = MatchingService.ValidateRelease(
            Release("PGA.Championship.2026.Final.Round.1080p.WEB.H264"),
            evt);

        result.IsMatch.Should().BeTrue();
        result.IsHardRejection.Should().BeFalse();
        Scorer.CalculateMatchScore("PGA.Championship.2026.Final.Round.1080p.WEB.H264", evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void DifferentGolfTournamentFinalRound_IsRejected()
    {
        var evt = CreateEvent(
            "PGA Championship Final Round",
            "Golf",
            "PGA Tour",
            new DateTime(2026, 5, 17));

        var result = MatchingService.ValidateRelease(
            Release("USPGA Barracuda Championship Final Round 1080p50"),
            evt);

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Theory]
    [InlineData("KPMG Womens PGA Championship 2026 Final Round 1080p WEB H264")]
    [InlineData("Senior PGA Championship 2026 Final Round 1080p WEB H264")]
    public void DifferentPgaDivisionFinalRound_IsRejected(string releaseTitle)
    {
        var evt = CreateEvent(
            "PGA Championship Final Round",
            "Golf",
            "PGA Tour",
            new DateTime(2026, 5, 17));

        var result = MatchingService.ValidateRelease(Release(releaseTitle), evt);

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void SeniorPgaChampionshipRelease_MatchesSeniorEvent()
    {
        var evt = CreateEvent(
            "Senior PGA Championship Final Round",
            "Golf",
            "PGA Tour Champions",
            new DateTime(2026, 5, 24));

        var result = MatchingService.ValidateRelease(
            Release("Senior PGA Championship 2026 Final Round 1080p WEB H264"),
            evt);

        result.IsMatch.Should().BeTrue();
        result.IsHardRejection.Should().BeFalse();
    }

    [Fact]
    public void GolfRelease_WithOldStandaloneYear_IsRejected()
    {
        var evt = CreateEvent(
            "PGA Championship Final Round",
            "Golf",
            "PGA Tour",
            new DateTime(2026, 5, 17));

        var result = MatchingService.ValidateRelease(
            Release("PGA Championship 1999 Sunday Final Round 18/08"),
            evt);

        result.IsHardRejection.Should().BeTrue();
    }

    private static Event CreateEvent(string title, string sport, string leagueName, DateTime date) => new()
    {
        ExternalId = "ev-test",
        Title = title,
        Sport = sport,
        Season = date.Year.ToString(),
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        League = new League
        {
            ExternalId = "lg-test",
            Name = leagueName,
            Sport = sport
        }
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Test"
    };
}
