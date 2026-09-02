using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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

/// <summary>
/// A file for an event that already has one is judged by the shared upgrade
/// rule on every path. A library scan shows the rejection, an automatic
/// import leaves a rejected copy where it is, and a manual import of a
/// better copy that already sits in the league folder takes over in place
/// and leaves the old file on disk untracked.
/// </summary>
public class ImportUpgradeBehaviourTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SportarrDbContext _db;
    private readonly LibraryImportService _service;

    public ImportUpgradeBehaviourTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sportarr-import-upgrade-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_tempDir, "NFL", "Season 2025"));
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
            new NotificationService(Mock.Of<IServiceProvider>(), Mock.Of<ILogger<NotificationService>>(), new HttpClient(), Mock.Of<IHttpClientFactory>()),
            Mock.Of<IMetadataWriterService>());
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private string SeasonDir => Path.Combine(_tempDir, "NFL", "Season 2025");

    private (Event evt, EventFile held) SeedEventWithFile()
    {
        var nfl = new League { Name = "NFL", Sport = "American Football", ExternalId = "lg-000032" };
        _db.Leagues.Add(nfl);
        _db.SaveChanges();
        var evt = new Event
        {
            Title = "Carolina Panthers vs Cleveland Browns", Sport = "American Football", Season = "2025",
            EventDate = new DateTime(2025, 8, 8), LeagueId = nfl.Id, ExternalId = "ev-312923",
            SeasonNumber = 2025, EpisodeNumber = 6, Status = "completed", HasFile = true, Monitored = true,
        };
        _db.Events.Add(evt);
        _db.SaveChanges();
        var heldPath = Write("NFL - S2025E06 - Carolina Panthers vs Cleveland Browns - WEBDL-1080p.mkv");
        var held = new EventFile
        {
            EventId = evt.Id, FilePath = heldPath, Quality = "WEBDL-1080p", Exists = true,
            OriginalTitle = "NFL - S2025E06 - Carolina Panthers vs Cleveland Browns - WEBDL-1080p",
        };
        _db.EventFiles.Add(held);
        evt.FilePath = heldPath;
        _db.SaveChanges();
        return (evt, held);
    }

    private string Write(string name)
    {
        var path = Path.Combine(SeasonDir, name);
        File.WriteAllBytes(path, new byte[64 * 1024]);
        return path;
    }

    [Fact]
    public async Task TheScanShowsWhyAWorseCopyWouldNotReplaceTheHeldFile()
    {
        SeedEventWithFile();
        Write("NFL - S2025E06 - Second Copy - HDTV-720p - sportarr-ev-312923.mkv");

        var result = await _service.ScanFolderAsync(SeasonDir, includeSubfolders: false);

        var copy = result.MatchedFiles.Should().ContainSingle(f => f.FileName.Contains("Second Copy")).Subject;
        copy.MatchedEventId.Should().NotBeNull();
        copy.Rejections.Should().ContainSingle().Which.Should().Contain("Not an upgrade");
    }

    [Fact]
    public async Task AnAutomaticImportLeavesAWorseCopyWhereItIs()
    {
        var (evt, held) = SeedEventWithFile();
        var copy = Write("NFL - S2025E06 - Second Copy - HDTV-720p - sportarr-ev-312923.mkv");

        var result = await _service.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = copy, EventId = evt.Id, OnlyIfUpgrade = true },
        });

        result.Imported.Should().BeEmpty();
        result.Rejected.Should().ContainSingle().Which.Reason.Should().Contain("Not an upgrade");
        File.Exists(copy).Should().BeTrue("a rejected copy is left where it is");
        File.Exists(held.FilePath).Should().BeTrue();
        _db.EventFiles.Single(f => f.EventId == evt.Id).FilePath.Should().Be(held.FilePath);
    }

    [Fact]
    public async Task AnEqualCopyIsListedForReviewInsteadOfTakingOver()
    {
        var (evt, held) = SeedEventWithFile();
        var copy = Write("NFL - S2025E06 - Equal Copy - WEBDL-1080p - sportarr-ev-312923.mkv");

        var scan = await _service.ScanFolderAsync(SeasonDir, includeSubfolders: false);
        var listed = scan.MatchedFiles.Should().ContainSingle(f => f.FileName.Contains("Equal Copy")).Subject;
        listed.Rejections.Should().ContainSingle().Which.Should().Contain("already has a file as good as this one");

        var result = await _service.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = copy, EventId = evt.Id, OnlyIfUpgrade = true },
        });

        result.Imported.Should().BeEmpty();
        result.Rejected.Should().ContainSingle().Which.Reason.Should().Contain("already has a file as good as this one");
        File.Exists(copy).Should().BeTrue("the user decides what happens to it");
        _db.EventFiles.Single(f => f.EventId == evt.Id).FilePath.Should().Be(held.FilePath);
    }

    [Fact]
    public async Task AManualImportOfABetterCopyInTheLeagueFolderTakesOverAndLeavesTheOldFile()
    {
        var (evt, held) = SeedEventWithFile();
        var copy = Write("NFL - S2025E06 - Better Copy - WEBDL-2160p - sportarr-ev-312923.mkv");

        var result = await _service.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = copy, EventId = evt.Id },
        });

        result.Imported.Should().ContainSingle().Which.Should().Be(copy, "a copy already in the league folder is imported in place");
        File.Exists(held.FilePath).Should().BeTrue("the file it replaces stays on disk");
        File.Exists(copy).Should().BeTrue();
        var files = _db.EventFiles.Where(f => f.EventId == evt.Id).ToList();
        files.Should().ContainSingle().Which.FilePath.Should().Be(copy);
    }
}
