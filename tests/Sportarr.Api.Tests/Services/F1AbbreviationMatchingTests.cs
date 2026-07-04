using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Cross-sport-detection coverage for issue #101: a release using the bare "F1"
/// abbreviation (common on French-language releases) was hard rejected as a
/// "different sport" against its own Formula 1 event. The internal sport label
/// "Formula1" is a fused word (kept distinct from "Formula2"/"Formula3"), so it
/// never literally appeared in the real league name "Formula 1" (with a space),
/// and the reverse regex check only tolerated separator variations for the
/// "formula...1" spelled-out pattern, not the bare "f1" abbreviation pattern -
/// so this combination had no escape hatch at all in either matcher.
/// </summary>
public class F1AbbreviationMatchingTests
{
    private readonly ReleaseMatchingService _matchingSvc;
    private readonly ReleaseMatchScorer _scorer = new();

    public F1AbbreviationMatchingTests()
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

    private static Event ChineseGrandPrix() => new()
    {
        Id = 1,
        Title = "Chinese Grand Prix",
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 3, 22, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" }
    };

    // The reporter's exact release title.
    private const string BareF1ReleaseTitle = "F1.Grand.Prix.De.Chine.2026.Course.2026.VOF.1080p.WEB.AAC.2.0.x264";

    [Fact]
    public void BareF1Abbreviation_DoesNotFalselyTriggerDifferentSportRejection_ReleaseMatchingService()
    {
        var result = _matchingSvc.ValidateRelease(Rel(BareF1ReleaseTitle), ChineseGrandPrix());

        result.Rejections.Should().NotContain(r => r.Contains("Different sport"));
        result.IsHardRejection.Should().BeFalse();
    }

    [Fact]
    public void BareF1Abbreviation_DoesNotFalselyTriggerDifferentSportRejection_ReleaseMatchScorer()
    {
        var score = _scorer.CalculateMatchScore(BareF1ReleaseTitle, ChineseGrandPrix());

        score.Should().BeGreaterThan(0, because: "a bare 'F1' release abbreviation must still match its own Formula 1 event");
    }

    [Fact]
    public void GenuinelyDifferentMotorsportSeries_IsStillHardRejected_ReleaseMatchingService()
    {
        // The separator-insensitive fallback must not become a blanket pass -
        // a real cross-series release (Moto3) must still be rejected.
        var result = _matchingSvc.ValidateRelease(
            Rel("Moto3.2026.China.Race.SkyF1HD.1080p"), ChineseGrandPrix());

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Different sport"));
    }

    [Fact]
    public void GenuinelyDifferentMotorsportSeries_IsStillHardRejected_ReleaseMatchScorer()
    {
        var score = _scorer.CalculateMatchScore("Moto3.2026.China.Race.SkyF1HD.1080p", ChineseGrandPrix());

        score.Should().Be(0, because: "a Moto3 release must not match the F1 Chinese Grand Prix");
    }
}
