using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily71SearchTests
{
    private const string ObservedDreamRelease =
        "DREAM IGF Fight For Japan Genki Desu Ka New Year 2011 HDTV";

    private static readonly EventQueryService QueryService =
        new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    [Fact]
    public void DreamYearEndUsesOneObservedReleaseQuery()
    {
        QueryService.BuildEventQueries(YearEndEvent()).Should().Equal(
            "DREAM Genki Desu Ka 2011");
    }

    [Fact]
    public void DreamSiblingKeepsItsOwnQuery()
    {
        QueryService.BuildEventQueries(MayEvent()).Should().Equal(
            "Dream: Fight for Japan!");
    }

    [Fact]
    public void ObservedDreamReleaseMatchesOnlyTheYearEndEvent()
    {
        var yearEnd = YearEndEvent();
        var result = Matcher.ValidateRelease(Release(ObservedDreamRelease), yearEnd);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        new ReleaseMatchScorer().CalculateMatchScore(ObservedDreamRelease, yearEnd)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        Matcher.ValidateRelease(Release(ObservedDreamRelease), MayEvent())
            .IsMatch.Should().BeFalse();
    }

    [Fact]
    public void DreamYearEndRejectsAnotherYear()
    {
        Matcher.ValidateRelease(
                Release("DREAM IGF Fight For Japan Genki Desu Ka New Year 2012 HDTV"),
                YearEndEvent())
            .IsMatch.Should().BeFalse();
    }

    private static Event YearEndEvent() => new()
    {
        Id = 2,
        ExternalId = "ev-888355",
        Title = "Fight for Japan Genki Desu Ka! Omisoka",
        Sport = "Combat",
        Season = "2011",
        EventDate = new DateTime(2011, 12, 31, 0, 0, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2011, 12, 31),
        LeagueId = 1,
        League = new League { Id = 1, ExternalId = "lg-000231", Name = "DREAM", Sport = "Combat" }
    };

    private static Event MayEvent() => new()
    {
        Id = 1,
        ExternalId = "ev-888352",
        Title = "Dream: Fight for Japan!",
        Sport = "Combat",
        Season = "2011",
        EventDate = new DateTime(2011, 5, 29, 0, 0, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2011, 5, 29),
        LeagueId = 1,
        League = new League { Id = 1, ExternalId = "lg-000231", Name = "DREAM", Sport = "Combat" }
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/dream.nzb",
        Indexer = "Frozen provider",
        PublishDate = new DateTime(2012, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}
