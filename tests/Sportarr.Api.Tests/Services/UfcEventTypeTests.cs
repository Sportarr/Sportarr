using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #388: The Ultimate Fighter finales are fight cards, so they belong
/// with Fight Night in the UFC event type filter. Weekly episodes do not.
/// </summary>
public class UfcEventTypeTests
{
    [Theory]
    [InlineData("The Ultimate Fighter 28 Finale")]
    [InlineData("Ultimate Fighter 24 Finale Johnson vs Elliott")]
    [InlineData("The Ultimate Fighter Team McGregor vs Team Faber Finale")]
    [InlineData("UFC.The.Ultimate.Fighter.27.Finale.1080p.WEB.h264")]
    [InlineData("TUF 28 Finale Prelims")]
    public void AUltimateFighterFinaleIsAFightNight(string title)
    {
        EventPartDetector.DetectUfcEventType(title).Should().Be(EventPartDetector.UfcEventType.FightNight);
    }

    [Theory]
    [InlineData("The Ultimate Fighter S32E01 720p WEB h264")]
    [InlineData("The Ultimate Fighter Season 32 Episode 4")]
    [InlineData("The.Ultimate.Fighter.S28E14.Season.Finale.720p.WEB")]
    public void AWeeklyEpisodeIsNotACard(string title)
    {
        EventPartDetector.DetectUfcEventType(title).Should().Be(EventPartDetector.UfcEventType.Other);
    }

    [Theory]
    [InlineData("UFC 310", EventPartDetector.UfcEventType.PPV)]
    [InlineData("UFC Fight Night 262", EventPartDetector.UfcEventType.FightNight)]
    [InlineData("Dana Whites Contender Series season 9 Week 9", EventPartDetector.UfcEventType.ContenderSeries)]
    public void OtherUfcTypesAreUnchanged(string title, EventPartDetector.UfcEventType expected)
    {
        EventPartDetector.DetectUfcEventType(title).Should().Be(expected);
    }

    [Fact]
    public void ATufFinaleKeepsTheFullFightCardParts()
    {
        // TUF finales had early prelims, which Fight Night parts do not have.
        EventPartDetector.IsFightNightStyleEvent("The Ultimate Fighter 28 Finale", "UFC").Should().BeFalse();
        EventPartDetector.IsFightNightStyleEvent("UFC Fight Night 262", "UFC").Should().BeTrue();
    }
}
