using System;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A broadcast-shifted event: an NRL Sunday afternoon in Sydney
/// (2026-03-01 02:15 UTC) carries a US broadcast date of 2026-02-28.
/// The exact-day rule for verified team-sport dates compares against
/// the broadcast date alone. The venue-local date is not available to
/// this app (and the UTC date is not a safe substitute: a late US game
/// rolls INTO the next UTC day, which is the next game of a series),
/// so a venue-dated release does not match until the metadata carries
/// the venue date or the league's broadcast timezone matches its venue.
/// These tests document that boundary alongside the protections.
/// </summary>
public class BroadcastShiftedDateTests
{
    private readonly ReleaseMatchingService _svc;
    private readonly ReleaseMatchScorer _scorer = new();

    public BroadcastShiftedDateTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _svc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event SydneyGame() => new()
    {
        Id = 437,
        Title = "Newcastle Knights vs North Queensland Cowboys",
        Sport = "Rugby",
        HomeTeamId = 20,
        AwayTeamId = 21,
        HomeTeamName = "Newcastle Knights",
        AwayTeamName = "North Queensland Cowboys",
        EventDate = new DateTime(2026, 3, 1, 2, 15, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2026, 2, 28),
        BroadcastDateVerified = true,
        League = new League { Id = 3, Name = "NRL", Sport = "Rugby" }
    };

    [Fact]
    public void A_broadcast_dated_release_matches_exactly()
    {
        var result = _svc.ValidateRelease(
            Rel("NRL 2026 02 28 Newcastle Knights vs North Queensland Cowboys 1080p"),
            SydneyGame());

        result.IsMatch.Should().BeTrue();
        result.MatchReasons.Should().Contain("Date matches exactly");
    }

    [Fact]
    public void The_prior_days_game_is_rejected()
    {
        var result = _svc.ValidateRelease(
            Rel("NRL 2026 02 27 Newcastle Knights vs North Queensland Cowboys 1080p"),
            SydneyGame());

        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void The_next_utc_day_is_not_treated_as_this_game()
    {
        // The UTC date of this event IS 03-01, but accepting it would
        // also accept the next game of a daily series for late US
        // events, so the exact-day rule stays on the broadcast date.
        var result = _svc.ValidateRelease(
            Rel("NRL 2026 03 01 Newcastle Knights vs North Queensland Cowboys 1080p"),
            SydneyGame());

        result.IsMatch.Should().BeFalse(
            because: "the venue date is not in the metadata yet, and UTC is not a safe stand-in");
    }

    [Fact]
    public void The_scorer_vetoes_the_prior_day_for_any_team_fixture()
    {
        // NRL is not in the date-based prefix list; the veto now runs
        // for every fixture with both teams known.
        var score = _scorer.CalculateMatchScore(
            "NRL.2026.02.27.Newcastle.Knights.vs.North.Queensland.Cowboys.1080p.WEB.h264-GRP",
            SydneyGame());

        score.Should().Be(0);
    }
}
