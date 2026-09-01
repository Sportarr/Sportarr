using System;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report: searching the Aug 29 Tigers vs Dodgers game surfaced the
/// Aug 28 game of the same series at 84% confidence, labeled "Date matches
/// exactly", and the user downloaded the wrong game. The event's broadcast
/// date was correct; the one-day grace meant for the UTC-vs-venue rollover
/// handed over yesterday's matchup, because an MLB series plays the same
/// two teams daily. With a broadcast-local date in hand the rollover is
/// already absorbed, so team sports now require the exact day; the grace
/// survives only for events with no broadcast date.
/// </summary>
public class MlbBackToBackSeriesDateTests
{
    private readonly SportsFileNameParser _parser;
    private readonly ReleaseMatchingService _svc;
    private readonly ReleaseMatchScorer _scorer = new();

    public MlbBackToBackSeriesDateTests()
    {
        _parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _svc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), _parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event Aug29Game(bool withBroadcastDate = true) => new()
    {
        Id = 436,
        Title = "Detroit Tigers vs Los Angeles Dodgers",
        Sport = "Baseball",
        HomeTeamId = 10,
        AwayTeamId = 11,
        HomeTeamName = "Detroit Tigers",
        AwayTeamName = "Los Angeles Dodgers",
        EventDate = new DateTime(2026, 8, 29, 17, 10, 0, DateTimeKind.Utc),
        BroadcastDate = withBroadcastDate ? new DateTime(2026, 8, 29) : null,
        BroadcastDateVerified = withBroadcastDate,
        League = new League { Id = 2, Name = "MLB", Sport = "Baseball" }
    };

    [Fact]
    public void Yesterdays_game_of_the_series_is_rejected()
    {
        var result = _svc.ValidateRelease(
            Rel("MLB RS 2026 Los Angeles Dodgers vs Detroit Tigers 28 08 1080pEN60fps SNLA"),
            Aug29Game());

        result.IsMatch.Should().BeFalse(
            because: "the Aug 28 game is a different game, not a timezone rollover of the Aug 29 one");
    }

    [Fact]
    public void Todays_game_still_matches()
    {
        var result = _svc.ValidateRelease(
            Rel("MLB RS 2026 Los Angeles Dodgers vs Detroit Tigers 29 08 1080pEN60fps SNLA"),
            Aug29Game());

        result.IsMatch.Should().BeTrue();
        result.MatchReasons.Should().Contain("Date matches exactly");
    }

    [Fact]
    public void Without_a_broadcast_date_the_rollover_grace_survives()
    {
        // A legacy event with no broadcast-local date stored can genuinely
        // sit one UTC day away from the release's venue-local title date.
        var result = _svc.ValidateRelease(
            Rel("MLB RS 2026 Los Angeles Dodgers vs Detroit Tigers 28 08 1080pEN60fps SNLA"),
            Aug29Game(withBroadcastDate: false));

        result.IsMatch.Should().BeTrue();
        result.MatchReasons.Should().Contain("Date within 1 day (timezone rollover)");
    }

    [Fact]
    public void An_unverified_backfilled_date_keeps_the_grace()
    {
        // Legacy installs carry a boot-time backfill: BroadcastDate holds
        // the UTC date and can sit one day off the venue-local truth, so
        // the exact-day rule stays disarmed until a sync serves the real
        // broadcast date and flips the provenance flag.
        var evt = Aug29Game();
        evt.BroadcastDateVerified = false;

        var result = _svc.ValidateRelease(
            Rel("MLB RS 2026 Los Angeles Dodgers vs Detroit Tigers 28 08 1080pEN60fps SNLA"), evt);

        result.IsMatch.Should().BeTrue();
        result.MatchReasons.Should().Contain("Date within 1 day (timezone rollover)");
    }

    [Fact]
    public void The_scorer_vetoes_yesterdays_game_the_same_way()
    {
        var score = _scorer.CalculateMatchScore(
            "MLB.2026.08.28.Los.Angeles.Dodgers.vs.Detroit.Tigers.1080p.WEB.h264-NiGHTNiNJAS",
            Aug29Game());

        score.Should().Be(0);
    }

    [Fact]
    public void The_scorer_keeps_the_rollover_grace_without_a_broadcast_date()
    {
        var score = _scorer.CalculateMatchScore(
            "MLB.2026.08.28.Los.Angeles.Dodgers.vs.Detroit.Tigers.1080p.WEB.h264-NiGHTNiNJAS",
            Aug29Game(withBroadcastDate: false));

        score.Should().BeGreaterThan(ReleaseMatchScorer.MinimumMatchScore);
    }
}
