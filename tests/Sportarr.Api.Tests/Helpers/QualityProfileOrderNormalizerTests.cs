using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Startup;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

public class QualityProfileOrderNormalizerTests
{
    [Fact]
    public async Task LegacySqliteTableWithoutImportedProfileColumnsDoesNotBlockStartup()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new SportarrDbContext(options);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE QualityProfiles (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Items TEXT NOT NULL)");

        (await QualityProfileOrderNormalizer.NormalizeAsync(db)).Should().Be(0);
    }

    [Fact]
    public async Task CustomizedImportKeepsItsSavedOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options;

        await using (var db = new SportarrDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.QualityProfiles.Add(new QualityProfile
            {
                Name = "Old ungrouped import",
                IsSynced = true,
                IsCustomized = true,
                TrashId = "imported-profile",
                Items =
                [
                    new() { Name = "Unknown", Quality = 0, Allowed = false },
                    new() { Name = "SDTV", Quality = 1, Allowed = true },
                    new() { Name = "WEBDL-1080p", Quality = 0, Allowed = true },
                    new() { Name = "WEBRip-1080p", Quality = 0, Allowed = true }
                ]
            });
            await db.SaveChangesAsync();
            (await QualityProfileOrderNormalizer.NormalizeAsync(db)).Should().Be(0);
        }

        await using (var db = new SportarrDbContext(options))
        {
            var profile = await db.QualityProfiles.SingleAsync(p => p.Name == "Old ungrouped import");
            profile.Items.Select(item => item.Name)
                .Should().Equal("Unknown", "SDTV", "WEBDL-1080p", "WEBRip-1080p");
        }
    }

    [Fact]
    public async Task ExistingImportStaysBestFirstAfterUngroupingAndSaving()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options;

        await using (var db = new SportarrDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.QualityProfiles.Add(new QualityProfile
            {
                Name = "Imported",
                IsSynced = true,
                TrashId = "imported-profile",
                CutoffQuality = 2,
                Items =
                [
                    new() { Name = "Unknown", Quality = 0, Allowed = false },
                    new() { Name = "SDTV", Quality = 1, Allowed = true },
                    new() { Name = "WEB 1080p", Quality = 2, Allowed = true, Items =
                    [
                        new() { Name = "WEBDL-1080p", Quality = 0, Allowed = true },
                        new() { Name = "WEBRip-1080p", Quality = 0, Allowed = true }
                    ] }
                ]
            });
            await db.SaveChangesAsync();

            (await QualityProfileOrderNormalizer.NormalizeAsync(db)).Should().Be(1);
            (await QualityProfileOrderNormalizer.NormalizeAsync(db)).Should().Be(0);
        }

        await using (var db = new SportarrDbContext(options))
        {
            var profile = await db.QualityProfiles.SingleAsync(p => p.Name == "Imported");
            profile.Items.Select(item => item.Name).Should().Equal("WEB 1080p", "SDTV", "Unknown");
            profile.Items.RemoveAt(0);
            profile.Items.InsertRange(0,
            [
                new QualityItem { Name = "WEBDL-1080p", Quality = 0, Allowed = true },
                new QualityItem { Name = "WEBRip-1080p", Quality = 0, Allowed = true }
            ]);
            profile.IsCustomized = true;
            await db.SaveChangesAsync();
        }

        await using (var db = new SportarrDbContext(options))
        {
            var profile = await db.QualityProfiles.SingleAsync(p => p.Name == "Imported");
            QualityProfileRanker.Compare(profile, "WEBDL-1080p", "SDTV").Should().BePositive();
        }
    }
}
