using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class DiskScanPartCompletenessTests
{
    [Fact]
    public async Task DiskScanUnmonitorsPartialEventWhenItsLastFileWasDeleted()
    {
        var root = Path.Combine(Path.GetTempPath(), "sportarr-scan-deleted-part-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            var evt = new Event
            {
                Title = "UFC 9997", Sport = "Fighting",
                League = new League { Name = "UFC", Sport = "Fighting", MonitoredParts = "Main Card,Prelims" },
                EventDate = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc),
                Monitored = true, HasFile = false
            };
            db.Events.Add(evt);
            db.MediaManagementSettings.Add(new MediaManagementSettings { UnmonitorDeletedEvents = true });
            await db.SaveChangesAsync();
            db.EventFiles.Add(new EventFile
            {
                EventId = evt.Id, FilePath = Path.Combine(root, "deleted-main.mkv"),
                Size = 1024, Exists = true, PartName = "Main Card", PartNumber = 3
            });
            await db.SaveChangesAsync();

            var settings = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Sportarr:DataPath"] = Path.Combine(root, "config") })
                .Build();
            var config = new ConfigService(settings, NullLogger<ConfigService>.Instance);
            using var services = new ServiceCollection()
                .AddSingleton(db)
                .AddSingleton(config)
                .BuildServiceProvider();
            var scan = new DiskScanService(services, NullLogger<DiskScanService>.Instance);
            var method = typeof(DiskScanService).GetMethod("ScanAllFilesAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)method.Invoke(scan, new object[] { CancellationToken.None })!;

            (await db.EventFiles.AsNoTracking().SingleAsync()).Exists.Should().BeFalse();
            var saved = await db.Events.AsNoTracking().SingleAsync();
            saved.HasFile.Should().BeFalse();
            saved.Monitored.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    public async Task DiskScanKeepsStoredCompletenessAlignedWithImportedParts(
        bool hasPrelims, bool fullEvent, bool initiallyComplete, bool expectedComplete)
    {
        var root = Path.Combine(Path.GetTempPath(), "sportarr-scan-parts-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            var league = new League { Name = "UFC", Sport = "Fighting", MonitoredParts = "Main Card,Prelims" };
            var evt = new Event
            {
                Title = "UFC 9997", Sport = "Fighting", League = league,
                EventDate = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc),
                Monitored = true, HasFile = initiallyComplete
            };
            db.Events.Add(evt);
            db.MediaManagementSettings.Add(new MediaManagementSettings { UnmonitorDeletedEvents = true });
            await db.SaveChangesAsync();

            async Task AddFileAsync(int? partNumber, string name)
            {
                var path = Path.Combine(root, name);
                await File.WriteAllBytesAsync(path, new byte[1024]);
                db.EventFiles.Add(new EventFile
                {
                    EventId = evt.Id, FilePath = path, Size = 1024,
                    Exists = true, PartNumber = partNumber
                });
            }

            await AddFileAsync(fullEvent ? null : 3, "main.mkv");
            if (hasPrelims) await AddFileAsync(2, "prelims.mkv");
            await db.SaveChangesAsync();

            var settings = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Sportarr:DataPath"] = Path.Combine(root, "config") })
                .Build();
            var config = new ConfigService(settings, NullLogger<ConfigService>.Instance);
            using var services = new ServiceCollection()
                .AddSingleton(db)
                .AddSingleton(config)
                .BuildServiceProvider();
            var scan = new DiskScanService(services, NullLogger<DiskScanService>.Instance);
            var method = typeof(DiskScanService).GetMethod("ScanAllFilesAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)method.Invoke(scan, new object[] { CancellationToken.None })!;

            var saved = await db.Events.AsNoTracking().SingleAsync();
            saved.HasFile.Should().Be(expectedComplete);
            saved.Monitored.Should().BeTrue("a partial card is not a deleted event");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
