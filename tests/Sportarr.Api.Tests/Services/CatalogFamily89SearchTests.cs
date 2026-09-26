using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily89SearchTests
{
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Fact]
    public void SummerLeagueRejectsObservedRegularSeasonReleaseWithUnparseableDate()
    {
        const string title = "NBA RS 2026 Memphis Grizzlies vs Golden State Warriors 09 021080p60 FSN MEM";
        var evt = TeamEvent("NBA Summer League", new DateTime(2026, 7, 19));

        var validation = Matcher.ValidateRelease(Release(title), evt);

        LeagueReleaseNamePolicy.HasIdentityConflict(title, evt).Should().BeTrue();
        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Fact]
    public void SummerLeagueKeepsObservedChampionshipRelease()
    {
        const string title = "NBA Summer League Championship Game 2026 Golden State Warriors vs Memphis Grizzlies 19 07 720pEN60fps ESPN";
        var evt = TeamEvent("NBA Summer League", new DateTime(2026, 7, 19));

        LeagueReleaseNamePolicy.HasIdentityConflict(title, evt).Should().BeFalse();
        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        Scorer.CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void RegularSeasonEventDoesNotUseSummerLeagueCompetitionGuard()
    {
        const string title = "NBA RS 2026 Memphis Grizzlies vs Golden State Warriors 09 02 720pEN60fps NBCSBA";
        var evt = TeamEvent("NBA", new DateTime(2026, 2, 9));

        LeagueReleaseNamePolicy.HasIdentityConflict(title, evt).Should().BeFalse();
        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
    }

    private static Event TeamEvent(string league, DateTime date) => new()
    {
        ExternalId = "ev-2392608",
        Title = "Memphis Grizzlies vs Golden State Warriors",
        Sport = "Basketball",
        Season = "2026",
        EventDate = DateTime.SpecifyKind(date.AddDays(1).AddHours(1), DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = "Memphis Grizzlies",
        AwayTeamName = "Golden State Warriors",
        League = new League { Name = league, Sport = "Basketball" }
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/release",
        Indexer = "Fixture",
        Protocol = "Torrent"
    };
}
