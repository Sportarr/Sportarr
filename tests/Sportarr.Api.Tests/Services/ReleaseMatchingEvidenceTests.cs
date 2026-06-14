using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Insufficient-evidence guard: a release identified only by year or
/// season/episode (and/or league name) must not match a specific event,
/// because indexers number releases off IMDB/TheTVDB and that numbering does
/// not map to Sportarr's per-event numbering. Field case: a bare
/// "Formula1 S2026E38" was grabbed for the wrong Grand Prix.
/// </summary>
public class ReleaseMatchingEvidenceTests
{
    private readonly ReleaseMatchingService _svc;

    public ReleaseMatchingEvidenceTests()
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

    [Fact]
    public void BareSeasonEpisodeRelease_IsRejectedForInsufficientEvidence()
    {
        // The grabbed release title carried no date/location/session — only
        // "S2026E38". The indexer's E38 (TheTVDB) is Monaco qualifying; Sportarr's
        // E38 is Dutch GP Practice 1. The two numbering schemes must not be trusted.
        var evt = new Event
        {
            Id = 1,
            Title = "Dutch Grand Prix Practice 1",
            Sport = "Motorsport",
            EventDate = new DateTime(2026, 8, 21, 10, 30, 0, DateTimeKind.Utc),
            EpisodeNumber = 38,
            League = new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" }
        };

        var result = _svc.ValidateRelease(
            Rel("Formula.1.1950.S2026E38.VFF.1080p.WEB.EAC3.2.0.x264-THESYNDiCATE"), evt);

        result.IsHardRejection.Should().BeTrue();
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void TitledMotorsportRelease_IsNotRejectedForLackOfEvidence()
    {
        // Same event, but a properly-titled release (GP name + session) is fine.
        var evt = new Event
        {
            Id = 1,
            Title = "Dutch Grand Prix Practice 1",
            Sport = "Motorsport",
            EventDate = new DateTime(2026, 8, 21, 10, 30, 0, DateTimeKind.Utc),
            EpisodeNumber = 38,
            League = new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" }
        };

        var result = _svc.ValidateRelease(
            Rel("Formula1.2026.Dutch.Grand.Prix.Practice.1.1080p.WEB.x264-GROUP"), evt);

        result.Rejections.Should().NotContain(r => r.StartsWith("Insufficient evidence"));
    }

    [Fact]
    public void TournamentReleaseIdentifiedByTitle_IsNotRejectedForLackOfEvidence()
    {
        // Individual/tournament sports identify by title (no date/round/team token).
        // The event's distinctive words appearing in the release count as evidence.
        var evt = new Event
        {
            Id = 1,
            Title = "Wimbledon Final",
            Sport = "Tennis",
            EventDate = new DateTime(2026, 7, 13, 13, 0, 0, DateTimeKind.Utc),
            League = new League { Id = 1, Name = "Wimbledon", Sport = "Tennis" }
        };

        var result = _svc.ValidateRelease(
            Rel("Wimbledon.2026.Final.1080p.WEB.x264-GROUP"), evt);

        result.Rejections.Should().NotContain(r => r.StartsWith("Insufficient evidence"));
    }
}
