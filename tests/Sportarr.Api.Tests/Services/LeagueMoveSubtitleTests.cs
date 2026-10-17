using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class LeagueMoveSubtitleTests
{
    [Fact]
    public async Task ReorganizeMovesTheVideoAndItsSubtitlesTogether()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sportarr-league-move-" + Guid.NewGuid());
        var sourceRoot = Path.Combine(folder, "source");
        var targetRoot = Path.Combine(folder, "target");
        var relativePath = Path.Combine("League", "Season 2026", "Race.mkv");
        var sourcePath = Path.Combine(sourceRoot, relativePath);
        var targetPath = Path.Combine(targetRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        Directory.CreateDirectory(targetRoot);
        File.WriteAllText(sourcePath, "video");
        File.WriteAllText(Path.ChangeExtension(sourcePath, ".en.srt"), "subtitle");

        try
        {
            await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var source = new RootFolder { Path = sourceRoot };
            var target = new RootFolder { Path = targetRoot };
            db.RootFolders.AddRange(source, target);
            var league = new League { Name = "League", Sport = "Motorsport", RootFolder = source };
            var evt = new Event
            {
                Title = "Race", Sport = "Motorsport", EventDate = DateTime.UtcNow,
                League = league, FilePath = sourcePath, HasFile = true
            };
            var file = new EventFile { Event = evt, FilePath = sourcePath, Size = 5 };
            db.EventFiles.Add(file);
            await db.SaveChangesAsync();

            var writer = new MetadataWriterService(db, Mock.Of<IHttpClientFactory>(),
                Mock.Of<ILogger<MetadataWriterService>>());
            var mover = new LeagueMoveService(db, Mock.Of<ILogger<LeagueMoveService>>(), writer);

            var result = await mover.ReorganizeLeagueAsync(league.Id, target.Id);

            result.Success.Should().BeTrue();
            File.Exists(targetPath).Should().BeTrue();
            File.ReadAllText(Path.ChangeExtension(targetPath, ".en.srt")).Should().Be("subtitle");
            File.Exists(sourcePath).Should().BeFalse();
            file.FilePath.Should().Be(targetPath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
