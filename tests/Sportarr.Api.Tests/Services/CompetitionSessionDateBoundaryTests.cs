using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.Services;

public sealed class CompetitionSessionDateBoundaryTests(ITestOutputHelper output)
{
    private readonly ReleaseMatchingService _matching = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    [Theory]
    [InlineData(-3)]
    [InlineData(-2)]
    [InlineData(2)]
    [InlineData(3)]
    public void DatedAthleticsFinalCannotFillDifferentCompetitionDay(int offset)
    {
        var evt = AthleticsFinal();
        var full = Match(Title(evt.EventDate), evt);
        Assert.True(new ReleaseMatchScorer().CalculateMatchScore(Title(evt.EventDate), evt) >= ReleaseMatchScorer.MinimumMatchScore);
        Assert.False(full.IsHardRejection);
        var wrong = Match(Title(evt.EventDate.AddDays(offset)), evt);
        Assert.True(wrong.IsHardRejection);
        Assert.False(wrong.IsMatch);
        Assert.Contains(wrong.Rejections, reason => reason.Contains("Date mismatch"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void UnknownLocalDateKeepsExistingOneDayRollover(int offset)
    {
        var evt = AthleticsFinal();
        var result = Match(Title(evt.EventDate.AddDays(offset)), evt);
        Assert.True(new ReleaseMatchScorer().CalculateMatchScore(Title(evt.EventDate.AddDays(offset)), evt) >= ReleaseMatchScorer.MinimumMatchScore);
        Assert.False(result.IsHardRejection);
        Assert.Contains(result.MatchReasons, reason => reason.Contains("Date matches") || reason.Contains("Date within 1 day"));
    }

    [Fact]
    public void RecurringWrestlingBroadcastRequiresTheExactDate()
    {
        var evt = new Event
        {
            Id = 2, Title = "AEW Dynamite", Sport = "Wrestling",
            EventDate = new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Id = 2, Name = "AEW", Sport = "Wrestling" }
        };
        var result = Match("AEW Dynamite 2026 06 10 1080p WEB h264", evt);
        Assert.True(result.IsHardRejection);
        Assert.Contains(result.Rejections, reason => reason.Contains("Date mismatch"));
    }

    private ReleaseMatchResult Match(string title, Event evt)
    {
        var parsed = _matching.ParseRelease(title);
        Assert.NotNull(parsed.EventDate);
        var result = _matching.ValidateRelease(new ReleaseSearchResult
        {
            Title = title, Guid = title, DownloadUrl = "http://fixture.invalid/descriptor", Indexer = "Fixture"
        }, evt);
        output.WriteLine(JsonSerializer.Serialize(new { ReleaseTitle = title, evt.Title, evt.Sport, evt.EventDate,
            evt.BroadcastDate, ParsedDate = parsed.EventDate, ManualMatchScore = new ReleaseMatchScorer().CalculateMatchScore(title, evt), result.IsMatch, result.IsHardRejection,
            result.Confidence, result.MatchReasons, result.Rejections }));
        return result;
    }

    private static string Title(DateTime date) =>
        $"Diamond.League.{date:yyyy.MM.dd}.Womens.100.metres.Final.at.Fir.Meeting.720p.WEB-DL.H264.MULTi-FIELD";

    private static Event AthleticsFinal() => new()
    {
        Id = 1, ExternalId = "fixture:71fd4260b242d98918fbc534c47bbd53",
        Title = "Womens 100 metres Final at Fir Meeting", Sport = "Athletics", Season = "2022",
        EventDate = new DateTime(2022, 7, 15, 18, 0, 0, DateTimeKind.Utc),
        BroadcastDate = null, HomeTeamName = null, AwayTeamName = null,
        League = new League { Id = 1, Name = "Diamond League", Sport = "Athletics", AllowHighlights = false }
    };
}
