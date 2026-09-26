using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily63SearchTests
{
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    private const string ReturnLeg =
        "Copa Libertadores 2026 Flamengo vs Independiente del Valle 18 09 1080p30fps EN beIN";
    private const string FirstLeg =
        "Copa Libertadores 2026 Independiente del Valle vs Flamengo 11 09 1080p30fps EN beIN";

    [Fact]
    public void VerifiedLibertadoresUtcDateCanIdentifyTheObservedReturnLeg()
    {
        var evt = ReturnFixture();
        var result = Matcher.ValidateRelease(Release(ReturnLeg), evt);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        Scorer.CalculateMatchScore(ReturnLeg, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        EventDateMatchContext.ShouldLoadPeers(evt).Should().BeTrue();
    }

    [Theory]
    [InlineData(FirstLeg)]
    [InlineData("Copa Libertadores 2026 Independiente del Valle vs Flamengo 10 09 720pEN60fps bEIN")]
    [InlineData("Copa Libertadores Femenina 2026 Flamengo vs Independiente del Valle 18 09 1080p")]
    [InlineData("Copa Libertadores Feminina 2026 Flamengo vs Independiente del Valle 18 09 1080p")]
    [InlineData("Copa Libertadores U20 2026 Flamengo vs Independiente del Valle 18 09 1080p")]
    [InlineData("Copa Libertadores Sub-20 2026 Flamengo vs Independiente del Valle 18 09 1080p")]
    public void OtherFixtureOrCompetitionCannotFillTheReturnLeg(string title)
    {
        Matcher.ValidateRelease(Release(title), ReturnFixture()).IsMatch.Should().BeFalse();
        Scorer.CalculateMatchScore(title, ReturnFixture())
            .Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("Copa Libertadores Feminina 2026 Flamengo vs Independiente del Valle 18 09 1080p", "Copa Libertadores Femenina")]
    [InlineData("Copa Libertadores Sub-20 2026 Flamengo vs Independiente del Valle 18 09 1080p", "Copa Libertadores U20")]
    public void EquivalentParticipantLabelsKeepTheirOwnCategory(string title, string leagueName)
    {
        var evt = ReturnFixture();
        evt.League!.Name = leagueName;

        SearchNormalizationService.HasParticipantCategoryConflict(title, evt).Should().BeFalse();
    }

    [Fact]
    public void AnotherFixtureOnTheReleaseDayBlocksTheUtcDateWindow()
    {
        var evt = ReturnFixture();
        var another = ReturnFixture();
        another.Id = 2;
        another.ExternalId = "ev-other";
        another.BroadcastDate = new DateTime(2026, 9, 18);
        another.EventDate = new DateTime(2026, 9, 18, 21, 0, 0, DateTimeKind.Utc);

        Matcher.ValidateRelease(Release(ReturnLeg), evt, datePeers: [another])
            .IsMatch.Should().BeFalse();
    }

    private static Event ReturnFixture() => new()
    {
        Id = 1,
        ExternalId = "ev-2590542",
        Title = "Flamengo vs Independiente del Valle",
        Sport = "Soccer",
        Season = "2026",
        Round = "125",
        EventDate = new DateTime(2026, 9, 18, 0, 30, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2026, 9, 17),
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = "Flamengo",
        AwayTeamName = "Independiente del Valle",
        LeagueId = 1,
        League = new League { Id = 1, Name = "Copa Libertadores", Sport = "Soccer" }
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture"
    };
}
