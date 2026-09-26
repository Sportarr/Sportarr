using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PriorityCricketRugbySearchTests
{
    private readonly EventQueryService _queries = new(NullLogger<EventQueryService>.Instance);
    private readonly ReleaseMatchingService _matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private readonly ReleaseMatchScorer _scorer = new();

    [Theory]
    [MemberData(nameof(QueryCases))]
    public void BuildEventQueries_UsesOneObservedReleaseIdentity(Event evt, string expected)
    {
        _queries.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return new object[]
        {
            TeamEvent("Indian Premier League", "Cricket", "Delhi Capitals", "Mumbai Indians", "2026", "8", new DateTime(2026, 4, 4)),
            "IPL 2026 M08"
        };
        yield return new object[]
        {
            TeamEvent("Indian Premier League", "Cricket", "Gujarat Titans", "Royal Challengers Bangalore", "2026", "200", new DateTime(2026, 5, 31)),
            "IPL 2026 Final Gujarat Titans Royal Challengers Bangalore"
        };
        yield return new object[]
        {
            TeamEvent("Australian Big Bash League", "Cricket", "Perth Scorchers", "Sydney Sixers", "2025-2026", "1", new DateTime(2025, 12, 14)),
            "BBL 2025 26 Perth Scorchers Sydney Sixers"
        };
        yield return new object[]
        {
            TeamEvent("Australian Big Bash League", "Cricket", "Perth Scorchers", "Sydney Sixers", "2025-2026", "150", new DateTime(2026, 1, 20)),
            "BBL 2025 26 Perth Scorchers Sydney Sixers"
        };
        yield return new object[]
        {
            TeamEvent("Australian Big Bash League", "Cricket", "Perth Scorchers", "Sydney Sixers", "2025-2026", "200", new DateTime(2026, 1, 25)),
            "BBL 2025 26 Final Perth Scorchers Sydney Sixers"
        };
        yield return new object[]
        {
            WorldCupOpening(),
            "Cricket World Cup 2023"
        };
        yield return new object[]
        {
            WorldCupFinal(),
            "Cricket World Cup 2023"
        };
        yield return new object[]
        {
            TeamEvent("Six Nations Championship", "Rugby", "Italy Rugby", "Scotland Rugby", "2026", "1", new DateTime(2026, 2, 7)),
            "Six Nations Rugby 2026 Italy Scotland"
        };
        yield return new object[]
        {
            TeamEvent("Rugby Championship", "Rugby", "South Africa Rugby", "Australia Rugby", "2025", "1", new DateTime(2025, 8, 16)),
            "Rugby Championship 2025 South Africa Australia"
        };
        yield return new object[]
        {
            TeamEvent("Australian National Rugby League", "Rugby", "Melbourne Storm", "Parramatta Eels", "2026", "1", new DateTime(2026, 3, 5)),
            "NRL 2026 Storm Eels"
        };
        yield return new object[]
        {
            TeamEvent("Australian National Rugby League", "Rugby", "Cronulla Sharks", "North Queensland Cowboys", "2026", "125", new DateTime(2026, 9, 12)),
            "NRL 2026 Sharks Cowboys"
        };
    }

    [Fact]
    public void BuildEventQueries_UsesDenormalizedTeamsWhenNavigationsAreNotLinked()
    {
        var evt = TeamEvent("Indian Premier League", "Cricket", "Delhi Capitals", "Mumbai Indians", "2026", "8", new DateTime(2026, 4, 4));
        evt.HomeTeamId = null;
        evt.AwayTeamId = null;

        _queries.BuildEventQueries(evt).Should().Equal("IPL 2026 M08");
    }

    [Theory]
    [MemberData(nameof(WrongReleaseCases))]
    public void WrongSiblingRelease_IsRejectedByMatcherAndScorer(Event evt, string title)
    {
        var result = _matcher.ValidateRelease(Release(title), evt);
        var score = _scorer.CalculateMatchScore(title, evt);

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        var (parsed, importTitle) = ParseForImport(title);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core.Should().BeLessOrEqualTo(0);
        LibraryScore(title, importTitle, evt, parsed).Should().BeLessThan(40);
    }

    public static IEnumerable<object[]> WrongReleaseCases()
    {
        yield return new object[]
        {
            TeamEvent("Indian Premier League", "Cricket", "Delhi Capitals", "Mumbai Indians", "2026", "8", new DateTime(2026, 4, 4)),
            "WPL 2026 M03 Mumbai Indians Women vs Delhi Capitals Women 1080p"
        };
        yield return new object[]
        {
            TeamEvent("Indian Premier League", "Cricket", "Gujarat Titans", "Rajasthan Royals", "2026", "9", new DateTime(2026, 4, 4)),
            "IPL 2026 M73 Qualifier 2 Rajasthan Royals vs Gujarat Titans 1080p"
        };
        yield return new object[]
        {
            TeamEvent("Australian Big Bash League", "Cricket", "Perth Scorchers", "Sydney Sixers", "2025-2026", "1", new DateTime(2025, 12, 14)),
            "Final Perth Scorchers vs Sydney Sixers BBL 2025 26 Full Match Replay 1080p"
        };
        yield return new object[]
        {
            TeamEvent("Australian Big Bash League", "Cricket", "Perth Scorchers", "Sydney Sixers", "2025-2026", "150", new DateTime(2026, 1, 20)),
            "WBBL 42nd Match Challenger Sydney Sixers vs Perth Scorchers 1080p"
        };
        yield return new object[]
        {
            TeamEvent("Australian Big Bash League", "Cricket", "Perth Scorchers", "Sydney Sixers", "2025-2026", "150", new DateTime(2026, 1, 20)),
            "M01 Perth Scorchers vs Sydney Sixers BBL 2025 26 Full Match Replay 1080p"
        };
        yield return new object[]
        {
            TeamEvent("Australian Big Bash League", "Cricket", "Perth Scorchers", "Sydney Sixers", "2025-2026", "200", new DateTime(2026, 1, 25)),
            "BBL Final Perth Scorchers Vs Sydney Sixers 1080p"
        };
        yield return new object[]
        {
            TeamEvent("Australian Big Bash League", "Cricket", "Perth Scorchers", "Sydney Sixers", "2025-2026", "200", new DateTime(2026, 1, 25)),
            "M01 Perth Scorchers vs Sydney Sixers BBL 2025 26 Full Match Replay 1080p"
        };
        yield return new object[]
        {
            TeamEvent("Rugby Championship", "Rugby", "South Africa Rugby", "Australia Rugby", "2025", "1", new DateTime(2025, 8, 16)),
            "Rugby Championship 2025 South Africa vs Australia 23 08 1080p"
        };
        yield return new object[]
        {
            TeamEvent("Australian National Rugby League", "Rugby", "Cronulla Sharks", "North Queensland Cowboys", "2026", "125", new DateTime(2026, 9, 12)),
            "NRL 2026 Round 8 Cowboys v Sharks 1080p"
        };
    }

    [Theory]
    [MemberData(nameof(ValidReleaseCases))]
    public void ObservedRelease_IsViableAcrossSearchAndImportRoutes(Event evt, string title)
    {
        var result = _matcher.ValidateRelease(Release(title), evt);
        var score = _scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        parsed.EventTitle.Should().NotBeNull("the production sports parser must preserve the event identity");
        result.IsMatch.Should().BeTrue();
        result.IsHardRejection.Should().BeFalse();
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(50);
        libraryScore.Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void CricketHighlightsRemainViableWhenTheLeagueAllowsThem()
    {
        var evt = TeamEvent(
            "Indian Premier League",
            "Cricket",
            "Delhi Capitals",
            "Mumbai Indians",
            "2026",
            "8",
            new DateTime(2026, 4, 4));
        evt.League!.AllowHighlights = true;
        const string title =
            "IPL 2026 M08 Delhi Capitals vs Mumbai Indians Extended Highlights 1080p";

        var result = _matcher.ValidateRelease(Release(title), evt);

        result.IsHardRejection.Should().BeFalse();
        result.IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }

    public static IEnumerable<object[]> ValidReleaseCases()
    {
        yield return new object[]
        {
            TeamEvent("Indian Premier League", "Cricket", "Delhi Capitals", "Mumbai Indians", "2026", "8", new DateTime(2026, 4, 4)),
            "IPL 2026 M08 Delhi Capitals vs Mumbai Indians Full Match Replay 1080p"
        };
        yield return new object[]
        {
            TeamEvent("Australian Big Bash League", "Cricket", "Perth Scorchers", "Sydney Sixers", "2025-2026", "1", new DateTime(2025, 12, 14)),
            "M01 Perth Scorchers vs Sydney Sixers BBL 2025 26 Full Match Replay 1080p"
        };
        yield return new object[]
        {
            TeamEvent("Australian Big Bash League", "Cricket", "Perth Scorchers", "Sydney Sixers", "2025-2026", "150", new DateTime(2026, 1, 20)),
            "BBL2025 Qualifier Perth Scorchers V Sydney Sixers 1080p AAC FULL REPLAY x264"
        };
        yield return new object[]
        {
            TeamEvent("Australian Big Bash League", "Cricket", "Perth Scorchers", "Sydney Sixers", "2025-2026", "200", new DateTime(2026, 1, 25)),
            "Final Perth Scorchers vs Sydney Sixers BBL 2025 26 Full Match Replay 1080p"
        };
        yield return new object[]
        {
            TeamEvent("Rugby Championship", "Rugby", "South Africa Rugby", "Australia Rugby", "2025", "1", new DateTime(2025, 8, 16)),
            "Rugby Championship South Africa vs Australia 16 08 2025 1080p"
        };
        yield return new object[]
        {
            TeamEvent("Australian National Rugby League", "Rugby", "Melbourne Storm", "Parramatta Eels", "2026", "1", new DateTime(2026, 3, 5)),
            "NRL 2026 Melbourne Storm vs Parramatta Eels 05 03 720p"
        };
        yield return new object[]
        {
            TeamEvent("Australian National Rugby League", "Rugby", "Melbourne Storm", "Parramatta Eels", "2026", "1", new DateTime(2026, 3, 5)),
            "NRL.2026.03.05.Melbourne.Storm.vs.Parramatta.Eels.720p"
        };
        yield return new object[]
        {
            TeamEvent("Six Nations Championship", "Rugby", "England Rugby", "Wales Rugby", "2026", "2", new DateTime(2026, 2, 14)),
            "Six Nations Rugby 2026 England vs Wales 1080p WEB H264 DD5.1"
        };
        yield return new object[]
        {
            TeamEvent("Rugby Championship", "Rugby", "South Africa Rugby", "Australia Rugby", "2026", "2", new DateTime(2026, 8, 22)),
            "Rugby Championship 2026 South Africa vs Australia 1080p WEB H264 DD5.1"
        };
        yield return new object[]
        {
            TeamEvent("United Rugby Championship", "Rugby", "Leinster", "Lions", "2026", "17", new DateTime(2026, 5, 9)),
            "URC 2026 Leinster vs Lions 09 05 1080p WEB H264 DD5.1"
        };
        yield return new object[]
        {
            TeamEvent("Australian National Rugby League", "Rugby", "Melbourne Storm", "Parramatta Eels", "2026", "1", new DateTime(2026, 3, 5)),
            "NRL 2026 Melbourne Storm vs Parramatta Eels 1080p WEB H264 DD5.1"
        };
    }

    [Theory]
    [InlineData("Six Nations Championship", "Rugby", "England Rugby", "Wales Rugby", "Six Nations Rugby 2026 England vs Wales 1080p WEB H264 DD5.1")]
    [InlineData("Six Nations Championship", "Rugby", "England Rugby", "Wales Rugby", "Six Nations Rugby 2026 England vs Wales 1080p WEB H264 DD.5.1")]
    [InlineData("Six Nations Championship", "Rugby", "England Rugby", "Wales Rugby", "Six Nations Rugby 2026 England vs Wales 1080p WEB H264 AAC 5 1")]
    [InlineData("Six Nations Championship", "Rugby", "England Rugby", "Wales Rugby", "Six Nations Rugby 2026 England vs Wales 1080p WEB H264 DTS-HD.MA.5.1")]
    [InlineData("Rugby Championship", "Rugby", "South Africa Rugby", "Australia Rugby", "Rugby Championship 2026 South Africa vs Australia 1080p WEB H264 DD5.1")]
    [InlineData("United Rugby Championship", "Rugby", "Leinster", "Lions", "URC 2026 Leinster vs Lions 1080p WEB H264 DD5.1")]
    [InlineData("Australian National Rugby League", "Rugby", "Melbourne Storm", "Parramatta Eels", "NRL 2026 Melbourne Storm vs Parramatta Eels 1080p WEB H264 DD5.1")]
    public void TechnicalAudioSuffixIsNotReadAsAnEventDate(
        string league, string sport, string home, string away, string title)
    {
        var evt = TeamEvent(league, sport, home, away, "2026", "1", new DateTime(2026, 2, 14));

        CricketRugbyReleaseNamePolicy.HasIdentityConflict(title, evt).Should().BeFalse();
    }

    [Theory]
    [InlineData("Womens Big Bash League", "Cricket", "Sydney Sixers Women", "Perth Scorchers Women", "WBBL 2026 Sydney Sixers Women vs Perth Scorchers Women 1080p WEB H264")]
    [InlineData("Womens Six Nations", "Rugby", "England Women Rugby", "Wales Women Rugby", "Womens Six Nations 2026 England Women vs Wales Women 1080p WEB H264")]
    public void WomensLeagueReleaseDoesNotConflictWithItsOwnLeague(
        string league, string sport, string home, string away, string title)
    {
        var evt = TeamEvent(league, sport, home, away, "2026", "1", new DateTime(2026, 4, 11));

        CricketRugbyReleaseNamePolicy.HasIdentityConflict(title, evt).Should().BeFalse();
    }

    [Theory]
    [InlineData("Cricket India vs Australia 5th T20i Full Match Dec 03, 2023 720pEng30fps", 2023, 12, 3)]
    [InlineData("Cricket India vs Australia 4th T20i Full Match Dec 01, 2023 720pEng30fps", 2023, 12, 1)]
    [InlineData("Cricket India vs Australia 3rd T20i Full Match Nov 28, 2023 720pEng30fps", 2023, 11, 28)]
    [InlineData("Cricket India vs Australia 2nd T20i Full Match Nov 26, 2023 720pEng30fps", 2023, 11, 26)]
    [InlineData("Cricket India vs Australia 2nd ODi Full Match Sep 24, 2023 720pEng30fps", 2023, 9, 24)]
    [InlineData("Cricket India vs Australia 3rd ODi Full Match Sep 27, 2023 720pEng30fps", 2023, 9, 27)]
    [InlineData("Cricket India vs Australia 1st ODi Full Match Sep 22, 2023 720pEng30fps", 2023, 9, 22)]
    public void WorldCupFinalRejectsDatedMatchesBetweenTheSameTeams(
        string title, int year, int month, int day)
    {
        var evt = WorldCupFinal();
        var parsed = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);

        parsed.EventDate.Should().Be(new DateTime(year, month, day));
        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(title, evt.Title, null, evt, parsed).Core.Should().BeLessOrEqualTo(0);
    }

    [Theory]
    [InlineData("Cricket World Test Championship 2023 Australia vs India Full Match 720p WEB x264 Willow")]
    [InlineData("Cricket.World.Cup.2023-M05-India.v.Australia.1080p50.WEB-DL.H264-nVa")]
    public void WorldCupFinalRejectsAnotherCompetitionOrStage(string title)
    {
        var evt = WorldCupFinal();
        var parsed = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(title, evt.Title, null, evt, parsed).Core.Should().BeLessOrEqualTo(0);
    }

    [Theory]
    [InlineData("Cricket.World.Cup.2023-M48-The.Final-India.v.Australia-1080p50.WEB-DL.H264.-nVa")]
    [InlineData("Cricket India vs Australia Full Match Nov 19, 2023 1080p")]
    public void WorldCupFinalKeepsItsOwnRelease(string title)
    {
        var evt = WorldCupFinal();
        var (parsed, importTitle) = ParseForImport(title);

        _matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core.Should().BeGreaterThanOrEqualTo(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void WorldCupFinalDoesNotImportNextDaysMeeting()
    {
        const string title = "Cricket India vs Australia Full Match Nov 20, 2023 1080p";
        var evt = WorldCupFinal();
        var (parsed, importTitle) = ParseForImport(title);

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core.Should().BeLessThan(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeLessThan(40);
    }

    [Theory]
    [InlineData("Cricket World Cup 2023 M01 England vs New Zealand 1st Innings 720p x264 EN SKY")]
    [InlineData("Cricket World Cup 2023 M01 England vs New Zealand 2nd Innings 720p x264 EN SKY")]
    public void WorldCupOpeningDoesNotTreatAnInningsAsTheFullMatch(string title)
    {
        var evt = WorldCupOpening();
        var (parsed, importTitle) = ParseForImport(title);

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core.Should().BeLessThan(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeLessThan(40);
    }

    [Fact]
    public void WorldCupSemiFinalDoesNotConflictWithFinalStage()
    {
        const string title = "Cricket.World.Cup.2023-M46-Semi.Final.1-New.Zealand.v.India-Highlights.1080p.WEB-DL.H264-nVa";
        var evt = TeamEvent(
            "Cricket World Cup", "Cricket", "India Cricket", "New Zealand Cricket",
            "2023", "150", new DateTime(2023, 11, 15));

        CricketRugbyReleaseNamePolicy.HasIdentityConflict(title, evt).Should().BeFalse();
    }

    [Fact]
    public void WorldCupOpeningKeepsTheFullMatch()
    {
        const string title = "Cricket.World.Cup.2023-M01-England.v.New.Zealand.1080p50.WEB-DL.H264-nVa";
        var evt = WorldCupOpening();
        var (parsed, importTitle) = ParseForImport(title);

        _matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core.Should().BeGreaterThanOrEqualTo(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    private static Event WorldCupOpening() => TeamEvent(
        "Cricket World Cup", "Cricket", "England Cricket", "New Zealand Cricket",
        "2023", "1", new DateTime(2023, 10, 5));

    private static Event WorldCupFinal() => TeamEvent(
        "Cricket World Cup", "Cricket", "India Cricket", "Australia Cricket",
        "2023", "200", new DateTime(2023, 11, 19));

    private static Event TeamEvent(
        string leagueName,
        string sport,
        string home,
        string away,
        string season,
        string round,
        DateTime date) => new()
    {
        Title = $"{home} vs {away}",
        Sport = sport,
        Season = season,
        Round = round,
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = leagueName, Sport = sport }
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture"
    };

    private static (SportsParseResult Parsed, string EventTitle) ParseForImport(string title)
    {
        var sports = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);
        var media = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(title);
        var eventTitle = sports.Confidence >= 60 && !string.IsNullOrWhiteSpace(sports.EventTitle)
            ? sports.EventTitle
            : media.EventTitle;
        return (sports, eventTitle ?? string.Empty);
    }

    private static int LibraryScore(
        string sourceTitle,
        string eventTitle,
        Event evt,
        SportsParseResult parsed) => LibraryImportService.CalculateMatchConfidence(
            eventTitle,
            evt.Title,
            parsed.Organization,
            evt,
            parsed.EventDate,
            parsed.EventYear,
            parsed.RoundNumber,
            parsed.SeasonYearEnd,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport,
            sourceTitle: sourceTitle);
}
