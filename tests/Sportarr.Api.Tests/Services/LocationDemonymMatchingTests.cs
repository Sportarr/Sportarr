using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// End-to-end matching coverage for issue #98: a release naming the correct
/// event location was still hard rejected because a scene language tag
/// elsewhere in the title doubles as a country demonym - "FRENCH" (the audio
/// language) aliases to the France location table, and "China" (the actual,
/// correct race location) rarely appears alone in Sportarr's own event titles
/// (which use the demonym "Chinese Grand Prix"). Uses the reporter's exact
/// release title. Not caught by any existing test before this - the fix that
/// added the location-escape-hatch (2026-06-29) shipped without one.
/// </summary>
public class LocationDemonymMatchingTests
{
    private readonly ReleaseMatchingService _svc;

    public LocationDemonymMatchingTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _svc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
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
        Title = "Chinese Grand Prix - Race",
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" }
    };

    [Fact]
    public void FrenchLanguageTag_DoesNotFalselyConflictWithCorrectLocation()
    {
        // The reporter's exact release title. "FRENCH" here means the audio
        // language, not the French Grand Prix - the release clearly says
        // "China" (the event's own location) too.
        var result = _svc.ValidateRelease(
            Rel("Formula1.2026.China.Grand.Prix.FRENCH.1080p.WEB.x264"), ChineseGrandPrix());

        result.Rejections.Should().NotContain(r => r.Contains("Location mismatch"));
        result.IsHardRejection.Should().BeFalse();
    }

    [Fact]
    public void GenuinelyWrongLocation_IsStillHardRejected()
    {
        // A release for a different race entirely (Monaco) must still be
        // rejected against the Chinese Grand Prix - the escape hatch must not
        // become a blanket pass for any language-tagged release.
        var result = _svc.ValidateRelease(
            Rel("Formula1.2026.Monaco.Grand.Prix.FRENCH.1080p.WEB.x264"), ChineseGrandPrix());

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Location mismatch"));
    }
}
