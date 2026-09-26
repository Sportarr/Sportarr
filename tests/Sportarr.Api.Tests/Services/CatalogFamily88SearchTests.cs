using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily88SearchTests
{
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Theory]
    [InlineData("FIFA Womens World Cup 2023 07 20 MD1 New Zealand vs Norway 540p EN BBC")]
    [InlineData("FIFA Women's World Cup 2023 New Zealand vs Norway 20 07 720pEN60fps Fox")]
    [InlineData("FIFA.Womens.World.Cup.2023.Group.A.New.Zealand.Vs.Norway.1080p.HDTV.H264-DARKSPORT")]
    public void WomensWorldCupAcceptsObservedCountryOnlyTeamNames(string title)
    {
        var evt = WomensWorldCupOpener();

        var validation = Matcher.ValidateRelease(Release(title), evt);

        validation.IsMatch.Should().BeTrue(string.Join("; ", validation.Rejections));
        Scorer.CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void WomensWorldCupFinalAcceptsObservedCountryOnlyTeamNames()
    {
        const string title = "FIFA Womens World Cup 2023 08 20 Final Spain vs England 1080p 25fps EN BBC";
        var evt = TeamEvent("FIFA Womens World Cup", "Spain Women vs England Women",
            "Spain Women", "England Women", "Soccer", new DateTime(2023, 8, 20), "200");

        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("FIFA World Cup 2023 07 20 New Zealand vs Norway 1080p")]
    [InlineData("FIFA U20 Womens World Cup 2023 07 20 New Zealand vs Norway 1080p")]
    [InlineData("FIFA Womens World Cup 2023 07 21 New Zealand vs Norway 1080p")]
    public void WomensWorldCupDoesNotTakeOtherCompetitionOrDate(string title)
    {
        Matcher.ValidateRelease(Release(title), WomensWorldCupOpener()).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void RugbyWorldCupRejectsObservedRugbyChampionshipRelease()
    {
        const string title = "Rugby Championship 2023 R02 720p New Zealand vs South Africa EN SKY";
        var evt = RugbyWorldCupFinal();

        var validation = Matcher.ValidateRelease(Release(title), evt);

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Fact]
    public void RugbyWorldCupKeepsObservedFinalRelease()
    {
        const string title = "Rugby World Cup 2023 10 28 New Zealand Vs South Africa 1080p WEB H264 SPORTSNET";
        var evt = RugbyWorldCupFinal();

        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void RugbyUnionWorldCupLabelDoesNotMatchRugbyChampionship()
    {
        const string title = "Rugby Union World Cup 2023 09 08 France vs New Zealand ITV Full Broadcast 576p x264";
        var evt = TeamEvent("Rugby Championship", "France Rugby vs New Zealand Rugby",
            "France Rugby", "New Zealand Rugby", "Rugby", new DateTime(2023, 9, 8), "1");

        var validation = Matcher.ValidateRelease(Release(title), evt);

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    private static Event WomensWorldCupOpener() => TeamEvent(
        "FIFA Womens World Cup", "New Zealand Women vs Norway Women",
        "New Zealand Women", "Norway Women", "Soccer", new DateTime(2023, 7, 20), "1");

    private static Event RugbyWorldCupFinal() => TeamEvent(
        "Rugby World Cup", "New Zealand Rugby vs South Africa Rugby",
        "New Zealand Rugby", "South Africa Rugby", "Rugby", new DateTime(2023, 10, 28), "200");

    private static Event TeamEvent(string league, string title, string home, string away,
        string sport, DateTime date, string round) => new()
    {
        Title = title,
        Sport = sport,
        Season = date.Year.ToString(),
        EventDate = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Round = round,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = league, Sport = sport }
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
