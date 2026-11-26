using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #462: the MotoGP "Gear Up" pre-show has no session word,
/// so the "no session indicator, assume Race" branch accepted it for the Race
/// event. It is now rejected as non-event content, like other pre-shows.
/// </summary>
public class MotoGpGearUpReleaseTests
{
    private readonly ReleaseMatchingService _matching = new(
        Mock.Of<ILogger<ReleaseMatchingService>>(),
        new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
        new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()));

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event CatalunyaRace() => new()
    {
        Id = 1,
        Title = "Catalonia GP - Race",
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc),
        Location = "Catalonia",
        Round = "6",
        Season = "2026",
        Monitored = true,
        League = new League { Id = 1, Name = "MotoGP", Sport = "Motorsport" }
    };

    [Theory]
    [InlineData("MotoGP.2026.Round06.Spain.Catalunya.Gear.Up.WEB-DL.1080p.H264.English-MWR")]
    [InlineData("MotoGP.2026.Round06.Catalonia.Gear.Up.WEB-DL.1080p.H264.English-MWR")]
    [InlineData("MotoGP 2026 Round06 Catalonia Gear-Up WEB-DL 1080p H264 English-MWR")]
    [InlineData("MotoGP.2026.Round06.Catalonia.GearUp.WEB-DL.1080p.H264.English-MWR")]
    public void GearUpPreShow_CannotFillMonitoredRace(string title)
    {
        var result = _matching.ValidateRelease(Rel(title), CatalunyaRace());

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Non-event content"));
    }

    [Fact]
    public void RaceRelease_StillMatchesMonitoredRace()
    {
        var result = _matching.ValidateRelease(
            Rel("MotoGP.2026.Round06.Catalonia.Race.WEB-DL.1080p.H264.English-MWR"),
            CatalunyaRace());

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.IsHardRejection.Should().BeFalse();
    }

    [Theory]
    [InlineData("MotoGP.2026.Round06.Catalonia.Gear.Up.WEB-DL.1080p.H264.English-MWR", "Gear Up Pre-show")]
    [InlineData("MotoGP.2026.Round06.Catalonia.Race.WEB-DL.1080p.H264.English-MWR", null)]
    [InlineData("MotoGP.2026.Round06.Catalonia.Sprint.WEB-DL.1080p.H264.English-MWR", null)]
    public void GearUp_IsDetectedAsNonEventContent(string title, string? expected)
    {
        ReleaseMatchingService.DetectNonEventContentUncached(title).Should().Be(expected);
    }
}
