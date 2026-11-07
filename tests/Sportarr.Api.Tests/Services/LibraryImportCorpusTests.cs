using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;
using Sportarr.Api.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Scans a library built from real file names: TV-style names from another
/// database, the production library, and names from issue reports. Every
/// event of each corpus season is in the library, so a file that names its
/// event weakly meets many decoys. A confident match to the wrong event is a
/// failure. Recall may only rise from the recorded figure.
/// </summary>
public class LibraryImportCorpusTests : IDisposable
{
    public const int RecordedRecallPercent = 54; // raised by each task, never lowered

    private readonly ITestOutputHelper _output;
    private readonly string _root;
    private readonly SportarrDbContext _db;
    private readonly LibraryImportService _service;
    private readonly IReadOnlyList<CorpusCase> _cases;

    public LibraryImportCorpusTests(ITestOutputHelper output)
    {
        _output = output;
        var fixturesDir = Path.Combine(
            Path.GetDirectoryName(typeof(LibraryImportCorpusTests).Assembly.Location)!,
            "Services", "Fixtures", "LibraryImportCorpus");
        _cases = CorpusLibraryBuilder.ReadCases(fixturesDir);

        _root = Path.Combine(Path.GetTempPath(), "sportarr-import-corpus-tests-" + Guid.NewGuid());
        foreach (var c in _cases)
        {
            var path = Path.Combine(_root, c.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Array.Empty<byte>());
        }

        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new SportarrDbContext(options);
        CorpusLibraryBuilder.Seed(_db, fixturesDir);

        var fileParser = new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>());
        var config = new ConfigService(new ConfigurationBuilder().Build(), Mock.Of<ILogger<ConfigService>>());
        _service = new LibraryImportService(
            _db,
            Mock.Of<ILogger<LibraryImportService>>(),
            fileParser,
            new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
            new FileNamingService(Mock.Of<ILogger<FileNamingService>>()),
            new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()),
            config,
            EpisodeResolverFixture.Create(_db, config),
            new DiskSpaceService(Mock.Of<ILogger<DiskSpaceService>>()),
            new CustomFormatService(fileParser),
            null!,
            Mock.Of<IMetadataWriterService>());
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Corpus_NoWrongMatchAtSixtyOrAbove_AndRecallHolds()
    {
        var scan = await _service.ScanFolderAsync(_root, includeSubfolders: true);
        scan.Errors.Should().BeEmpty();
        var byPath = scan.MatchedFiles.Concat(scan.UnmatchedFiles).ToDictionary(f => f.FilePath);
        byPath.Should().HaveCount(_cases.Count, "the scan reports every corpus file once");
        var wrong = new List<string>();
        var hit = 0; var expected = 0;
        foreach (var c in _cases)
        {
            var f = byPath[Path.Combine(_root, c.RelativePath)];
            var matchedExternal = f.MatchedEventId is int id ? _db.Events.Find(id)!.ExternalId : null;
            if (c.ExpectedEventExternalId is not null)
            {
                expected++;
                if (matchedExternal == c.ExpectedEventExternalId && (f.MatchConfidence ?? 0) >= 75) hit++;
            }
            // A known wrong case is the baseline of the task that fixes it.
            if (!c.KnownWrong && matchedExternal is not null && matchedExternal != c.ExpectedEventExternalId && (f.MatchConfidence ?? 0) >= 60)
                wrong.Add($"{c.RelativePath} -> {f.MatchedEventTitle} ({f.MatchConfidence})");
        }
        var recall = expected == 0 ? 100 : hit * 100 / expected;
        // Print before the asserts so a failing run still shows the figures.
        _output.WriteLine($"corpus recall {recall}% ({hit}/{expected})");
        foreach (var line in wrong) _output.WriteLine($"wrong: {line}");
        wrong.Should().BeEmpty();
        recall.Should().BeGreaterThanOrEqualTo(RecordedRecallPercent);
    }
}
