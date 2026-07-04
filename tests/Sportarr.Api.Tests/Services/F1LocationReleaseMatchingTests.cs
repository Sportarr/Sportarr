using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// End-to-end matching coverage for issue #103: F1 releases naming the Grand
/// Prix location instead of the round number (e.g. "BILLIE"-group releases)
/// were never found, because the F1 file-name pattern had no location
/// extractor, the query builder never searched by location, and even when a
/// location-based query did return the release, its match confidence landed
/// below the default threshold. The location extractor and location-aware
/// query generation (EventQueryServiceMotorsportTests) already shipped; this
/// covers the remaining leg - that a parsed, queried BILLIE-style release
/// actually clears the confidence threshold against the event it belongs to.
/// Uses the reporter's exact release title.
/// </summary>
public class F1LocationReleaseMatchingTests
{
    private readonly ReleaseMatchingService _matchingSvc;
    private readonly ReleaseMatchScorer _scorer = new();

    public F1LocationReleaseMatchingTests()
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

    private static Event ChineseGpQualifying() => new()
    {
        Id = 1,
        Title = "Chinese Grand Prix - Qualifying",
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 3, 21, 7, 0, 0, DateTimeKind.Utc),
        Location = "China",
        Round = "2",
        League = new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" }
    };

    // The reporter's exact release title.
    private const string BillieReleaseTitle = "Formula1.2026.China.Grand.Prix.Qualifying.DV.HDR.2160p.WEB.h265-BILLIE";

    [Fact]
    public void LocationBasedRelease_ClearsMinimumMatchConfidence()
    {
        var result = _matchingSvc.ValidateRelease(Rel(BillieReleaseTitle), ChineseGpQualifying());

        result.IsHardRejection.Should().BeFalse();
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void LocationBasedRelease_ScoresAboveZero()
    {
        var score = _scorer.CalculateMatchScore(BillieReleaseTitle, ChineseGpQualifying());

        score.Should().BeGreaterThan(0, because: "a location-named F1 release must still match its round-numbered event");
    }
}
