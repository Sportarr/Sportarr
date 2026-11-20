using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Startup;

namespace Sportarr.Api.Tests.Migrations;

public class LegacySafetyNetMigrationTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task SqliteUpgradeAcceptsColumnsFromEarlierSafetyNets(bool hasPackColumn, bool hasFailureTimeColumn)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        if (!hasPackColumn)
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE PendingReleases DROP COLUMN IsPack");
        if (!hasFailureTimeColumn)
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE DownloadQueue DROP COLUMN FailedAt");
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_PendingReleases_EventId");
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_PendingReleases_Status_ReleasableAt");

        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL)");
        foreach (var migration in db.Database.GetMigrations()
            .Where(migration => migration is not "20260908224021_PreservePendingPackIntent"
                and not "20260926044143_AddDownloadFailureTime"))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO __EFMigrationsHistory VALUES ({migration}, '9.0.9')");

        DatabaseInitializer.ReconcileExistingColumnMigrations(db);
        await db.Database.MigrateAsync();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains("20260908224021_PreservePendingPackIntent", applied);
        Assert.Contains("20260926044143_AddDownloadFailureTime", applied);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('PendingReleases') WHERE name='IsPack'";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('DownloadQueue') WHERE name='FailedAt'";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name LIKE 'IX_PendingReleases_%'";
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task SqliteSafetyNetAddsTheGrabSourceColumnsToALegacyGrabHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var evt = new Sportarr.Api.Models.Event { Title = "Legacy event", Sport = "Soccer", EventDate = DateTime.UtcNow };
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        db.GrabHistory.Add(new Sportarr.Api.Models.GrabHistory
        {
            EventId = evt.Id, Title = "Legacy.Release.2024", Indexer = "Legacy",
            DownloadUrl = "http://legacy.invalid/1.nzb", Guid = "legacy-1", Protocol = "Usenet"
        });
        await db.SaveChangesAsync();

        // A legacy table has none of the four columns.
        string[] columns = { "GrabSource", "MatchScoreAtGrab", "IdentityRejectedAtGrab", "CrowdHintShown" };
        await using var command = connection.CreateCommand();
        foreach (var column in columns)
        {
            command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('GrabHistory') WHERE name='{column}'";
            if ((long)(await command.ExecuteScalarAsync())! == 1L)
                await db.Database.ExecuteSqlRawAsync($"ALTER TABLE GrabHistory DROP COLUMN \"{column}\"");
        }

        DatabaseInitializer.EnsureCriticalColumns(db);

        foreach (var (column, notNull, defaultValue) in new[]
        {
            ("GrabSource", 1L, "0"),
            ("MatchScoreAtGrab", 0L, (string?)null),
            ("IdentityRejectedAtGrab", 1L, "0"),
            ("CrowdHintShown", 1L, "0"),
        })
        {
            command.CommandText = $"SELECT type, \"notnull\", dflt_value FROM pragma_table_info('GrabHistory') WHERE name='{column}'";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), $"{column} is missing");
            Assert.Equal("INTEGER", reader.GetString(0));
            Assert.Equal(notNull, reader.GetInt64(1));
            Assert.Equal(defaultValue, reader.IsDBNull(2) ? null : reader.GetString(2));
        }

        command.CommandText = "SELECT GrabSource, MatchScoreAtGrab, IdentityRejectedAtGrab, CrowdHintShown FROM GrabHistory";
        await using (var row = await command.ExecuteReaderAsync())
        {
            Assert.True(await row.ReadAsync());
            Assert.Equal(0L, row.GetInt64(0));
            Assert.True(row.IsDBNull(1));
            Assert.Equal(0L, row.GetInt64(2));
            Assert.Equal(0L, row.GetInt64(3));
        }
    }
}
