using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Streaming service names used to sit in the same pattern as the real source
/// tokens. A regex takes the leftmost match and sports titles lead with the
/// broadcaster, so a broadcast capture was filed as WEB-DL and then accepted,
/// rejected or upgraded against the wrong rules.
/// </summary>
public class QualityParserSourceTests
{
    [Theory]
    [InlineData("ESPN.NFL.2026.Week.05.Chiefs.vs.Bills.1080p.HDTV.x264-GROUP", "HDTV-1080p")]
    [InlineData("DAZN.Boxing.2026.05.09.Wardley.vs.Dubois.720p.HDTV.x264-GROUP", "HDTV-720p")]
    [InlineData("MLB.TV.2026.05.01.Cubs.vs.Reds.1080p.HDTV.x264-GROUP", "HDTV-1080p")]
    public void A_broadcast_capture_is_not_called_web_just_because_a_service_is_named(string title, string expected)
    {
        var parsed = QualityParser.ParseQuality(title);
        parsed.Quality.Name.Should().Be(expected);
    }

    [Theory]
    [InlineData("ESPN.NFL.2026.Week.05.Chiefs.vs.Bills.1080p.x264-GROUP", "WEBDL-1080p")]
    [InlineData("Amazon.NFL.2026.Week.05.1080p.x264-GROUP", "WEBDL-1080p")]
    public void A_service_still_implies_web_when_nothing_else_names_a_source(string title, string expected)
    {
        var parsed = QualityParser.ParseQuality(title);
        parsed.Quality.Name.Should().Be(expected);
    }

    [Fact]
    public void An_explicit_web_dl_tag_still_wins()
    {
        QualityParser.ParseQuality("NFL.2026.Week.05.1080p.WEB-DL.x264-GROUP")
            .Quality.Name.Should().Be("WEBDL-1080p");
    }
}
