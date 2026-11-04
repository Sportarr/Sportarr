using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sportarr.Api.Data;
using Sportarr.Api.Startup;

namespace Sportarr.Api.Tests.Migrations;

public class SpecialEventTeamModesMigrationTests
{
    private const string PreviousMigration = "20261010220814_ClientCleanupTracking";

    [Fact]
    public async Task SqliteMigrationAppliesToAFreshDatabase()
    {
        // On a fresh SQLite database the toggle columns don't exist yet, because
        // the startup safety nets add them after migrations run. The migration
        // must not reference them.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task SqliteBackfillCarriesTogglesOverToTeamModes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        // An existing install: the safety nets added the toggle columns on an
        // earlier startup.
        foreach (var column in new[] { "MonitorFinals", "MonitorPlayoffs", "MonitorPreseason" })
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE \"Leagues\" ADD COLUMN \"{column}\" INTEGER NOT NULL DEFAULT 0");

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Leagues" ("Name", "Sport", "Monitored", "Added", "MonitorFinals", "MonitorPlayoffs", "MonitorPreseason")
            VALUES ('Finals only', 'Football', 1, {DateTime.UtcNow}, 1, 0, 0),
                   ('Playoffs and preseason', 'Football', 1, {DateTime.UtcNow}, 0, 1, 1),
                   ('Nothing extra', 'Football', 1, {DateTime.UtcNow}, 0, 0, 0)
            """);

        await migrator.MigrateAsync();
        DatabaseInitializer.BackfillSpecialEventTeamModes(db);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Name", "FinalTeamMode", "PlayoffTeamMode", "PreseasonTeamMode"
            FROM "Leagues" ORDER BY "Id"
            """;
        var rows = new List<(string, long, long, long)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)));

        Assert.Equal(new[]
        {
            ("Finals only", 2L, 0L, 0L),
            ("Playoffs and preseason", 0L, 2L, 2L),
            ("Nothing extra", 0L, 0L, 0L),
        }, rows);
    }
}
