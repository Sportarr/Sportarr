using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class EventDateMatchContextTests
{
    [Fact]
    public void Verified_midnight_copa_events_need_date_peers()
    {
        var copa = Game(1, 1, new DateTime(2024, 6, 21, 0, 0, 0, DateTimeKind.Utc));
        copa.League = new League { Id = 1, Name = "Copa America", Sport = "Soccer" };
        copa.BroadcastDateVerified = true;
        var daytime = Game(2, 1, new DateTime(2024, 6, 21, 12, 0, 0, DateTimeKind.Utc));
        daytime.League = copa.League;
        daytime.BroadcastDateVerified = true;

        EventDateMatchContext.ShouldLoadPeers(copa).Should().BeTrue();
        EventDateMatchContext.ShouldLoadPeers(daytime).Should().BeFalse();
    }

    [Theory]
    [InlineData("EHF Champions League")]
    [InlineData("World Mens Curling Championship")]
    [InlineData("Concacaf W Gold Cup")]
    [InlineData("Concacaf Central American Cup")]
    [InlineData("Concacaf Gold Cup Qualifying")]
    [InlineData("China FA Cup")]
    public void Verified_leagues_with_adjacent_day_release_windows_load_date_peers(string leagueName)
    {
        var evt = Game(1, 1, new DateTime(2026, 9, 18, 18, 0, 0, DateTimeKind.Utc));
        evt.League = new League { Id = 1, Name = leagueName, Sport = "Soccer" };
        evt.BroadcastDateVerified = true;

        EventDateMatchContext.ShouldLoadPeers(evt).Should().BeTrue();
    }

    [Fact]
    public async Task Loads_only_nearby_events_from_the_same_league()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SportarrDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Leagues.AddRange(
            new League { Id = 1, Name = "MLB", Sport = "Baseball" },
            new League { Id = 2, Name = "NPB", Sport = "Baseball" });
        var target = Game(1, 1, new DateTime(2026, 8, 29, 17, 10, 0, DateTimeKind.Utc));
        var localDateBoundary = Game(5, 1, new DateTime(2026, 9, 1, 0, 10, 0, DateTimeKind.Utc));
        localDateBoundary.BroadcastDate = new DateTime(2026, 8, 31);
        db.Events.AddRange(
            target,
            Game(2, 1, new DateTime(2026, 8, 28, 17, 10, 0, DateTimeKind.Utc)),
            Game(3, 2, new DateTime(2026, 8, 28, 17, 10, 0, DateTimeKind.Utc)),
            Game(4, 1, new DateTime(2026, 8, 25, 17, 10, 0, DateTimeKind.Utc)),
            localDateBoundary);
        await db.SaveChangesAsync();

        var peers = await EventDateMatchContext.LoadAsync(db, target);

        peers.Select(peer => peer.Id).Should().BeEquivalentTo(new[] { 1, 2, 5 });
    }

    [Fact]
    public async Task Loads_unmonitored_peers_for_release_dates_in_monitored_leagues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SportarrDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Leagues.AddRange(
            new League { Id = 1, Name = "MLB", Sport = "Baseball" },
            new League { Id = 2, Name = "NPB", Sport = "Baseball" });
        var monitoredTarget = Game(1, 1, new DateTime(2026, 8, 29, 17, 10, 0, DateTimeKind.Utc));
        monitoredTarget.Monitored = true;
        var unmonitoredNamedGame = Game(2, 1, new DateTime(2026, 8, 28, 17, 10, 0, DateTimeKind.Utc));
        unmonitoredNamedGame.Monitored = false;
        var oldPeer = Game(5, 1, new DateTime(2016, 8, 28, 17, 10, 0, DateTimeKind.Utc));
        db.Events.AddRange(
            monitoredTarget,
            unmonitoredNamedGame,
            Game(3, 2, new DateTime(2026, 8, 28, 17, 10, 0, DateTimeKind.Utc)),
            Game(4, 1, new DateTime(2026, 8, 25, 17, 10, 0, DateTimeKind.Utc)),
            oldPeer,
            Game(6, 1, new DateTime(2020, 8, 28, 17, 10, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var peers = await EventDateMatchContext.LoadAsync(
            db,
            new[] { new DateTime(2016, 8, 28), new DateTime(2026, 8, 28) },
            new[] { 1 });

        peers.Select(peer => peer.Id).Should().BeEquivalentTo(new[] { 1, 2, 5 });
    }

    private static Event Game(int id, int leagueId, DateTime eventDate) => new()
    {
        Id = id,
        Title = "Detroit Tigers vs Los Angeles Dodgers",
        Sport = "Baseball",
        LeagueId = leagueId,
        HomeTeamName = "Detroit Tigers",
        AwayTeamName = "Los Angeles Dodgers",
        EventDate = eventDate,
        BroadcastDate = eventDate.Date,
        BroadcastDateVerified = false
    };
}
