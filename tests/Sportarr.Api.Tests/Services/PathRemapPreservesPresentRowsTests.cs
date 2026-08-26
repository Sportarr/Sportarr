using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The remap rewrite touches only rows already flagged missing. It used to
/// match every row under the old prefix, so a drift affecting one mount
/// rewrote the records of files sitting exactly where their record said.
/// SQLite-backed because the rewrite is raw SQL the InMemory provider
/// cannot run.
/// </summary>
public class PathRemapPreservesPresentRowsTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public PathRemapPreservesPresentRowsTests()
    {
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

    private static Event Fixture() => new()
    {
        Id = 1,
        Title = "Some Event",
        Sport = "Baseball",
    };

    private static EventFile Row(int id, string path, bool exists) => new()
    {
        Id = id,
        EventId = 1,
        FilePath = path,
        Exists = exists,
        MissingSince = exists ? null : DateTime.UtcNow,
    };

    [Fact]
    public async Task Rewrites_missing_rows_and_leaves_present_ones_alone()
    {
        using var db = CreateDb();
        db.Events.Add(Fixture());
        db.EventFiles.AddRange(
            Row(1, "/mnt/old/NFL/game one.mkv", exists: false),
            Row(2, "/mnt/old/NFL/game two.mkv", exists: true));
        await db.SaveChangesAsync();

        var svc = new PathRemapService(db, NullLogger<PathRemapService>.Instance);
        var affected = await svc.ApplyRemapAsync("/mnt/old", "/mnt/new");

        affected.Should().Be(1);
        (await db.EventFiles.AsNoTracking().SingleAsync(f => f.Id == 1)).FilePath
            .Should().Be("/mnt/new/NFL/game one.mkv");
        (await db.EventFiles.AsNoTracking().SingleAsync(f => f.Id == 2)).FilePath
            .Should().Be("/mnt/old/NFL/game two.mkv",
                "a file sitting where its record says must not be pointed away from itself");
    }

    [Fact]
    public async Task A_literal_underscore_in_the_prefix_is_not_a_wildcard()
    {
        using var db = CreateDb();
        // LIKE would read the underscore as any-one-character and catch the
        // sibling; SUBSTR equality must not.
        db.Events.Add(Fixture());
        db.EventFiles.AddRange(
            Row(1, "/mnt/red_sox/game.mkv", exists: false),
            Row(2, "/mnt/redisox/game.mkv", exists: false));
        await db.SaveChangesAsync();

        var svc = new PathRemapService(db, NullLogger<PathRemapService>.Instance);
        var affected = await svc.ApplyRemapAsync("/mnt/red_sox", "/mnt/boston");

        affected.Should().Be(1);
        (await db.EventFiles.AsNoTracking().SingleAsync(f => f.Id == 2)).FilePath
            .Should().Be("/mnt/redisox/game.mkv");
    }
}
