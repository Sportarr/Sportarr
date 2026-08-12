using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #228: the metadata source titles the Silverstone round
/// "United Kingdom" while release groups name it "Great Britain". Every alias
/// table carried Britain, British, UK, Great Britain, and Silverstone, but not
/// United Kingdom, so the event's own location matched none of its releases.
/// Uses the reporter's exact release titles.
/// </summary>
public class MotoGpBritainReleaseMatchingTests
{
    private readonly ReleaseMatchingService _matchingSvc;
    private readonly ReleaseMatchScorer _scorer = new();

    public MotoGpBritainReleaseMatchingTests()
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

    private static Event UnitedKingdomSprint() => new()
    {
        Id = 1,
        Title = "United Kingdom Sprint Race",
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc),
        Location = "United Kingdom",
        Round = "12",
        Season = "2026",
        League = new League { Id = 1, Name = "MotoGP", Sport = "Motorsport" }
    };

    [Theory]
    [InlineData("MotoGP 2026 Round12 Great Britain Sprint TNT WEB-DL 1080p H264 DDP5 1 English-MWR")]
    [InlineData("MotoGP 2026 Round12 Great Britain Sprint WEB-DL 1080p H264 English-MWR")]
    public void GreatBritainRelease_MatchesUnitedKingdomEvent(string releaseTitle)
    {
        var result = _matchingSvc.ValidateRelease(Rel(releaseTitle), UnitedKingdomSprint());

        result.IsHardRejection.Should().BeFalse();
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void GreatBritainRelease_ScoresAboveZero()
    {
        var score = _scorer.CalculateMatchScore(
            "MotoGP 2026 Round12 Great Britain Sprint TNT WEB-DL 1080p H264 DDP5 1 English-MWR",
            UnitedKingdomSprint());

        score.Should().BeGreaterThan(0,
            because: "Great Britain and United Kingdom name the same round");
    }

    [Theory]
    [InlineData("United Kingdom", "Great Britain")]
    [InlineData("United Kingdom", "British")]
    [InlineData("United Kingdom", "Silverstone")]
    [InlineData("Great Britain", "United Kingdom")]
    public void BritainNames_AreTreatedAsTheSamePlace(string eventTitle, string releaseTitle)
    {
        SearchNormalizationService.IsReleaseMatch(releaseTitle, eventTitle)
            .Should().BeTrue("both name the Silverstone round");
    }
}
