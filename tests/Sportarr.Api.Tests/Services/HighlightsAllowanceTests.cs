using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// League.AllowHighlights opt-in: highlights releases are hard-rejected as
/// non-event content by default (right for most sports), but short-format
/// sports like sumo ship each day as a multi-hour Live cut and a short
/// Highlights cut, and users may specifically want the highlights. The
/// exemption applies ONLY to the Highlights label - every other non-event
/// content type (press conferences, recaps, condensed cuts) must stay
/// hard-rejected even when the league opts in.
/// </summary>
public class HighlightsAllowanceTests
{
    private readonly ReleaseMatchingService _svc;

    public HighlightsAllowanceTests()
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

    private static Event SumoEvent(bool allowHighlights) => new()
    {
        Id = 1,
        Title = "Hatsu Basho Day 15",
        Sport = "Wrestling",
        EventDate = new DateTime(2026, 1, 25, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "Grand Sumo", Sport = "Wrestling", AllowHighlights = allowHighlights }
    };

    private const string HighlightsTitle = "Grand.Sumo.Highlights.2026.Hatsu.Basho.Day.15.720p.HDTV.H.264-JFF";
    private const string LiveTitle = "Grand.Sumo.Live.2026.Hatsu.Basho.Day.15.720p.HDTV.H.264-JFF";

    [Fact]
    public void HighlightsRelease_IsHardRejected_WhenLeagueDoesNotOptIn()
    {
        var result = _svc.ValidateRelease(Rel(HighlightsTitle), SumoEvent(allowHighlights: false));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Non-event content"));
    }

    [Fact]
    public void HighlightsRelease_IsNotRejectedAsNonEventContent_WhenLeagueOptsIn()
    {
        var result = _svc.ValidateRelease(Rel(HighlightsTitle), SumoEvent(allowHighlights: true));

        result.Rejections.Should().NotContain(r => r.Contains("Non-event content"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LiveRelease_IsNeverTreatedAsNonEventContent(bool allowHighlights)
    {
        var result = _svc.ValidateRelease(Rel(LiveTitle), SumoEvent(allowHighlights));

        result.Rejections.Should().NotContain(r => r.Contains("Non-event content"));
    }

    [Theory]
    [InlineData("Grand.Sumo.2026.Hatsu.Basho.Day.15.Press.Conference.720p")]
    [InlineData("Grand.Sumo.2026.Hatsu.Basho.Day.15.Recap.720p")]
    [InlineData("Grand.Sumo.2026.Hatsu.Basho.Day.15.Condensed.720p")]
    public void OtherNonEventContent_StaysRejected_EvenWhenLeagueOptsIn(string title)
    {
        var result = _svc.ValidateRelease(Rel(title), SumoEvent(allowHighlights: true));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Non-event content"));
    }
}
