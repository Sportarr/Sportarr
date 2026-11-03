using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report, November 2026: releases from other sports and TV shows were
/// grabbed and imported. Basketball and women's AFL "Round N" releases filled
/// Dana Whites Contender Series "Week N", a cooking show filled the 2010
/// Belgian Grand Prix race, and a quiz show filled UFC 75. RSS sync checks a
/// release only with ValidateRelease, so these tests use that path. The
/// events carry the values the live database holds.
/// </summary>
public class WrongSportReleaseMatchingTests
{
    private readonly ReleaseMatchingService _matchingSvc;

    public WrongSportReleaseMatchingTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _matchingSvc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static readonly League Ufc = new() { Id = 2, Name = "UFC", Sport = "Combat" };
    private static readonly League FormulaOne = new() { Id = 3, Name = "Formula 1", Sport = "Motorsport" };

    // Round follows the league's card order, so it is not the week number.
    private static Event DwcsWeek(int week)
    {
        var (round, date) = week switch
        {
            2 => ("33", new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc)),
            4 => ("38", new DateTime(2026, 9, 1, 23, 0, 0, DateTimeKind.Utc)),
            5 => ("40", new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc)),
            8 => ("46", new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc)),
            _ => throw new ArgumentOutOfRangeException(nameof(week)),
        };
        return new Event
        {
            Id = 667863 + week,
            Title = $"Dana Whites Contender Series season 10 Week {week}",
            Sport = "Combat",
            Season = "2026",
            SeasonNumber = 2026,
            Round = round,
            EventDate = date,
            Location = "US",
            League = Ufc,
        };
    }

    private static Event BelgianGp2010Race() => new()
    {
        Id = 13145,
        Title = "Belgian Grand Prix - Race",
        Sport = "Motorsport",
        Season = "2010",
        SeasonNumber = 2010,
        Round = "13",
        EventDate = new DateTime(2010, 8, 29, 0, 0, 0, DateTimeKind.Utc),
        League = FormulaOne,
    };

    private static Event Ufc75() => new()
    {
        Id = 11628,
        Title = "UFC 75 Champion vs Champion",
        Sport = "Combat",
        Season = "2007",
        SeasonNumber = 2007,
        Round = "13",
        EventDate = new DateTime(2007, 9, 8, 0, 0, 0, DateTimeKind.Utc),
        League = Ufc,
    };

    [Theory]
    [InlineData(4, "NBL.2026-27.Round.04.Perth.Wildcats.Vs.New.Zealand.Breakers.1080p.HDTV.H264-DARKSPORT")]
    [InlineData(4, "NZ.Rally.Championship.2026.S01E04.Round.4.Hawkes.Bay.Rally.1080p.OSN.WEB-DL.AAC2.0.H.264-POWER")]
    [InlineData(8, "AFLW.2026.Round.08.Power.Vs.Crows.1080p.HDTV.H264-DARKSPORT")]
    [InlineData(2, "WNBL.2026-27.Round.02.Townsville.Fire.Vs.Sydney.Flames.1080p.HDTV.H264-DARKSPORT")]
    [InlineData(5, "NBL.2026-27.Round.05.South.East.Melbourne.Phoenix.Vs.Cairns.Taipans.1080p.HDTV.H264-DARKSPORT")]
    public void AnotherSportsRoundIsNotTheContenderSeriesWeek(int week, string release)
    {
        var result = _matchingSvc.ValidateRelease(Rel(release), DwcsWeek(week));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void AWeekNumberAloneGivesNoLeagueIdentity()
    {
        var result = _matchingSvc.ValidateRelease(
            Rel("NBL.2026-27.Round.04.Perth.Wildcats.Vs.New.Zealand.Breakers.1080p.HDTV.H264-DARKSPORT"), DwcsWeek(4));

        result.MatchReasons.Should().NotContain("Event number matches");
        result.Rejections.Should().Contain(r => r.StartsWith("No league identity"));
    }

    [Fact]
    public void ACookingShowIsNotTheBelgianGrandPrix()
    {
        var result = _matchingSvc.ValidateRelease(
            Rel("The.Great.Food.Truck.Race.2010.S11E05.Belgian.Waffles.Breakfast.Sandwiches.and.Maple.Syrup.1080p.DSCP.WEB-DL.DDP2.0.H.264-FUZEER"),
            BelgianGp2010Race());

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void AQuizShowIsNotUfc75()
    {
        var result = _matchingSvc.ValidateRelease(
            Rel("Only Connect (2008) - S00E60 - Champion of Champions Special Strigiformes v Thrifters [WEBDL-1080p][AAC 2.0][h264]-KALLISTI"),
            Ufc75());

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void ATitleWordOnBothSidesOfVsIsNotAFighterPair()
    {
        EventPartDetector.TryExtractFighterSurnames("UFC 75 Champion vs Champion", out _, out _).Should().BeFalse();

        EventPartDetector.TryExtractFighterSurnames("UFC 300: Pereira vs Hill", out var a, out var b).Should().BeTrue();
        a.Should().BeEquivalentTo("pereira");
        b.Should().BeEquivalentTo("hill");
    }

    [Fact]
    public void TwoFightersWhoShareASurnameAreStillAPair()
    {
        EventPartDetector.TryExtractFighterSurnames("UFC Fight Night: Wanderlei Silva vs Thiago Silva", out var a, out var b).Should().BeTrue();
        a.Should().BeEquivalentTo("silva");
        b.Should().BeEquivalentTo("silva");
    }

    [Fact]
    public void AReleaseRoundNumberGivesNoLeagueIdentityToANumberedCard()
    {
        var pfl4 = new Event
        {
            Id = 404,
            Title = "PFL 4",
            Sport = "Combat",
            Season = "2026",
            SeasonNumber = 2026,
            EventDate = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Id = 5, Name = "PFL", Sport = "Combat" },
        };

        var result = _matchingSvc.ValidateRelease(Rel("AFLW.2026.Round.04.Power.Vs.Crows.1080p.HDTV.H264-DARKSPORT"), pfl4);

        result.MatchReasons.Should().NotContain("Event number matches");
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void ASeriesLabelCanGivePartOfTheLeagueName()
    {
        var cookOut400 = new Event
        {
            Id = 8,
            Title = "Cook Out 400",
            Sport = "Motorsport",
            Season = "2026",
            SeasonNumber = 2026,
            Round = "8",
            EventDate = new DateTime(2026, 4, 12, 18, 0, 0, DateTimeKind.Utc),
            League = new League { Id = 7, Name = "NASCAR Cup Series", Sport = "Motorsport" },
        };

        var result = _matchingSvc.ValidateRelease(Rel("NASCAR.Cup.2026.S01E08.Martinsville.Race.1080p.WEB.h264-VERUM"), cookOut400);

        result.Rejections.Should().NotContain(r => r.StartsWith("Series label"));
    }

    [Fact]
    public void ANumberedCardStillGivesLeagueIdentity()
    {
        var ufc300 = new Event
        {
            Id = 300,
            Title = "UFC 300: Pereira vs Hill",
            Sport = "Combat",
            Season = "2024",
            SeasonNumber = 2024,
            EventDate = new DateTime(2024, 4, 13, 22, 0, 0, DateTimeKind.Utc),
            League = Ufc,
        };

        var result = _matchingSvc.ValidateRelease(Rel("UFC.300.Pereira.vs.Hill.PPV.1080p.WEB.h264-VERUM"), ufc300);

        result.MatchReasons.Should().Contain("Event number matches");
        result.IsMatch.Should().BeTrue();
    }

    [Theory]
    [InlineData("UFC.Tuesday.Night.Contender.Series.S10W04.1080p.WEB.h264-VERUM")]
    [InlineData("Dana.Whites.Contender.Series.S10W04.1080p.WEB.h264-VERUM")]
    public void AContenderSeriesLabelNamesTheEvent(string release)
    {
        var result = _matchingSvc.ValidateRelease(Rel(release), DwcsWeek(4));

        result.Rejections.Should().NotContain(r => r.StartsWith("Series label"));
    }

    [Fact]
    public void AFormulaOneEpisodeLabelNamesTheLeague()
    {
        var chinese = new Event
        {
            Id = 2,
            Title = "Chinese Grand Prix - Race",
            Sport = "Motorsport",
            Season = "2026",
            SeasonNumber = 2026,
            Round = "2",
            EventDate = new DateTime(2026, 3, 22, 7, 0, 0, DateTimeKind.Utc),
            League = FormulaOne,
        };

        var result = _matchingSvc.ValidateRelease(Rel("Formula 1 - S2026E02 - Chinese Grand Prix - Race [WEBDL-1080p]"), chinese);

        result.Rejections.Should().NotContain(r => r.StartsWith("Series label"));
        result.IsMatch.Should().BeTrue();
    }

    [Theory]
    [InlineData("WEC.2026.S01E03.6.Hours.of.Spa.Race.1080p.WEB.h264-VERUM")]
    [InlineData("FIA.WEC.2026.S01E03.6.Hours.of.Spa.Race.1080p.WEB.h264-VERUM")]
    public void ASeriesLabelCanNameTheLeagueByItsAbbreviation(string release)
    {
        var spa = new Event
        {
            Id = 9,
            Title = "6 Hours of Spa-Francorchamps",
            Sport = "Motorsport",
            Season = "2026",
            SeasonNumber = 2026,
            Round = "3",
            EventDate = new DateTime(2026, 5, 9, 12, 0, 0, DateTimeKind.Utc),
            League = new League { Id = 9, Name = "FIA World Endurance Championship", Sport = "Motorsport" },
        };

        var result = _matchingSvc.ValidateRelease(Rel(release), spa);

        result.Rejections.Should().NotContain(r => r.StartsWith("Series label"));
    }

    [Fact]
    public void AnOlderLeagueNameInTheLabelStillNamesTheLeague()
    {
        var bathurst = new Event
        {
            Id = 19,
            Title = "Bathurst 1000",
            Sport = "Motorsport",
            Season = "2026",
            SeasonNumber = 2026,
            Round = "19",
            EventDate = new DateTime(2026, 10, 11, 3, 0, 0, DateTimeKind.Utc),
            League = new League { Id = 19, Name = "Australian V8 Supercars", Sport = "Motorsport" },
        };

        var result = _matchingSvc.ValidateRelease(Rel("V8.Supercars.2026.S01E19.Bathurst.1000.Race.1080p.WEB.h264-VERUM"), bathurst);

        result.Rejections.Should().NotContain(r => r.StartsWith("Series label"));
    }

    [Fact]
    public void AFormulaOneLabelSpelledOutNamesTheLeague()
    {
        var chinese = new Event
        {
            Id = 2,
            Title = "Chinese Grand Prix - Race",
            Sport = "Motorsport",
            Season = "2026",
            SeasonNumber = 2026,
            Round = "2",
            EventDate = new DateTime(2026, 3, 22, 7, 0, 0, DateTimeKind.Utc),
            League = FormulaOne,
        };

        var result = _matchingSvc.ValidateRelease(Rel("Formula.One.2026.S01E02.Chinese.Grand.Prix.Race.1080p.WEB.h264-VERUM"), chinese);

        result.Rejections.Should().NotContain(r => r.StartsWith("Series label"));
    }
}
