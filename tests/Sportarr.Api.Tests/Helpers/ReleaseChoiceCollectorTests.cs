using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Helpers;

public class ReleaseChoiceCollectorTests
{
    // The same title and key as the shared stem fixture.
    private const string FixtureTitle = "Formula1.2024.Round01.Bahrain.Race.1080p.F1TV.WEB-DL.AAC2.0.H.264-GROUP";
    private const string FixtureKey = "formula1 2024 round01 bahrain race f1tv";

    private static Event Evt(string? eventId = "ev-0012345", string? leagueId = "lg-000142", string? season = "2024") => new()
    {
        Title = "Bahrain Grand Prix", Sport = "Motorsport", ExternalId = eventId, Season = season,
        League = leagueId == null ? null : new League { Name = "Formula 1", Sport = "Motorsport", ExternalId = leagueId },
    };

    private static GrabHistory Grab(string title = FixtureTitle, GrabSource source = GrabSource.InteractivePick,
        int? score = 35, bool rejected = false, string protocol = "Torrent", bool hint = false) => new()
    {
        Title = title, Indexer = "Private tracker", DownloadUrl = "http://indexer.invalid/1", Guid = "g-1",
        Protocol = protocol, GrabSource = source, MatchScoreAtGrab = score, IdentityRejectedAtGrab = rejected,
        CrowdHintShown = hint,
    };

    private static ReleaseChoice? Build(GrabHistory? grab = null, bool pack = false, Event? evt = null,
        bool year = true, IndexerType? type = IndexerType.Torznab) =>
        ReleaseChoiceCollector.FromImport(grab ?? Grab(), pack, evt ?? Evt(), year, type);

    [Fact]
    public void AValidPickBuildsTheFullChoice()
    {
        var choice = Build();

        Assert.NotNull(choice);
        Assert.Equal("lg-000142", choice!.LeagueId);
        Assert.Equal("ev-0012345", choice.EventId);
        Assert.Equal("2024", choice.Season);
        Assert.Equal(FixtureKey, choice.TitleNorm);
        Assert.Equal(FixtureTitle, choice.TitleDisplay);
        Assert.Equal("override", choice.Kind);
        Assert.Equal("low", choice.Verdict);
        Assert.Equal(35, choice.MatchScore);
        Assert.Equal("torznab", choice.IndexerFamily);
        Assert.Equal("torrent", choice.Protocol);
        Assert.False(choice.HintShown);
    }

    [Fact]
    public void TheHintFlagTravelsWithTheChoice() =>
        Assert.True(Build(Grab(hint: true))!.HintShown);

    [Theory]
    [InlineData(GrabSource.Unknown)]
    [InlineData(GrabSource.Rss)]
    [InlineData(GrabSource.Automatic)]
    [InlineData(GrabSource.UserStartedAutomatic)]
    [InlineData(GrabSource.Regrab)]
    [InlineData(GrabSource.PendingReleaseReaper)]
    [InlineData(GrabSource.SeasonSearch)]
    public void OnlyAPickByHandIsReported(GrabSource source) =>
        Assert.Null(Build(Grab(source: source)));

    [Fact]
    public void APackIsNotReported() => Assert.Null(Build(pack: true));

    [Fact]
    public void AReleaseTheAppMatchedWellIsNotReported() => Assert.Null(Build(Grab(score: 70)));

    [Fact]
    public void ARejectionWithNoScoreIsNotReported() => Assert.Null(Build(Grab(score: null, rejected: true)));

    [Theory]
    [InlineData(null)]
    [InlineData("12345")]
    [InlineData("ev-12345")]
    [InlineData("lg-0012345")]
    public void AnEventWithNoHubIdIsNotReported(string? eventId) =>
        Assert.Null(Build(evt: Evt(eventId: eventId)));

    [Theory]
    [InlineData(null)]
    [InlineData("4387")]
    [InlineData("ev-000142")]
    // The hub's release choice handler accepts exactly six digits.
    [InlineData("lg-0001420")]
    [InlineData("lg-00142")]
    public void ALeagueWithNoHubIdIsNotReported(string? leagueId) =>
        Assert.Null(Build(evt: Evt(leagueId: leagueId)));

    [Theory]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p {sportarr-ev-312923}")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p sportarr-lg-000032")]
    public void ATitleWithASportarrIdIsNotReported(string title) => Assert.Null(Build(Grab(title)));

    [Fact]
    public void ATitleWithNoYearIsNotReported() => Assert.Null(Build(year: false));

    [Theory]
    [InlineData("F1 2024/25 Bahrain Race 1080p")]
    [InlineData(@"F1 2024\25 Bahrain Race 1080p")]
    public void ATitleWithASlashIsNotReported(string title) => Assert.Null(Build(Grab(title)));

    [Fact]
    public void ATitleWithAWebAddressIsNotReported() =>
        Assert.Null(Build(Grab("[www.Example.org] - Formula1.2024.Round01.Bahrain.Race.1080p")));

    [Theory]
    [InlineData("[TGx] UFC.300.Pereira.vs.Hill.PPV.2024.1080p.WEB.h264-VERUM")]
    [InlineData("(Fixture) UFC.300.Pereira.vs.Hill.PPV.2024.1080p")]
    [InlineData("{Fixture} UFC.300.Pereira.vs.Hill.PPV.2024.1080p")]
    [InlineData("  [TGx] UFC.300.Pereira.vs.Hill.PPV.2024.1080p")]
    public void ATitleThatStartsWithABracketTagIsNotReported(string title) => Assert.Null(Build(Grab(title)));

    [Fact]
    public void ABracketTagLaterInTheTitleIsStillReported() =>
        Assert.NotNull(Build(Grab("UFC.300.Pereira.vs.Hill.PPV.2024.1080p.WEB.h264-VERUM[TGx]")));

    [Fact]
    public void ATitleOverTheLengthCapIsNotReported()
    {
        var title = "Formula1.2024.Round01.Bahrain.Race." + new string('x', ReleaseChoiceCollector.MaxTitleLength);
        Assert.Null(Build(Grab(title)));
    }

    [Fact]
    public void ATitleAtTheLengthCapIsSentUncut()
    {
        var title = "Formula1.2024.Round01.Bahrain.Race.";
        title += new string('x', ReleaseChoiceCollector.MaxTitleLength - title.Length);
        Assert.Equal(title, Build(Grab(title))!.TitleDisplay);
    }

    [Fact]
    public void TheTitleIsTrimmedBeforeTheKeyAndTheDisplay()
    {
        var choice = Build(Grab("  " + FixtureTitle + " \t"));

        Assert.Equal(FixtureTitle, choice!.TitleDisplay);
        Assert.Equal(FixtureKey, choice.TitleNorm);
    }

    [Fact]
    public void ATitleAtTheLengthCapAfterTrimmingIsSent()
    {
        var title = "Formula1.2024.Round01.Bahrain.Race.";
        title += new string('x', ReleaseChoiceCollector.MaxTitleLength - title.Length);
        Assert.Equal(title, Build(Grab(" " + title + " "))!.TitleDisplay);
    }

    [Fact]
    public void ATitleWithAShortKeyIsNotReported() => Assert.Null(Build(Grab("F1.1080p.WEB-DL.H264-GROUP")));

    [Theory]
    [InlineData("2024", "2024")]
    [InlineData("2024-2025", "2024-2025")]
    [InlineData("Season 3", null)]
    [InlineData("12345678901234567", null)]
    [InlineData(null, null)]
    public void TheSeasonIsSentOnlyInTheHubShape(string? season, string? expected) =>
        Assert.Equal(expected, Build(evt: Evt(season: season))!.Season);

    [Theory]
    [InlineData(14, false, "nomatch")]
    [InlineData(15, false, "low")]
    [InlineData(49, false, "low")]
    [InlineData(50, false, null)]
    [InlineData(null, false, null)]
    [InlineData(80, true, "rejected")]
    [InlineData(0, true, "rejected")]
    public void TheVerdictFollowsTheScoreBands(int? score, bool rejected, string? expected) =>
        Assert.Equal(expected, ReleaseChoiceCollector.VerdictOf(score, rejected));

    [Theory]
    [InlineData(IndexerType.Torznab, "torznab")]
    [InlineData(IndexerType.Newznab, "newznab")]
    [InlineData(IndexerType.Rss, "rss")]
    [InlineData(IndexerType.Torrent, "other")]
    [InlineData(IndexerType.BroadcasTheNet, "other")]
    [InlineData(null, "other")]
    public void TheIndexerFamilyNeverNamesATracker(IndexerType? type, string expected) =>
        Assert.Equal(expected, ReleaseChoiceCollector.IndexerFamilyOf(type));

    [Theory]
    [InlineData("Torrent", "torrent")]
    [InlineData("TORRENT", "torrent")]
    [InlineData("Usenet", "usenet")]
    [InlineData("usenet", "usenet")]
    [InlineData("ftp", null)]
    [InlineData(null, null)]
    public void TheProtocolIsTorrentOrUsenet(string? protocol, string? expected) =>
        Assert.Equal(expected, ReleaseChoiceCollector.ProtocolOf(protocol));

    [Theory]
    [InlineData("https://example.org/Formula1.2024")]
    [InlineData("Formula1.2024 from www.example.org")]
    [InlineData("WWW.EXAMPLE.ORG - Formula1.2024.Round01")]
    [InlineData("[Example.org] Formula1.2024.Round01")]
    [InlineData("(site.net) Formula1.2024.Round01")]
    [InlineData("{tracker.cc} Formula1.2024.Round01")]
    [InlineData("example.com - Formula1.2024.Round01")]
    [InlineData("Example.ORG Formula1 2024 Round01")]
    [InlineData("rarbg.to-Formula1.2024.Round01")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB.h264-GRP[eztv.re]")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB.h264-GRP-rarbg.to")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB-DL-GRP [site.com]")]
    [InlineData("Formula1 2024 Round01 Bahrain Race 1080p - example.net")]
    [InlineData("Formula1 2024 Round01 (tracker.org) Race 1080p")]
    [InlineData("Formula1_2024_Round01_Race_example.com")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.site.com")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB.h264-GRP[1337x.st]")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB.h264-GRP[torrentsite.co]")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB.h264-GRP[ettv.tv]")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB.h264-GRP-site.lol")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB.h264-GRP[tracker.example.org]")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB-DL-GRP [site.co.uk]")]
    [InlineData("Torrentsite.com.Formula1.2024.Round01.Bahrain.Race.1080p")]
    [InlineData("rarbg.to.Formula1.2024.Round01.Bahrain.Race.1080p")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB.h264-GRP-site.cx")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB.h264-GRP-site.si")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.site.cx")]
    public void AWebAddressIsFound(string title) =>
        Assert.True(ReleaseChoiceCollector.HasWebAddress(title));

    [Theory]
    [InlineData("Formula1.2024.Round01.F1.TV.1080p")]
    [InlineData(FixtureTitle)]
    [InlineData("[TGx] UFC.300.Pereira.vs.Hill.PPV.1080p")]
    [InlineData("UFC.300.Pereira.vs.Hill.PPV.1080p.WEB.h264-VERUM[TGx]")]
    [InlineData("NBA.2024.11.05.Lakers.vs.Celtics.720p.WEB.h264-GRP")]
    [InlineData("MotoGP 2024 Round 01 Qatar Race 1080p WEB-DL AAC2.0 H 264-GROUP")]
    [InlineData("WWE.Road.to.Glory.2024.1080p.WEB.h264-GRP")]
    [InlineData("F1.TV.2024.Round01.Bahrain.Race.1080p")]
    [InlineData("UFC.300.Prelims.Me.vs.You.2024.1080p")]
    [InlineData("UFC 300 Countdown.to Glory 2024 1080p")]
    [InlineData("WWE 2024 Road.to WrestleMania 1080p")]
    [InlineData("UFC.300.Pre-Show.Re-Air.2024.1080p")]
    [InlineData("Formula1.2024.Monaco.Paddock-Pass.Club 1080p")]
    // These dotted words sit inside a title, where no web address rule reads them.
    [InlineData("UFC.300.Prelims.vs.co.Main.Event.2024.1080p")]
    [InlineData("Formula1.2024.Monaco.Sky.Sports.F1.co.uk.Feed.1080p.WEB-DL")]
    [InlineData("UFC.Fight.Night.on.ESPN.Plus")]
    [InlineData("UFC.Fight.Night.2024.on.ESPN.Plus.1080p")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB-DL.AAC2.0")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB-DL.DDP5.1")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB-DL.H.264")]
    [InlineData("Formula1.2024.Round01.Bahrain.Race.1080p.WEB-DL [AAC2.0] [H.264]")]
    [InlineData("Formula1 2024 Round01 Bahrain Race 1080p (2024.03.02) WEB-DL")]
    [InlineData("NHL.St.Louis.Blues.vs.Chicago.Blackhawks.2024.10.22.720p")]
    [InlineData("AFL.St.Kilda.vs.Carlton.2024.Round.05.720p")]
    [InlineData("IndyCar.St.Petersburg.2024.Race.1080p")]
    [InlineData("MotoGP.me.2024.Round01.Race.1080p")]
    public void AReleaseNameIsNotAWebAddress(string title) =>
        Assert.False(ReleaseChoiceCollector.HasWebAddress(title));
}
