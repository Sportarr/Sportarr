using Sportarr.Api.Helpers;
using FluentAssertions;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

public class ReleaseTypeDetectorTests
{
    [Theory]
    [InlineData("NFL.2024.Week.10.Kansas.City.Chiefs.vs.Tampa.Bay.Buccaneers.1080p.WEB.H264")]
    [InlineData("NBA.2010.11.05.Heat.vs.Hornets.720p.HDTV.x264-T0nK4")]
    [InlineData("Premier.League.2025-26.Matchday.32.Arsenal.vs.Liverpool.1080p50.H265")]
    [InlineData("UFC.328.Chimaev.vs.Strickland.PPV.1080p.WEB.h264-VERUM")]
    [InlineData("ATP.2009.Wimbledon.R4.Murray.vs.Wawrinka.ENG.Xvid")]
    public void Detect_TitleWithVsMatchup_ReturnsSingleEvent(string title)
    {
        ReleaseTypeDetector.Detect(title).Should().Be(ReleaseType.SingleEvent);
    }

    [Theory]
    [InlineData("Premier.League.2025-26.Matchday.32.Match.Pack.1080p50.H265")]
    [InlineData("WWE.2007.PPV.Pack.DVDRip.x264-RUDOS")]
    [InlineData("NFL.2024.Week.10.PACK.1080p.WEB.H264")]
    [InlineData("NBA.2024.Complete.Season.1080p.WEB.H264")]
    public void Detect_TitleWithExplicitPackMarker_ReturnsPack(string title)
    {
        ReleaseTypeDetector.Detect(title).Should().Be(ReleaseType.Pack);
    }

    [Theory]
    [InlineData("NFL.2024.Week.10.1080p.WEB.H264")]
    [InlineData("Premier.League.2025-26.Matchday.32.1080p50.H265")]
    public void Detect_RoundMarkerWithoutMatchup_ReturnsPack(string title)
    {
        ReleaseTypeDetector.Detect(title).Should().Be(ReleaseType.Pack);
    }

    [Fact]
    public void Detect_NoRoundOrPackMarkerButHasMatchup_ReturnsSingleEvent()
    {
        ReleaseTypeDetector.Detect("UFC.Fight.Night.276.Allen.vs.Costa.1080p.WEB-DL.H264.Fight-BB")
            .Should().Be(ReleaseType.SingleEvent);
    }

    [Theory]
    [InlineData("Some.Random.Sports.Broadcast.1080p.WEB.H264")]
    [InlineData("Formula1.2025.Spanish.Grand.Prix.Qualifying.1080p.AHDTV.x264-DARKSPORT")]
    public void Detect_NoMatchupAndNoPackMarker_ReturnsUnknown(string title)
    {
        // F1 sessions have no "vs" matchup and no pack/round marker (confirmed not a
        // pack convention by research), so they land here rather than as a false SingleEvent.
        ReleaseTypeDetector.Detect(title).Should().Be(ReleaseType.Unknown);
    }

    [Fact]
    public void Detect_EmptyTitle_ReturnsUnknown()
    {
        ReleaseTypeDetector.Detect("").Should().Be(ReleaseType.Unknown);
    }

    [Fact]
    public void Detect_SportarrEventId_TakesPrecedenceOverTitle_ReturnsSingleEvent()
    {
        ReleaseTypeDetector.Detect("NFL.2024.Week.10.PACK.1080p.WEB.H264", sportarrEventId: "ev-1234567")
            .Should().Be(ReleaseType.SingleEvent);
    }

    [Fact]
    public void Detect_SportarrLeagueId_TakesPrecedenceOverTitle_ReturnsPack()
    {
        ReleaseTypeDetector.Detect("Chiefs.vs.Bills.1080p.WEB.H264", sportarrLeagueId: "lg-123456")
            .Should().Be(ReleaseType.Pack);
    }
}
