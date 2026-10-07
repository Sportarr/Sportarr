using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The disk scan suggested an event for an untracked file by title alone, so a
/// file Sportarr named for this season's game was offered for an older game
/// between the same teams. A sportarr event token in the filename names the
/// event outright and wins over the title.
/// </summary>
public class DiskScanSuggestionTests : IDisposable
{
    private readonly string _tempDir;

    public DiskScanSuggestionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sportarr-scan-suggest-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static SportarrDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SportarrDbContext(options);
    }

    private static Task InvokeDiscoverAsync(SportarrDbContext db)
    {
        var svc = new DiskScanService(Mock.Of<IServiceProvider>(), Mock.Of<ILogger<DiskScanService>>());
        var method = typeof(DiskScanService).GetMethod(
            "DiscoverNewFilesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(svc, new object?[] { db, null, CancellationToken.None })!;
    }

    private async Task<(Event Old, Event Current)> SeedAsync(SportarrDbContext db, string fileName)
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, fileName), "video");
        db.RootFolders.Add(new RootFolder { Path = _tempDir });
        var old = new Event
        {
            ExternalId = "ev-312286", Title = "Minnesota Vikings vs Miami Dolphins",
            Sport = "American Football", EventDate = new DateTime(2018, 12, 16)
        };
        var current = new Event
        {
            ExternalId = "ev-1969755", Title = "Minnesota Vikings vs Miami Dolphins",
            Sport = "American Football", EventDate = new DateTime(2026, 10, 4), HasFile = true
        };
        db.Events.AddRange(old, current);
        await db.SaveChangesAsync();
        return (old, current);
    }

    [Fact]
    public async Task SuggestsTheEventTheFilenameTokenNames()
    {
        using var db = CreateDb();
        var (_, current) = await SeedAsync(db,
            "NFL - S2026E108 - Minnesota Vikings vs Miami Dolphins [WEBDL-720p] [nightninjas] sportarr-ev-1969755.mkv");

        await InvokeDiscoverAsync(db);

        db.PendingImports.Should().ContainSingle()
            .Which.SuggestedEventId.Should().Be(current.Id,
                "the token names this season's game, not the older one with the same title");
    }

    [Fact]
    public async Task FallsBackToTheTitleWithoutAToken()
    {
        using var db = CreateDb();
        var (old, _) = await SeedAsync(db, "Minnesota Vikings vs Miami Dolphins.mkv");

        await InvokeDiscoverAsync(db);

        db.PendingImports.Should().ContainSingle()
            .Which.SuggestedEventId.Should().Be(old.Id);
    }
}
