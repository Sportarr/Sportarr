using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Reported on Discord 2026-08-29. The manual import dialog offered three NFL
/// games as candidates for an MLB release, all at the same confidence.
/// </summary>
public class ManualImportSuggestionScoringTests
{
    private readonly ITestOutputHelper _out;
    public ManualImportSuggestionScoringTests(ITestOutputHelper output) => _out = output;

    private const string MlbRelease =
        "MLB - S2026E135 - San Francisco Giants vs Houston Astros - HDTV-1080p";

    private static readonly MediaFileParser Media = new(NullLogger<MediaFileParser>.Instance);
    private static readonly SportsFileNameParser Sports = new(NullLogger<SportsFileNameParser>.Instance);
    private static readonly EventPartDetector Parts = new(NullLogger<EventPartDetector>.Instance);

    private static ImportMatchingService CreateService()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ImportMatchingService(new SportarrDbContext(options), Media, Sports, Parts,
            NullLogger<ImportMatchingService>.Instance);
    }

    private static Event NflGame(string title) => new()
    {
        Id = 668181,
        Title = title,
        Sport = "Football",   // what the live database actually stores
        EventDate = new DateTime(2027, 1, 10, 18, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 22, Name = "NFL", Sport = "Football" }
    };

    [Theory]
    [InlineData("Arizona Cardinals vs San Francisco 49ers")]
    [InlineData("San Francisco 49ers vs Philadelphia Eagles")]
    [InlineData("Kansas City Chiefs vs San Francisco 49ers")]
    public void NflGameIsNotOfferedForAnMlbRelease(string nflTitle)
    {
        var svc = CreateService();
        var sports = Sports.Parse(MlbRelease);
        var media = Media.Parse(MlbRelease);

        // Mirrors GetAllPossibleMatchesAsync exactly.
        var searchTitle = sports.Confidence >= 60 && !string.IsNullOrEmpty(sports.EventTitle)
            ? sports.EventTitle
            : media.EventTitle;
        var detectedPart = Parts.DetectPart(MlbRelease, sports.Sport ?? "Fighting")?.SegmentName;

        var score = svc.CalculateMatchConfidence(searchTitle, nflTitle, detectedPart, NflGame(nflTitle), sports);

        _out.WriteLine($"sportsSport='{sports.Sport}' sportsOrg='{sports.Organization}' sportsConf={sports.Confidence}");
        _out.WriteLine($"sportsEventTitle='{sports.EventTitle}' mediaEventTitle='{media.EventTitle}'");
        _out.WriteLine($"searchTitle='{searchTitle}' detectedPart='{detectedPart}' => score={score}");

        Assert.True(score <= 0, $"NFL '{nflTitle}' scored {score} for an MLB release");
    }

    [Theory]
    [InlineData("Football")]
    [InlineData("")]
    [InlineData(null)]
    public void ScoreDependsOnTheEventSportField(string? eventSport)
    {
        var svc = CreateService();
        var sports = Sports.Parse(MlbRelease);
        var media = Media.Parse(MlbRelease);
        var searchTitle = sports.Confidence >= 60 && !string.IsNullOrEmpty(sports.EventTitle)
            ? sports.EventTitle
            : media.EventTitle;

        var evt = NflGame("Arizona Cardinals vs San Francisco 49ers");
        evt.Sport = eventSport!;

        var score = svc.CalculateMatchConfidence(searchTitle, evt.Title, null, evt, sports);
        _out.WriteLine($"evt.Sport='{eventSport ?? "<null>"}' => score={score}");
        Assert.True(score <= 0,
            $"An NFL event with Sport='{eventSport ?? "<null>"}' scored {score} for an MLB release");
    }
}
