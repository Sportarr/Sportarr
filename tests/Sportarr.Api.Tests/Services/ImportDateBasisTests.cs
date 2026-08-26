using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #256. An evening game in the United States is stored in
/// UTC as the following calendar day, so a 19:05 first pitch on 18 August is
/// 00:05Z on 19 August. Scoring a release against that raw timestamp put every
/// night game a day ahead of the date its release is named with, and the
/// previous day's game in the same series scored higher than the right one.
///
/// Dates below are the real shape of the stored data: EventDate in UTC,
/// BroadcastDate holding the local date the game was played on.
/// </summary>
public class ImportDateBasisTests
{
    private static ImportMatchingService CreateService()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new SportarrDbContext(options);

        return new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);
    }

    private static Event NightGame(string title, DateTime utcStart, DateTime broadcastDate) => new()
    {
        Id = 1,
        Title = title,
        Sport = "Baseball",
        EventDate = utcStart,
        BroadcastDate = broadcastDate,
        League = new League { Id = 1, Name = "MLB", Sport = "Baseball" }
    };

    private static SportsParseResult Parsed(DateTime date) => new()
    {
        OriginalFilename = "MLB.2026.08.18.Rangers.vs.Nationals.1080p.WEB.h264-GROUP",
        Organization = "MLB",
        EventDate = date
    };

    [Fact]
    public void A_night_game_scores_highest_against_the_date_it_was_played()
    {
        var service = CreateService();
        const string title = "Texas Rangers vs Washington Nationals";

        // Played the evening of 18 August, stored as 00:05Z on the 19th.
        var rightGame = NightGame(title, new DateTime(2026, 8, 19, 0, 5, 0), new DateTime(2026, 8, 18));

        // The same two teams the evening before, stored as 00:05Z on the 18th.
        var previousGame = NightGame(title, new DateTime(2026, 8, 18, 0, 5, 0), new DateTime(2026, 8, 17));

        var release = Parsed(new DateTime(2026, 8, 18));

// A search title that only partially matches keeps the base score below the
        // hundred-point ceiling, so the date bonus is visible rather than
        // saturated away.
        const string searchTitle = "Rangers vs Nationals";

        var rightScore = service.CalculateMatchConfidence(searchTitle, title, null, rightGame, release);
        var wrongScore = service.CalculateMatchConfidence(searchTitle, title, null, previousGame, release);

        rightScore.Should().BeGreaterThan(wrongScore,
            "the release names the date the game was played, not the UTC day it rolled over into");
    }

    [Fact]
    public void An_afternoon_game_still_matches_its_own_date()
    {
        var service = CreateService();
        const string title = "Pittsburgh Pirates vs Detroit Tigers";

        // An afternoon game does not roll over, so both dates agree.
        var game = NightGame(title, new DateTime(2026, 8, 19, 16, 35, 0), new DateTime(2026, 8, 19));
        var next = NightGame(title, new DateTime(2026, 8, 20, 16, 35, 0), new DateTime(2026, 8, 20));

        var release = Parsed(new DateTime(2026, 8, 19));

const string searchTitle = "Pirates vs Tigers";

        service.CalculateMatchConfidence(searchTitle, title, null, game, release)
            .Should().BeGreaterThan(service.CalculateMatchConfidence(searchTitle, title, null, next, release));
    }

    [Fact]
    public void An_event_without_a_broadcast_date_falls_back_to_its_own()
    {
        var service = CreateService();
        const string title = "Some League Final";

        var game = new Event
        {
            Id = 1,
            Title = title,
            Sport = "Baseball",
            EventDate = new DateTime(2026, 8, 19, 16, 35, 0),
            BroadcastDate = null,
            League = new League { Id = 1, Name = "MLB", Sport = "Baseball" }
        };

const string searchTitle = "League Final";

        var onTheDay = service.CalculateMatchConfidence(searchTitle, title, null, game, Parsed(new DateTime(2026, 8, 19)));
        var aWeekOut = service.CalculateMatchConfidence(searchTitle, title, null, game, Parsed(new DateTime(2026, 8, 26)));

        onTheDay.Should().BeGreaterThan(aWeekOut);
    }
}
