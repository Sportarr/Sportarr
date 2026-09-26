using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class ReleaseMatchScorerDateAmbiguityTests
{
    [Theory]
    [InlineData("3-2")]
    [InlineData("24-6")]
    [InlineData("14-7")]
    public void FinalScoreDoesNotBecomeTheFixtureDate(string finalScore)
    {
        var evt = new Event
        {
            Title = "Kansas City Chiefs vs Buffalo Bills",
            Sport = "Football",
            Season = "2025",
            EventDate = new DateTime(2025, 9, 6, 20, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2025, 9, 6),
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Kansas City Chiefs",
            AwayTeamName = "Buffalo Bills",
            League = new League { Name = "NFL", Sport = "Football" }
        };

        var scorer = new ReleaseMatchScorer();

        var title = $"NFL 2025 Kansas City Chiefs vs Buffalo Bills {finalScore} 1080p";
        scorer.CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        matcher.ValidateRelease(new ReleaseSearchResult
        {
            Title = title,
            Guid = title,
            DownloadUrl = "http://fixture.invalid/release.nzb",
            Indexer = "Fixture"
        }, evt).IsMatch.Should().BeTrue();
        scorer.CalculateMatchScore(
                "NFL 2025 Kansas City Chiefs vs Buffalo Bills 03-02-2025 1080p", evt)
            .Should().Be(0);
        scorer.CalculateMatchScore(
                "NFL 2025 Kansas City Chiefs vs Buffalo Bills 15-09-2025 1080p", evt)
            .Should().Be(0);
    }
}
