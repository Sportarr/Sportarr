using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.Services;

public sealed class CompetitionDatePreservationTests(ITestOutputHelper output)
{
    private readonly ReleaseMatchingService _matching = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void IndividualFinalKeepsBroadcastDateBasisAndOneDayRollover(bool verified, int offset)
    {
        var evt = AthleticsFinal();
        evt.BroadcastDate = evt.EventDate.Date.AddDays(-1);
        evt.BroadcastDateVerified = verified;
        var release = Release(Title(evt.BroadcastDate.Value.AddDays(offset), evt.Title));
        var result = Match(release, evt);
        Assert.False(result.IsHardRejection);
        Assert.Contains(result.MatchReasons, reason => reason == (offset == 0
            ? "Date matches exactly" : "Date within 1 day (timezone rollover)"));
    }

    [Theory]
    [InlineData(2022, false)]
    [InlineData(2021, true)]
    public void YearOnlyFinalKeepsExistingYearDisposition(int year, bool rejected)
    {
        var evt = AthleticsFinal();
        var release = Release($"Diamond.League.{year}.Womens.100.metres.Final.at.Fir.Meeting.720p.WEB-DL.H264.MULTi-FIELD");
        var parsed = _matching.ParseRelease(release.Title);
        Assert.Null(parsed.EventDate);
        Assert.Equal(year, parsed.EventYear);
        var result = Match(release, evt);
        Assert.Equal(rejected, result.IsHardRejection);
        if (rejected) Assert.Contains(result.Rejections, reason => reason.StartsWith("Year mismatch:", StringComparison.Ordinal));
        else Assert.Contains(result.MatchReasons, reason => reason == "Year matches (2022)");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ExplicitPackKeepsItsExistingDateDispositionWithoutGrantingMembership(int offset)
    {
        var evt = AthleticsFinal();
        var release = Release(Title(evt.EventDate.AddDays(offset), evt.Title));
        release.IsPack = true;
        var result = Match(release, evt);
        Assert.True(release.IsPack);
        Assert.False(result.IsHardRejection);
        Assert.Contains(result.MatchReasons, reason => reason == $"Date within {offset} days");
    }

    [Fact]
    public void FinalTokenDoesNotRelaxTheRecurringWrestlingDateRule()
    {
        var evt = AthleticsFinal();
        evt.Title = "AEW Dynamite Final";
        evt.Sport = "Wrestling";
        evt.League = new League { Id = 1, Name = "AEW", Sport = "Wrestling" };
        var release = Release("AEW.Dynamite.Final.2022.07.18.720p.WEB-DL.H264-GROUP");
        var result = Match(release, evt);
        Assert.True(result.IsHardRejection);
        Assert.Contains(result.Rejections, reason => reason.StartsWith("Date mismatch:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Finalist")]
    [InlineData("Finalé")]
    [InlineData("Final2")]
    [InlineData("Final\u0301")]
    public void MetadataSubstringIsNotAnExplicitFinalProgram(string token)
    {
        var evt = AthleticsFinal();
        evt.Title = evt.Title.Replace("Final", token, StringComparison.Ordinal);
        var release = Release(Title(evt.EventDate.AddDays(2), evt.Title));
        var result = Match(release, evt);
        Assert.False(result.IsHardRejection);
        Assert.Contains(result.MatchReasons, reason => reason == "Date within 2 days");
    }

    [Fact]
    public void ReleaseGroupFinalCannotChangeMetadataHeatClassification()
    {
        var evt = AthleticsFinal();
        evt.Title = evt.Title.Replace("Final", "Heat", StringComparison.Ordinal);
        var release = Release(Title(evt.EventDate.AddDays(2), evt.Title).Replace("MULTi-FIELD", "FINAL", StringComparison.Ordinal));
        release.ReleaseGroup = "FINAL";
        var result = Match(release, evt);
        Assert.False(result.IsHardRejection);
        Assert.Contains(result.MatchReasons, reason => reason == "Date within 2 days");
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(0, true)]
    public void MatchingCanonicalIdKeepsAuthorityBeforeDateAndYearScoring(int offset, bool wrongYear)
    {
        var evt = AthleticsFinal();
        evt.ExternalId = "ev-2336155";
        var date = wrongYear ? evt.EventDate.AddYears(-1) : evt.EventDate.AddDays(offset);
        var release = Release(Title(date, evt.Title));
        release.SportarrEventId = evt.ExternalId;
        var result = Match(release, evt);
        Assert.True(result.IsMatch);
        Assert.False(result.IsHardRejection);
        Assert.Equal(100, result.Confidence);
        Assert.Equal("Sportarr id token match (ev-2336155)", Assert.Single(result.MatchReasons));
    }

    [Fact]
    public void ContradictoryCanonicalIdStillRejectsAnOtherwiseExactDatedFinal()
    {
        var evt = AthleticsFinal();
        evt.ExternalId = "ev-2336155";
        var release = Release(Title(evt.EventDate, evt.Title));
        release.SportarrEventId = "ev-2336156";
        var result = Match(release, evt);
        Assert.False(result.IsMatch);
        Assert.True(result.IsHardRejection);
        Assert.Equal(0, result.Confidence);
        Assert.Contains(result.Rejections, reason => reason.Contains("different event (ev-2336156", StringComparison.Ordinal));
    }

    [Fact]
    public void MatchingDayAndMonthCannotOverrideAConflictingExplicitYear()
    {
        var evt = new Event
        {
            Id = 2,
            Title = "Boston Celtics vs New York Knicks",
            Sport = "Basketball",
            EventDate = new DateTime(2026, 5, 5, 19, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 5, 5),
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Boston Celtics",
            AwayTeamName = "New York Knicks",
            League = new League { Id = 2, Name = "NBA", Sport = "Basketball" }
        };
        const string title = "NBA.Boston.Celtics.vs.New.York.Knicks.05.05.2025.1080p.WEB";
        var release = Release(title);
        var parsed = _matching.ParseRelease(title);

        var validation = _matching.ValidateRelease(release, evt, preParsed: parsed);
        var import = ImportMatchingTestHarness.Service().ScoreMatch(
            parsed.EventTitle ?? title, evt.Title, null, evt, parsed);

        Assert.True(validation.IsHardRejection);
        Assert.True(import.Core <= 0);
    }

    [Fact]
    public void MatchingDayAndMonthCannotOverrideAConflictingShortYear()
    {
        var evt = new Event
        {
            Id = 5,
            Title = "Boston Celtics vs New York Knicks",
            Sport = "Basketball",
            EventDate = new DateTime(2026, 5, 5, 19, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 5, 5),
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Boston Celtics",
            AwayTeamName = "New York Knicks",
            League = new League { Id = 5, Name = "NBA", Sport = "Basketball" }
        };
        const string title = "NBA.Boston.Celtics.vs.New.York.Knicks.05.05.25.1080p.WEB";
        var release = Release(title);
        var parsed = _matching.ParseRelease(title);

        var validation = _matching.ValidateRelease(release, evt, preParsed: parsed);
        var import = ImportMatchingTestHarness.Service().ScoreMatch(
            parsed.EventTitle ?? title, evt.Title, null, evt, parsed);

        Assert.Equal(2025, parsed.EventDate?.Year);
        Assert.True(validation.IsHardRejection);
        Assert.Equal(0, new ReleaseMatchScorer().CalculateMatchScore(title, evt));
        Assert.True(import.Core <= 0);
    }

    [Fact]
    public void AudioChannelTokenCannotOverrideAConflictingFullDate()
    {
        var evt = new Event
        {
            Id = 6,
            Title = "Boston Celtics vs New York Knicks",
            Sport = "Basketball",
            EventDate = new DateTime(2026, 1, 5, 19, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 1, 5),
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Boston Celtics",
            AwayTeamName = "New York Knicks",
            League = new League { Id = 6, Name = "NBA", Sport = "Basketball" }
        };
        const string title = "NBA.Boston.Celtics.vs.New.York.Knicks.2026.06.08.DDP5.1.1080p.WEB";
        var release = Release(title);
        var parsed = _matching.ParseRelease(title);

        var validation = _matching.ValidateRelease(release, evt, preParsed: parsed);
        var import = ImportMatchingTestHarness.Service().ScoreMatch(
            parsed.EventTitle ?? title, evt.Title, null, evt, parsed);

        Assert.True(validation.IsHardRejection);
        Assert.Equal(0, new ReleaseMatchScorer().CalculateMatchScore(title, evt));
        Assert.True(import.Core <= 0);
    }

    [Theory]
    [InlineData(1996, "96")]
    [InlineData(2025, "25")]
    public void ShortYearScoringUsesTheMatchingCentury(int year, string shortYear)
    {
        var evt = new Event
        {
            Id = 7,
            Title = "Boston Celtics vs New York Knicks",
            Sport = "Basketball",
            EventDate = new DateTime(year, 3, 16, 19, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(year, 3, 16),
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Boston Celtics",
            AwayTeamName = "New York Knicks",
            League = new League { Id = 7, Name = "NBA", Sport = "Basketball" }
        };

        Assert.True(new ReleaseMatchScorer().CalculateMatchScore(
                $"NBA.Boston.Celtics.vs.New.York.Knicks.16.03.{shortYear}.1080p.WEB", evt) >=
            ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void DayAndMonthCannotSwapAnExplicitYearMonthDayDate()
    {
        var evt = new Event
        {
            Id = 3,
            Title = "Boston Celtics vs New York Knicks",
            Sport = "Basketball",
            EventDate = new DateTime(2026, 6, 5, 19, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 6, 5),
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Boston Celtics",
            AwayTeamName = "New York Knicks",
            League = new League { Id = 3, Name = "NBA", Sport = "Basketball" }
        };
        const string title = "NBA.Boston.Celtics.vs.New.York.Knicks.2026.05.06.1080p.WEB";
        var release = Release(title);
        var parsed = _matching.ParseRelease(title);

        var validation = _matching.ValidateRelease(release, evt, preParsed: parsed);
        var import = ImportMatchingTestHarness.Service().ScoreMatch(
            parsed.EventTitle ?? title, evt.Title, null, evt, parsed);

        Assert.True(validation.IsHardRejection);
        Assert.True(import.Core <= 0);
    }

    [Fact]
    public void TeamGameNumberDoesNotDisableTimezoneRollover()
    {
        var evt = new Event
        {
            Id = 4,
            Title = "Boston Celtics vs New York Knicks Game 2",
            Sport = "Basketball",
            EventDate = new DateTime(2026, 6, 11, 0, 30, 0, DateTimeKind.Utc),
            BroadcastDateVerified = false,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Boston Celtics",
            AwayTeamName = "New York Knicks",
            League = new League { Id = 4, Name = "NBA", Sport = "Basketball" }
        };

        Assert.True(new ReleaseMatchScorer().CalculateMatchScore(
            "NBA.Boston.Celtics.vs.New.York.Knicks.Game.2.2026.06.10.1080p.WEB", evt) >=
            ReleaseMatchScorer.AutoGrabMatchScore);
    }

    private ReleaseMatchResult Match(ReleaseSearchResult release, Event evt)
    {
        var parsed = _matching.ParseRelease(release.Title);
        var result = _matching.ValidateRelease(release, evt, enableMultiPartEpisodes: false);
        output.WriteLine(JsonSerializer.Serialize(new { release.Title, release.IsPack, release.ReleaseGroup,
            SourceEventId = release.SportarrEventId, EventTitle = evt.Title, evt.ExternalId, evt.Sport,
            evt.EventDate, evt.BroadcastDate, evt.BroadcastDateVerified, parsed.EventYear,
            ParsedDate = parsed.EventDate, result.IsMatch, result.IsHardRejection, result.Confidence,
            result.MatchReasons, result.Rejections }));
        return result;
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title, Guid = "date-preservation-offer", Indexer = "Fixture",
        DownloadUrl = "http://fixture.invalid/descriptor"
    };

    private static string Title(DateTime date, string eventTitle) =>
        $"Diamond.League.{date:yyyy.MM.dd}.{eventTitle.Replace(' ', '.')}.720p.WEB-DL.H264.MULTi-FIELD";

    private static Event AthleticsFinal() => new()
    {
        Id = 1, ExternalId = "fixture:71fd4260b242d98918fbc534c47bbd53",
        Title = "Womens 100 metres Final at Fir Meeting", Sport = "Athletics", Season = "2022",
        EventDate = new DateTime(2022, 7, 15, 18, 0, 0, DateTimeKind.Utc),
        BroadcastDate = null, HomeTeamName = null, AwayTeamName = null,
        League = new League { Id = 1, Name = "Diamond League", Sport = "Athletics", AllowHighlights = false }
    };
}
