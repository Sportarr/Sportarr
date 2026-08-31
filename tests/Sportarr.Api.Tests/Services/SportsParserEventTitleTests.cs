using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The generic organization extractor built its event title from whatever
/// followed the organization, so a release named with a season/episode code
/// kept "S2026E135" and the separators around it. That noise stopped the
/// title matching the event it belonged to.
/// </summary>
public class SportsParserEventTitleTests
{
    private static readonly SportsFileNameParser Parser =
        new(NullLogger<SportsFileNameParser>.Instance);

    [Theory]
    [InlineData("MLB - S2026E135 - San Francisco Giants vs Houston Astros - HDTV-1080p",
                "MLB San Francisco Giants vs Houston Astros")]
    [InlineData("NBA - S2026E410 - Boston Celtics vs Miami Heat - WEB-DL-1080p",
                "NBA Boston Celtics vs Miami Heat")]
    public void EpisodeCodesAndSeparatorsAreStrippedFromTheEventTitle(string release, string expected)
    {
        var result = Parser.Parse(release);
        Assert.Equal(expected, result.EventTitle);
    }

    [Theory]
    [InlineData("UFC 310 Prelims 1080p WEB-DL", "UFC 310 Prelims")]
    public void TitlesWithoutAnEpisodeCodeAreUnchanged(string release, string expected)
    {
        Assert.Equal(expected, Parser.Parse(release).EventTitle);
    }

    [Fact]
    public void TheEventTitleCarriesNoLeftoverSeparators()
    {
        var title = Parser.Parse(
            "MLB - S2026E135 - San Francisco Giants vs Houston Astros - HDTV-1080p").EventTitle;
        Assert.NotNull(title);
        Assert.DoesNotContain("S2026E135", title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(title!.Trim(' ', '.', '-', '_'), title);
    }
}
