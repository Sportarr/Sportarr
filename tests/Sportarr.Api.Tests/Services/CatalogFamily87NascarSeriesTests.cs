using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily87NascarSeriesTests
{
    private const string ObservedCupRelease =
        "NASCAR Cup Series 2026 Round01 Daytona 500 Race 720p60fps EN FOX";
    private const string CupWithoutSeries =
        "NASCAR Cup 2026 Round01 Fresh From Florida 250 Race 720p60fps EN FOX";
    private const string XfinityWithoutSeries =
        "NASCAR Xfinity 2026 Round01 Fresh From Florida 250 Race 720p60fps EN FOX";
    private const string ArcaWithoutSeries =
        "NASCAR ARCA Menards 2026 Round01 Fresh From Florida 250 Race 720p60fps EN FOX";
    private const string CupWithArcaRaceTitle =
        "NASCAR Cup Series 2026 Round01 Fresh From Florida 250 Race 720p60fps EN FOX";

    [Theory]
    [InlineData(ObservedCupRelease)]
    [InlineData(CupWithoutSeries)]
    [InlineData(XfinityWithoutSeries)]
    [InlineData(ArcaWithoutSeries)]
    public void SearchMatchingRejectsOtherSeriesReleaseForTruckRace(string releaseTitle)
    {
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));

        var result = matcher.ValidateRelease(Release(releaseTitle), TruckEvent());

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Theory]
    [InlineData(ObservedCupRelease)]
    [InlineData(CupWithoutSeries)]
    [InlineData(XfinityWithoutSeries)]
    [InlineData(ArcaWithoutSeries)]
    public void SearchScoringRejectsOtherSeriesReleaseForTruckRace(string releaseTitle)
    {
        new ReleaseMatchScorer().CalculateMatchScore(releaseTitle, TruckEvent())
            .Should().Be(0);
    }

    [Theory]
    [InlineData(ObservedCupRelease)]
    [InlineData(CupWithoutSeries)]
    [InlineData(XfinityWithoutSeries)]
    [InlineData(ArcaWithoutSeries)]
    public void ImportScoringRejectsOtherSeriesReleaseForTruckRace(string releaseTitle)
    {
        using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var matcher = new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);
        var parsed = parser.Parse(releaseTitle);
        var evt = TruckEvent();

        matcher.ScoreMatch(parsed.EventTitle ?? releaseTitle, evt.Title, null, evt, parsed).Core
            .Should().BeLessOrEqualTo(0);
    }

    [Fact]
    public void SearchMatchingRejectsCupReleaseForArcaRace()
    {
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));

        var result = matcher.ValidateRelease(Release(CupWithArcaRaceTitle), ArcaEvent());

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void SearchScoringRejectsCupReleaseForArcaRace()
    {
        new ReleaseMatchScorer().CalculateMatchScore(CupWithArcaRaceTitle, ArcaEvent())
            .Should().Be(0);
    }

    [Fact]
    public void ImportScoringRejectsCupReleaseForArcaRace()
    {
        using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var matcher = new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);
        var parsed = parser.Parse(CupWithArcaRaceTitle);
        var evt = ArcaEvent();

        matcher.ScoreMatch(parsed.EventTitle ?? CupWithArcaRaceTitle, evt.Title, null, evt, parsed).Core
            .Should().BeLessOrEqualTo(0);
    }

    [Theory]
    [InlineData("NASCAR Craftsman Truck Series 2026 Round01 Fresh From Florida 250 Race 1080p WEB")]
    [InlineData("NASCAR Truck 2026 Round01 Fresh From Florida 250 Race 1080p WEB")]
    public void MatchingKeepsAnExplicitTruckRaceInItsOwnSeries(string truckRelease)
    {
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var evt = TruckEvent();

        matcher.ValidateRelease(Release(truckRelease), evt).IsHardRejection.Should().BeFalse();
        new ReleaseMatchScorer().CalculateMatchScore(truckRelease, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("NASCAR Truck 2026 Round01 Fresh From Florida 250 Race", "NASCAR Xfinity Series")]
    [InlineData("NASCAR O'Reilly 2026 Round01 Fresh From Florida 250 Race", "NASCAR Truck Series")]
    [InlineData("NASCAR ARCA 2026 Round01 Fresh From Florida 250 Race", "NASCAR Cup Series")]
    public void ShortSeriesLabelsKeepSiblingLeaguesSeparate(string releaseTitle, string leagueName)
    {
        SearchNormalizationService.HasConflictingNascarSeries(
                releaseTitle, "Fresh From Florida 250", leagueName)
            .Should().BeTrue();
    }

    [Fact]
    public void ExplicitCupLabelOutweighsASecondarySeriesSponsorInRaceTitle()
    {
        SearchNormalizationService.HasConflictingNascarSeries(
                "NASCAR Cup Series 2026 Round01 Martinsville Xfinity 500 Race",
                "Martinsville 500", "NASCAR Cup Series")
            .Should().BeFalse();
    }

    [Fact]
    public void CupRaceNamedXfinity500IsNotASecondarySeriesRelease()
    {
        const string releaseTitle = "NASCAR Xfinity 500 2026 Round35 Race 1080p WEB";
        var evt = TruckEvent();
        evt.Title = "Xfinity 500";
        evt.Round = "35";
        evt.League!.Name = "NASCAR Cup Series";
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var searchMatcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance, parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var importMatcher = new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);
        var parsed = parser.Parse(releaseTitle);

        searchMatcher.ValidateRelease(Release(releaseTitle), evt).IsHardRejection.Should().BeFalse();
        new ReleaseMatchScorer().CalculateMatchScore(releaseTitle, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importMatcher.ScoreMatch(parsed.EventTitle ?? releaseTitle, evt.Title, null, evt, parsed).Core
            .Should().BeGreaterOrEqualTo(50);
    }

    [Fact]
    public void ExplicitXfinitySeriesStillConflictsWithCupRaceNamedXfinity500()
    {
        SearchNormalizationService.HasConflictingNascarSeries(
                "NASCAR Xfinity Series 2026 Round35 Xfinity 500 Race",
                "Xfinity 500", "NASCAR Cup Series")
            .Should().BeTrue();
    }

    [Fact]
    public void MatchingKeepsCupWithoutSeriesInItsOwnSeries()
    {
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var evt = TruckEvent();
        evt.League!.Name = "NASCAR Cup Series";

        matcher.ValidateRelease(Release(CupWithoutSeries), evt).IsHardRejection.Should().BeFalse();
        new ReleaseMatchScorer().CalculateMatchScore(CupWithoutSeries, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void MatchingKeepsAnExplicitArcaRaceInItsOwnSeries()
    {
        const string arcaRelease =
            "ARCA Menards Series 2026 Round01 Fresh From Florida 250 Race 1080p WEB";
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var evt = ArcaEvent();

        matcher.ValidateRelease(Release(arcaRelease), evt).IsHardRejection.Should().BeFalse();
        SearchNormalizationService.HasConflictingNascarSeries(arcaRelease, evt.Title, evt.League!.Name)
            .Should().BeFalse();
    }

    private static Event TruckEvent() => new()
    {
        Title = "Fresh From Florida 250",
        Sport = "Motorsport",
        Round = "1",
        Season = "2026",
        EventDate = new DateTime(2026, 2, 13, 23, 30, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2026, 2, 13),
        LeagueId = 1,
        League = new League { Name = "NASCAR Truck Series", Sport = "Motorsport" }
    };

    private static Event ArcaEvent()
    {
        var evt = TruckEvent();
        evt.League!.Name = "ARCA Menards Series";
        return evt;
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/release",
        Indexer = "Fixture",
        Protocol = "Torrent"
    };
}
