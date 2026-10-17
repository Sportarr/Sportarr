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
/// Check that file identity tokens guide disk scan suggestions.
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

        var pending = db.PendingImports.Should().ContainSingle().Which;
        pending.SuggestedEventId.Should().Be(current.Id);
        pending.SuggestionConfidence.Should().Be(100);
    }

    [Fact]
    public async Task DoesNotGuessFromDuplicateTitlesWithoutAToken()
    {
        using var db = CreateDb();
        await SeedAsync(db, "Minnesota Vikings vs Miami Dolphins.mkv");

        await InvokeDiscoverAsync(db);

        var pending = db.PendingImports.Should().ContainSingle().Which;
        pending.SuggestedEventId.Should().BeNull();
        pending.SuggestionConfidence.Should().Be(0);
    }

    [Fact]
    public async Task FallsBackToUniqueTitleWithoutAToken()
    {
        using var db = CreateDb();
        var (old, current) = await SeedAsync(db, "Minnesota Vikings vs Miami Dolphins.mkv");
        db.Events.Remove(current);
        await db.SaveChangesAsync();

        await InvokeDiscoverAsync(db);

        var pending = db.PendingImports.Should().ContainSingle().Which;
        pending.SuggestedEventId.Should().Be(old.Id);
        pending.SuggestionConfidence.Should().Be(50);
    }

    [Fact]
    public async Task DoesNotSuggestAFilledEventFromTitleAlone()
    {
        using var db = CreateDb();
        var (old, current) = await SeedAsync(db, "Minnesota Vikings vs Miami Dolphins.mkv");
        db.Events.Remove(old);
        await db.SaveChangesAsync();

        await InvokeDiscoverAsync(db);

        var pending = db.PendingImports.Should().ContainSingle().Which;
        pending.SuggestedEventId.Should().BeNull();
        pending.SuggestionConfidence.Should().Be(0);
    }

    [Theory]
    [InlineData("sportarr-ev-1969755 sportarr-ev-312286")]
    [InlineData("sportarr-ev-9999999")]
    public async Task DoesNotGuessByTitleWhenTokenIsConflictingOrUnknown(string tokens)
    {
        using var db = CreateDb();
        await SeedAsync(db, $"Minnesota Vikings vs Miami Dolphins {tokens}.mkv");

        await InvokeDiscoverAsync(db);

        var pending = db.PendingImports.Should().ContainSingle().Which;
        pending.SuggestedEventId.Should().BeNull();
        pending.SuggestionConfidence.Should().Be(0);
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(100, false)]
    public async Task RefreshesExistingDiskSuggestionWithoutOverridingUserChoice(int confidence, bool shouldRefresh)
    {
        using var db = CreateDb();
        const string filename = "Minnesota Vikings vs Miami Dolphins sportarr-ev-1969755.mkv";
        var (old, current) = await SeedAsync(db, filename);
        db.PendingImports.Add(new PendingImport
        {
            DownloadId = "disk-existing", Title = filename,
            FilePath = Path.Combine(_tempDir, filename), Size = 5,
            SuggestedEventId = old.Id, SuggestionConfidence = confidence,
            Status = PendingImportStatus.Pending
        });
        await db.SaveChangesAsync();

        await InvokeDiscoverAsync(db);

        var pending = db.PendingImports.Should().ContainSingle().Which;
        pending.SuggestedEventId.Should().Be(shouldRefresh ? current.Id : old.Id);
        pending.SuggestionConfidence.Should().Be(shouldRefresh ? 100 : confidence);
    }

    [Fact]
    public async Task ClearsExistingAutoSuggestionForConflictingTokens()
    {
        using var db = CreateDb();
        const string filename = "Minnesota Vikings vs Miami Dolphins sportarr-ev-1969755 sportarr-ev-312286.mkv";
        var (old, _) = await SeedAsync(db, filename);
        db.PendingImports.Add(new PendingImport
        {
            DownloadId = "disk-existing", Title = filename,
            FilePath = Path.Combine(_tempDir, filename), Size = 5,
            SuggestedEventId = old.Id, SuggestionConfidence = 50,
            Status = PendingImportStatus.Pending
        });
        await db.SaveChangesAsync();

        await InvokeDiscoverAsync(db);

        var pending = db.PendingImports.Should().ContainSingle().Which;
        pending.SuggestedEventId.Should().BeNull();
        pending.SuggestionConfidence.Should().Be(0);
    }

    [Fact]
    public async Task DoesNotGuessFromDuplicateEventIds()
    {
        using var db = CreateDb();
        await SeedAsync(db, "Minnesota Vikings vs Miami Dolphins sportarr-ev-1969755.mkv");
        db.Events.Add(new Event
        {
            ExternalId = "ev-1969755", Title = "Other event", Sport = "American Football",
            EventDate = new DateTime(2026, 10, 4)
        });
        await db.SaveChangesAsync();

        await InvokeDiscoverAsync(db);

        var pending = db.PendingImports.Should().ContainSingle().Which;
        pending.SuggestedEventId.Should().BeNull();
        pending.SuggestionConfidence.Should().Be(0);
    }
}
