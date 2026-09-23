using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for the Formula E city-vs-country report. The metadata source titles
/// rounds by host city ("London ePrix", venue "London ExCeL Circuit") while
/// release groups name them by country ("Great.Britain"). The location hierarchy
/// and the alias map in CheckForDifferentLocation did not contain Formula E host
/// cities, so the country in the release was classified as a different location
/// and every release for the round was hard-rejected (score 0). Uses the
/// reporter's release titles. Companion to MotoGpBritainReleaseMatchingTests
/// (#228).
/// </summary>
public class FormulaECityVenueReleaseMatchingTests
{
    private readonly ReleaseMatchingService _matchingSvc;
    private readonly ReleaseMatchScorer _scorer = new();

    public FormulaECityVenueReleaseMatchingTests()
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

    private static Event LondonEPrix(string title = "London ePrix", string? round = null) => new()
    {
        Id = 1,
        Title = title,
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc),
        Location = "GB",
        Venue = "London ExCeL Circuit",
        Round = round ?? "17",
        Season = "2025-2026",
        League = new League { Id = 1, Name = "Formula E", Sport = "Motorsport" }
    };

    private static Event TokyoEPrix() => new()
    {
        Id = 2,
        Title = "Tokyo ePrix",
        Sport = "Motorsport",
        EventDate = new DateTime(2025, 7, 26, 12, 0, 0, DateTimeKind.Utc),
        Location = "JP",
        Venue = "Tokyo Street Circuit",
        Round = "8",
        Season = "2024-2025",
        League = new League { Id = 1, Name = "Formula E", Sport = "Motorsport" }
    };

    [Theory]
    [InlineData("FormulaE.2026.Round17.Great.Britain.Race.STAN.WEB-DL.1080p.H264.English-MWR", "London ePrix")]
    [InlineData("FormulaE.2026.Round17.Great.Britain.Qualifying.STAN.WEB-DL.1080p.H264.English-MWR", "London ePrix - Qualifying")]
    [InlineData("FormulaE.2026.Round17.Great.Britain.FP3.STAN.WEB-DL.1080p.H264.English-MWR", "London ePrix - Free Practice 3")]
    public void GreatBritainRelease_MatchesLondonEPrixEvent(string releaseTitle, string eventTitle)
    {
        var result = _matchingSvc.ValidateRelease(Rel(releaseTitle), LondonEPrix(eventTitle));

        result.IsHardRejection.Should().BeFalse(
            because: "Great Britain is the country the London ePrix is held in");
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void GreatBritainRelease_MeetsProductionMatchThreshold()
    {
        // AutomaticSearchService approves a release only when
        // CalculateMatchScore >= ReleaseMatchScorer.MinimumMatchScore.
        // Before the alias fix the location conflict returned -50 and the
        // total score was 0.
        var score = _scorer.CalculateMatchScore(
            "FormulaE.2026.Round17.Great.Britain.Race.STAN.WEB-DL.1080p.H264.English-MWR",
            LondonEPrix());

        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore,
            because: "Great Britain names the country of the London ePrix");
    }

    [Fact]
    public void TokyoRelease_MeetsProductionMatchThreshold()
    {
        var score = _scorer.CalculateMatchScore(
            "FormulaE.2025.Round08.Tokyo.Race.STAN.WEB-DL.1080p.h264.English-MWR",
            TokyoEPrix());

        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore,
            because: "Tokyo names the host city of the Tokyo ePrix");
    }

    [Fact]
    public void TokyoRelease_MatchesTokyoEPrixEvent()
    {
        var result = _matchingSvc.ValidateRelease(
            Rel("FormulaE.2025.Round08.Tokyo.Race.STAN.WEB-DL.1080p.h264.English-MWR"),
            TokyoEPrix());

        result.IsHardRejection.Should().BeFalse();
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void ChinaRelease_MatchesSanyaEPrixEvent()
    {
        var sanya = new Event
        {
            Id = 3,
            Title = "Sanya ePrix",
            Sport = "Motorsport",
            EventDate = new DateTime(2027, 2, 13, 12, 0, 0, DateTimeKind.Utc),
            Location = "CN",
            Venue = "Haitang Bay Circuit",
            Round = "5",
            Season = "2026-2027",
            League = new League { Id = 1, Name = "Formula E", Sport = "Motorsport" }
        };

        var result = _matchingSvc.ValidateRelease(
            Rel("FormulaE.2027.Round05.China.Race.STAN.WEB-DL.1080p.H264.English-MWR"),
            sanya);

        result.IsHardRejection.Should().BeFalse(
            because: "China is the country the Sanya ePrix is held in");

        var score = _scorer.CalculateMatchScore(
            "FormulaE.2027.Round05.China.Race.STAN.WEB-DL.1080p.H264.English-MWR",
            sanya);
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore,
            because: "China names the country of the Sanya ePrix");
    }

    [Fact]
    public void WrongCountryRelease_IsHardRejected()
    {
        // Monaco is a known location that is not Britain.
        var releaseTitle = "FormulaE.2026.Round10.Monaco.Race.STAN.WEB-DL.1080p.H264.English-MWR";
        var result = _matchingSvc.ValidateRelease(Rel(releaseTitle), LondonEPrix());

        result.IsHardRejection.Should().BeTrue(
            because: "Monaco is a different location than London");

        _scorer.CalculateMatchScore(releaseTitle, LondonEPrix())
            .Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Fact]
    public void SameCountryDifferentSeriesRelease_IsRejected()
    {
        // Great Britain hosts both the London ePrix and MotoGP's Silverstone
        // round. Once London resolves to Britain, the country-level conflict
        // no longer fires for this pair. The motorsport series check, the
        // round comparison, and the score threshold must reject it instead.
        var releaseTitle = "MotoGP.2026.Round12.Great.Britain.Sprint.TNT.WEB-DL.1080p.H264.DDP5.1.English-MWR";

        _scorer.CalculateMatchScore(releaseTitle, LondonEPrix())
            .Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore,
                because: "a MotoGP Silverstone release is not the London ePrix");
    }

    [Fact]
    public void SameCountryWrongRoundRelease_IsRejected()
    {
        // Round 16 is also Great Britain. Same country and series, but a
        // different round than the Round 17 event.
        var releaseTitle = "FormulaE.2026.Round16.Great.Britain.Race.STAN.WEB-DL.1080p.H264.English-MWR";

        _scorer.CalculateMatchScore(releaseTitle, LondonEPrix())
            .Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore,
                because: "the Round 16 race is a different event than the Round 17 race");
    }
}
