using FluentAssertions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Services;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Sportarr.Api.Tests.Services;

public class AutomaticAcquisitionPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reaper_RefusesExpiredHolds_WithoutRevivingUnverifiedPacks(bool expired)
    {
        var directory = Path.Combine(Path.GetTempPath(), "sportarr-policy-" + Guid.NewGuid());
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Sportarr:DataPath"] = directory }).Build();
            var config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
            var options = new DbContextOptionsBuilder<SportarrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            await using var db = new SportarrDbContext(options);
            var evt = new Event
            {
                Title = "Test match", Sport = "Soccer", EventDate = DateTime.UtcNow.AddDays(expired ? -20 : -1), Monitored = true,
                League = new League { Name = "Test", Sport = "Soccer", AutomaticMissingMaxAgeDays = 7 }
            };
            var single = new PendingRelease { Event = evt, Title = "Single", IsPack = false, ReleasableAt = DateTime.UtcNow.AddMinutes(-1) };
            var legacy = new PendingRelease { Event = evt, Title = "Legacy unknown", IsPack = null, ReleasableAt = single.ReleasableAt, QualityScore = 100 };
            var pack = new PendingRelease { Event = evt, Title = "Pack", IsPack = true, ReleasableAt = single.ReleasableAt, QualityScore = 200 };
            db.PendingReleases.AddRange(single, legacy, pack);
            await db.SaveChangesAsync();
            using var services = new ServiceCollection().AddSingleton(db).AddSingleton(config)
                .AddSingleton(new DownloadClientService(null!, null!, NullLogger<DownloadClientService>.Instance, null!, config, null!))
                .AddSingleton(new NotificationService(null!, NullLogger<NotificationService>.Instance, null!, null!))
                .BuildServiceProvider();
            using var reaper = new PendingReleaseReaperService(services, NullLogger<PendingReleaseReaperService>.Instance);
            var method = typeof(PendingReleaseReaperService).GetMethod("ReapAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(reaper, new object[] { CancellationToken.None })!;
            single.Status.Should().Be(expired ? PendingReleaseStatus.Cancelled : PendingReleaseStatus.Failed);
            if (expired) single.Reason.Should().Contain("7-day");
            legacy.Status.Should().Be(PendingReleaseStatus.Cancelled);
            pack.Status.Should().Be(PendingReleaseStatus.Cancelled);
            legacy.Reason.Should().Contain("pack");
            pack.Reason.Should().Contain("pack");
            (await db.DownloadQueue.CountAsync()).Should().Be(0);
            evt.Monitored.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Catchup_AutomaticRefusalPreservesManualValidation(bool isAutomatic)
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new SportarrDbContext(options);
        var evt = new Event
        {
            Title = "Old match", Sport = "Soccer", EventDate = DateTime.UtcNow.AddDays(-20), Monitored = true,
            League = new League { Name = "Test", Sport = "Soccer", AutomaticMissingMaxAgeDays = 7 }
        };
        var recording = new DvrRecording
        {
            Title = "Old match", Event = evt, IsAutomatic = isAutomatic,
            Method = DvrRecordingMethod.Catchup, Status = DvrRecordingStatus.Scheduled
        };
        db.DvrRecordings.Add(recording);
        await db.SaveChangesAsync();
        using var services = new ServiceCollection().BuildServiceProvider();
        using var catchup = new CatchupDownloadService(services, NullLogger<CatchupDownloadService>.Instance);
        var method = typeof(CatchupDownloadService).GetMethod("ProcessRecordingAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(catchup, new object[] { services, db, new Config(), recording, CancellationToken.None })!;
        recording.Status.Should().Be(isAutomatic ? DvrRecordingStatus.Cancelled : DvrRecordingStatus.Failed);
        recording.ErrorMessage.Should().Contain(isAutomatic ? "7-day" : "catchup requires");
        recording.OutputPath.Should().BeNull();
        evt.Monitored.Should().BeTrue();
        (await db.EventFiles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void CandidatePolicy_DistinguishesMissingPartsFullFilesAndPacks()
    {
        var evt = new Event
        {
            Title = "Test card", Sport = "MMA", EventDate = Now,
            League = new League { Name = "Test", Sport = "MMA", AutomaticUpgradesEnabled = false }
        };
        evt.Files.Add(new EventFile { FilePath = "/media/prelims.mkv", Exists = true, PartName = "Prelims" });
        AutomaticAcquisitionPolicy.RefusalReason(evt, "Main Card", Now).Should().BeNull();
        AutomaticAcquisitionPolicy.RefusalReason(evt, "Prelims", Now).Should().NotBeNull();
        AutomaticAcquisitionPolicy.RefusalReason(evt, "Main Card", Now, isPack: true).Should().Contain("pack");
        evt.Files.Add(new EventFile { FilePath = "/media/full.mkv", Exists = true });
        AutomaticAcquisitionPolicy.RefusalReason(evt, "Main Card", Now).Should().NotBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SqliteUpgrade_PreservesRowsAndEnabledUnlimitedDefaults(bool pendingTableExists)
    {
        SQLitePCL.Batteries_V2.Init();
        var options = new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite("Data Source=:memory:").Options;
        await using var db = new SportarrDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE Leagues (Id INTEGER PRIMARY KEY); INSERT INTO Leagues VALUES (1); CREATE TABLE DvrRecordings (Id INTEGER PRIMARY KEY); INSERT INTO DvrRecordings VALUES (1);");
        if (pendingTableExists)
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE PendingReleases (Id INTEGER PRIMARY KEY, EventId INTEGER, Status INTEGER, ReleasableAt TEXT); INSERT INTO PendingReleases (Id) VALUES (1);");
        var migration = new Sportarr.Data.Migrations.AddAutomaticAcquisitionPolicy();
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations);
        foreach (var command in commands)
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        await using var query = db.Database.GetDbConnection().CreateCommand();
        query.CommandText = "SELECT AutomaticMissingEnabled, AutomaticUpgradesEnabled, AutomaticMissingMaxAgeDays, AutomaticUpgradeMaxAgeDays FROM Leagues WHERE Id = 1";
        await using var reader = await query.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        reader.GetInt32(1).Should().Be(1);
        reader.GetInt32(2).Should().Be(0);
        reader.GetInt32(3).Should().Be(0);
    }

    [Fact]
    public async Task Rss_ExpiredEventIsRefusedBeforeAnyClientWork()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new SportarrDbContext(options);
        var evt = new Event
        {
            Title = "Old match", Sport = "Soccer", EventDate = DateTime.UtcNow.AddDays(-15), Monitored = true,
            League = new League { Name = "Test", Sport = "Soccer", AutomaticMissingMaxAgeDays = 14 }
        };
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        using var services = new ServiceCollection().BuildServiceProvider();
        using var rss = new RssSyncService(services, NullLogger<RssSyncService>.Instance);
        var method = typeof(RssSyncService).GetMethod("ShouldGrabReleaseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = await (Task<(bool Grab, string Reason, string? Part)>)method.Invoke(rss,
            new object?[] { db, evt, new ReleaseSearchResult { Title = "Old match 1080p", Guid = "test", DownloadUrl = "https://example.invalid/test", Indexer = "Test" }, new Config(), null, null, null, null, CancellationToken.None })!;
        result.Grab.Should().BeFalse();
        result.Reason.Should().Contain("14-day");
        (await db.DownloadQueue.CountAsync()).Should().Be(0);
        (await db.PendingReleases.CountAsync()).Should().Be(0);
        evt.Monitored.Should().BeTrue();
    }

    [Fact]
    public void Migrations_PreserveExistingLeagueDefaults()
    {
        var migrations = new Microsoft.EntityFrameworkCore.Migrations.Migration[]
        {
            new Sportarr.Data.Migrations.AddAutomaticAcquisitionPolicy(),
            new Sportarr.Api.Migrations.Postgres.Migrations.AddAutomaticAcquisitionPolicy()
        };
        foreach (var migration in migrations)
        {
            var columns = migration.UpOperations.OfType<AddColumnOperation>().ToDictionary(column => column.Name);
            columns["AutomaticMissingEnabled"].DefaultValue.Should().Be(true);
            columns["AutomaticUpgradesEnabled"].DefaultValue.Should().Be(true);
            columns["AutomaticMissingMaxAgeDays"].DefaultValue.Should().Be(0);
            columns["AutomaticUpgradeMaxAgeDays"].DefaultValue.Should().Be(0);
            columns["IsAutomatic"].DefaultValue.Should().Be(false);
        }
    }

    [Fact]
    public async Task FreshGuard_RespectsChangedSettingsAndPartFiles_WithoutMutatingLibrary()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new SportarrDbContext(options);
        var league = new League { Name = "Test", Sport = "MMA" };
        var evt = new Event { Title = "Test Event", Sport = "MMA", League = league, EventDate = DateTime.UtcNow.AddDays(-20), Monitored = true };
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        (await AutomaticAcquisitionPolicy.RefusalReasonAsync(db, evt.Id)).Should().BeNull();
        await using (var settingsDb = new SportarrDbContext(options))
        {
            var stored = await settingsDb.Leagues.SingleAsync();
            stored.AutomaticUpgradesEnabled = false;
            settingsDb.EventFiles.Add(new EventFile { EventId = evt.Id, FilePath = "/media/prelims.mkv", Quality = "HDTV-1080p", Exists = true, PartName = "Prelims" });
            await settingsDb.SaveChangesAsync();
        }
        (await AutomaticAcquisitionPolicy.RefusalReasonAsync(db, evt.Id, "Prelims")).Should().NotBeNull();
        (await AutomaticAcquisitionPolicy.RefusalReasonAsync(db, evt.Id, "Main Card")).Should().BeNull();
        (await AutomaticAcquisitionPolicy.RefusalReasonAsync(db, evt.Id, "Prelims", isManual: true)).Should().BeNull();
        (await AutomaticAcquisitionPolicy.RefusalReasonAsync(db, evt.Id, isPack: true)).Should().NotBeNull();
        (await AutomaticAcquisitionPolicy.RefusalReasonAsync(db, evt.Id, isManual: true, isPack: true)).Should().BeNull();
        (await db.Events.AsNoTracking().SingleAsync()).Monitored.Should().BeTrue();
        (await db.EventFiles.SingleAsync()).Exists.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Defaults_AllowOldEvents(bool isUpgrade)
    {
        AutomaticAcquisitionPolicy.RefusalReason(new League { Name = "Test", Sport = "Soccer" }, Now.AddYears(-10), isUpgrade, Now)
            .Should().BeNull();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, false)]
    [InlineData(14, true)]
    [InlineData(30, true)]
    public void AgeWindow_IsConfigurable(int days, bool allowed)
    {
        var league = new League { Name = "Test", Sport = "Soccer", AutomaticMissingMaxAgeDays = days };
        var reason = AutomaticAcquisitionPolicy.RefusalReason(league, Now.AddDays(-14), false, Now);
        (reason == null).Should().Be(allowed);
    }

    [Fact]
    public void ExactCutoff_IsEligible_ButOneTickOlderIsNot()
    {
        var league = new League { Name = "Test", Sport = "Soccer", AutomaticMissingMaxAgeDays = 14 };
        AutomaticAcquisitionPolicy.RefusalReason(league, Now.AddDays(-14), false, Now).Should().BeNull();
        AutomaticAcquisitionPolicy.RefusalReason(league, Now.AddDays(-14).AddTicks(-1), false, Now)
            .Should().NotBeNull();
        AutomaticAcquisitionPolicy.RefusalReason(league, Now.AddDays(1), false, Now).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualActions_BypassDisabledAndExpiredPolicy(bool isUpgrade)
    {
        var league = new League
        {
            Name = "Test", Sport = "Soccer",
            AutomaticMissingEnabled = false, AutomaticUpgradesEnabled = false,
            AutomaticMissingMaxAgeDays = 1, AutomaticUpgradeMaxAgeDays = 1
        };
        AutomaticAcquisitionPolicy.RefusalReason(league, Now.AddDays(-30), isUpgrade, Now, true)
            .Should().BeNull();
        AutomaticAcquisitionPolicy.RefusalReason(league, Now, isUpgrade, Now).Should().NotBeNull();
    }

    [Fact]
    public void MissingAndUpgradePolicy_AreIndependent()
    {
        var league = new League { Name = "Test", Sport = "Soccer", AutomaticMissingMaxAgeDays = 30, AutomaticUpgradeMaxAgeDays = 7 };
        AutomaticAcquisitionPolicy.RefusalReason(league, Now.AddDays(-14), false, Now).Should().BeNull();
        AutomaticAcquisitionPolicy.RefusalReason(league, Now.AddDays(-14), true, Now).Should().NotBeNull();
        league.AutomaticMissingEnabled = false;
        AutomaticAcquisitionPolicy.RefusalReason(league, Now, false, Now).Should().NotBeNull();
        AutomaticAcquisitionPolicy.RefusalReason(league, Now, true, Now).Should().BeNull();
    }
}