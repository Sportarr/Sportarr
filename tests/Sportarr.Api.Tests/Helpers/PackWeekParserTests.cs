using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// This parser was a regex three times and wrong three times. Every shape
/// that bit is pinned here: a range read as its first week, a resolution
/// after a dash read as a range end, and a dash before the word Week
/// refusing the week entirely.
/// </summary>
public class PackWeekParserTests
{
    [Theory]
    [InlineData("NFL.2025.Week.5.Complete.1080p.WEB", 5)]
    [InlineData("NFL 2025 Week 07 All Games 720p", 7)]
    [InlineData("NFL.2025.Week.5-1080p.WEB", 5)]
    [InlineData("NFL 2025 Week 5 - 1080p", 5)]
    [InlineData("NFL.2025.Week.5-2160p.WEB", 5)]
    [InlineData("NFL 2025 Week 5 - 2025-10-05 Pack", 5)]
    [InlineData("NFL 2025 Week 5 - 10-05-2025", 5)]
    [InlineData("NFL 2025 Week 5 - 10.05.2025", 5)]
    [InlineData("NFL 2025 Week 5 - 12 Games", 5)]
    [InlineData("NFL 2025 Week 5 - 2 Games", 5)]
    [InlineData("NFL 2025 Week 5 - 1 of 2", 5)]
    [InlineData("NFL 2025 - Week 5 - 1080p", 5)]
    [InlineData("NFL 2025 - Week 5", 5)]
    [InlineData("NFL-2025-Week-5-1080p", 5)]
    [InlineData("NFL_2025_-_Week_5", 5)]
    [InlineData("NFL_2025_Week_5_1080p_WEB", 5)]
    [InlineData("NFL 2025 – Week 5", 5)]
    [InlineData("NFL.2025.-.Week.5.1080p", 5)]
    [InlineData("NFL 2025 Toronto Week 5", 5)]
    [InlineData("NFL 2025 Week 5 and Week 6", 5)]
    [InlineData("NFL 2025 Week 5 - 7.5GB", 5)]
    [InlineData("NFL 2025 Week 5 - 8.5 GB", 5)]
    [InlineData("NFL 2025 Week 5 - 7.1 Surround", 5)]
    [InlineData("NFL 2025 Week 5 - 10/05/2025", 5)]
    [InlineData("NFL 2025 Week 5 - 7.500GB", 5)]
    [InlineData("NFL 2025 Week 5 - 12.500 GB", 5)]
    [InlineData("NFL 2025 Week 5 - 7,5GB", 5)]
    [InlineData("NFL 2025 Week 5 - 7,5 GB", 5)]
    // Ambiguous on purpose: a size joined to the range end by a bare dot
    // reads as a single week. The rejection is visible; a hidden week is not.
    [InlineData("NFL 2025 Week 1-18.15GB", 1)]
    public void A_single_week_is_read(string title, int expected)
    {
        PackWeekParser.SingleWeek(title).Should().Be(expected);
    }

    [Theory]
    [InlineData("NFL.2025.Week.1-18.Complete")]
    [InlineData("NFL 2025 Week 01-18")]
    [InlineData("NFL 2025 Week 1 - Week 18")]
    [InlineData("NFL 2025 Week 1 – 18")]
    [InlineData("NFL 2025 Week 1 to 18 Season Pack")]
    [InlineData("NFL 2025 Week 1 thru 18")]
    [InlineData("NFL_2025_Week_1_to_18_Complete")]
    [InlineData("NFL.2025.Week.1-18.1080p.WEB.H264")]
    [InlineData("NFL.2025.Week.01-18.2160p.WEB")]
    [InlineData("NFL 2025 Week 1-18 1080p WEB-DL")]
    [InlineData("NFL_2025_Week_1-18_1080p")]
    [InlineData("NFL 2025 Week 1 - Week 18 - 1080p")]
    [InlineData("NFL 2025 Week 5-7 1080p")]
    [InlineData("NFL 2025 Week 5 – 7")]
    [InlineData("NFL 2025 Week 5-6.1080p")]
    [InlineData("NFL 2025 Week 5-10.1080p")]
    [InlineData("NFL 2025 Week 5-6.720p")]
    [InlineData("NFL 2025 Week 1-18.720p.HDTV")]
    [InlineData("NFL 2025 Week 1-18.576i.HDTV")]
    [InlineData("NFL 2025 Week 5-7.1080i.HDTV")]
    public void A_week_range_yields_no_week(string title)
    {
        PackWeekParser.SingleWeek(title).Should().BeNull();
    }

    [Theory]
    [InlineData("NFL.2025.Season.Complete.1080p")]
    [InlineData("")]
    [InlineData(null)]
    public void No_week_yields_no_week(string? title)
    {
        PackWeekParser.SingleWeek(title).Should().BeNull();
    }
}
