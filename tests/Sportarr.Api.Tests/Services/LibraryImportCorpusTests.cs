using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Import;
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
/// failure. Recall may only rise from the recorded figure. A second pass
/// writes the aired date of the expected event into a sidecar beside each
/// file that another database named.
/// </summary>
public class LibraryImportCorpusTests : IDisposable
{
    // The floors that the corpus has reached. A change may raise them when
    // it lifts recall. No change may lower them.
    public const int RecordedRecallPercent = 76;
    public const int RecordedForeignHits = 134; // hits on files without an id token

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
        // A library scan reads the folders only under a configured root.
        _db.RootFolders.Add(new RootFolder { Path = _root });
        _db.SaveChanges();

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

    // The page pre-selects every match at 40 and above. The download
    // monitor and the library rescan auto-import at 85. So no mode may make
    // a wrong match at 40 or above.
    // Each mode runs in the natural path order and in the reverse, so a
    // result that depends on the order fails one of the two.
    [Theory]
    [InlineData(nameof(ScanOptions.DownloadFolder), false, false)]
    [InlineData(nameof(ScanOptions.DownloadFolder), false, true)]
    [InlineData(nameof(ScanOptions.DownloadFolder), true, false)]
    [InlineData(nameof(ScanOptions.DownloadFolder), true, true)]
    [InlineData(nameof(ScanOptions.LibraryBackground), false, false)]
    [InlineData(nameof(ScanOptions.LibraryBackground), false, true)]
    [InlineData(nameof(ScanOptions.LibraryBackground), true, false)]
    [InlineData(nameof(ScanOptions.LibraryBackground), true, true)]
    public async Task Corpus_NoWrongMatchAtTheImportPageFloor_AndRecallHolds(string mode, bool sidecars, bool reverse)
    {
        var options = (mode switch
        {
            nameof(ScanOptions.DownloadFolder) => ScanOptions.DownloadFolder,
            nameof(ScanOptions.LibraryBackground) => ScanOptions.LibraryBackground,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        }) with { ReversePathOrder = reverse };
        if (sidecars)
            WriteAiredSidecars().Should().BeGreaterThanOrEqualTo(20);

        var scan = await _service.ScanFolderAsync(_root, includeSubfolders: true, options: options);
        scan.Errors.Should().BeEmpty();
        var byPath = scan.MatchedFiles.Concat(scan.UnmatchedFiles).ToDictionary(f => f.FilePath);
        byPath.Should().HaveCount(_cases.Count, "the scan reports every corpus file once");
        var wrong = new List<string>();
        var wrongAtSixty = 0;
        int tokenHits = 0, tokenExpected = 0, foreignHits = 0, foreignExpected = 0;
        foreach (var c in _cases)
        {
            var f = byPath[Path.Combine(_root, c.RelativePath)];
            var matchedExternal = f.MatchedEventId is int id ? _db.Events.Find(id)!.ExternalId : null;
            var confidence = f.MatchConfidence ?? 0;
            if (c.ExpectedEventExternalId is not null)
            {
                var isHit = matchedExternal == c.ExpectedEventExternalId && confidence >= 75;
                if (HasIdToken(c)) { tokenExpected++; if (isHit) tokenHits++; }
                else { foreignExpected++; if (isHit) foreignHits++; }
            }
            if (matchedExternal is not null && matchedExternal != c.ExpectedEventExternalId && confidence >= 40)
            {
                wrong.Add($"{c.RelativePath} -> {f.MatchedEventTitle} ({confidence})");
                if (confidence >= 60) wrongAtSixty++;
            }
        }
        var hit = tokenHits + foreignHits;
        var expected = tokenExpected + foreignExpected;
        var recall = expected == 0 ? 100 : hit * 100 / expected;
        // Print before the asserts so a failing run still shows the figures.
        _output.WriteLine($"corpus {mode} sidecars={sidecars} reverse={reverse}: recall {recall}% ({hit}/{expected}), " +
            $"token {tokenHits}/{tokenExpected}, foreign {foreignHits}/{foreignExpected}, " +
            $"wrong at 40+ {wrong.Count}, wrong at 60+ {wrongAtSixty}");
        foreach (var line in wrong) _output.WriteLine($"wrong: {line}");
        wrong.Should().BeEmpty();
        recall.Should().BeGreaterThanOrEqualTo(RecordedRecallPercent);
        foreignHits.Should().BeGreaterThanOrEqualTo(RecordedForeignHits);
    }

    private static bool HasIdToken(CorpusCase c) =>
        Path.GetFileName(c.RelativePath).Contains("sportarr-ev-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes a sidecar with the aired date of the expected event beside
    /// each file named by another database. Returns the count.
    /// </summary>
    private int WriteAiredSidecars()
    {
        var written = 0;
        foreach (var c in _cases.Where(c => c.ExpectedEventExternalId != null &&
                     c.Source?.StartsWith("tvdb-", StringComparison.Ordinal) == true))
        {
            var evt = _db.Events.Single(e => e.ExternalId == c.ExpectedEventExternalId);
            var nfo = Path.ChangeExtension(Path.Combine(_root, c.RelativePath), ".nfo");
            File.WriteAllText(nfo,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<episodedetails>" +
                $"<aired>{evt.EventDate:yyyy-MM-dd}</aired></episodedetails>");
            written++;
        }
        return written;
    }
}
