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

    [Fact]
    public async Task SqliteMigrationMarksLibraryImportedRecordingsOnly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260927070006_AddEventTypeFolders");

        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        var files = new (string Path, string? Quality, string? ReleaseTitle)[]
        {
            ("/sports/NFL/Bills vs Patriots [HDTV-1080p] [] sportarr-ev-1.ts", "HDTV-1080p", null),
            ("/sports/NFL/Bills vs Patriots [1080P HDTV] [] sportarr-ev-2.TS", "1080P HDTV", null),
            ("/sports/NFL/Bills vs Patriots [HDTV-1080p] [] sportarr-ev-3.ts", "HDTV-1080p", "NFL.2026.Bills.Patriots.1080p.HDTV-GRP"),
            ("/sports/NFL/Bills vs Patriots [HDTV-1080p] [] sportarr-ev-4.mkv", "HDTV-1080p", null),
            ("/sports/NFL/Bills vs Patriots [WEBDL-1080p] [] sportarr-ev-5.ts", "WEBDL-1080p", null),
        };
        foreach (var (path, quality, releaseTitle) in files)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO EventFiles (EventId, FilePath, Quality, ReleaseTitle, Size, Added, "Exists", QualityScore, CustomFormatScore)
                VALUES (1, {path}, {quality}, {releaseTitle}, 0, {DateTime.UtcNow}, 1, 0, 0)
                """);

        await migrator.MigrateAsync();

        var marked = await db.EventFiles.AsNoTracking().Where(f => f.IsIptvRecording).Select(f => f.FilePath).ToListAsync();
        Assert.Equal(new[]
        {
            "/sports/NFL/Bills vs Patriots [HDTV-1080p] [] sportarr-ev-1.ts",
            "/sports/NFL/Bills vs Patriots [1080P HDTV] [] sportarr-ev-2.TS",
        }, marked.OrderBy(path => Array.FindIndex(files, f => f.Path == path)));
    }
}
