using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The source and quality patterns had no token boundaries. "TS" matched
/// inside team names like Patriots and Nets, filing an ordinary broadcast as a
/// camcorder rip, and a generic "HD" inside "HDTV" won on position over the
/// real resolution later in the name.
/// </summary>
public class MediaFileParserTokenTests
{
    private static ParsedFileInfo Parse(string filename)
    {
        var parser = new MediaFileParser(NullLogger<MediaFileParser>.Instance);
        return parser.Parse(filename);
    }

    [Theory]
    [InlineData("NFL.2026.Week.05.Chiefs.vs.Patriots.1080p.HDTV.x264-GROUP")]
    [InlineData("NBA.2026.03.11.Celtics.vs.Nets.720p.HDTV.x264-GROUP")]
    [InlineData("NFL.2026.Week.01.Highlights.1080p.HDTV.x264-GROUP")]
    public void A_team_name_ending_in_ts_is_not_read_as_a_telesync(string filename)
    {
        Parse(filename).Source.Should().Be("HDTV");
    }

    [Fact]
    public void A_standalone_telesync_token_is_still_recognised()
    {
        Parse("Boxing.2026.05.09.Main.Card.720p.TS.x264-GROUP").Source.Should().Be("TS");
    }

    [Theory]
    [InlineData("NFL.2026.Week.05.Chiefs.vs.Bills.HDTV.1080p.x264-GROUP", "1080P")]
    [InlineData("NHL.2026.01.02.Winter.Classic.HDTV.720p.x264-GROUP", "720P")]
    [InlineData("F1.2026.Round.03.Race.HDTV.2160p.HDR.x265-GROUP", "2160P")]
    public void A_named_resolution_beats_a_generic_hd_earlier_in_the_name(string filename, string expected)
    {
        Parse(filename).Resolution.Should().Be(expected);
    }

    [Fact]
    public void A_generic_quality_is_still_used_when_no_resolution_is_named()
    {
        Parse("Soccer.2026.02.02.Derby.HD.x264-GROUP").Resolution.Should().Be("HD");
    }
}
