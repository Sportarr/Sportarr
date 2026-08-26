using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// "Early Prelims" contains "prelim", so a search for the regular prelims
/// scored an early-prelims release exactly as highly and could grab it
/// instead.
/// </summary>
public class PartRelevanceTests
{
    [Fact]
    public void An_early_prelims_release_does_not_win_a_prelims_search()
    {
        var wanted = PartRelevanceHelper.GetPartRelevanceScore(
            "UFC.300.Prelims.1080p.WEB.h264", "Prelims");
        var wrong = PartRelevanceHelper.GetPartRelevanceScore(
            "UFC.300.Early.Prelims.1080p.WEB.h264", "Prelims");

        wanted.Should().BeGreaterThan(wrong);
        wrong.Should().BeLessThan(0);
    }

    [Fact]
    public void A_prelims_release_does_not_win_an_early_prelims_search()
    {
        var wanted = PartRelevanceHelper.GetPartRelevanceScore(
            "UFC.300.Early.Prelims.1080p.WEB.h264", "Early Prelims");
        var wrong = PartRelevanceHelper.GetPartRelevanceScore(
            "UFC.300.Prelims.1080p.WEB.h264", "Early Prelims");

        wanted.Should().BeGreaterThan(wrong);
    }

    [Fact]
    public void The_main_card_still_outranks_the_prelims_when_nothing_was_asked_for()
    {
        PartRelevanceHelper.GetPartRelevanceScore("UFC.300.Main.Card.1080p", null)
            .Should().BeGreaterThan(
                PartRelevanceHelper.GetPartRelevanceScore("UFC.300.Prelims.1080p", null));
    }
}
