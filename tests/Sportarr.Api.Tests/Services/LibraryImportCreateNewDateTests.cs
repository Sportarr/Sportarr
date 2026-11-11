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

/// <summary>
/// A file name with a year but no date gives no air date. A new event made
/// from such a file must still take that year, or its folder and file name
/// carry the year of the import.
/// </summary>
public class LibraryImportCreateNewDateTests
{
    [Fact]
    public async Task CreateNewFromABareYearName_DatesTheEventInThatYear()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sportarr-create-new-date-" + Guid.NewGuid());
        var sourceFolder = Path.Combine(folder, "source");
        var libraryFolder = Path.Combine(folder, "library");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(libraryFolder);
        try
        {
            var sourcePath = Path.Combine(sourceFolder, "Fury vs Usyk 2023 720p.mkv");
            await File.WriteAllBytesAsync(sourcePath, new byte[64 * 1024]);

            await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite("Data Source=:memory:").Options);
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();

            var root = new RootFolder { Path = libraryFolder };
            db.RootFolders.Add(root);
            await db.SaveChangesAsync();
            var league = new League { Name = "Boxing", Sport = "Boxing", RootFolderId = root.Id };
            db.Leagues.Add(league);
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

            var result = await service.ImportFilesAsync(new List<FileImportRequest>
            {
                new()
                {
                    FilePath = sourcePath, CreateNew = true, EventTitle = "Fury vs Usyk",
                    LeagueId = league.Id, ImportMode = "copy"
                }
            });

            result.Failed.Should().BeEmpty();
            var created = await db.Events.SingleAsync();
            created.EventDate.Year.Should().Be(2023);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
