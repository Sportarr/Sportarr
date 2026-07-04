using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Year extraction coverage for the {Year}x{Round} naming format (e.g.
/// "2026x02"), used by scene releases for round-based sports — Sky's Formula 1
/// feed is one example (Formula.1.2026x02.China.Race.SkyF1HD.1080p). The 'x'
/// joining the year and round is a word character, so the bare year regex never
/// saw a word boundary after the year and left it unextracted — the release
/// lost the year-match bonus and scored below the confidence threshold, so RSS
/// sync silently dropped correct matches (issue #107). These tests pin the
/// year/round extraction and guard the existing formats against regression.
/// </summary>
public class SportsFileNameParserTests
{
    private readonly SportsFileNameParser _parser = new(Mock.Of<ILogger<SportsFileNameParser>>());

    [Theory]
    // The exact Sky F1 releases from issue #107.
    [InlineData("Formula.1.2026x02.China.Race.SkyF1UHD.4K-GROUP", 2026)]
    [InlineData("Formula.1.2026x02.China.Race.SkyF1HD.1080p", 2026)]
    [InlineData("Formula.1.2026x02.China.Qualifying.SkyF1UHD.4K-GROUP", 2026)]
    [InlineData("Formula.1.2026x02.China.Qualifying.SkyF1HD.1080p", 2026)]
    [InlineData("Formula.1.2026x02.China.Sprint.Race.SkyF1UHD.4K-GROUP", 2026)]
    [InlineData("Formula.1.2026x02.China.Sprint.Qualifying.SkyF1HD.1080p", 2026)]
    [InlineData("Formula.1.2026x01.Australia.Race.SkyF1HD.1080p", 2026)]
    // Single-digit round and uppercase separator are handled too.
    [InlineData("Formula.1.2025x9.Canada.Race.SkyF1HD.1080p", 2025)]
    [InlineData("Formula.1.2024X05.Miami.Race.SkyF1HD.1080p", 2024)]
    public void Parse_YearXRoundFormat_ExtractsYear(string filename, int expectedYear)
    {
        var result = _parser.Parse(filename);

        result.EventYear.Should().Be(expectedYear);
        // The round is deliberately NOT surfaced here — it's matched off the
        // title in ReleaseMatchingService against Event.Round. Surfacing it
        // would feed LibraryImportService's round-vs-EpisodeNumber check, which
        // mis-penalises motorsport's cumulative episode numbering.
        result.RoundNumber.Should().BeNull();
    }

    [Theory]
    // Regression guard: the formats that already worked must keep extracting the year.
    [InlineData("Formula1.2026.Round08.Monaco.Grand.Prix.1080p.WEB-GROUP", 2026)]
    [InlineData("Formula.1.2026.Monaco.Grand.Prix.SkyF1HD.1080p", 2026)]
    [InlineData("Formula1.S2026E34.Monaco.1080p.WEB.x264-GROUP", 2026)]
    public void Parse_ExistingYearFormats_StillExtractYear(string filename, int expectedYear)
    {
        _parser.Parse(filename).EventYear.Should().Be(expectedYear);
    }

    [Theory]
    // Resolution-like NNNNxNNNN tokens must not be misread as {Year}x{Round}:
    // 1920/2560/3840 are not 20[12]x years, and 2026x1080p has a 4-digit second
    // term that the 1-2 digit round (plus word boundary) deliberately rejects.
    [InlineData("Some.Stream.1920x1080.h264-GROUP")]
    [InlineData("Some.Stream.2560x1440.h264-GROUP")]
    [InlineData("Formula.1.2026x1080p.China.Race-GROUP")]
    public void Parse_ResolutionTokens_AreNotTreatedAsYear(string filename)
    {
        _parser.Parse(filename).EventYear.Should().BeNull();
    }

    [Theory]
    // Issue #92: a cross-year season written dot-separated - "NBA.2025.2026...",
    // the dominant scene-release convention - was never recognized as a season
    // span at all (the old regex only accepted a hyphen or slash separator), so
    // the release fell back to year-only extraction and grabbed just 2025,
    // hard-rejecting a genuine 2025-2026 season game dated in 2026.
    [InlineData("NBA.2025.2026.San.Antonio.Spurs.Vs.Philadelphia.76ers.03.03.2026.VFF.1080p.WEBRip.AAC.2.0.AVC-GROUP", 2025, 2026)]
    [InlineData("NBA.2025.2026.Washington.Wizards.vs.Boston.Celtics.14.03.2026.VFF.1080p.WEBRip.AAC.2.0.x264-GROUP", 2025, 2026)]
    // Existing hyphen/slash-separated formats must keep working unchanged.
    [InlineData("NFL.2025-2026.Season.Chiefs.vs.Bills.1080p.WEB-GROUP", 2025, 2026)]
    [InlineData("NHL.2025/2026.Rangers.vs.Bruins.1080p.WEB-GROUP", 2025, 2026)]
    [InlineData("NHL.2025-26.Rangers.vs.Bruins.1080p.WEB-GROUP", 2025, 2026)]
    public void Parse_SeasonSpan_ExtractsStartAndEndYear(string filename, int expectedStart, int expectedEnd)
    {
        // These titles also carry an explicit day.month.year date, which DatePattern
        // only recognizes in year-first order - so a release using day-first order
        // (VFF/French releases) falls through to season-span extraction exactly like
        // the reported case, which is what this test needs to exercise.
        var result = _parser.Parse(filename);

        result.EventYear.Should().Be(expectedStart);
        result.SeasonYearEnd.Should().Be(expectedEnd);
    }

    [Theory]
    // A dot-separated 2-digit "short" end year must NOT be accepted: unlike the
    // hyphen/slash form, a dot before a 2-digit number is indistinguishable from
    // a month/day token in a plain date (e.g. "2026.12.25"), which DatePattern
    // above already claims first as a real date - this guards against
    // SeasonSpanPattern misreading a date's month as a short season-end year.
    [InlineData("NBA.2026.12.25.Lakers.vs.Celtics.1080p.WEB-GROUP")]
    public void Parse_DotSeparatedDate_IsNotMisreadAsSeasonSpan(string filename)
    {
        var result = _parser.Parse(filename);

        result.EventDate.Should().Be(new DateTime(2026, 12, 25),
            because: "DatePattern claims year-first dot-separated dates before season-span extraction ever runs");
        result.SeasonYearEnd.Should().BeNull();
    }
}
