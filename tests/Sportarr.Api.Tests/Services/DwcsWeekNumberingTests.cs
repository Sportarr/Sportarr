using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #243: Contender Series season 10 events are titled
/// "... season 10 Week 1" and its releases are named "UFC Tuesday Night
/// Contender Series S10W01". Neither side of the matcher accepted the week
/// wording, the automatic query never mentioned the release's show title, and
/// the season/episode compare was a string compare, so "1" never equalled
/// "01" and even a correct release scored as the wrong episode.
/// </summary>
public class DwcsWeekNumberingTests
{
    private readonly ReleaseMatchScorer _scorer = new();

    private static Event DwcsEvent(int week) => new()
    {
        Id = 1,
        Title = $"Dana Whites Contender Series season 10 Week {week}",
        Sport = "Fighting",
        EventDate = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "UFC", Sport = "Fighting" },
    };

    [Fact]
    public void CorrectWeekRelease_ScoresAboveTheFloor()
    {
        var score = _scorer.CalculateMatchScore(
            "UFC.Tuesday.Night.Contender.Series.S10W01.720p.WEB-DL.H264.Fight-BB",
            DwcsEvent(1));

        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore,
            "the release names the same season and week as the event");
    }

    [Fact]
    public void WrongWeekRelease_ScoresBelowTheCorrectOne()
    {
        var right = _scorer.CalculateMatchScore(
            "UFC.Tuesday.Night.Contender.Series.S10W01.720p.WEB-DL.H264.Fight-BB", DwcsEvent(1));
        var wrong = _scorer.CalculateMatchScore(
            "UFC.Tuesday.Night.Contender.Series.S10W03.720p.WEB-DL.H264.Fight-BB", DwcsEvent(1));

        wrong.Should().BeLessThan(right, "a different week is a different card");
    }

    [Fact]
    public void ClassicEpisodeNumbering_StillMatches()
    {
        var score = _scorer.CalculateMatchScore(
            "UFC.Dana.Whites.Contender.Series.S07E01.1080p.WEB-DL.H264",
            new Event
            {
                Id = 2,
                Title = "Dana Whites Contender Series season 7 Episode 1",
                Sport = "Fighting",
                EventDate = new DateTime(2023, 8, 8, 0, 0, 0, DateTimeKind.Utc),
                League = new League { Name = "UFC", Sport = "Fighting" },
            });

        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore,
            "zero padding differences must not read as a different episode");
    }

    [Theory]
    [InlineData("DWCS.S10W01.720p.WEB-DL.H264-GRP")]
    [InlineData("DWCS.S10E01.720p.WEB-DL.H264-GRP")]
    [InlineData("Dana.Whites.Contender.Series.S10W01.1080p.WEB-DL.H264-GRP")]
    [InlineData("Dana.Whites.Contender.Series.S10E01.1080p.WEB-DL.H264-GRP")]
    public void EveryKnownReleaseNamingConvention_StillMatches(string releaseTitle)
    {
        var score = _scorer.CalculateMatchScore(releaseTitle, DwcsEvent(1));

        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore,
            "old and short show titles with either numbering letter name the same card");
    }

    [Fact]
    public void AutomaticSearch_AlsoAsksForTheReleaseShowTitle()
    {
        var service = new EventQueryService(NullLogger<EventQueryService>.Instance);

        var queries = service.BuildEventQueries(DwcsEvent(1));

        queries.Should().Contain("UFC Tuesday Night Contender Series S10W01",
            "season 10 releases use this show title and week numbering");
        queries.Should().Contain("Dana Whites Contender Series S10W01",
            "some groups keep the classic show title with the week numbering");
        queries.Should().Contain("Dana Whites Contender Series S10E01",
            "older-style releases keep the episode numbering and must stay findable");
    }
}
