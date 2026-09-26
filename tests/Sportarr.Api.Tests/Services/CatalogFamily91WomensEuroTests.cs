using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily91WomensEuroTests
{
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Theory]
    [InlineData("UEFA Womens Euro 2025 07 02 Group A Iceland vs Finland 1080p WEB AAC H264")]
    [InlineData("Womens Euro 2025 07 02 MD1 Iceland vs Finland Full Broadcast 720p x264 EN ITV")]
    [InlineData("Womens UEFA Euro 2025 07 02 Group A Iceland Vs Finland HDTV H264 DARKSPORT 1080p EN")]
    [InlineData("Women's UEFA Euro 2025 Iceland vs Finland 02 07 720pEN60fps Fox")]
    public void IcelandMatchAcceptsObservedCountryOnlyTeamNames(string title)
    {
        var validation = Matcher.ValidateRelease(Release(title), IcelandMatch());

        validation.IsMatch.Should().BeTrue(string.Join("; ", validation.Rejections));
        Scorer.CalculateMatchScore(title, IcelandMatch())
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("UEFA Womens Euro 2025 07 27 Final England vs Spain 1080p WEB AAC H264")]
    [InlineData("Womens Euro 2025 07 27 Final England vs Spain 720p50 x264 EN ITV")]
    [InlineData("Women's UEFA Euro Final 2025 England vs Spain 27 07 720pEN60fps Fox")]
    public void FinalAcceptsObservedCountryOnlyTeamNames(string title)
    {
        var validation = Matcher.ValidateRelease(Release(title), Final());

        validation.IsMatch.Should().BeTrue(string.Join("; ", validation.Rejections));
        Scorer.CalculateMatchScore(title, Final())
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("UEFA Euro 2025 07 27 Final England vs Spain 1080p")]
    [InlineData("UEFA Womens Nations League 2025 07 27 England vs Spain 1080p")]
    [InlineData("Womens Euro 2025 06 03 England vs Spain 1080p")]
    [InlineData("Womens Euro 2025 07 27 England vs Germany 1080p")]
    [InlineData("Womens Euro 2025 07 27 England vs Spain Pre Match 1080p")]
    public void FinalRejectsOtherCompetitionDateTeamOrPart(string title)
    {
        Matcher.ValidateRelease(Release(title), Final()).IsMatch.Should().BeFalse();
    }

    private static Event IcelandMatch() => TeamEvent(
        "Iceland Women vs Finland Women", "Iceland Women", "Finland Women", new DateTime(2025, 7, 2), "1");

    private static Event Final() => TeamEvent(
        "England Women vs Spain Women", "England Women", "Spain Women", new DateTime(2025, 7, 27), "200");

    private static Event TeamEvent(string title, string home, string away, DateTime date, string round) => new()
    {
        Title = title,
        Sport = "Soccer",
        Season = "2025",
        EventDate = DateTime.SpecifyKind(date.AddHours(16), DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Round = round,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = "UEFA Womens Euro", Sport = "Soccer" }
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/release",
        Indexer = "Fixture",
        Protocol = "Torrent"
    };
}
