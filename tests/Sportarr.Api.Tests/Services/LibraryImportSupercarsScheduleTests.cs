using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Services;

public class LibraryImportSupercarsScheduleTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "sportarr-supercars-schedule-" + Guid.NewGuid());

    [Fact]
    public async Task FolderScanLoadsEachSupercarsRoundScheduleOnce()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Path.Combine(
            _folder, "Supercars 2026 Round09 Ipswich Race 1 1080p WEB.mkv"), new byte[64 * 1024]);
        File.WriteAllBytes(Path.Combine(
            _folder, "Supercars 2026 Round09 Ipswich Race 2 1080p WEB.mkv"), new byte[64 * 1024]);

        var counter = new SupercarsScheduleQueryCounter();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite("Data Source=:memory:")
            .AddInterceptors(counter)
            .Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var league = new League { Name = "Supercars", Sport = "Motorsport" };
        db.Leagues.Add(league);
        await db.SaveChangesAsync();
        for (var race = 26; race <= 28; race++)
        {
            db.Events.Add(new Event
            {
                Title = $"Century Batteries Ipswich Super 440 - Race {race}",
                Sport = "Motorsport",
                Season = "2026",
                Round = "9",
                EpisodeNumber = race,
                EventDate = new DateTime(2026, 8, 21).AddDays(race - 26),
                LeagueId = league.Id,
                League = league
            });
        }
        await db.SaveChangesAsync();

        var parser = new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>());
        var config = new ConfigService(
            new ConfigurationBuilder().Build(), Mock.Of<ILogger<ConfigService>>());
        var service = new LibraryImportService(
            db,
            Mock.Of<ILogger<LibraryImportService>>(),
            parser,
            new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
            new FileNamingService(Mock.Of<ILogger<FileNamingService>>()),
            new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()),
            config,
            EpisodeResolverFixture.Create(db, config),
            new DiskSpaceService(Mock.Of<ILogger<DiskSpaceService>>()),
            new CustomFormatService(parser),
            null!,
            Mock.Of<IMetadataWriterService>());

        counter.Reset();
        await service.ScanFolderAsync(_folder, includeSubfolders: false);

        counter.Count.Should().Be(1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private sealed class SupercarsScheduleQueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public void Reset() => Count = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("SELECT \"e\".\"Title\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("\"e\".\"Round\"", StringComparison.Ordinal))
            {
                Count++;
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
