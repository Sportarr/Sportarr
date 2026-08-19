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
/// Coverage for issue #102: TheSportsDB now lists each WRC special stage as
/// its own event ("WRC Rallye Monte-Carlo SS5"). Scene releases name the
/// rally first ("WRC.Rallye.Monte-Carlo.2026.SS5"), which no sports pattern
/// handled. The org-only fallback kept the year and the dots in the search
/// title, so candidate lookup found only the ten most recent WRC events and
/// the right stage never reached the scorer.
/// </summary>
public class WrcStageMatchingTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public WrcStageMatchingTests()
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

    private sealed record Seeded(Event Umbrella, Event StageFour, Event StageFive);

    private static async Task<Seeded> SeedWrcSeasonAsync(SportarrDbContext db)
    {
        var league = new League { Name = "WRC", Sport = "Motorsport" };
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        Event Ev(string title, DateTime date) => new()
        {
            Title = title,
            Sport = "Motorsport",
            LeagueId = league.Id,
            EventDate = date,
            Season = "2026",
            Monitored = true
        };

        var umbrella = Ev("WRC Rallye Monte-Carlo", new DateTime(2026, 1, 22));
        var stageFour = Ev("WRC Rallye Monte-Carlo SS4", new DateTime(2026, 1, 23));
        var stageFive = Ev("WRC Rallye Monte-Carlo SS5", new DateTime(2026, 1, 24));
        db.Events.AddRange(umbrella, stageFour, stageFive);

        // A later rally with more than ten events, so the most-recent-by-league
        // lookup alone can never surface Monte-Carlo. This reproduces the field
        // failure, where "Rally Italia Sardegna" outranked the right stage.
        db.Events.Add(Ev("WRC Rally Italia Sardegna", new DateTime(2026, 6, 4)));
        for (var stage = 1; stage <= 11; stage++)
        {
            db.Events.Add(Ev($"WRC Rally Italia Sardegna SS{stage}", new DateTime(2026, 6, 5).AddHours(stage)));
        }
        await db.SaveChangesAsync();

        return new Seeded(umbrella, stageFour, stageFive);
    }

    [Fact]
    public async Task StageRelease_MatchesItsExactStageEvent()
    {
        await using var db = CreateDb();
        var seeded = await SeedWrcSeasonAsync(db);

        var suggestion = await CreateSvc(db).FindBestMatchAsync(
            "WRC.Rallye.Monte-Carlo.2026.SS5.1080p.WEB.h264-RALLY", "/library/x.mkv");

        suggestion.Should().NotBeNull();
        suggestion!.EventId.Should().Be(seeded.StageFive.Id);
        suggestion.Confidence.Should().BeGreaterThanOrEqualTo(50,
            "a stage release naming the rally and the stage is unambiguous");
    }

    [Fact]
    public async Task NeighbouringStageRelease_MatchesItsOwnStage()
    {
        await using var db = CreateDb();
        var seeded = await SeedWrcSeasonAsync(db);

        var suggestion = await CreateSvc(db).FindBestMatchAsync(
            "WRC.Rallye.Monte-Carlo.2026.SS4.1080p.WEB.h264-RALLY", "/library/x.mkv");

        suggestion.Should().NotBeNull();
        suggestion!.EventId.Should().Be(seeded.StageFour.Id);
    }

    [Fact]
    public async Task RallyReleaseWithoutStage_PrefersTheUmbrellaEvent()
    {
        await using var db = CreateDb();
        var seeded = await SeedWrcSeasonAsync(db);

        var suggestion = await CreateSvc(db).FindBestMatchAsync(
            "WRC.Rallye.Monte-Carlo.2026.1080p.WEB.h264-RALLY", "/library/x.mkv");

        suggestion.Should().NotBeNull();
        suggestion!.EventId.Should().Be(seeded.Umbrella.Id);
    }

    [Fact]
    public async Task StageRelease_WithoutTheRallyeWord_StillMatchesItsStage()
    {
        // Groups drop "Rallye"/"Rally" freely; the stage token has to carry
        // the disambiguation on its own.
        await using var db = CreateDb();
        var seeded = await SeedWrcSeasonAsync(db);

        var suggestion = await CreateSvc(db).FindBestMatchAsync(
            "WRC.Monte.Carlo.2026.SS5.1080p.WEB.h264-RALLY", "/library/x.mkv");

        suggestion.Should().NotBeNull();
        suggestion!.EventId.Should().Be(seeded.StageFive.Id);
        suggestion.Confidence.Should().BeGreaterThanOrEqualTo(50);
    }
}

/// <summary>
/// Parser-level coverage for the rally-name-first WRC format.
/// </summary>
public class WrcStageParsingTests
{
    private static SportsParseResult Parse(string title) =>
        new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()).Parse(title);

    [Fact]
    public void NameFirstStageRelease_ParsesACleanEventTitle()
    {
        var result = Parse("WRC.Rallye.Monte-Carlo.2026.SS5.1080p.WEB.h264-RALLY");

        result.Sport.Should().Be("Motorsport");
        result.Organization.Should().Be("WRC");
        result.EventTitle.Should().Be("WRC Rallye Monte Carlo SS5",
            "the search title must carry no year, no separators, and keep the stage token");
        result.Confidence.Should().BeGreaterThanOrEqualTo(60);
    }

    [Fact]
    public void NameFirstRallyRelease_WithoutStage_ParsesTheRallyTitle()
    {
        var result = Parse("WRC.Rallye.Monte-Carlo.2026.1080p.WEB.h264-RALLY");

        result.Organization.Should().Be("WRC");
        result.EventTitle.Should().Be("WRC Rallye Monte Carlo");
    }
}
