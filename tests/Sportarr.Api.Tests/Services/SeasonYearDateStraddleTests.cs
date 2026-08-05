using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Regression: DatePattern let its separator class match independently at each
/// position, so a title carrying BOTH a season year and a date --
/// "MLB_2026__05.08.2026__..." , which CleanFilename turns into
/// "MLB 2026  05.08.2026  ..." -- anchored on the leading SEASON YEAR and then
/// consumed the day/month of the real date that followed it.
///
/// The visible half was already handled: when the stolen pair was day-first with a
/// day of 13-31, new DateTime(2026, 29, 7) threw and the swap-retry recovered it.
///
/// The silent half was not. With a day of 1-12 the straddle produced a VALID but
/// WRONG date -- 5 Aug 2026 read as 8 May 2026 -- returned with no exception and no
/// log line at any level. That is the damaging case: EventDate feeds the +/-3-day
/// hard rejection in ReleaseMatchingService, so an ~89-day error makes the CORRECT
/// release get hard-rejected for its own event, and the event stays missing with
/// nothing in the logs to explain why.
/// </summary>
public class SeasonYearDateStraddleTests
{
    private static SportsFileNameParser NewParser() =>
        new(new Mock<ILogger<SportsFileNameParser>>().Object);

    [Theory]
    // The silent half -- day <= 12. These returned a wrong date before the fix.
    [InlineData("MLB_2026__05.08.2026__Boston_Red_Sox_vs_Athletics_1080p60fps_NESN", 2026, 8, 5)]
    [InlineData("MLB_2026__01.02.2026__Chicago_Cubs_vs_St._Louis_Cardinals_1080p60fps_MARQ", 2026, 2, 1)]
    [InlineData("MLB_2026__11.08.2026__Texas_Rangers_vs_Houston_Astros_1080p60fps_ATV", 2026, 8, 11)]
    [InlineData("MLB_2026__12.08.2026__New_York_Yankees_vs_Chicago_Cubs_1080p60fps_YES", 2026, 8, 12)]
    // The loud half -- day >= 13. Already recovered by the swap-retry; pinned so the
    // separator change does not regress it.
    [InlineData("MLB_2026__29.07.2026__Milwaukee_Brewers_vs_San_Francisco_Giants_1080p60fps_BREW", 2026, 7, 29)]
    [InlineData("MLB_2026__31.07.2026__Washington_Nationals_vs_Atlanta_Braves_1080p60fps_NATS", 2026, 7, 31)]
    public void SeasonYearPrefix_DoesNotStraddleIntoTheRealDate(string title, int y, int m, int d)
    {
        NewParser().Parse(title).EventDate.Should().Be(new DateTime(y, m, d));
    }

    [Theory]
    // Year-first with a consistent separator must keep working unchanged.
    [InlineData("Formula1.2026.07.26.Belgian.GP.Qualifying.1080p.F1TV", 2026, 7, 26)]
    [InlineData("NBA_Today_2026_30_07_720pEN60fps_ESPN", 2026, 7, 30)]
    [InlineData("NFL_Live_2026_31_07_720pEN60fps_ESPN", 2026, 7, 31)]
    // Underscore-separated fields collapse to a repeated space run; the
    // back-reference must accept that run as long as it is consistent.
    [InlineData("Sport_2026__07__26__Something_1080p", 2026, 7, 26)]
    public void ConsistentlySeparatedDates_StillParse(string title, int y, int m, int d)
    {
        NewParser().Parse(title).EventDate.Should().Be(new DateTime(y, m, d));
    }

    [Fact]
    public void RealDateWins_WhenSeasonYearAndIsoDateBothPresent()
    {
        NewParser().Parse("MLB_2026__2026.08.05__Boston_Red_Sox_vs_Athletics_1080p")
            .EventDate.Should().Be(new DateTime(2026, 8, 5));
    }

    [Theory]
    // Titles with no date must not gain one -- the year-only and SyyyyExx
    // fallbacks only run when no date was extracted.
    [InlineData("Formula1.2026.Monaco.Grand.Prix.1080p.WEB.h264-BILLIE")]
    [InlineData("Formula1.2026.Round13.Hungarian.Grand.Prix.Race.SkyF1HD.1080p.x264")]
    public void DatelessTitles_YieldNoDate(string title)
    {
        NewParser().Parse(title).EventDate.Should().BeNull();
    }
}
