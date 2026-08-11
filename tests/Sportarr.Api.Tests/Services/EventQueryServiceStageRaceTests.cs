using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Stage races name the stage in the event title, and the metadata title is
/// always English. A user who wants releases in another language must be able
/// to read the stage number out of the title and write the stage word itself.
/// </summary>
public class EventQueryServiceStageRaceTests
{
    private static EventQueryService CreateService() =>
        new(NullLogger<EventQueryService>.Instance);

    private static Event CyclingEvent(string title, string? round = "26") => new()
    {
        Title = title,
        Sport = "Cycling",
        Season = "2026",
        EventDate = new DateTime(2026, 7, 4, 0, 0, 0, DateTimeKind.Utc),
        Round = round,
        League = new League { Name = "UCI World Tour", Sport = "Cycling" },
    };

    [Theory]
    [InlineData("Tour de France Stage 1", 1)]
    [InlineData("Tour de France Stage 16", 16)]
    [InlineData("Giro d'Italia Etappe 7", 7)]
    [InlineData("Some Race Leg 3", 3)]
    [InlineData("Tour de France STAGE 21", 21)]
    [InlineData("Tour de France Stage21", 21)]
    public void ExtractStageNumber_ReadsTrailingStage(string title, int expected)
    {
        EventQueryService.ExtractStageNumber(title).Should().Be(expected);
    }

    [Theory]
    [InlineData("Tour de France")]
    [InlineData("Austrian Grand Prix - Race")]
    [InlineData("Golf PGA Masters 2024 Round 4")]
    [InlineData("Stage 5 Highlights Special")]
    [InlineData("")]
    [InlineData(null)]
    public void ExtractStageNumber_ReturnsNullWhenNoTrailingStage(string? title)
    {
        EventQueryService.ExtractStageNumber(title).Should().BeNull();
    }

    [Fact]
    public void StripStageFromTitle_RemovesOnlyTheStageSuffix()
    {
        EventQueryService.StripStageFromTitle("Tour de France Stage 16").Should().Be("Tour de France");
        EventQueryService.StripStageFromTitle("Tour de France").Should().Be("Tour de France");
    }

    [Fact]
    public void StripStageFromTitle_KeepsGolfRoundTitlesIntact()
    {
        // Golf and motorsport use "Round", and {Round} already serves them.
        EventQueryService.StripStageFromTitle("Golf PGA Masters 2024 Round 4")
            .Should().Be("Golf PGA Masters 2024 Round 4");
    }

    [Fact]
    public void BuildQueryFromTemplate_GermanStageTemplate_ProducesGermanQuery()
    {
        // The reported problem: the title is always English, so a German
        // release named "Tour.De.France.2026.Etappe.16..." was unreachable.
        var query = CreateService().BuildQueryFromTemplate(
            "{EventName} {Year} Etappe {Stage} German",
            CyclingEvent("Tour de France Stage 16"));

        query.Should().Be("Tour de France 2026 Etappe 16 German");
    }

    [Fact]
    public void BuildQueryFromTemplate_StagePaddingVariants()
    {
        var evt = CyclingEvent("Tour de France Stage 1");
        var service = CreateService();

        service.BuildQueryFromTemplate("{Stage}", evt).Should().Be("1");
        service.BuildQueryFromTemplate("{Stage:0}", evt).Should().Be("1");
        service.BuildQueryFromTemplate("{Stage:00}", evt).Should().Be("01");
    }

    [Fact]
    public void BuildQueryFromTemplate_StageIsEmptyWhenTitleNamesNoStage()
    {
        var query = CreateService().BuildQueryFromTemplate(
            "{EventName} {Year} Etappe {Stage}",
            CyclingEvent("Tour de France"));

        query.Should().Be("Tour de France 2026 Etappe");
    }

    [Fact]
    public void BuildQueryFromTemplate_RoundStaysSeparateFromStage()
    {
        // Round carries a season-wide event index for these leagues, which is
        // why it can not name the stage. Both tokens must stay independent.
        var query = CreateService().BuildQueryFromTemplate(
            "{Round:0}|{Stage}",
            CyclingEvent("Tour de France Stage 1", round: "26"));

        query.Should().Be("26|1");
    }
}
