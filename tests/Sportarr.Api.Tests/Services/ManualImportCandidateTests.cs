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
/// Coverage for issue #234: the manual candidate list parsed release names
/// with the generic parser only, so the sport, the championship, the round,
/// and the session never reached event lookup or confidence scoring. A
/// reviewer picking a match by hand saw weaker candidates than the automatic
/// matcher would have considered, including events from other championships.
/// </summary>
public class ManualImportCandidateTests : IDisposable
{
    private const string MotoGpRelease =
        "MotoGP 2026 Round 12 Silverstone Race 1080p WEB-DL";

    private readonly SqliteConnection _connection;

    public ManualImportCandidateTests()
    {
        // SQLite, not the in-memory provider: candidate lookup runs LIKE
        // queries, which the in-memory provider cannot evaluate.
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

    private static async Task<(League league, Event evt)> SeedEventAsync(
        SportarrDbContext db, string leagueName, string sport, string eventTitle, DateTime date)
    {
        var league = new League { Name = leagueName, Sport = sport };
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        var evt = new Event
        {
            Title = eventTitle,
            Sport = sport,
            LeagueId = league.Id,
            EventDate = date,
            Season = date.Year.ToString(),
            Monitored = true
        };
        db.Events.Add(evt);
        await db.SaveChangesAsync();

        return (league, evt);
    }

    [Fact]
    public async Task ManualCandidates_RankTheRightChampionshipFirst()
    {
        await using var db = CreateDb();
        var (_, motogp) = await SeedEventAsync(
            db, "MotoGP", "Motorsport", "Silverstone Race", new DateTime(2026, 8, 2));
        await SeedEventAsync(
            db, "ONE Championship", "Fighting", "Silverstone Race", new DateTime(2026, 3, 9));

        var suggestions = await CreateSvc(db).GetAllPossibleMatchesAsync(MotoGpRelease);

        suggestions.Should().NotBeEmpty("a manual reviewer needs candidates to choose from");
        suggestions[0].EventId.Should().Be(motogp.Id,
            "the release names MotoGP, so it must outrank the identically titled fighting event");
    }

    [Fact]
    public async Task ManualCandidates_CarryTheParsedSportsMetadata()
    {
        await using var db = CreateDb();
        await SeedEventAsync(
            db, "MotoGP", "Motorsport", "Silverstone Race", new DateTime(2026, 8, 2));

        var suggestions = await CreateSvc(db).GetAllPossibleMatchesAsync(MotoGpRelease);

        suggestions.Should().NotBeEmpty();
        // The UI shows these next to a suggestion, and they feed new-event
        // creation when the reviewer rejects every candidate.
        suggestions[0].ParsedSport.Should().NotBeNullOrEmpty();
        suggestions[0].ParsedOrganization.Should().Be("MotoGP",
            "the championship must reach the suggestion so the UI and new-event creation can use it");
    }
}
