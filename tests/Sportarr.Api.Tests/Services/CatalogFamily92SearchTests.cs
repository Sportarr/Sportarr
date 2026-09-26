using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily92SearchTests
{
    private readonly ReleaseMatchScorer _scorer = new();
    private readonly ReleaseMatchingService _matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    [Fact]
    public void GLeagueFinalGameNumberDoesNotHideTheFollowingFixtureDate()
    {
        var evt = EventFor("NBA G League", "Basketball", "Stockton Kings vs Greensboro Swarm",
            "Stockton Kings", "Greensboro Swarm", new DateTime(2026, 4, 10),
            new DateTime(2026, 4, 11, 2, 0, 0, DateTimeKind.Utc), "2025-2026", null);
        const string exact = "NBA G League Finals 2026 Stockton Kings vs Greensboro Swarm Game 2 10 04 720pEN60fps ESPN";
        const string otherGame = "NBA G League Finals 2026 Stockton Kings vs Greensboro Swarm Game 1 08 04 720pEN60fps ESPNU";

        _matcher.ValidateRelease(Release(exact), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(exact, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        _matcher.ValidateRelease(Release(otherGame), evt).IsMatch.Should().BeFalse();
        _scorer.CalculateMatchScore(otherGame, evt).Should().Be(0);
    }

    [Fact]
    public void ChampionsTrophyGroupMatchDoesNotMatchTheFinal()
    {
        var evt = EventFor("ICC Champions Trophy", "Cricket", "India Cricket vs New Zealand Cricket",
            "India Cricket", "New Zealand Cricket", new DateTime(2025, 3, 9),
            new DateTime(2025, 3, 9, 9, 0, 0, DateTimeKind.Utc), "2025", "200");
        const string group = "ICC Champions Trophy 2025 M12 India vs New Zealand Full Match Replay Sky Cricket 1080p50fps Jinx8004";
        const string final = "ICC Champions Trophy 2025 Final India vs New Zealand Full Match Replay Sky Cricket 1080p50fps Jinx8004";

        _matcher.ValidateRelease(Release(group), evt).IsMatch.Should().BeFalse();
        _scorer.CalculateMatchScore(group, evt).Should().Be(0);
        _matcher.ValidateRelease(Release(final), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(final, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void ChampionsTrophyNumberedGroupMatchCannotUseTheFinalDate()
    {
        var evt = EventFor("ICC Champions Trophy", "Cricket", "India Cricket vs New Zealand Cricket",
            "India Cricket", "New Zealand Cricket", new DateTime(2025, 3, 9),
            new DateTime(2025, 3, 9, 9, 0, 0, DateTimeKind.Utc), "2025", "200");
        const string groupWithFinalDate = "ICC Champions Trophy 2025 M12 India vs New Zealand 09 03 Full Match Replay Sky Cricket 1080p50fps Jinx8004";

        _matcher.ValidateRelease(Release(groupWithFinalDate), evt).IsMatch.Should().BeFalse();
        _scorer.CalculateMatchScore(groupWithFinalDate, evt).Should().Be(0);
    }

    [Fact]
    public void ChampionsTrophyPaddedGroupMatchCannotUseTheFinalDate()
    {
        var evt = EventFor("ICC Champions Trophy", "Cricket", "India Cricket vs New Zealand Cricket",
            "India Cricket", "New Zealand Cricket", new DateTime(2025, 3, 9),
            new DateTime(2025, 3, 9, 9, 0, 0, DateTimeKind.Utc), "2025", "200");
        const string groupWithFinalDate = "ICC Champions Trophy 2025 M012 India vs New Zealand 09 03 Full Match Replay Sky Cricket 1080p50fps Jinx8004";

        _matcher.ValidateRelease(Release(groupWithFinalDate), evt).IsMatch.Should().BeFalse();
        _scorer.CalculateMatchScore(groupWithFinalDate, evt).Should().Be(0);
    }

    [Fact]
    public void ChampionsTrophyOpeningMatchKeepsItsNumberedRelease()
    {
        var evt = EventFor("ICC Champions Trophy", "Cricket", "New Zealand Cricket vs Pakistan Cricket",
            "New Zealand Cricket", "Pakistan Cricket", new DateTime(2025, 2, 19),
            new DateTime(2025, 2, 19, 9, 0, 0, DateTimeKind.Utc), "2025", "1");
        const string firstMatch = "ICC Champions Trophy 2025 M01 Pakistan vs New Zealand Full Match Replay Sky Cricket 1080p50fps Jinx8004";

        _matcher.ValidateRelease(Release(firstMatch), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(firstMatch, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    private static Event EventFor(string leagueName, string sport, string title, string home,
        string away, DateTime broadcastDate, DateTime eventDateUtc, string season, string? round) => new()
    {
        Title = title,
        Sport = sport,
        Season = season,
        Round = round,
        EventDate = eventDateUtc,
        BroadcastDate = broadcastDate,
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
        DownloadUrl = "http://fixture.invalid/release.nzb",
        Indexer = "Fixture"
    };
}
