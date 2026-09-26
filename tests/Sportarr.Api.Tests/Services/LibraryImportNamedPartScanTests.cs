using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Services;

public class LibraryImportNamedPartScanTests
{
    [Fact]
    public async Task ReassigningLibraryFileClearsPreviousEventsFileState()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sportarr-reassign-" + Guid.NewGuid());
        var sourceFolder = Path.Combine(folder, "source");
        var libraryFolder = Path.Combine(folder, "library");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(libraryFolder);
        try
        {
            var sourcePath = Path.Combine(sourceFolder, "UFC.9999.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(sourcePath, new byte[64 * 1024]);

            await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite("Data Source=:memory:").Options);
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();

            var root = new RootFolder { Path = libraryFolder };
            db.RootFolders.Add(root);
            await db.SaveChangesAsync();
            var league = new League
            {
                Name = "UFC", Sport = "Fighting", RootFolderId = root.Id,
                MonitoredParts = "Main Card"
            };
            db.Leagues.Add(league);
            db.MediaManagementSettings.Add(new MediaManagementSettings
            {
                RenameEvents = true,
                StandardFileFormat = "{Series} - {Season}{Episode}{Part} - {Event Title}"
            });
            var eventDate = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc);
            var previous = new Event
            {
                Title = "UFC 9999", Sport = "Fighting", League = league,
                EventDate = eventDate, Season = "2026", SeasonNumber = 2026,
                EpisodeNumber = 1, Monitored = true
            };
            var replacement = new Event
            {
                Title = "UFC 9999", Sport = "Fighting", League = league,
                EventDate = eventDate, Season = "2026", SeasonNumber = 2026,
                EpisodeNumber = 1, Monitored = true
            };
            db.Events.AddRange(previous, replacement);
            await db.SaveChangesAsync();

            var parser = new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>());
            var config = new ConfigService(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Sportarr:DataPath"] = Path.Combine(folder, "config") }).Build(),
                Mock.Of<ILogger<ConfigService>>());
            var service = new LibraryImportService(
                db, Mock.Of<ILogger<LibraryImportService>>(), parser,
                new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
                new FileNamingService(Mock.Of<ILogger<FileNamingService>>()),
                new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()), config,
                EpisodeResolverFixture.Create(db, config),
                new DiskSpaceService(Mock.Of<ILogger<DiskSpaceService>>()),
                new CustomFormatService(parser), null!, Mock.Of<IMetadataWriterService>());

            var first = await service.ImportFilesAsync(new List<FileImportRequest>
            {
                new() { FilePath = sourcePath, EventId = previous.Id, PartName = "Main Card", ImportMode = "copy" }
            });
            first.Failed.Should().BeEmpty();
            var libraryPath = first.Imported.Should().ContainSingle().Subject;
            previous.HasFile.Should().BeTrue();

            var second = await service.ImportFilesAsync(new List<FileImportRequest>
            {
                new() { FilePath = libraryPath, EventId = replacement.Id, PartName = "Main Card", ImportMode = "copy" }
            });

            second.Failed.Should().BeEmpty();
            (await db.EventFiles.SingleAsync()).EventId.Should().Be(replacement.Id);
            replacement.HasFile.Should().BeTrue();
            previous.HasFile.Should().BeFalse();
            previous.FilePath.Should().BeNull();
            previous.FileSize.Should().BeNull();
            previous.Quality.Should().BeNull();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MainCardImportKeepsPrelimsWanted(bool createNew)
    {
        var folder = Path.Combine(Path.GetTempPath(), "sportarr-partial-card-" + Guid.NewGuid());
        var sourceFolder = Path.Combine(folder, "source");
        var libraryFolder = Path.Combine(folder, "library");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(libraryFolder);
        try
        {
            var sourcePath = Path.Combine(sourceFolder, "UFC.9999.2026.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(sourcePath, new byte[64 * 1024]);

            await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite("Data Source=:memory:").Options);
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();

            var root = new RootFolder { Path = libraryFolder };
            db.RootFolders.Add(root);
            await db.SaveChangesAsync();
            var league = new League
            {
                Name = "UFC", Sport = "Fighting", RootFolderId = root.Id,
                MonitoredParts = "Main Card,Prelims"
            };
            db.Leagues.Add(league);
            db.MediaManagementSettings.Add(new MediaManagementSettings
            {
                RenameEvents = true,
                StandardFileFormat = "{Series} - {Season}{Episode}{Part} - {Event Title}"
            });
            await db.SaveChangesAsync();

            Event? evt = null;
            if (!createNew)
            {
                evt = new Event
                {
                    Title = "UFC 9999", Sport = "Fighting", LeagueId = league.Id, League = league,
                    EventDate = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc),
                    Season = "2026", SeasonNumber = 2026, EpisodeNumber = 1,
                    Monitored = true
                };
                db.Events.Add(evt);
                await db.SaveChangesAsync();
            }

            var parser = new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>());
            var config = new ConfigService(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Sportarr:DataPath"] = Path.Combine(folder, "config") }).Build(),
                Mock.Of<ILogger<ConfigService>>());
            var service = new LibraryImportService(
                db, Mock.Of<ILogger<LibraryImportService>>(), parser,
                new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
                new FileNamingService(Mock.Of<ILogger<FileNamingService>>()),
                new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()), config,
                EpisodeResolverFixture.Create(db, config),
                new DiskSpaceService(Mock.Of<ILogger<DiskSpaceService>>()),
                new CustomFormatService(parser), null!, Mock.Of<IMetadataWriterService>());

            var result = await service.ImportFilesAsync(new List<FileImportRequest>
            {
                new()
                {
                    FilePath = sourcePath, EventId = evt?.Id, CreateNew = createNew,
                    EventTitle = "UFC 9999", LeagueId = league.Id,
                    EventDate = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc),
                    Season = "2026", PartName = "Main Card", ImportMode = "copy"
                }
            });

            result.Failed.Should().BeEmpty();
            var imported = db.EventFiles.Should().ContainSingle().Subject;
            imported.PartName.Should().Be("Main Card");
            var importedEvent = await db.Events.SingleAsync();
            importedEvent.HasFile.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitFullEventKeepsRenamedFileAndStoredPartUnsegmented(bool createNew)
    {
        var folder = Path.Combine(Path.GetTempPath(), "sportarr-full-event-" + Guid.NewGuid());
        var sourceFolder = Path.Combine(folder, "source");
        var libraryFolder = Path.Combine(folder, "library");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(libraryFolder);
        try
        {
            var sourcePath = Path.Combine(sourceFolder, "UFC.9999.2026.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(sourcePath, new byte[64 * 1024]);

            await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite("Data Source=:memory:").Options);
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();

            var root = new RootFolder { Path = libraryFolder };
            db.RootFolders.Add(root);
            await db.SaveChangesAsync();
            var league = new League { Name = "UFC", Sport = "Fighting", RootFolderId = root.Id };
            db.Leagues.Add(league);
            db.MediaManagementSettings.Add(new MediaManagementSettings
            {
                RenameEvents = true,
                StandardFileFormat = "{Series} - {Season}{Episode}{Part} - {Event Title}"
            });
            await db.SaveChangesAsync();

            Event? evt = null;
            if (!createNew)
            {
                evt = new Event
                {
                    Title = "UFC 9999", Sport = "Fighting", LeagueId = league.Id, League = league,
                    EventDate = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc),
                    Season = "2026", SeasonNumber = 2026, EpisodeNumber = 1
                };
                db.Events.Add(evt);
                await db.SaveChangesAsync();
            }

            var parser = new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>());
            var config = new ConfigService(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Sportarr:DataPath"] = Path.Combine(folder, "config") }).Build(),
                Mock.Of<ILogger<ConfigService>>());
            var service = new LibraryImportService(
                db, Mock.Of<ILogger<LibraryImportService>>(), parser,
                new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
                new FileNamingService(Mock.Of<ILogger<FileNamingService>>()),
                new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()), config,
                EpisodeResolverFixture.Create(db, config),
                new DiskSpaceService(Mock.Of<ILogger<DiskSpaceService>>()),
                new CustomFormatService(parser), null!, Mock.Of<IMetadataWriterService>());

            var result = await service.ImportFilesAsync(new List<FileImportRequest>
            {
                new()
                {
                    FilePath = sourcePath,
                    EventId = evt?.Id,
                    CreateNew = createNew,
                    EventTitle = "UFC 9999",
                    LeagueId = league.Id,
                    EventDate = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc),
                    Season = "2026",
                    PartName = "Full Event",
                    ImportMode = "copy"
                }
            });

            result.Failed.Should().BeEmpty();
            var imported = db.EventFiles.Should().ContainSingle().Subject;
            imported.PartName.Should().BeNull();
            imported.PartNumber.Should().BeNull();
            Path.GetFileName(imported.FilePath).Should().Be("UFC - S2026E01 - UFC 9999.mkv");
            File.Exists(imported.FilePath).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("UFC", "UFC 9999", "Fighting", "UFC.9999.2026.09.01.Prelims.720p.WEB-DL.mkv")]
    [InlineData("UFC", "UFC 9999", "fighting", "UFC.9999.2026.09.01.Prelims.720p.WEB-DL.mkv")]
    [InlineData("ONE Championship", "ONE Fight Night 26", "Combat", "ONE.Fight.Night.26.2026.09.01.Lead.Card.720p.WEB-DL.mkv")]
    public async Task NamedPartMatchesEventThatAlreadyHasMainCard(
        string leagueName, string eventTitle, string eventSport, string filename)
    {
        var folder = Path.Combine(Path.GetTempPath(), "sportarr-named-part-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, filename);
            await File.WriteAllBytesAsync(path, new byte[64 * 1024]);

            await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite("Data Source=:memory:").Options);
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();

            var league = new League { Name = leagueName, Sport = eventSport };
            db.Leagues.Add(league);
            await db.SaveChangesAsync();
            var evt = new Event
            {
                Title = eventTitle, Sport = eventSport, LeagueId = league.Id, League = league,
                EventDate = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc),
                Season = "2026", SeasonNumber = 2026, EpisodeNumber = 1, HasFile = true,
                FilePath = Path.Combine(folder, "already-imported-main-card.mkv")
            };
            db.Events.Add(evt);
            await db.SaveChangesAsync();

            var parser = new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>());
            var config = new ConfigService(new ConfigurationBuilder().Build(), Mock.Of<ILogger<ConfigService>>());
            var service = new LibraryImportService(
                db, Mock.Of<ILogger<LibraryImportService>>(), parser,
                new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
                new FileNamingService(Mock.Of<ILogger<FileNamingService>>()),
                new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()), config,
                EpisodeResolverFixture.Create(db, config),
                new DiskSpaceService(Mock.Of<ILogger<DiskSpaceService>>()),
                new CustomFormatService(parser), null!, Mock.Of<IMetadataWriterService>());

            var scan = await service.ScanFolderAsync(folder, includeSubfolders: false);

            scan.Errors.Should().BeEmpty();
            scan.MatchedFiles.Should().ContainSingle()
                .Which.MatchedEventId.Should().Be(evt.Id);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
