using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A history row must resolve to the file IT produced, never to whatever file
/// the event happens to hold. A user deleted a 720p row and lost the 4K file
/// they had, because the row's action was scoped to the event
/// (log: "[FILES] Deleting all 1 files for event 11196").
///
/// These run the real resolution against SQLite, not a stub, so a projection
/// EF cannot translate fails here instead of emptying the History tab.
/// </summary>
public class GrabHistoryFileOwnershipTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public GrabHistoryFileOwnershipTests()
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

    private static GrabHistory Grab(int eventId, string title, string? destinationPath) => new()
    {
        EventId = eventId,
        Title = title,
        Indexer = "Uindex",
        DownloadUrl = "https://example.invalid/x",
        Guid = title,
        Protocol = "Torrent",
        WasImported = destinationPath != null,
        FileExists = destinationPath != null,
        DestinationPath = destinationPath,
    };

    /// <summary>Mirrors the ownership resolution the /api/grab-history projection uses.</summary>
    private static async Task<(bool FileExists, int? EventFileId)> ResolveAsync(SportarrDbContext db, int grabId)
    {
        var row = await db.GrabHistory.Where(g => g.Id == grabId).Select(g => new
        {
            FileExists = db.EventFiles.Any(f => f.EventId == g.EventId && f.FilePath == g.DestinationPath),
            EventFileId = db.EventFiles
                .Where(f => f.EventId == g.EventId && f.FilePath == g.DestinationPath)
                .Select(f => (int?)f.Id).FirstOrDefault(),
        }).SingleAsync();
        return (row.FileExists, row.EventFileId);
    }

    [Fact]
    public async Task SupersededGrab_OwnsNoFile_EvenWhenTheEventStillHasOne()
    {
        await using var db = CreateDb();
        const string uhdPath = "/sports/F1/Austrian Grand Prix - Race - UHD.mkv";

        db.Events.Add(new Event { Id = 11196, Title = "Austrian Grand Prix - Race", Sport = "Motorsport", HasFile = true });
        db.EventFiles.Add(new EventFile { EventId = 11196, FilePath = uhdPath, Exists = true });
        var sd = Grab(11196, "formula1 2026 austrian grand prix 720p WEB H264-JFF",
            "/sports/F1/Austrian Grand Prix - Race - 720p.mkv");
        var uhd = Grab(11196, "Formula.1.2026x08.Austria.Race.SkyF1UHD.4K-HLG", uhdPath);
        db.GrabHistory.AddRange(sd, uhd);
        await db.SaveChangesAsync();

        // The 720p file is gone, replaced by the UHD one. Its row must offer
        // nothing to delete, or deleting it takes the UHD file with it.
        var superseded = await ResolveAsync(db, sd.Id);
        superseded.FileExists.Should().BeFalse();
        superseded.EventFileId.Should().BeNull();

        // The row that really owns the file still resolves to it.
        var current = await ResolveAsync(db, uhd.Id);
        current.FileExists.Should().BeTrue();
        current.EventFileId.Should().NotBeNull();
    }

    [Fact]
    public async Task GrabWithNoRecordedPath_OwnsNoFile()
    {
        await using var db = CreateDb();

        db.Events.Add(new Event { Id = 500, Title = "Legacy Event", Sport = "Motorsport", HasFile = true });
        db.EventFiles.Add(new EventFile { EventId = 500, FilePath = "/sports/legacy.mkv", Exists = true });
        var legacy = Grab(500, "legacy release", destinationPath: null);
        db.GrabHistory.Add(legacy);
        await db.SaveChangesAsync();

        // Rows from before the path was recorded must not adopt the event's
        // current file. Offering no delete is the safe direction.
        var resolved = await ResolveAsync(db, legacy.Id);
        resolved.FileExists.Should().BeFalse();
        resolved.EventFileId.Should().BeNull();
    }
}
