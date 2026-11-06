using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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

    private static async Task InvokeDiscoverAsync(SportarrDbContext db,
        DownloadOwnershipCoordinator? coordinator = null, ILogger<DiskScanService>? logger = null)
    {
        using var services = new ServiceCollection().AddSingleton(coordinator ?? new DownloadOwnershipCoordinator()).BuildServiceProvider();
        var svc = new DiskScanService(services, logger ?? Mock.Of<ILogger<DiskScanService>>());
        var method = typeof(DiskScanService).GetMethod(
            "DiscoverNewFilesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(svc, new object?[] { db, null, CancellationToken.None })!;
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
    public async Task IgnoresCompletedDuringTheWalkPreventNewCandidatesAndStaleRefreshes()
    {
        var coordinator = new DownloadOwnershipCoordinator();
        var guard = new DiscoverySaveGuard(coordinator);
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(guard).Options;
        using var db = new SportarrDbContext(options);
        var folder = Path.Combine(_tempDir, "download");
        Directory.CreateDirectory(folder);
        var newFile = Path.Combine(folder, "new.mkv");
        var existingFile = Path.Combine(_tempDir, "race sportarr-ev-1969755.mkv");
        var otherFile = Path.Combine(_tempDir, "other.mkv");
        foreach (var path in new[] { newFile, existingFile, otherFile }) await File.WriteAllTextAsync(path, "fixture");
        db.RootFolders.Add(new RootFolder { Path = _tempDir });
        db.Events.Add(new Event { ExternalId = "ev-1969755", Title = "Race", Sport = "Motorsport", EventDate = DateTime.UtcNow });
        db.PendingImports.Add(new PendingImport { Title = "Race", DownloadId = "existing", FilePath = existingFile });
        await db.SaveChangesAsync();
        var ignoredDuringWalk = false;
        var logger = new Mock<ILogger<DiskScanService>>();
        logger.Setup(l => l.Log(LogLevel.Debug, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.StartsWith("[Disk Scan] Discovered untracked file:")),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(_ =>
            {
                if (ignoredDuringWalk) return;
                using var decision = coordinator.TryEnterExternalDecision();
                Assert.NotNull(decision); // Scanning stays outside the gate.
                using var writer = new SportarrDbContext(options);
                var existing = writer.PendingImports.Single();
                existing.Status = PendingImportStatus.Ignored;
                existing.FilePath = ""; // A concurrent job-ID-only ignore must retain its metadata.
                writer.PendingImports.Add(new PendingImport { Title = "Folder", DownloadId = "folder",
                    FilePath = folder, Status = PendingImportStatus.Ignored });
                writer.SaveChanges();
                ignoredDuringWalk = true;
            }));
        await InvokeDiscoverAsync(db, coordinator, logger.Object);
        Assert.True(ignoredDuringWalk && guard.SawCandidateSave);
        var records = await db.PendingImports.AsNoTracking().ToListAsync();
        Assert.Equal(3, records.Count);
        Assert.Equal(otherFile, Assert.Single(records.Where(p => p.Status == PendingImportStatus.Pending)).FilePath);
        Assert.Equal(0, records.Single(p => p.DownloadId == "existing").SuggestionConfidence);
        Assert.True(File.Exists(newFile) && File.Exists(existingFile));
    }

    private sealed class DiscoverySaveGuard(DownloadOwnershipCoordinator coordinator) : SaveChangesInterceptor
    {
        public bool SawCandidateSave { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (data.Context!.ChangeTracker.Entries<PendingImport>().Any(e => e.State == EntityState.Added && e.Entity.DownloadId.StartsWith("disk-", StringComparison.Ordinal)))
            {
                using var decision = coordinator.TryEnterExternalDecision();
                Assert.Null(decision);
                SawCandidateSave = true;
            }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task DoesNotSuppressCaseDistinctFilesBecauseAnotherPathWasIgnored()
    {
        using var db = CreateDb();
        const string fileName = "Race.mkv";
        await SeedAsync(db, fileName);
        db.PendingImports.Add(new PendingImport { DownloadId = "ignored", Title = fileName,
            FilePath = Path.Combine(_tempDir, fileName).ToUpperInvariant(), Status = PendingImportStatus.Ignored });
        await db.SaveChangesAsync();
        await InvokeDiscoverAsync(db);
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, await db.PendingImports.CountAsync());
    }

    [Fact]
    public async Task IgnoredDownloadFolderSuppressesChildrenUntilUnignoredButNotSiblingFolders()
    {
        using var db = CreateDb();
        var folder = Path.Combine(_tempDir, "ignored");
        var sibling = folder + "-other";
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(sibling);
        var ignoredFile = Path.Combine(folder, "race.mkv");
        var siblingFile = Path.Combine(sibling, "other.mkv");
        await File.WriteAllTextAsync(ignoredFile, "video");
        await File.WriteAllTextAsync(siblingFile, "video");
        db.RootFolders.Add(new RootFolder { Path = _tempDir });
        var ignored = new PendingImport { Title = "Ignored", DownloadId = "client-job",
            FilePath = folder + Path.DirectorySeparatorChar, Status = PendingImportStatus.Ignored };
        db.PendingImports.Add(ignored);
        await db.SaveChangesAsync();
        await InvokeDiscoverAsync(db);
        (await db.PendingImports.ToListAsync()).Should().NotContain(p => p.FilePath == ignoredFile)
            .And.Contain(p => p.FilePath == siblingFile);
        db.PendingImports.Remove(ignored);
        await db.SaveChangesAsync();
        await InvokeDiscoverAsync(db);
        (await db.PendingImports.ToListAsync()).Should().ContainSingle(p => p.FilePath == ignoredFile);
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
