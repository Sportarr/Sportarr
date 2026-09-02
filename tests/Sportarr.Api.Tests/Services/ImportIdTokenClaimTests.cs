using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The Sportarr id in a file name is exact: a library scan matches the
/// file to that event every time, whether or not the event already has
/// a file. A second copy of a game is still that game, and the import
/// decides between the copies. Before, an already-filed event was treated
/// as unavailable and the copy fell to a fuzzy match on another game,
/// which then got that game's nfo (seen live on the rig).
/// </summary>
public class ImportIdTokenClaimTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SportarrDbContext _db;
    private readonly LibraryImportService _service;

    public ImportIdTokenClaimTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sportarr-import-token-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new SportarrDbContext(options);
        var fileParser = new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>());
        _service = new LibraryImportService(
            _db,
            Mock.Of<ILogger<LibraryImportService>>(),
            fileParser,
            new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
            new FileNamingService(Mock.Of<ILogger<FileNamingService>>()),
            new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()),
            new ConfigService(new ConfigurationBuilder().Build(), Mock.Of<ILogger<ConfigService>>()),
            null!,
            new DiskSpaceService(Mock.Of<ILogger<DiskSpaceService>>()),
            new CustomFormatService(fileParser),
            null!,
            Mock.Of<IMetadataWriterService>());
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private (Event filed, Event other) Seed(bool filedHasFile)
    {
        var nfl = new League { Name = "NFL", Sport = "American Football", ExternalId = "lg-000032" };
        _db.Leagues.Add(nfl);
        _db.SaveChanges();
        var filed = new Event
        {
            Title = "Carolina Panthers vs Cleveland Browns", Sport = "American Football", Season = "2025",
            EventDate = new DateTime(2025, 8, 8), LeagueId = nfl.Id, ExternalId = "ev-312923",
            SeasonNumber = 2025, EpisodeNumber = 6, Status = "completed", HasFile = filedHasFile, Monitored = true,
        };
        var other = new Event
        {
            Title = "Baltimore Ravens vs Detroit Lions", Sport = "American Football", Season = "2025",
            EventDate = new DateTime(2025, 9, 22), LeagueId = nfl.Id, ExternalId = "ev-313015",
            SeasonNumber = 2025, EpisodeNumber = 97, Status = "completed", HasFile = false, Monitored = true,
        };
        _db.Events.AddRange(filed, other);
        _db.SaveChanges();
        return (filed, other);
    }

    private string WriteFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[64 * 1024]);
        return path;
    }

    [Fact]
    public async Task AFileWhoseIdNamesAnAlreadyFiledEventStillMatchesThatEvent()
    {
        var (filed, other) = Seed(filedHasFile: true);
        WriteFile("NFL - S2025E97 - Some Copy - sportarr-ev-312923.mkv");

        var result = await _service.ScanFolderAsync(_tempDir, includeSubfolders: false);

        var match = result.MatchedFiles.Should().ContainSingle().Subject;
        match.MatchedEventId.Should().Be(filed.Id, "the id names that game, filed or not; no other game may take the copy");
        match.MatchedEventId.Should().NotBe(other.Id);
        match.MatchConfidence.Should().Be(100);
        result.UnmatchedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task AFileWhoseIdNamesAnOpenEventMatchesItExactly()
    {
        var (filed, _) = Seed(filedHasFile: false);
        WriteFile("NFL - S2025E97 - Some Copy - sportarr-ev-312923.mkv");

        var result = await _service.ScanFolderAsync(_tempDir, includeSubfolders: false);

        var match = result.MatchedFiles.Should().ContainSingle().Subject;
        match.MatchedEventId.Should().Be(filed.Id);
        match.MatchConfidence.Should().Be(100);
        result.UnmatchedFiles.Should().BeEmpty();
    }
}
