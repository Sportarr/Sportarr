using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PriorityMotorsportSearchTests
{
    private static readonly EventQueryService QueryService =
        new(NullLogger<EventQueryService>.Instance);

    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    private static readonly ReleaseMatchScorer Scorer = new();

    public static TheoryData<string, string, string, string, string> QueryCases => new()
    {
        { "NASCAR Cup Series", "Daytona 500", "1", "", "NASCAR Cup Series 2026" },
        { "NASCAR Cup Series", "DuraMAX Grand Prix - Race", "3", "", "NASCAR Cup Series 2026" },
        { "NASCAR Cup Series", "Cook Out Southern 500", "27", "", "NASCAR Cup Series 2026" },
        { "IndyCar Series", "Firestone Grand Prix of St. Petersburg - Qualifying", "1", "", "IndyCar 2026 Qualifying" },
        { "IndyCar Series", "110th Running of the Indianapolis 500", "7", "", "IndyCar 2026 Indianapolis 500" },
        { "IndyCar Series", "IndyCar Grand Prix of Monterey Final Practice", "18", "", "IndyCar 2026 Round18" },
        { "WEC", "6 Hours of Spa Francorchamps Qualifying - Hypercar", "2", "", "WEC 2026" },
        { "WEC", "24 Hours of Le Mans", "3", "", "WEC 2026 Le Mans" },
        { "WEC", "Lone Star Le Mans Hyperpole - LMGT3", "5", "", "WEC 2026" },
        { "WRC", "WRC Rallye Monte-Carlo SS1", "1", "", "WRC 2026" },
        { "WRC", "WRC Safari Rally Kenya", "3", "", "WRC 2026" },
        { "WRC", "WRC Rally Islas Canarias - Rally of Spain SS17", "5", "", "WRC 2026" },
        { "SBK", "Australian - Race 1", "1", "Phillip Island", "WSBK 2026 Round01 Australia" },
        { "SBK", "Australian Superpole - Race", "1", "Phillip Island", "WSBK 2026 Round01 Australia" },
        { "SBK", "Acerbis French Round - Race 2", "9", "Magny-Cours", "WSBK 2026 Round09 France" },
    };

    [Theory]
    [MemberData(nameof(QueryCases))]
    public void EvidenceBackedLeaguesUseOneEventQuery(
        string leagueName,
        string title,
        string round,
        string location,
        string expected)
    {
        var queries = QueryService.BuildEventQueries(Event(leagueName, title, round, location));

        queries.Should().Equal(expected);
    }

    [Theory]
    [InlineData("NASCAR Cup Series", "Daytona 500", "1", "NASCAR Cup Series 2026 Daytona 500")]
    [InlineData("WEC", "6 Hours of Spa Francorchamps Qualifying - Hypercar", "2", "WEC 2026 Round02")]
    [InlineData("WRC", "WRC Rally Islas Canarias - Rally of Spain SS17", "5", "WRC 2026 Round05")]
    public void SeasonWideQueriesHaveOneConditionalEventProbe(
        string leagueName,
        string title,
        string round,
        string expected)
    {
        var evt = Event(leagueName, title, round);
        var queries = QueryService.BuildEventQueries(evt);

        QueryService.BuildMetadataTitleProbe(evt, queries).Should().Be(expected);
    }

    [Fact]
    public void CustomTemplateStillOverridesMotorsportDefaults()
    {
        var evt = Event("SBK", "Australian - Race 1", "1", "Phillip Island");

        QueryService.BuildEventQueries(evt, customTemplate: "my {League} {Year} {Round}")
            .Should().Equal("my SBK 2026 01");
    }

    public static TheoryData<string, string, string, string, bool> ReleaseCases => new()
    {
        { "NASCAR Cup Series", "Daytona 500", "1", "NASCAR Cup Series 2026 Round01 Daytona 500 Race 720p60fps EN FOX", true },
        { "NASCAR Cup Series", "Daytona 500", "1", "NASCAR Cup Series 2026 Round01 Daytona Race 1080p WEB H.264", true },
        { "NASCAR Cup Series", "Daytona 500", "1", "NASCAR Cup Series 2026 Round01 Daytona 500 Duels 1080p60fps EN FS1", false },
        { "NASCAR Cup Series", "Daytona 500", "1", "NASCAR Cup Series 2026 Round01 Daytona 500 Quali 1080p60fps EN FS1", false },
        { "NASCAR Cup Series", "Daytona 500", "1", "NASCAR Cup Series 2026 Round01 Daytona 500 FP1 1080p60fps EN FS1", false },
        { "NASCAR Cup Series", "Daytona 500", "1", "NASCAR Cup Series 2026 Coke Zero Sugar 400 Daytona Weekend IPTV 1080p 60fps EN", false },
        { "NASCAR Cup Series", "Cook Out Southern 500", "27", "NASCAR Cup Series 2026 Round 26 Cook Out Southern 500 Weekend", true },
        { "NASCAR Cup Series", "Cook Out Southern 500", "27", "NASCAR ARCA Menards Series 2026 Du Quoin 06 09 720pEN60fps FS1", false },
        { "IndyCar Series", "Firestone Grand Prix of St. Petersburg - Qualifying", "1", "IndyCar Series 2026 Round01 St Petersburg Qualifying STAN WEB DL 1080p H264 English MWR", true },
        { "IndyCar Series", "IndyCar Grand Prix of Monterey Final Practice", "18", "IndyCar Series 2026 Round18 Laguna Seca FP STAN WEB DL 1080p H264 English MWR", true },
        { "IndyCar Series", "IndyCar Grand Prix of Monterey Final Practice", "18", "IndyCar_2026_Round18_Laguna_Seca_Final_Practice_1080p_WEB_H264", true },
        { "IndyCar Series", "IndyCar Grand Prix of Monterey Final Practice", "18", "IndyCar Series 2026 Round18 Laguna Seca FP1 STAN WEB DL 1080p H264 English MWR", false },
        { "WEC", "6 Hours of Spa Francorchamps Qualifying - Hypercar", "2", "WEC 2026 Round02 Belgium Qualifying STAN WEB DL 1080p H264 English MWR", true },
        { "WEC", "6 Hours of Spa Francorchamps Qualifying - Hypercar", "2", "FIA WEC 2026 6 Hours Of Spa Francorchamps 1080p HMAX WEB DL", false },
        { "WEC", "24 Hours of Le Mans", "3", "FIA WEC 2026 24 Hours Of Le Mans 1080p HMAX WEB DL", true },
        { "WEC", "Lone Star Le Mans Hyperpole - LMGT3", "5", "WEC 2026 Round05 USA Race STAN WEB DL 1080p H264 English MWR", false },
        { "WEC", "Lone Star Le Mans Hyperpole - LMGT3", "5", "FIA WEC 2026 24 Hours Of Le Mans 1080p HMAX WEB DL", false },
        { "WRC", "WRC Safari Rally Kenya", "3", "WRC 2026 Round03 Safari Rally Kenya FULL EVENT 1080p50fps EN RallyTV", true },
        { "WRC", "WRC Safari Rally Kenya", "3", "WRC Rally 2026 Kenya Stage 2 12 03 720pEN50fps DAZN", false },
        { "WRC", "WRC Rally Islas Canarias - Rally of Spain SS17", "5", "WRC 2026 Round05 Rally Islas Canarias Day 3 Full Coverage 1080p50fps", false },
        { "WRC", "WRC Rally Islas Canarias - Rally of Spain SS17", "5", "WRC Rally 2026 Spain Stage 17 26 04 720pEN50fps DAZN", true },
        { "SBK", "Australian - Race 1", "1", "WSBK 2026 Round01 Australia Race One WEB DL 1080p H264 English MWR", true },
        { "SBK", "Australian - Race 1", "1", "WSBK 2026 Round01 Australia Test Upload WEB DL 1080p H264 English MWR", false },
        { "SBK", "Australian - Race 1", "1", "WSBK 2026 Round01 Australia Race Two WEB DL 1080p H264 English MWR", false },
        { "SBK", "Australian - Race 1", "1", "WSBK 2026 Round01 Australia Superpole Race WEB DL 1080p H264 English MWR", false },
        { "SBK", "Australian Superpole - Race", "1", "WSBK 2026 Round01 Australia Superpole Race WEB DL 1080p H264 English MWR", true },
        { "SBK", "Australian Superpole - Race", "1", "WSBK 2026 Round01 Australia Superpole WEB DL 1080p H264 English MWR", false },
        { "SBK", "Acerbis French Round - Race 2", "9", "WSBK 2026 Round09 France Race Two WEB DL 1080p H264 English MWR", true },
        { "SBK", "Acerbis French Round - Race 2", "9", "WSBK_2026_Round09_France_Race_Two_1080p_WEB_H264", true },
        { "SBK", "Acerbis French Round - Race 2", "9", "WSBK 2026 Round09 France Race One WEB DL 1080p H264 English MWR", false },
    };

    [Theory]
    [MemberData(nameof(ReleaseCases))]
    public void RealReleaseTitlesKeepOnlyTheRequestedEvent(
        string leagueName,
        string eventTitle,
        string round,
        string releaseTitle,
        bool expected)
    {
        var evt = Event(leagueName, eventTitle, round);
        var validation = Matcher.ValidateRelease(Release(releaseTitle), evt);
        var score = Scorer.CalculateMatchScore(releaseTitle, evt);

        validation.IsMatch.Should().Be(expected,
            $"confidence {validation.Confidence}; reasons {string.Join("; ", validation.MatchReasons)}; rejections {string.Join("; ", validation.Rejections)}");
        if (expected)
        {
            validation.IsHardRejection.Should().BeFalse();
            score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        }
        else
        {
            validation.IsHardRejection.Should().BeTrue();
            score.Should().Be(0);
        }
    }

    [Fact]
    public void ImportScoringSeparatesWorldSuperbikeSessions()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var service = ImportMatcher(db, parser);
        const string release = "WSBK 2026 Round09 France Race Two WEB DL 1080p H264 English MWR";
        var parsed = parser.Parse(release);
        var wanted = Event("SBK", "Acerbis French Round - Race 2", "9");
        var sibling = Event("SBK", "Acerbis French Round - Race 1", "9");

        service.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeGreaterOrEqualTo(50);
        service.ScoreMatch(parsed.EventTitle ?? release, sibling.Title, null, sibling, parsed).Core
            .Should().BeLessOrEqualTo(0);
    }

    [Fact]
    public void ImportScoringRejectsSupercarsShootoutForARace()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var service = ImportMatcher(db, parser);
        var wanted = Event("Supercars", "NTI Townsville 500 - Race 21", "7");
        const string race = "Supercars 2026 Round07 Townsville Race2 2160p FOXTEL WEB Rip AAC 2 0 H265 English";
        const string shootout = "Supercars 2026 Round07 Townsville Race2 Top Ten Shootout 2160p FOXTEL WEB Rip DD5 1 H265 English";
        var parsedRace = parser.Parse(race);
        var parsedShootout = parser.Parse(shootout);

        service.ScoreMatch(parsedRace.EventTitle ?? race, wanted.Title, null, wanted, parsedRace, new[] { 20, 21 }).Core
            .Should().BeGreaterOrEqualTo(50);
        service.ScoreMatch(parsedShootout.EventTitle ?? shootout, wanted.Title, null, wanted, parsedShootout, new[] { 20, 21 }).Core
            .Should().BeLessOrEqualTo(0);
        LibraryScore(race, wanted, parsedRace, new[] { 20, 21 }).Should().BeGreaterOrEqualTo(50);
        LibraryScore(shootout, wanted, parsedShootout, new[] { 20, 21 }).Should().Be(0);
    }

    [Theory]
    [InlineData("Supercars 2026 Round10 Tailem Bend 2160p FoxSports WEB DL H265 English")]
    [InlineData("Supercars 2026 Round10 Tailem Bend Highlights 2160p FoxSports WEB DL H265 English")]
    public void ImportScoringKeepsSingleRaceSupercarsFilesWithoutASession(string release)
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var service = ImportMatcher(db, parser);
        var wanted = Event("Supercars", "AirTouch 500 at The Bend - Race 29", "10");
        wanted.League!.AllowHighlights = true;
        var parsed = parser.Parse(release);

        service.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed, new[] { 29 }).Core
            .Should().BeGreaterOrEqualTo(50);
        LibraryScore(release, wanted, parsed, new[] { 29 }).Should().BeGreaterOrEqualTo(50);
    }

    [Fact]
    public void ImportScoringSeparatesLaterSupercarsPracticeSessions()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var service = ImportMatcher(db, parser);
        const string release = "Supercars 2026 Round11 Bathurst 1000 Practice 5 1080p WEB H264";
        var parsed = parser.Parse(release);
        var wanted = Event("Supercars", "Repco Bathurst 1000 Practice 6", "11");

        service.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeLessOrEqualTo(0);
        LibraryScore(release, wanted, parsed, Array.Empty<int>()).Should().Be(0);
    }

    [Fact]
    public void ImportScoringRequiresTheRequestedRallyStage()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var service = ImportMatcher(db, parser);
        var stageEvent = Event("WRC", "WRC Rally Islas Canarias - Rally of Spain SS17", "5");
        var dayPackage = parser.Parse("WRC 2026 Round05 Rally Islas Canarias Day 3 Full Coverage 1080p50fps");
        var stage = parser.Parse("WRC Rally 2026 Spain Stage 17 26 04 720pEN50fps DAZN");

        service.ScoreMatch(dayPackage.EventTitle ?? "", stageEvent.Title, null, stageEvent, dayPackage).Core
            .Should().BeLessOrEqualTo(0);
        service.ScoreMatch(stage.EventTitle ?? "", stageEvent.Title, null, stageEvent, stage).Core
            .Should().BeGreaterOrEqualTo(50);
    }

    [Fact]
    public void MatchingRequiresTheRequestedRallyWhenStageNumbersAgree()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var importMatcher = ImportMatcher(db, parser);
        var wanted = Event("WRC", "WRC Rally Estonia SS1", "8");
        const string release = "WRC.2026.Rally.Croatia.SS1.1080p.WEB.H264";
        var parsed = parser.Parse(release);

        Matcher.ValidateRelease(Release(release), wanted).IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore(release, wanted).Should().Be(0);
        importMatcher.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeLessOrEqualTo(0);
    }

    [Fact]
    public void MatchingRequiresTheRequestedRallyWithSpacedStageTokens()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var importMatcher = ImportMatcher(db, parser);
        var wanted = Event("WRC", "WRC Rally Estonia SS 1", "8");
        const string release = "WRC.2026.Rally.Croatia.SS.1.1080p.WEB.H264";
        var parsed = parser.Parse(release);

        Matcher.ValidateRelease(Release(release), wanted).IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore(release, wanted).Should().Be(0);
        importMatcher.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeLessOrEqualTo(0);
    }

    [Theory]
    [InlineData("Daytona 500 Qualifying", "NASCAR Cup Series 2026 Round01 Daytona 500 Qualifying 1080p WEB H264")]
    [InlineData("Daytona 500 Qualifying", "NASCAR Cup Series 2026 Round01 Daytona 500 Quali 1080p WEB H264")]
    [InlineData("Daytona 500 Practice", "NASCAR Cup Series 2026 Round01 Daytona 500 Practice 1080p WEB H264")]
    [InlineData("Daytona 500 Practice", "NASCAR Cup Series 2026 Round01 Daytona 500 FP1 1080p WEB H264")]
    [InlineData("Daytona 500 Practice 1", "NASCAR.Cup.Series.2026.Round01.Daytona.500.Practice.1.1080p.WEB.H264")]
    [InlineData("Daytona 500 Practice 1", "NASCAR_Cup_Series_2026_Round01_Daytona_500_Practice_One_1080p_WEB_H264")]
    [InlineData("Daytona 500 Duels", "NASCAR Cup Series 2026 Round01 Daytona 500 Duels 1080p WEB H264")]
    public void MatchingAllowsTheRequestedNascarSession(string eventTitle, string release)
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var importMatcher = ImportMatcher(db, parser);
        var wanted = Event("NASCAR Cup Series", eventTitle, "1");
        var parsed = parser.Parse(release);

        var validation = Matcher.ValidateRelease(Release(release), wanted);
        validation.IsMatch.Should().BeTrue(
            $"confidence {validation.Confidence}; reasons {string.Join("; ", validation.MatchReasons)}; rejections {string.Join("; ", validation.Rejections)}");
        validation.IsHardRejection.Should().BeFalse();
        Scorer.CalculateMatchScore(release, wanted)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importMatcher.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeGreaterOrEqualTo(50);
    }

    [Fact]
    public void MatchingRejectsAConflictingNascarSession()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var importMatcher = ImportMatcher(db, parser);
        var wanted = Event(
            "NASCAR Cup Series",
            "Daytona 500 Qualifying",
            "1",
            "Daytona",
            new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc));
        const string release = "NASCAR Cup Series 2026 Round01 Daytona 500 Duels 2026.02.15 1080p WEB H264";
        var parsed = parser.Parse(release);

        Matcher.ValidateRelease(Release(release), wanted).IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore(release, wanted).Should().Be(0);
        importMatcher.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeLessOrEqualTo(0);
    }

    [Theory]
    [InlineData("NASCAR Cup Series 2026 Round01 Daytona 500 Practice 2 2026.02.15 1080p WEB H264")]
    [InlineData("NASCAR.Cup.Series.2026.Round01.Daytona.500.Practice.Two.2026.02.15.1080p.WEB.H264")]
    public void MatchingRejectsAConflictingNumberedNascarPractice(string release)
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var importMatcher = ImportMatcher(db, parser);
        var wanted = Event(
            "NASCAR Cup Series",
            "Daytona 500 Practice 1",
            "1",
            "Daytona",
            new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc));
        var parsed = parser.Parse(release);

        Matcher.ValidateRelease(Release(release), wanted).IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore(release, wanted).Should().Be(0);
        importMatcher.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeLessOrEqualTo(0);
    }

    [Fact]
    public void NascarCupRaceSponsorIsNotTreatedAsTheSiblingSeries()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var importMatcher = ImportMatcher(db, parser);
        var wanted = Event("NASCAR Cup Series", "Xfinity 500", "35");
        const string release = "NASCAR.Cup.Series.2026.Round35.Xfinity.500.1080p.WEB.H264";
        var parsed = parser.Parse(release);

        Matcher.ValidateRelease(Release(release), wanted).IsHardRejection.Should().BeFalse();
        Scorer.CalculateMatchScore(release, wanted)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importMatcher.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeGreaterOrEqualTo(50);
    }

    [Fact]
    public void ImportScoringUsesNamedNascarRaceAcrossMetadataRoundDrift()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var service = ImportMatcher(db, parser);
        var wanted = Event("NASCAR Cup Series", "Cook Out Southern 500", "27");
        var other = Event("NASCAR Cup Series", "Daytona 500", "27");
        const string release = "NASCAR Cup Series 2026 Round 26 Cook Out Southern 500 Weekend";
        var parsed = parser.Parse(release);

        service.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeGreaterOrEqualTo(50);
        service.ScoreMatch(parsed.EventTitle ?? release, other.Title, null, other, parsed).Core
            .Should().BeLessOrEqualTo(0);

        const string wrongRace = "NASCAR Cup Series 2026 Coke Zero Sugar 400 Daytona Weekend IPTV 1080p 60fps EN";
        var wrongParsed = parser.Parse(wrongRace);
        var daytona = Event("NASCAR Cup Series", "Daytona 500", "1");
        service.ScoreMatch(wrongParsed.EventTitle ?? wrongRace, daytona.Title, null, daytona, wrongParsed).Core
            .Should().BeLessOrEqualTo(0);
    }

    [Fact]
    public void ImportScoringUsesNascarRoundWhenSponsoredRaceNameIsMissing()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var service = ImportMatcher(db, parser);
        const string release = "NASCAR Cup Series 2026 Round01 Daytona Race 1080p WEB H264";
        var parsed = parser.Parse(release);
        var wanted = Event("NASCAR Cup Series", "Daytona 500", "1");

        service.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeGreaterOrEqualTo(50);
    }

    [Fact]
    public void NascarReleaseWithoutTheNamedSupportSessionIsRejected()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var importMatcher = ImportMatcher(db, parser);
        var wanted = Event(
            "NASCAR Cup Series",
            "Daytona 500 Duels",
            "1",
            "Daytona",
            new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc));
        const string release = "NASCAR.Cup.Series.2026.Round01.Daytona.500.2026.02.15.1080p.WEB.H264";
        var parsed = parser.Parse(release);

        Matcher.ValidateRelease(Release(release), wanted).IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore(release, wanted).Should().Be(0);
        importMatcher.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeLessOrEqualTo(0);
    }

    [Fact]
    public void AlternateWorldSuperbikeLeagueNameKeepsDetailedRaceIdentity()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var importMatcher = ImportMatcher(db, parser);
        var wanted = Event("Superbike World Championship", "Acerbis French Round - Race 1", "9");
        const string release = "WSBK.2026.Round09.France.Race.Two.1080p.WEB.H264";
        var parsed = parser.Parse(release);

        Matcher.ValidateRelease(Release(release), wanted).IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore(release, wanted).Should().Be(0);
        importMatcher.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeLessOrEqualTo(0);
    }

    [Theory]
    [InlineData("WSBK 2026 Round09 France Race Two 1080p WEB H264")]
    [InlineData("WSBK.2026.Round09.France.Race.Two.1080p.WEB.H264")]
    [InlineData("WSBK_2026_Round09_France_Race_Two_1080p_WEB_H264")]
    public void WorldSuperbikeSessionDetectionHandlesReleaseSeparators(string title)
    {
        EventPartDetector.DetectWorldSuperbikeSession(title).Should().Be("Race 2");
    }

    [Theory]
    [InlineData("IndyCar 2026 Round18 Laguna Seca Final Practice 1080p WEB H264")]
    [InlineData("IndyCar.2026.Round18.Laguna.Seca.Final.Practice.1080p.WEB.H264")]
    [InlineData("IndyCar_2026_Round18_Laguna_Seca_Final_Practice_1080p_WEB_H264")]
    public void IndyCarFinalPracticeDetectionHandlesReleaseSeparators(string title)
    {
        EventPartDetector.DetectMotorsportSessionIdentity(title, "IndyCar Series", releaseTitle: true)
            .Should().Be("Final Practice");
    }

    [Fact]
    public void ImportScoringUsesIndyCarFinalPracticeIdentity()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var importMatcher = ImportMatcher(db, parser);
        const string release = "IndyCar Series 2026 Round18 Laguna Seca Final Practice 1080p WEB H264";
        var parsed = parser.Parse(release);
        var wanted = Event("IndyCar Series", "IndyCar Grand Prix of Monterey Final Practice", "18");

        importMatcher.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeGreaterOrEqualTo(50);
    }

    [Fact]
    public void NascarVenueAndDateIdentifyAReleaseWithoutTheSponsoredRaceName()
    {
        const string release = "NASCAR Cup Series 2026 Cota 01 03 720pEN60fps Fox";
        var wanted = Event(
            "NASCAR Cup Series",
            "DuraMAX Grand Prix - Race",
            "3",
            "Circuit of the Americas",
            new DateTime(2026, 3, 1, 20, 30, 0, DateTimeKind.Utc));
        var other = Event(
            "NASCAR Cup Series",
            "Daytona 500",
            "1",
            "Daytona International Speedway",
            new DateTime(2026, 2, 15, 19, 30, 0, DateTimeKind.Utc));

        var wantedValidation = Matcher.ValidateRelease(Release(release), wanted);
        var otherValidation = Matcher.ValidateRelease(Release(release), other);

        wantedValidation.IsMatch.Should().BeTrue(
            $"confidence {wantedValidation.Confidence}; reasons {string.Join("; ", wantedValidation.MatchReasons)}; rejections {string.Join("; ", wantedValidation.Rejections)}");
        wantedValidation.IsHardRejection.Should().BeFalse();
        Scorer.CalculateMatchScore(release, wanted)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        otherValidation.IsMatch.Should().BeFalse();
        otherValidation.IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore(release, other).Should().Be(0);

        var sameVenueWrongDate = Event(
            "NASCAR Cup Series",
            "Texas Grand Prix",
            "30",
            "Circuit of the Americas",
            new DateTime(2026, 9, 20, 19, 0, 0, DateTimeKind.Utc));
        Scorer.CalculateMatchScore(release, sameVenueWrongDate).Should().Be(0);
    }

    [Fact]
    public void ImportScoringUsesNascarVenueAndDateWhenTheSponsoredRaceNameIsMissing()
    {
        using var db = Database();
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var service = ImportMatcher(db, parser);
        const string release = "NASCAR Cup Series 2026 Cota 01 03 720pEN60fps Fox";
        var parsed = parser.Parse(release);
        var wanted = Event(
            "NASCAR Cup Series",
            "DuraMAX Grand Prix - Race",
            "3",
            "Circuit of the Americas",
            new DateTime(2026, 3, 1, 20, 30, 0, DateTimeKind.Utc));
        var other = Event(
            "NASCAR Cup Series",
            "Daytona 500",
            "1",
            "Daytona International Speedway",
            new DateTime(2026, 2, 15, 19, 30, 0, DateTimeKind.Utc));

        service.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed).Core
            .Should().BeGreaterOrEqualTo(50);
        service.ScoreMatch(parsed.EventTitle ?? release, other.Title, null, other, parsed).Core
            .Should().BeLessOrEqualTo(0);
    }

    private static SportarrDbContext Database()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SportarrDbContext(options);
    }

    private static ImportMatchingService ImportMatcher(
        SportarrDbContext db,
        SportsFileNameParser parser) => new(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);

    private static Event Event(
        string leagueName,
        string title,
        string round,
        string location = "",
        DateTime? eventDate = null)
    {
        var date = eventDate ?? title switch
        {
            var value when value.Contains("Islas Canarias", StringComparison.OrdinalIgnoreCase) => new DateTime(2026, 4, 26, 12, 0, 0, DateTimeKind.Utc),
            var value when value.Contains("Safari", StringComparison.OrdinalIgnoreCase) => new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc),
            _ => new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc),
        };

        return new Event
        {
            Title = title,
            Sport = "Motorsport",
            Round = round,
            Season = "2026",
            SeasonNumber = 2026,
            EventDate = date,
            BroadcastDate = date,
            Location = location,
            League = new League { Name = leagueName, Sport = "Motorsport" },
        };
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://source.invalid/release",
        Indexer = "Fixture",
        Protocol = "Torrent",
        Size = 1_500_000_000,
        PublishDate = new DateTime(2026, 9, 6, 14, 0, 0, DateTimeKind.Utc),
    };

    private static int LibraryScore(
        string release,
        Event evt,
        SportsParseResult parsed,
        IReadOnlyList<int> roundRaceNumbers) => LibraryImportService.CalculateMatchConfidence(
            parsed.EventTitle ?? release,
            evt.Title,
            parsed.Organization,
            evt,
            parsed.EventDate,
            parsed.EventYear,
            parsed.RoundNumber,
            parsed.SeasonYearEnd,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport,
            roundRaceNumbers: roundRaceNumbers,
            sourceTitle: release);
}
