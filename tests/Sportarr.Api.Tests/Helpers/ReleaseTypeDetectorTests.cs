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

    [Fact]
    public void Detect_AflRoundWithVMatchup_IsNotAnImportPack()
    {
        const string title = "AFL 2026 Round 1 Game 1 Carlton Blues V Richmond Tigers 1080p WEB DL 50FPS AAC H264 FLOG";

        ReleaseTypeDetector.Detect(title).Should().Be(ReleaseType.SingleEvent);
        ReleaseTypeDetector.DetectForImport(title).Should().Be(ReleaseType.SingleEvent);
    }

    [Theory]
    [InlineData("NFL 2026-2027 / Week 04 / 04.10.2026 / Miami Dolphins @ Minnesota Vikings [Football, WEB-DL HD/720p]")]
    [InlineData("NFL.2026.Week.04.Miami.Dolphins.@.Minnesota.Vikings.720p")]
    [InlineData("NFL 2026-2027 / Week 06 / 12.10.2026 / Seattle Seahawks @ Los Angeles Rams [Football, WEB-DL HD/720p/60fps, MKV/H.264, EN/ESPN]")]
    [InlineData("NFL 2026-2027 / Week 06 / 12.10.2026 / Los Angeles Rams @ Baltimore Ravens [Football, WEB-DL HD/720p/60fps, MKV/H.264, EN/ESPN]")]
    [InlineData("NFL.2026.Week.04.Jets.@.Patriots.720p")]
    [InlineData("NFL.2026.Week.04.MIA.Dolphins.@.Vikings.720p")]
    [InlineData("NBA.2026.Week.04.Philadelphia.76ers.@.Orlando.Magic.720p")]
    public void Detect_WeekWithNamedAtMatchup_IsSingleGame(string title)
    {
        ReleaseTypeDetector.Detect(title).Should().Be(ReleaseType.SingleEvent);
        ReleaseTypeDetector.DetectForImport(title).Should().Be(ReleaseType.SingleEvent);
    }

    [Theory]
    [InlineData("NFL 2026 Week 04 @ Tracker")]
    [InlineData("NFL 2026 Week04 @ Tracker")]
    [InlineData("NFL 2026 Round04 @ Tracker")]
    [InlineData("NFL 2026 Matchday04 @ Tracker")]
    [InlineData("NFL 2026 Week 04 Sunday Games @ 1080p")]
    [InlineData("NFL.2026.Week.04.Sunday.Games.@.1080p.WEB-DL.H264")]
    [InlineData("NFL 2026 Week 04 Sunday Games @ 1080p WEB-DL")]
    [InlineData("NFL Week 04 Highlights Show @ Netflix")]
    [InlineData("NFL Week 04 Highlights Show @ Netflix WEB-DL")]
    [InlineData("NFL 2026 Week 04 Sunday Games @ Full HD 720p")]
    [InlineData("NFL 2026 Week 04 Sunday Games @ Blu-Ray 720p")]
    [InlineData("NFL 2026 Week 04 Sunday Games @ Patriots 720p")]
    [InlineData("NFL 2026 Week 04 PACK Miami Dolphins @ Minnesota Vikings and more 720p")]
    [InlineData("NFL.2026.Week.04.Miami.Dolphins.@.Minnesota.Vikings.Green.Bay.Packers.@.Chicago.Bears.720p")]
    [InlineData("NFL.2026.Week.04.Me@Home.Special.720p")]
    public void Detect_AtWithoutASingleGameRemainsPack(string title)
    {
        ReleaseTypeDetector.Detect(title).Should().Be(ReleaseType.Pack);
        ReleaseTypeDetector.DetectForImport(title).Should().Be(ReleaseType.Pack);
    }

    [Fact]
    public void Detect_AtWithoutRoundIdentityIsUnknown()
    {
        ReleaseTypeDetector.Detect("Sports.Upload.@.Tracker.720p").Should().Be(ReleaseType.Unknown);
    }

    [Theory]
    [InlineData("AFL 2026 Round 1 Carlton V Richmond and Geelong V Fremantle PACK 1080p")]
    [InlineData("AFL 2026 Carlton V Richmond FULL SEASON 1080p")]
    [InlineData("AFL 2026 Carlton V Richmond ALL GAMES 1080p")]
    public void Detect_ExplicitBundleWithVMatchup_RemainsAnImportPack(string title)
    {
        ReleaseTypeDetector.Detect(title).Should().Be(ReleaseType.Pack);
        ReleaseTypeDetector.DetectForImport(title).Should().Be(ReleaseType.Pack);
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
    public void Detect_WrcFullRallyWithRound_ReturnsSingleEvent()
    {
        const string title = "WRC 2026 Round03 Safari Rally Kenya FULL EVENT 1080p50fps EN RallyTV";

        ReleaseTypeDetector.Detect(title).Should().Be(ReleaseType.SingleEvent);
        ReleaseTypeDetector.DetectForImport(title).Should().Be(ReleaseType.SingleEvent);
    }

    [Fact]
    public void Detect_WrcFullRallyWithExplicitPackMarker_RemainsPack()
    {
        const string title = "WRC 2026 Round03 Safari Rally Kenya FULL EVENT PACK 1080p";

        ReleaseTypeDetector.Detect(title).Should().Be(ReleaseType.Pack);
        ReleaseTypeDetector.DetectForImport(title).Should().Be(ReleaseType.Pack);
    }

    [Fact]
    public void Detect_WrcFullRallyWithLeagueId_RemainsPack()
    {
        const string title = "WRC 2026 Round03 Safari Rally Kenya FULL EVENT 1080p50fps EN RallyTV";

        ReleaseTypeDetector.Detect(title, sportarrLeagueId: "lg-9900300").Should().Be(ReleaseType.Pack);
        ReleaseTypeDetector.DetectForImport(title, leagueId: "lg-9900300").Should().Be(ReleaseType.Pack);
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
