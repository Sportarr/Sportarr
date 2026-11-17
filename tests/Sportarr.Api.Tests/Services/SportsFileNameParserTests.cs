using System.Text.RegularExpressions;
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

    // The title keeps the whole name. A cut name such as "Proch" holds no
    // whole word of the event title, so the match lost its title award.
    [Theory]
    [InlineData("UFC - S2024E22 - pt3 - UFC 303 Pereira vs Procházka 2 - WEBDL-1080p", "UFC 303: Pereira vs Procházka")]
    [InlineData("UFC.299.OMalley.vs.Vera.2.1080p.WEB.h264-GRP", "UFC 299: OMalley vs Vera")]
    public void NumberedUfcCard_KeepsAnAccentedFighterName(string name, string title)
    {
        _parser.Parse(name).EventTitle.Should().Be(title);
    }

    // Some file systems store the accent as a separate combining mark (NFD).
    [Fact]
    public void NumberedUfcCard_KeepsADecomposedAccent()
    {
        var name = "UFC.303.Pereira.vs.Procházka.2.1080p.WEB.h264-GRP".Normalize(System.Text.NormalizationForm.FormD);

        _parser.Parse(name).EventTitle.Should().Be("UFC 303: Pereira vs Procházka".Normalize(System.Text.NormalizationForm.FormD));
    }

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
    // Resolution tokens such as 1920x1080 are not a {Year}x{Number} marker.
    // 1920, 2560 and 3840 are not 20[12]x years. In 2026x1080p the second
    // term has four digits, and the marker accepts one to three digits only.
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
    [Theory]
    // "Silverstone" and "Race One" both end in the promotion name "one". The
    // pattern was unanchored, so both scored 90% as ONE Championship and sent
    // motorsport releases down the fighting path.
    [InlineData("MotoGP 2026 Round 12 Silverstone Race 1080p WEB-DL", "Motorsport")]
    [InlineData("BSB 2026 Round01 Oulton Park International Race One TNT WEB-DL 1080p", null)]
    public void Parse_WordEndingInOne_IsNotReadAsOneChampionship(string filename, string? expectedSport)
    {
        var result = _parser.Parse(filename);

        result.Organization.Should().NotBe("ONE Championship");
        if (expectedSport != null)
            result.Sport.Should().Be(expectedSport);
    }

    [Theory]
    [InlineData("ONE Championship Fight Night 20 2026 1080p WEB-DL")]
    [InlineData("ONE.Fight.Night.24.2026.1080p")]
    public void Parse_RealOneChampionshipRelease_StillMatches(string filename)
    {
        var result = _parser.Parse(filename);

        result.Organization.Should().Be("ONE Championship");
        result.Sport.Should().Be("Fighting");
    }

    // Another database names library files "Series - SxxxxEyy - Title".
    // Its episode number is not Sportarr's, so the title must carry the
    // round, the location and the session.
    [Theory]
    [InlineData("Formula 1 - S2024E10 - Round 1: Bahrain (Race)", "Formula 1", 1, "Bahrain", "Race", 2024)]
    [InlineData("Formula 1 - S2023E11 - Bahrain (Race)", "Formula 1", null, "Bahrain", "Race", 2023)]
    [InlineData("Formula 1 - S2023E24 - Azerbaijan (Sprint Shootout)", "Formula 1", null, "Azerbaijan", "Sprint Shootout", 2023)]
    [InlineData("Formula 1 - S2024E09 - Round 1: Bahrain (Qualifying)", "Formula 1", 1, "Bahrain", "Qualifying", 2024)]
    [InlineData("MotoGP - S2024E12 - Qatar Airways Grand Prix of Qatar MotoGP Free Practice Nr. 1", "MotoGP", null, "Qatar", "Free Practice 1", 2024)]
    public void ForeignNamedMotorsportFile_YieldsRoundLocationSession(string name, string org, int? round, string location, string session, int year)
    {
        var result = _parser.Parse(name);

        result.Organization.Should().Be(org);
        result.RoundNumber.Should().Be(round);
        result.Location.Should().Be(location);
        result.Session.Should().Be(session);
        result.EventYear.Should().Be(year);
    }

    // A MotoGP library also holds the Moto2 and Moto3 sessions. They are not
    // MotoGP sessions, so the file must not read as one.
    [Theory]
    [InlineData("MotoGP - S2024E11 - Qatar Airways Grand Prix of Qatar Moto2 Free Practice", "Moto2")]
    [InlineData("MotoGP - S2024E10 - Qatar Airways Grand Prix of Qatar Moto3 Free Practice", "Moto3")]
    public void ForeignNamedSupportClassFile_DoesNotReadAsMotoGp(string name, string supportClass)
    {
        var result = _parser.Parse(name);

        result.Organization.Should().Be(supportClass);
        result.Session.Should().BeNull();
        result.EventTitle.Should().Contain(supportClass);
    }

    // The fighters name a Fight Night card, so the title keeps them.
    [Theory]
    [InlineData("UFC Fight Night 86 Rothwell vs dos Santos", "UFC Fight Night 86 Rothwell vs dos Santos")]
    [InlineData("UFC.Fight.Night.86.Rothwell.vs.dos.Santos.720p.HDTV.x264-GRP", "UFC Fight Night 86 Rothwell vs dos Santos")]
    [InlineData("UFC - S2026E16 - pt1 - UFC Fight Night 276 Allen vs Costa - WEBDL-1080p", "UFC Fight Night 276 Allen vs Costa")]
    [InlineData("UFC.Fight.Night.230.2024.1080p.WEB.h264-GRP", "UFC Fight Night 230")]
    [InlineData("UFC - S2016E86 - Fight Night 86", "UFC Fight Night 86")]
    public void FightNightFile_KeepsTheFighterNames(string name, string title)
    {
        _parser.Parse(name).EventTitle.Should().Be(title);
    }

    // The place ends at the first " - ", at a session word or at a quality
    // word. A location such as "Qatar Race WEBDL 1080p" agrees with every race.
    [Theory]
    [InlineData("MotoGP - S2026E05 - Grand Prix of Qatar - Race - WEBDL-1080p", "Qatar")]
    [InlineData("MotoGP - S2026E05 - Grand Prix of Qatar Race WEBDL-1080p", "Qatar")]
    [InlineData("MotoGP - S2026E05 - Grand Prix of Qatar 1080p", "Qatar")]
    [InlineData("Formula 1 - S2024E10 - Bahrain Grand Prix - Race - WEBDL-1080p", "Bahrain")]
    public void LibraryNameLocation_StopsAtADashASessionOrAQualityWord(string name, string location)
    {
        _parser.Parse(name).Location.Should().Be(location);
    }

    private static RegexMatchTimeoutException GaveUp() =>
        new("UFC 300 Pereira vs Hill", "stub", TimeSpan.FromMilliseconds(1));

    [Fact]
    public void ExtractorTimeout_IsCaughtLikeAPatternTimeout()
    {
        var stub = new SportsPattern
        {
            Sport = "Fighting", Organization = "Stub", Pattern = new Regex("^UFC"),
            TitleBuilder = _ => throw GaveUp(),
        };
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>(), new[] { stub });

        var parse = () => parser.Parse("UFC 300 Pereira vs Hill");

        var result = parse.Should().NotThrow().Subject;
        result.MatchedPattern.Should().BeNull("the pattern gave up, so it matched nothing");
        result.Organization.Should().Be("UFC", "the generic reading still runs");
    }

    [Fact]
    public void ExtractorTimeout_LeavesNoPartOfTheSkippedPattern()
    {
        // The title comes out, then the location extractor gives up.
        var stub = new SportsPattern
        {
            Sport = "Motorsport", Organization = "Stub", Pattern = new Regex("^UFC"),
            TitleBuilder = _ => "Stub Title",
            RoundExtractor = _ => 99,
            LocationExtractor = _ => throw GaveUp(),
        };
        var patterns = new[] { stub }.Concat(SportsFileNameParser.DefaultPatterns).ToList();
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>(), patterns);
        const string name = "UFC.300.2024.PPV.1080p.WEB.h264-GRP";

        var result = parser.Parse(name);

        var expected = _parser.Parse(name);
        result.Sport.Should().Be(expected.Sport);
        result.Organization.Should().Be(expected.Organization);
        result.EventTitle.Should().Be(expected.EventTitle);
        result.RoundNumber.Should().Be(expected.RoundNumber);
        result.MatchedPattern.Should().Be(expected.MatchedPattern);
    }
}
