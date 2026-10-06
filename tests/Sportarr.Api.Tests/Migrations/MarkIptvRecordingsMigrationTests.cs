using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sportarr.Api.Data;

namespace Sportarr.Api.Tests.Migrations;

public class MarkIptvRecordingsMigrationTests
{
    [Fact]
    public async Task SqliteMigrationMarksExistingIptvRecordingsOnly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260927070006_AddEventTypeFolders");

        // Some Events columns come from startup safety nets rather than
        // migrations, so the files stand alone here without their event.
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        var files = new (string Path, string? Source)[]
        {
            ("/sports/UFC/UFC 300 - Recording.ts", "IPTV"),
            ("/sports/UFC/UFC 300 - HDTV-1080p.DVR.mkv", "HDTV"),
            ("/sports/UFC/UFC 300 - HDTV-1080p.DVR - Main Card.mkv", "HDTV"),
            ("/sports/UFC/UFC.300.720p.DVRip.x264-GRP.mkv", "HDTV"),
            ("/sports/UFC/UFC.300.1080p.WEB-DL.x264-GRP.mkv", "WEB-DL"),
        };
        foreach (var (path, source) in files)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO EventFiles (EventId, FilePath, Source, Size, Added, "Exists", QualityScore, CustomFormatScore)
                VALUES (1, {path}, {source}, 0, {DateTime.UtcNow}, 1, 0, 0)
                """);

        await migrator.MigrateAsync();

        var marked = await db.EventFiles.AsNoTracking().Where(f => f.IsIptvRecording).Select(f => f.FilePath).ToListAsync();
        Assert.Equal(new[]
        {
            "/sports/UFC/UFC 300 - Recording.ts",
            "/sports/UFC/UFC 300 - HDTV-1080p.DVR.mkv",
            "/sports/UFC/UFC 300 - HDTV-1080p.DVR - Main Card.mkv",
        }, marked.OrderBy(path => Array.FindIndex(files, f => f.Path == path)));
    }
}
