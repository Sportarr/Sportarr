using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The round only ever picked candidates out of the database. Candidates also
/// arrive from the title, date and word searches, and the scorer never looked
/// at the round again, so the race of one round could win the file of another
/// on the strength of a matching session alone.
/// </summary>
public class MotorsportRoundMatchingTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public MotorsportRoundMatchingTests()
    {
        // SQLite, not the in-memory provider: candidate lookup runs LIKE queries.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private SportarrDbContext CreateDb()
    {
        var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(_connection)
            .Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static ImportMatchingService CreateSvc(SportarrDbContext db) =>
        new(db,
            new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>()),
            new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
            new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()),
            Mock.Of<ILogger<ImportMatchingService>>());

    private sealed record Seeded(Event RoundTwoRace, Event RoundFiveRace);

    private static async Task<Seeded> SeedSeasonAsync(SportarrDbContext db)
    {
        var league = new League { Name = "Formula 1", Sport = "Motorsport" };
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        Event Ev(string title, string round, DateTime date) => new()
        {
            Title = title,
            Sport = "Motorsport",
            LeagueId = league.Id,
            EventDate = date,
            Season = "2026",
            Round = round,
            Monitored = true
        };

        // Identical titles on purpose. Several championships name every
        // round the same way and leave the round number to tell them apart,
        // which is the case the scorer had no answer for.
        var roundTwoRace = Ev("Formula 1 Grand Prix Race", "2", new DateTime(2026, 3, 8));
        var roundFiveRace = Ev("Formula 1 Grand Prix Race", "5", new DateTime(2026, 5, 3));

        db.Events.AddRange(
            Ev("Formula 1 Grand Prix Qualifying", "2", new DateTime(2026, 3, 7)),
            roundTwoRace,
            Ev("Formula 1 Grand Prix Qualifying", "5", new DateTime(2026, 5, 2)),
            roundFiveRace);
        await db.SaveChangesAsync();

        return new Seeded(roundTwoRace, roundFiveRace);
    }

    [Fact]
    public async Task RaceRelease_MatchesTheRaceOfItsOwnRound()
    {
        await using var db = CreateDb();
        var seeded = await SeedSeasonAsync(db);

        var suggestion = await CreateSvc(db).FindBestMatchAsync(
            "Formula.1.2026.Round05.Grand.Prix.Race.1080p.WEB.h264-F1", "/library/x.mkv");

        suggestion.Should().NotBeNull();
        suggestion!.EventId.Should().Be(seeded.RoundFiveRace.Id);
    }

    [Fact]
    public async Task RaceRelease_DoesNotMatchTheRaceOfAnotherRound()
    {
        await using var db = CreateDb();
        var seeded = await SeedSeasonAsync(db);

        var suggestion = await CreateSvc(db).FindBestMatchAsync(
            "Formula.1.2026.Round02.Grand.Prix.Race.1080p.WEB.h264-F1", "/library/x.mkv");

        suggestion.Should().NotBeNull();
        suggestion!.EventId.Should().NotBe(seeded.RoundFiveRace.Id,
            "the race of round five must never take a round two file");
        suggestion.EventId.Should().Be(seeded.RoundTwoRace.Id);
    }
}
