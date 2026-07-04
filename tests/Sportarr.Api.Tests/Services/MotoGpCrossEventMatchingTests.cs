using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #156: a Hungarian MotoGP sprint release was grabbed for
/// Catalonia and France events. Three compounding gaps, all using the
/// reporter's exact release titles:
///
/// 1. "sprint" counted as a location key term, so ANY sprint release earned
///    the location-variation bonus against ANY sprint event.
/// 2. The "[FRENCH]" audio-language tag counted as France location evidence
///    both for the bonus and for the location guard's escape hatch, while
///    "Hongrie" (French for Hungary) was invisible to the English-only
///    location table - so a French-language Hungarian GP release looked MORE
///    like a France event than a Hungary one.
/// 3. "Catalonia" wasn't in the location table at all (only "Catalunya"),
///    so the mismatch guard couldn't resolve the event side and skipped.
/// </summary>
public class MotoGpCrossEventMatchingTests
{
    private readonly ReleaseMatchingService _svc;

    public MotoGpCrossEventMatchingTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _svc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static Event MotoGpEvent(string title, DateTime date) => new()
    {
        Id = 1,
        Title = title,
        Sport = "Motorsport",
        EventDate = date,
        League = new League { Id = 1, Name = "MotoGP", Sport = "Motorsport" }
    };

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private const string HungarySprintRelease = "MotoGP.2026.Hungary.Sprint.Race.1080p.WEB.h264-BILLIE";
    private const string FrenchLanguageHungarySprint = "[FRENCH] MotoGP Grand Prix De Hongrie 2026 Course Sprint VFF 1080p WEB EAC3 x264-PiXeL";
    private const string FrenchLanguageHungaryPractice = "[FRENCH] MotoGP Grand Prix De Hongrie 2026 Essais Libres 2 VFF 1080p WEB EAC3 x264-PiXeL";

    [Fact]
    public void HungaryRelease_IsHardRejectedAgainstCataloniaEvent()
    {
        var result = _svc.ValidateRelease(
            Rel(HungarySprintRelease),
            MotoGpEvent("Catalonia Sprint Race", new DateTime(2026, 5, 23, 12, 0, 0, DateTimeKind.Utc)));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Location mismatch"));
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void FrenchLanguageHungaryRelease_IsHardRejectedAgainstFranceSprintEvent()
    {
        // The FRENCH audio tag must not stand in for France, and Hongrie must
        // resolve to Hungary so the guard sees the real location.
        var result = _svc.ValidateRelease(
            Rel(FrenchLanguageHungarySprint),
            MotoGpEvent("France Sprint Race", new DateTime(2026, 5, 9, 12, 0, 0, DateTimeKind.Utc)));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Location mismatch"));
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void FrenchLanguageHungaryRelease_IsHardRejectedAgainstFranceGpEvent()
    {
        var result = _svc.ValidateRelease(
            Rel(FrenchLanguageHungaryPractice),
            MotoGpEvent("France GP", new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc)));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Location mismatch"));
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void HungaryRelease_StillMatchesTheHungaryEvent()
    {
        var result = _svc.ValidateRelease(
            Rel(HungarySprintRelease),
            MotoGpEvent("Hungary Sprint Race", new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc)));

        result.IsHardRejection.Should().BeFalse();
        result.IsMatch.Should().BeTrue();
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void FrenchLanguageTagAlone_DoesNotTriggerRejectionForFranceEvent()
    {
        // A genuinely French-race release in French audio: FRENCH appears as a
        // language tag AND the release names no other location. The weak
        // evidence must keep the release safe from rejection.
        var result = _svc.ValidateRelease(
            Rel("[FRENCH] MotoGP Grand Prix De France 2026 Course Sprint VFF 1080p WEB EAC3 x264-PiXeL"),
            MotoGpEvent("France Sprint Race", new DateTime(2026, 5, 9, 12, 0, 0, DateTimeKind.Utc)));

        result.Rejections.Should().NotContain(r => r.Contains("Location mismatch"));
        result.IsHardRejection.Should().BeFalse();
    }

    [Fact]
    public void CatalunyaRelease_IsNotRejectedAgainstCataloniaEvent()
    {
        // Alternate spellings of the same place must stay compatible.
        var result = _svc.ValidateRelease(
            Rel("MotoGP.2026.Catalunya.Sprint.Race.1080p.WEB.h264-BILLIE"),
            MotoGpEvent("Catalonia Sprint Race", new DateTime(2026, 5, 23, 12, 0, 0, DateTimeKind.Utc)));

        result.Rejections.Should().NotContain(r => r.Contains("Location mismatch"));
        result.IsHardRejection.Should().BeFalse();
        result.IsMatch.Should().BeTrue();
    }
}
