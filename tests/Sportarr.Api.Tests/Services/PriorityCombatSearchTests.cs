using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PriorityCombatQueryTests
{
    private readonly EventQueryService _service = new(NullLogger<EventQueryService>.Instance);

    [Theory]
    [InlineData("Boxing", "Zuffa Boxing 4 Opetaia vs Glanton", "2026-03-08", "Opetaia vs Glanton")]
    [InlineData("Boxing", "Tyson Fury vs Arslanbek Makhmudov", "2026-04-11", "Fury vs Makhmudov")]
    [InlineData("Boxing", "MVPW 06 Mayer vs Cameron", "2026-08-29", "Mayer vs Cameron")]
    [InlineData("ONE", "ONE Friday Fights 150 Kompetch vs Attachai", "2026-04-10", "ONE Friday Fights 150")]
    [InlineData("ONE", "ONE Fight Night 42 Mann vs Dzhabrailov", "2026-04-11", "ONE Fight Night 42")]
    [InlineData("ONE", "ONE Samurai 3 Nadaka vs Har Ling Om", "2026-09-12", "ONE Samurai 3")]
    [InlineData("Professional Fighters League", "PFL Africa 2 Nigeria", "2026-06-13", "PFL Africa Nigeria")]
    [InlineData("Professional Fighters League", "PFL Austin Eblen vs Kasanganay 2", "2026-07-18", "PFL Austin 2026")]
    [InlineData("Professional Fighters League", "PFL x Rizin", "2026-09-10", "PFL x Rizin")]
    [InlineData("WWE", "RAW #1724", "2026-06-08", "WWE Raw 2026 06 08")]
    [InlineData("WWE", "WrestleMania 42 Saturday", "2026-04-18", "WWE WrestleMania 42 Saturday")]
    [InlineData("WWE", "SummerSlam Sunday", "2026-08-02", "WWE SummerSlam Sunday 2026")]
    [InlineData("AEW", "Dynamite #350", "2026-06-17", "AEW Dynamite 2026 06 17")]
    [InlineData("AEW", "Forbidden Door", "2026-06-28", "AEW Forbidden Door 2026")]
    [InlineData("AEW", "Revolution Zero Hour", "2026-03-15", "AEW Revolution Zero Hour 2026")]
    [InlineData("Boxing", "Moses Itauma vs Filip Hrgovic", "2026-08-29", "Itauma vs Hrgovic")]
    [InlineData("ONE", "ONE Friday Fights 168 Wuttikrai vs Habibpour", "2026-08-28", "ONE Friday Fights 168")]
    [InlineData("Professional Fighters League", "PFL Tampa Cyborg vs Vieira", "2026-08-22", "PFL Tampa 2026")]
    [InlineData("Professional Fighters League", "PFL Europe 1 Hughes vs Miranda", "2026-05-24", "PFL Europe 1 2026")]
    [InlineData("Professional Fighters League", "PFL 2026 World Tournament 3 Johnny Eblen vs Josh Kasanganay", "2026-05-24", "PFL 2026 World Tournament 3")]
    [InlineData("Professional Fighters League", "PFL Africa 2 Nigeria Eblen vs Kasanganay", "2026-05-24", "PFL Africa Nigeria")]
    [InlineData("Professional Fighters League", "PFL Africa 2 Nigeria Eblen vs Josh Kasanganay", "2026-05-24", "PFL Africa Nigeria")]
    [InlineData("Professional Fighters League", "PFL Africa 2 Nigeria Johnny Eblen vs Kasanganay", "2026-05-24", "PFL Africa Nigeria")]
    [InlineData("WWE", "NXT #853", "2026-09-01", "WWE NXT 2026 09 01")]
    [InlineData("AEW", "All In London", "2026-08-30", "AEW All In London 2026")]
    public void FrozenCombatEvents_UseOneMeasuredQuery(
        string leagueName,
        string title,
        string broadcastDate,
        string expectedQuery)
    {
        var date = DateTime.Parse(broadcastDate);
        var evt = CombatEvent(leagueName, title, date);

        _service.BuildEventQueries(evt).Should().Equal(expectedQuery);
    }

    [Theory]
    [InlineData("PFL Austin Johnny Eblen vs Kasanganay 2", "PFL Austin 2026")]
    [InlineData("PFL San Diego Eblen vs Josh Kasanganay", "PFL San Diego 2026")]
    public void PflAsymmetricFighterNamesDoNotEnterTheCardQuery(
        string eventTitle,
        string expectedQuery)
    {
        var evt = CombatEvent(
            "Professional Fighters League",
            eventTitle,
            new DateTime(2026, 7, 18));

        _service.BuildEventQueries(evt).Should().Equal(expectedQuery);
    }

    private static Event CombatEvent(string leagueName, string title, DateTime broadcastDate) => new()
    {
        Id = 1,
        Title = title,
        Sport = "Combat",
        EventDate = broadcastDate,
        BroadcastDate = broadcastDate,
        BroadcastDateVerified = true,
        League = new League { Id = 1, Name = leagueName, Sport = "Combat" }
    };
}

public class PriorityCombatReleaseMatchingTests
{
    private readonly ReleaseMatchingService _service;

    public PriorityCombatReleaseMatchingTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var detector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _service = new ReleaseMatchingService(
            Mock.Of<ILogger<ReleaseMatchingService>>(), parser, detector);
    }

    [Theory]
    [InlineData("Boxing", "Zuffa Boxing 4 Opetaia vs Glanton", "2026-03-08", "Zuffa Boxing 4 Opetaia vs Glanton Full Event 08 03 26 Z3R0 720p")]
    [InlineData("ONE", "ONE Friday Fights 150 Kompetch vs Attachai", "2026-04-10", "One.Championship.ONE.Friday.Fights.150.1080p.WEBRip.H.264-TJ")]
    [InlineData("Professional Fighters League", "PFL Africa 2 Nigeria", "2026-06-13", "PFL.Africa.Nigeria.1080p.STAN.WEB.DL.AAC2.0.H.264-nVa")]
    [InlineData("Professional Fighters League", "PFL Austin Eblen vs Kasanganay 2", "2026-07-18", "2026 PFL Austin Main Card 18 07 720pEN60fps ESPN")]
    [InlineData("WWE", "RAW #1724", "2026-06-08", "WWE.RAW.2026.06.08.NF.DEF.1080p.WEB.h265-HEEL")]
    [InlineData("WWE", "RAW #1724", "2026-06-08", "WWE.RAW.20260608.1080p.WEB.H264-GROUP")]
    [InlineData("WWE", "SummerSlam Sunday", "2026-08-02", "WWE.SummerSlam.Sunday.2026.1080p.WEB.h264-HEEL")]
    [InlineData("AEW", "Dynamite #350", "2026-06-17", "AEW.Dynamite.2026.06.17.MAX.1080p.WEB.h264-HEEL")]
    [InlineData("AEW", "Forbidden Door", "2026-06-28", "AEW Forbidden Door 2026 Pack")]
    [InlineData("AEW", "Revolution Zero Hour", "2026-03-15", "AEW.Revolution.2026.Zero.Hour.TRILLERtV.1080p.WEBRip.h264-TJ")]
    [InlineData("WWE", "NXT #853", "2026-09-01", "WWE.NXT.2026.09.01.NF.iNT.1080p.WEB.h265-HEEL")]
    [InlineData("AEW", "All In London", "2026-08-30", "AEW.All.In.London.PPV.2026.Satfeed.480p.HDTV.H.264-Star")]
    public void ProviderRelease_WithExactEventIdentity_IsRssMatch(
        string leagueName,
        string eventTitle,
        string broadcastDate,
        string releaseTitle)
    {
        var result = _service.ValidateRelease(
            Release(releaseTitle),
            CombatEvent(leagueName, eventTitle, DateTime.Parse(broadcastDate)));

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
    }

    [Theory]
    [InlineData("PFL Austin Johnny Eblen vs Kasanganay 2", "PFL.Austin.2026.1080p.WEB.H264")]
    [InlineData("PFL San Diego Eblen vs Josh Kasanganay", "PFL.San.Diego.2026.1080p.WEB.H264")]
    public void PflCardNamedReleaseMatchesAsymmetricFighterMetadata(
        string eventTitle,
        string title)
    {
        var evt = CombatEvent(
            "Professional Fighters League",
            eventTitle,
            new DateTime(2026, 7, 18));
        var result = _service.ValidateRelease(Release(title), evt);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        new ReleaseMatchScorer().CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void PflCardNamedReleaseFromAnotherCityIsRejected()
    {
        var evt = CombatEvent(
            "Professional Fighters League",
            "PFL San Diego Eblen vs Josh Kasanganay",
            new DateTime(2026, 7, 18));

        _service.ValidateRelease(Release("PFL.San.Francisco.2026.1080p.WEB.H264"), evt)
            .IsMatch.Should().BeFalse();
    }

    [Theory]
    [InlineData("PFL Champions Series 1 Usman Nurmagomedov vs Paul Hughes", "PFL.Champions.Series.2.Nurmagomedov.vs.Hughes.2026.1080p.WEB.H264")]
    [InlineData("PFL Europe 1 Hughes vs Miranda", "PFL.Europe.2.2026.1080p.WEB.H264")]
    [InlineData("PFL 2026 World Tournament 3 Johnny Eblen vs Josh Kasanganay", "PFL.2026.World.Tournament.2.1080p.WEB.H264")]
    public void PflReleaseFromAnotherNumberedSeriesCardIsRejected(
        string eventTitle,
        string releaseTitle)
    {
        var evt = CombatEvent(
            "Professional Fighters League",
            eventTitle,
            new DateTime(2026, 7, 18));

        _service.ValidateRelease(Release(releaseTitle), evt).IsMatch.Should().BeFalse();
        new ReleaseMatchScorer().CalculateMatchScore(releaseTitle, evt).Should().Be(0);
    }

    [Theory]
    [InlineData("PFL.Africa.Dakar.Eblen.vs.Kasanganay.2026.1080p.WEB.H264")]
    [InlineData("PFL.Africa.2026.Dakar.Eblen.vs.Kasanganay.1080p.WEB.H264")]
    public void PflRegionalReleaseFromAnotherLocationIsRejected(string releaseTitle)
    {
        var evt = CombatEvent(
            "Professional Fighters League",
            "PFL Africa 2 Nigeria Eblen vs Kasanganay",
            new DateTime(2026, 7, 18));

        _service.ValidateRelease(Release(releaseTitle), evt).IsMatch.Should().BeFalse();
        new ReleaseMatchScorer().CalculateMatchScore(releaseTitle, evt).Should().Be(0);
    }

    [Theory]
    [InlineData("ONE", "ONE Fight Night 40 Smith vs Jones", "ONE.Fight.Night.40.Main.Card.Press.Conference.2026.1080p.WEB.H264", "Main Card")]
    [InlineData("AEW", "Forbidden Door", "AEW.Forbidden.Door.Zero.Hour.Trailer.2026.1080p.WEB.H264", "Countdown")]
    [InlineData("AEW", "Forbidden Door", "AEW.Forbidden.Door.Pre.Show.Trailer.2026.1080p.WEB.H264", "Countdown")]
    [InlineData("AEW", "Forbidden Door", "AEW.Forbidden.Door.Pre.Show.Highlights.2026.1080p.WEB.H264", "Countdown")]
    public void RequestedPartDoesNotAllowOtherNonEventContent(
        string leagueName,
        string eventTitle,
        string releaseTitle,
        string requestedPart)
    {
        var evt = CombatEvent(leagueName, eventTitle, new DateTime(2026, 7, 18));

        _service.ValidateRelease(
                Release(releaseTitle), evt, requestedPart, enableMultiPartEpisodes: true)
            .IsMatch.Should().BeFalse();
    }

    [Fact]
    public void PflSeriesReleaseMayOmitTheCardNumber()
    {
        var evt = CombatEvent(
            "Professional Fighters League",
            "PFL Champions Series 1 Usman Nurmagomedov vs Paul Hughes",
            new DateTime(2026, 7, 18));
        const string releaseTitle = "PFL.Champions.Series.2026.1080p.WEB.H264";

        var result = _service.ValidateRelease(Release(releaseTitle), evt);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
    }

    [Theory]
    [InlineData("Boxing", "Tyson Fury vs Arslanbek Makhmudov", "2026-04-11", "Boxing.Usyk.vs.Fury.2026.04.11.1080p.WEB")]
    [InlineData("ONE", "ONE Friday Fights 150 Kompetch vs Attachai", "2026-04-10", "ONE.Friday.Fights.151.1080p.WEB")]
    [InlineData("Professional Fighters League", "PFL Africa 2 Nigeria", "2026-06-13", "PFL.Europe.Brussels.1080p.WEB")]
    [InlineData("WWE", "RAW #1724", "2026-06-08", "WWE.RAW.2026.06.15.1080p.WEB")]
    [InlineData("WWE", "SummerSlam Sunday", "2026-08-02", "WWE.SummerSlam.Saturday.2026.1080p.WEB")]
    [InlineData("AEW", "Dynamite #350", "2026-06-17", "AEW.Dynamite.2026.06.24.1080p.WEB")]
    [InlineData("AEW", "Forbidden Door", "2026-06-28", "AEW.Forbidden.Door.2026.Pre.Show.The.Buy.In.1080p.WEB")]
    [InlineData("AEW", "All In London", "2026-08-30", "AEW.All.In.London.2026.Buy.In.MyAEW.1080p.WEB.H264-XWT")]
    [InlineData("Boxing", "Tyson Fury vs Arslanbek Makhmudov", "2026-04-11", "RING.2026.11.14.Fury.vs.Makhmudov.Main.Bout.1080p.WEB.x264-Z3R0")]
    [InlineData("WWE", "SummerSlam Sunday", "2026-08-02", "WWE 2026 SummerSlam Sunday 2026 02 08 720pEN60fps ESPN")]
    public void ConflictingCombatRelease_IsRejected(
        string leagueName,
        string eventTitle,
        string broadcastDate,
        string releaseTitle)
    {
        _service.ValidateRelease(
                Release(releaseTitle),
                CombatEvent(leagueName, eventTitle, DateTime.Parse(broadcastDate)))
            .IsMatch.Should().BeFalse();
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test"
    };

    internal static Event CombatEvent(string leagueName, string title, DateTime broadcastDate) => new()
    {
        Id = 1,
        Title = title,
        Sport = "Combat",
        EventDate = broadcastDate,
        BroadcastDate = broadcastDate,
        BroadcastDateVerified = true,
        League = new League { Id = 1, Name = leagueName, Sport = "Combat" }
    };
}

public class PriorityCombatScoringTests
{
    private readonly ReleaseMatchScorer _scorer = new();

    [Theory]
    [InlineData("Boxing", "Tyson Fury vs Arslanbek Makhmudov", "2026-04-11", "Tyson.Fury.vs.Arslanbek.Makhmudov.2026.1080p.WEB")]
    [InlineData("Boxing", "Tyson Fury vs Arslanbek Makhmudov", "2026-04-11", "RING Fury vs Makhmudov Main Card 11 04 26 Z3R0 1080p")]
    [InlineData("Boxing", "Zuffa Boxing 4 Opetaia vs Glanton", "2026-03-08", "Zuffa Boxing 4 Opetaia vs Glanton Full Event 08 03 26 Z3R0 720p")]
    [InlineData("ONE", "ONE Friday Fights 150 Kompetch vs Attachai", "2026-04-10", "One.Championship.ONE.Friday.Fights.150.1080p.WEBRip.H264-TJ")]
    [InlineData("Professional Fighters League", "PFL Africa 2 Nigeria", "2026-06-13", "PFL.Africa.Nigeria.1080p.WEB.H264-RBB")]
    [InlineData("Professional Fighters League", "PFL Austin Eblen vs Kasanganay 2", "2026-07-18", "2026 PFL Austin Main Card 18 07 720pEN60fps ESPN")]
    [InlineData("WWE", "RAW #1724", "2026-06-08", "WWE.RAW.2026.06.08.1080p.WEB")]
    [InlineData("AEW", "Forbidden Door", "2026-06-28", "AEW.Forbidden.Door.2026.1080p.WEB")]
    [InlineData("WWE", "NXT #853", "2026-09-01", "WWE.NXT.2026.09.01.NF.iNT.1080p.WEB.h265-HEEL")]
    [InlineData("AEW", "All In London", "2026-08-30", "AEW.All.In.London.PPV.2026.Satfeed.480p.HDTV.H.264-Star")]
    public void ExactCombatIdentity_IsSafeForAutomaticSearch(
        string leagueName,
        string eventTitle,
        string broadcastDate,
        string releaseTitle)
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            leagueName, eventTitle, DateTime.Parse(broadcastDate));

        _scorer.CalculateMatchScore(releaseTitle, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void ExactFightersCannotOverrideAConflictingPromotion()
    {
        const string title = "UFC.Fury.vs.Makhmudov.2026.04.11.1080p.WEB";
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "Boxing", "Tyson Fury vs Arslanbek Makhmudov", new DateTime(2026, 4, 11));
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse(title);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));

        matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(parsed.EventTitle ?? title, evt.Title, null, evt, parsed).Core
            .Should().BeLessOrEqualTo(0);
        LibraryImportService.CalculateMatchConfidence(
                parsed.EventTitle ?? title,
                evt.Title,
                parsed.Organization,
                evt,
                parsed.EventDate,
                parsed.EventYear,
                parsed.RoundNumber,
                parsed.SeasonYearEnd,
                parsedLocation: parsed.Location,
                parsedSport: parsed.Sport,
                sourceTitle: title)
            .Should().Be(0);
    }

    [Theory]
    [InlineData("Ultimate Fighting Championship", "Boxing.Fury.vs.Makhmudov.2026.04.11.1080p.WEB")]
    [InlineData("ONE FC", "UFC.Fury.vs.Makhmudov.2026.04.11.1080p.WEB")]
    [InlineData("World Wrestling Entertainment", "AEW.Fury.vs.Makhmudov.2026.04.11.1080p.WEB")]
    public void CanonicalPromotionAliasesRejectAConflictingPromotion(
        string league,
        string releaseTitle)
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            league, "Tyson Fury vs Arslanbek Makhmudov", new DateTime(2026, 4, 11));

        SearchNormalizationService.EvaluateCombatIdentity(
                releaseTitle, evt.Title, evt.League!.Name, evt.Sport)
            .Should().Be(CombatIdentityMatch.Mismatch);
    }

    [Theory]
    [InlineData("Looney.Tunes.Cartoons.S05E13.Yosemite.Samurai.1080p.WEB-DL")]
    [InlineData("One.Piece.983.The.Samurai.Warriors.1080p.WEB-DL")]
    [InlineData("SD.Gundam.Force.35.Samurai.Number.One.480p")]
    public void OneSamuraiQuery_UnrelatedTitlesStayBelowManualThreshold(string releaseTitle)
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "ONE", "ONE Samurai 3 Nadaka vs Har Ling Om", new DateTime(2026, 9, 12));

        _scorer.CalculateMatchScore(releaseTitle, evt)
            .Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Fact]
    public void AewMainEvent_BuyInReleaseStaysBelowManualThreshold()
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "AEW", "All In London", new DateTime(2026, 8, 30));

        _scorer.CalculateMatchScore(
                "AEW.All.In.London.2026.Buy.In.MyAEW.1080p.WEB.H264-XWT", evt)
            .Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Theory]
    [InlineData("WWE.RAW.20260608.1080p.WEB.H264-GROUP")]
    [InlineData("WWE_RAW_2026_06_08_1080p_WEB_H264_GROUP")]
    public void WeeklyWrestlingDateSeparatorsRemainSafeForAutomaticSearch(string title)
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "WWE", "RAW #1724", new DateTime(2026, 6, 8));

        _scorer.CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void NxtSpecialYearOnlyReleaseIsNotTreatedAsAWeeklyEpisode()
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "WWE", "NXT Stand & Deliver", new DateTime(2026, 4, 18));
        const string title = "WWE.NXT.Stand.and.Deliver.2026.1080p.WEB.H264-GROUP";
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var parsed = parser.Parse(title);

        matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        ImportMatchingTestHarness.Service().ScoreMatch(
                parsed.EventTitle ?? title, evt.Title, null, evt, parsed).Core
            .Should().BeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public void AnotherNxtSpecialCannotMatchTheSelectedSpecial()
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "WWE", "NXT Stand & Deliver", new DateTime(2026, 4, 18));
        const string title = "WWE.NXT.Deadline.2026.1080p.WEB.H264";
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var parsed = parser.Parse(title);

        matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeFalse();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
        ImportMatchingTestHarness.Service().ScoreMatch(
            parsed.EventTitle ?? title, evt.Title, null, evt, parsed).Core.Should().BeLessThan(50);
    }

    [Fact]
    public void ExactParticipantsCannotOverrideAConflictingOneCardNumber()
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "ONE", "ONE Friday Fights 150 Kompetch vs Attachai", new DateTime(2026, 4, 10));
        const string title = "ONE.Friday.Fights.151.Kompetch.vs.Attachai.2026.1080p.WEB.H264-GROUP";
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var parsed = parser.Parse(title);
        var validation = matcher.ValidateRelease(Release(title), evt);

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
        ImportMatchingTestHarness.Service().ScoreMatch(
                parsed.EventTitle ?? title, evt.Title, null, evt, parsed).Core
            .Should().BeLessThan(50);
    }

    [Fact]
    public void ExplicitSidePackageEventCannotBeOverriddenByRequestedMainShow()
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "AEW", "Revolution Zero Hour", new DateTime(2026, 3, 15));
        const string title = "AEW.Revolution.2026.PPV.1080p.WEB.H264-GROUP";
        var release = Release(title);
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));

        var validation = matcher.ValidateRelease(
            release, evt, requestedPart: "Main Show", enableMultiPartEpisodes: true);

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(
                title, evt, requestedPart: "Main Show", enableMultiPartEpisodes: true)
            .Should().Be(0);
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + title,
        Indexer = "Fixture"
    };

    [Theory]
    [InlineData("Boxing.Fury.vs.Usyk.1080p.WEB.DDP5.1.H264")]
    [InlineData("Boxing.Fury.vs.Usyk.1080p.WEB.DDP.5.1.H264")]
    [InlineData("Boxing.Fury.vs.Usyk.1080p.WEB.DD.5.1.H264")]
    [InlineData("Boxing.Fury.vs.Usyk.1080p.BluRay.TrueHD.5.1.H264")]
    [InlineData("Boxing.Fury.vs.Usyk.1080p.BluRay.DTS-HD.MA.5.1.H264")]
    [InlineData("Boxing.Fury.vs.Usyk.Atmos.5.1.H264")]
    [InlineData("Boxing.Fury.vs.Usyk.FLAC.5.1.H264")]
    [InlineData("Boxing.Fury.vs.Usyk.Opus.5.1.H264")]
    public void AudioChannelTokenIsNotACombatDate(string title)
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "Boxing", "Fury vs Usyk", new DateTime(2026, 4, 11));

        new ReleaseMatchScorer().CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }
}

public class PriorityCombatImportMatchingTests
{
    [Fact]
    public async Task ImportSuggestionRoutesRejectAnotherWrestleManiaEditionWithoutADate()
    {
        var options = new DbContextOptionsBuilder<Sportarr.Api.Data.SportarrDbContext>()
            .UseInMemoryDatabase($"wrestlemania-edition-import-{Guid.NewGuid()}")
            .Options;
        await using var db = new Sportarr.Api.Data.SportarrDbContext(options);
        var league = new League
        {
            Name = "WWE",
            Sport = "Combat",
            Added = DateTime.UtcNow,
            EventSortOrder = "desc",
            Tags = new List<int>()
        };
        var evt = new Event
        {
            Title = "WrestleMania 42 Saturday",
            Sport = "Combat",
            EventDate = new DateTime(2026, 4, 18),
            BroadcastDate = new DateTime(2026, 4, 18),
            BroadcastDateVerified = true,
            Season = "2026",
            League = league
        };
        db.Add(evt);
        await db.SaveChangesAsync();
        var service = new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);
        const string release = "WWE.WrestleMania.41.Saturday.1080p.WEB.H264";

        SearchNormalizationService.EvaluateCombatIdentity(
                release, evt.Title, league.Name, evt.Sport)
            .Should().Be(CombatIdentityMatch.Mismatch);
        new ReleaseMatchScorer().CalculateMatchScore(release, evt).Should().Be(0);
        var best = await service.FindBestMatchAsync(release, "/tmp/none.mkv");
        var all = await service.GetAllPossibleMatchesAsync(release);

        best!.EventId.Should().BeNull();
        all.Should().NotContain(candidate => candidate.EventId == evt.Id);
    }

    [Theory]
    [InlineData("PFL.Africa.Johnny.Eblen.vs.Josh.Kasanganay.2026.1080p.WEB.H264")]
    [InlineData("PFL.Africa.2026.Eblen.vs.Kasanganay.1080p.WEB.H264")]
    public void PflRegionalReleaseMayOmitLocationWhenTheParticipantsMatch(string releaseTitle)
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "Professional Fighters League",
            "PFL Africa 2 Nigeria Eblen vs Kasanganay",
            new DateTime(2026, 7, 18));
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse(releaseTitle);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));

        matcher.ValidateRelease(new ReleaseSearchResult
        {
            Title = releaseTitle,
            Guid = releaseTitle,
            DownloadUrl = "http://fixture.invalid/pfl",
            Indexer = "Fixture"
        }, evt).IsHardRejection.Should().BeFalse();
        new ReleaseMatchScorer().CalculateMatchScore(releaseTitle, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(parsed.EventTitle ?? releaseTitle, evt.Title, null, evt, parsed).Core
            .Should().BeGreaterThanOrEqualTo(50);
    }

    [Theory]
    [InlineData("PFL.Africa.Dakar.Eblen.vs.Kasanganay.2026.1080p.WEB.H264")]
    [InlineData("PFL.Africa.2026.Dakar.Eblen.vs.Kasanganay.1080p.WEB.H264")]
    public void ImportScoringRejectsPflFightersFromAnotherRegionalLocation(string releaseTitle)
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "Professional Fighters League",
            "PFL Africa 2 Nigeria Eblen vs Kasanganay",
            new DateTime(2026, 7, 18));
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse(releaseTitle);

        ImportMatchingTestHarness.Service()
            .ScoreMatch(parsed.EventTitle ?? releaseTitle, evt.Title, null, evt, parsed).Core
            .Should().BeLessOrEqualTo(0);
    }

    [Fact]
    public async Task ImportSuggestionRoutesRejectACombatReleaseFromAnotherYear()
    {
        var options = new DbContextOptionsBuilder<Sportarr.Api.Data.SportarrDbContext>()
            .UseInMemoryDatabase($"pfl-year-import-{Guid.NewGuid()}")
            .Options;
        await using var db = new Sportarr.Api.Data.SportarrDbContext(options);
        var league = new League
        {
            Name = "Professional Fighters League",
            Sport = "Combat",
            Added = DateTime.UtcNow,
            EventSortOrder = "desc",
            Tags = new List<int>()
        };
        var evt = new Event
        {
            Title = "PFL Austin Eblen vs Kasanganay 2",
            Sport = "Combat",
            EventDate = new DateTime(2026, 7, 18),
            BroadcastDate = new DateTime(2026, 7, 18),
            BroadcastDateVerified = true,
            Season = "2026",
            League = league
        };
        db.Add(evt);
        await db.SaveChangesAsync();
        var service = new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);
        const string release = "PFL.Austin.2025.1080p.WEB.H264";

        var best = await service.FindBestMatchAsync(release, "/tmp/none.mkv");
        var all = await service.GetAllPossibleMatchesAsync(release);

        best!.EventId.Should().BeNull();
        all.Should().NotContain(candidate => candidate.EventId == evt.Id);
    }

    [Fact]
    public async Task ImportCandidateRoutesDetectAewZeroHourInLeagueContext()
    {
        var options = new DbContextOptionsBuilder<Sportarr.Api.Data.SportarrDbContext>()
            .UseInMemoryDatabase($"aew-import-{Guid.NewGuid()}")
            .Options;
        await using var db = new Sportarr.Api.Data.SportarrDbContext(options);
        var league = new League
        {
            Name = "AEW",
            Sport = "Combat",
            Added = DateTime.UtcNow,
            EventSortOrder = "desc",
            Tags = new List<int>()
        };
        var evt = new Event
        {
            Title = "Forbidden Door",
            Sport = "Combat",
            EventDate = new DateTime(2026, 6, 29, 0, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 6, 28),
            BroadcastDateVerified = true,
            Season = "2026",
            League = league
        };
        db.Add(evt);
        await db.SaveChangesAsync();
        var service = new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);
        const string release = "AEW.Forbidden.Door.2026.06.28.Zero.Hour.1080p.WEB.H264";

        var best = await service.FindBestMatchAsync(release, "/tmp/none.mkv");
        var all = await service.GetAllPossibleMatchesAsync(release);

        best!.EventId.Should().Be(evt.Id);
        best.Part.Should().Be("Countdown");
        all.Should().ContainSingle(candidate =>
            candidate.EventId == evt.Id && candidate.Part == "Countdown");
    }

    [Theory]
    [InlineData("Boxing", "Tyson Fury vs Arslanbek Makhmudov", "2026-04-11", "Tyson.Fury.vs.Arslanbek.Makhmudov.2026.1080p.WEB")]
    [InlineData("ONE", "ONE Friday Fights 150 Kompetch vs Attachai", "2026-04-10", "One.Championship.ONE.Friday.Fights.150.1080p.WEBRip.H264-TJ")]
    [InlineData("Professional Fighters League", "PFL Africa 2 Nigeria", "2026-06-13", "PFL.Africa.Nigeria.1080p.WEB.H264-RBB")]
    [InlineData("WWE", "RAW #1724", "2026-06-08", "WWE.RAW.2026.06.08.1080p.WEB")]
    [InlineData("WWE", "RAW #1724", "2026-06-08", "WWE.RAW.20260608.1080p.WEB.H264-GROUP")]
    [InlineData("AEW", "Forbidden Door", "2026-06-28", "AEW.Forbidden.Door.2026.1080p.WEB")]
    [InlineData("WWE", "NXT #853", "2026-09-01", "WWE.NXT.2026.09.01.NF.iNT.1080p.WEB.h265-HEEL")]
    [InlineData("AEW", "All In London", "2026-08-30", "AEW.All.In.London.PPV.2026.Satfeed.480p.HDTV.H.264-Star")]
    public void ProviderRelease_HasImportableCoreIdentity(
        string leagueName,
        string eventTitle,
        string broadcastDate,
        string releaseTitle)
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            leagueName, eventTitle, DateTime.Parse(broadcastDate));
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse(releaseTitle);
        var service = ImportMatchingTestHarness.Service();

        service.ScoreMatch(releaseTitle, evt.Title, null, evt, parsed).Core
            .Should().BeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public void AewMainEvent_BuyInReleaseIsNotImportable()
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "AEW", "All In London", new DateTime(2026, 8, 30));
        var release = "AEW.All.In.London.2026.Buy.In.MyAEW.1080p.WEB.H264-XWT";
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse(release);

        ImportMatchingTestHarness.Service().ScoreMatch(release, evt.Title, null, evt, parsed).Core
            .Should().BeLessThan(50);
    }

    [Fact]
    public void RequestedCountdownAcceptsAewZeroHourNaming()
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "AEW", "Forbidden Door", new DateTime(2026, 6, 28));
        var release = new ReleaseSearchResult
        {
            Title = "AEW.Forbidden.Door.2026.Zero.Hour.1080p.WEB.H264",
            Guid = "countdown-fixture",
            DownloadUrl = "http://fixture.invalid/countdown",
            Indexer = "Fixture"
        };
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));

        var result = matcher.ValidateRelease(
            release, evt, requestedPart: "Countdown", enableMultiPartEpisodes: true);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.IsHardRejection.Should().BeFalse();
        new ReleaseMatchScorer().CalculateMatchScore(
                release.Title, evt, requestedPart: "Countdown", enableMultiPartEpisodes: true)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);

        var parsed = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(release.Title);
        ImportMatchingTestHarness.Service().ScoreMatch(
                parsed.EventTitle ?? release.Title, evt.Title, "Countdown", evt, parsed).Core
            .Should().BeGreaterThanOrEqualTo(50);
    }

    [Theory]
    [InlineData("WWE", "WrestleMania 42", "WWE.WrestleMania.42.Kickoff.1080p.WEB.H264")]
    [InlineData("AEW", "Forbidden Door", "AEW.Forbidden.Door.2026.ZeroHour.1080p.WEB.H264")]
    [InlineData("AEW", "Forbidden Door", "AEW.Forbidden.Door.2026.BuyIn.1080p.WEB.H264")]
    [InlineData("AEW", "Forbidden Door", "AEW.Forbidden.Door.2026.PreShow.1080p.WEB.H264")]
    [InlineData("AEW", "Forbidden Door", "AEW_Forbidden_Door_2026_06_28_Pre_Show_1080p_WEB_H264")]
    public void RequestedCountdownAcceptsTrackerSidePackageAliases(
        string leagueName,
        string eventTitle,
        string releaseTitle)
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            leagueName, eventTitle, new DateTime(2026, 6, 28));

        SearchNormalizationService.EvaluateCombatIdentity(
                releaseTitle,
                evt.Title,
                evt.League?.Name,
                evt.Sport,
                requestedPart: "Countdown",
                enableMultiPartEpisodes: true)
            .Should().Be(CombatIdentityMatch.Match);
        new ReleaseMatchScorer().CalculateMatchScore(
                releaseTitle, evt, requestedPart: "Countdown", enableMultiPartEpisodes: true)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var release = new ReleaseSearchResult
        {
            Title = releaseTitle,
            Guid = releaseTitle,
            DownloadUrl = "http://fixture.invalid/side-package",
            Indexer = "Fixture"
        };
        matcher.ValidateRelease(
                release, evt, requestedPart: "Countdown", enableMultiPartEpisodes: true)
            .IsMatch.Should().BeTrue();
    }

    [Fact]
    public void SameOpponentsOnAnotherDateCannotSatisfyANumberedRematch()
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "Boxing", "Fury vs Usyk 2", new DateTime(2026, 11, 14));
        const string release = "Boxing.Fury.vs.Usyk.2026.04.11.1080p.WEB.H264";
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse(release);

        matcher.ValidateRelease(new ReleaseSearchResult
        {
            Title = release,
            Guid = release,
            DownloadUrl = "http://fixture.invalid/rematch",
            Indexer = "Fixture"
        }, evt).IsHardRejection.Should().BeTrue();
        new ReleaseMatchScorer().CalculateMatchScore(release, evt).Should().Be(0);
        ImportMatchingTestHarness.Service().ScoreMatch(
            parsed.EventTitle ?? release, evt.Title, null, evt, parsed).Core.Should().BeLessOrEqualTo(0);
    }

    [Fact]
    public void AnotherNumberedCardWithTheSameFightersCannotBoostImportScoring()
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "Ultimate Fighting Championship",
            "UFC 287: Pereira vs Adesanya",
            new DateTime(2026, 4, 8));
        const string release = "UFC.281.Adesanya.vs.Pereira.1080p.WEB.H264";
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse(release);

        SearchNormalizationService.EvaluateCombatIdentity(
                release, evt.Title, evt.League?.Name, evt.Sport)
            .Should().Be(CombatIdentityMatch.Mismatch);
        ImportMatchingTestHarness.Service().ScoreMatch(
                parsed.EventTitle ?? release, evt.Title, null, evt, parsed).Core
            .Should().BeLessOrEqualTo(0);
    }

    [Fact]
    public void LaterNumberedRematchCannotSatisfyTheOriginalFight()
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "Boxing", "Fury vs Usyk", new DateTime(2026, 4, 11));
        const string release = "Boxing.Fury.vs.Usyk.2.2026.11.14.1080p.WEB.H264";
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse(release);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));

        matcher.ValidateRelease(new ReleaseSearchResult
        {
            Title = release,
            Guid = release,
            DownloadUrl = "http://fixture.invalid/rematch-two",
            Indexer = "Fixture"
        }, evt).IsHardRejection.Should().BeTrue();
        new ReleaseMatchScorer().CalculateMatchScore(release, evt).Should().Be(0);
        ImportMatchingTestHarness.Service().ScoreMatch(
            parsed.EventTitle ?? release, evt.Title, null, evt, parsed).Core.Should().BeLessOrEqualTo(0);
    }

    [Fact]
    public void WeeklyWrestlingImportRequiresTheEpisodeDate()
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "WWE", "RAW #1724", new DateTime(2026, 6, 8));
        const string release = "WWE.RAW.2026.06.15.1080p.WEB.H264";
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse(release);

        ImportMatchingTestHarness.Service().ScoreMatch(
            parsed.EventTitle ?? release, evt.Title, null, evt, parsed).Core.Should().BeLessThan(50);
    }

    [Fact]
    public void WeeklyWrestlingReleaseMustIdentifyTheEpisode()
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "WWE", "RAW #1724", new DateTime(2026, 6, 8));
        const string release = "WWE.RAW.2026.1080p.WEB.H264";
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse(release);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));

        matcher.ValidateRelease(new ReleaseSearchResult
        {
            Title = release,
            Guid = release,
            DownloadUrl = "http://fixture.invalid/weekly",
            Indexer = "Fixture"
        }, evt).IsHardRejection.Should().BeTrue();
        new ReleaseMatchScorer().CalculateMatchScore(release, evt).Should().Be(0);
        ImportMatchingTestHarness.Service().ScoreMatch(
            parsed.EventTitle ?? release, evt.Title, null, evt, parsed).Core.Should().BeLessThan(50);
    }

    [Fact]
    public void ReleaseGroupCannotSupplyTheMissingBoxingOpponent()
    {
        var evt = PriorityCombatReleaseMatchingTests.CombatEvent(
            "Boxing", "Wardley vs Dubois", new DateTime(2026, 5, 9));
        const string release = "RING.Wardley.vs.Hrgovic.2026.05.09.1080p.WEB-Dubois";

        SearchNormalizationService.EvaluateCombatIdentity(
            release, evt.Title, evt.League?.Name, evt.Sport).Should().Be(CombatIdentityMatch.Mismatch);
        new ReleaseMatchScorer().CalculateMatchScore(release, evt).Should().Be(0);
    }

    [Fact]
    public void ReleaseGroupCannotSupplyTheMissingTennisOpponent()
    {
        const string release = "Wimbledon.Alcaraz.vs.Djokovic.2026.1080p.WEB-Sinner";

        SearchNormalizationService.EvaluateTennisIdentity(
            release, "Wimbledon Carlos Alcaraz vs Jannik Sinner")
            .Should().Be(TennisIdentityMatch.ParticipantMismatch);
    }

    [Fact]
    public void HyphenatedTennisSurnameRemainsPartOfTheMatchup()
    {
        SearchNormalizationService.EvaluateTennisIdentity(
                "Wimbledon.2026.Alcaraz.vs.Auger-Aliassime.1080p.WEB",
                "Wimbledon Carlos Alcaraz vs Felix Auger-Aliassime")
            .Should().Be(TennisIdentityMatch.Match);
    }
}
