using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Import;
using Sportarr.Api.Services.Interfaces;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The Library Import page asks the hub which event other installs matched
/// each file to. A hit on a library event matches the file before fuzzy
/// matching runs. Background scans never ask, and nothing leaves the app
/// when Send Anonymous Usage Data is off.
/// </summary>
// The timeout test measures real time, so these run away from other test load.
[Collection(WallClockFixtureCollection.Name)]
public class ScanMatchMemoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _root;
    private readonly string _outside;
    private readonly SportarrDbContext _db;
    private readonly ConfigService _config;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _http;
    private readonly MemoryCache _cache;
    private readonly HubHandler _hub = new();
    private readonly CaptureLogger _log = new();
    private readonly Dictionary<string, NfoSidecar> _sidecars = new();

    private Event _bahrain = null!;
    private Event _saudi = null!;
    private Event _ufc300 = null!;

    public ScanMatchMemoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sportarr-scan-match-memory-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_tempDir, "library");
        _outside = Path.Combine(_tempDir, "incoming");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sportarr:DataPath"] = Path.Combine(_tempDir, "data"),
            ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json",
        }).Build();
        _config = new ConfigService(_configuration, Mock.Of<ILogger<ConfigService>>());
        _db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _http = new HttpClient(_hub);
        _cache = new MemoryCache(new MemoryCacheOptions());
        Service = CreateService();
        Seed();
    }

    private LibraryImportService Service { get; }

    private LibraryImportService CreateService(TimeSpan? matchMemoryTimeout = null)
    {
        var fileParser = new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>());
        var api = new SportarrApiClient(_http, Mock.Of<ILogger<SportarrApiClient>>(), _configuration, _config, _cache);
        return new LibraryImportService(
            _db,
            _log,
            fileParser,
            new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
            new FileNamingService(Mock.Of<ILogger<FileNamingService>>()),
            new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()),
            _config,
            EpisodeResolverFixture.Create(_db, _config),
            new DiskSpaceService(Mock.Of<ILogger<DiskSpaceService>>()),
            new CustomFormatService(fileParser),
            new NotificationService(Mock.Of<IServiceProvider>(), Mock.Of<ILogger<NotificationService>>(), new HttpClient(), Mock.Of<IHttpClientFactory>()),
            Mock.Of<IMetadataWriterService>(),
            api)
        {
            SidecarReader = path => _sidecars.GetValueOrDefault(path),
            MatchMemoryTimeout = matchMemoryTimeout ?? TimeSpan.FromSeconds(5),
        };
    }

    public void Dispose()
    {
        _db.Dispose();
        _http.Dispose();
        _cache.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private void Seed()
    {
        var f1 = new League { Name = "Formula 1", Sport = "Motorsport", ExternalId = "lg-000278" };
        var ufc = new League { Name = "UFC", Sport = "Fighting", ExternalId = "lg-000079" };
        _db.Leagues.AddRange(f1, ufc);
        // A scan reads league folders only under a configured root.
        _db.RootFolders.Add(new RootFolder { Path = _root });
        _db.SaveChanges();

        _bahrain = new Event
        {
            Title = "Bahrain Grand Prix - Race", Sport = "Motorsport", LeagueId = f1.Id, Season = "2024",
            SeasonNumber = 2024, EpisodeNumber = 10, Round = "1", EventDate = new DateTime(2024, 3, 2, 15, 0, 0, DateTimeKind.Utc),
            ExternalId = "ev-100001", Status = "completed", Monitored = true,
        };
        _saudi = new Event
        {
            Title = "Saudi Arabian Grand Prix - Race", Sport = "Motorsport", LeagueId = f1.Id, Season = "2024",
            SeasonNumber = 2024, EpisodeNumber = 11, Round = "2", EventDate = new DateTime(2024, 3, 9, 17, 0, 0, DateTimeKind.Utc),
            ExternalId = "ev-100002", Status = "completed", Monitored = true,
        };
        _ufc300 = new Event
        {
            Title = "UFC 300 Pereira vs Hill", Sport = "Fighting", LeagueId = ufc.Id, Season = "2024",
            SeasonNumber = 2024, EpisodeNumber = 10, EventDate = new DateTime(2024, 4, 13, 22, 0, 0, DateTimeKind.Utc),
            ExternalId = "ev-200300", Status = "completed", Monitored = true,
        };
        _db.Events.AddRange(_bahrain, _saudi, _ufc300);
        _db.SaveChanges();
    }

    // A folder outside every root gets no folder league and no hub league search.
    private string WriteOutside(string name) => WriteAt(Path.Combine(_outside, name));

    private string WriteInLibrary(params string[] parts) =>
        WriteAt(Path.Combine(new[] { _root }.Concat(parts).ToArray()));

    private static string WriteAt(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[64 * 1024]);
        return path;
    }

    private static string StemOf(string path) => FileStemNormalizer.Normalize(Path.GetFileNameWithoutExtension(path));

    // The hub answers a stem with the event that other installs matched it to.
    private void HubKnows(string path, string eventId, string leagueId) =>
        _hub.Answers[StemOf(path)] = (eventId, leagueId);

    private async Task UsageData(bool on) => (await _config.GetConfigAsync()).SendAnonymousUsageData = on;

    private static ImportableFile Find(LibraryScanResult scan, string path) =>
        scan.MatchedFiles.Concat(scan.UnmatchedFiles).Single(f => f.FilePath == path);

    [Fact]
    public async Task AHitOnALibraryEventMatchesTheFileAt90()
    {
        await UsageData(true);
        var file = WriteOutside("zz clip 2024 0001.mkv");
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_outside, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.MatchedEventId.Should().Be(_bahrain.Id);
        found.MatchConfidence.Should().Be(90);
        found.Rejections.Should().BeEmpty();
        _hub.LookupStems.Should().ContainSingle().Which.Should().Equal(StemOf(file));
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Information && l.Message.Contains("1 applied"));
    }

    [Fact]
    public async Task AHitMarksTheFileAsMatchedByMemory()
    {
        await UsageData(true);
        var file = WriteOutside("zz clip 2024 0001.mkv");
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_outside, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.MatchedEventId.Should().Be(_bahrain.Id);
        found.MatchedByMemory.Should().BeTrue();
    }

    [Fact]
    public async Task AFuzzyMatchIsNotMarkedAsMatchedByMemory()
    {
        await UsageData(true);
        var file = WriteInLibrary("Formula 1", "Season 2024", "Formula 1 2024 Bahrain Grand Prix Race.mkv");
        HubKnows(file, "ev-200300", "lg-000079");

        var scan = await Service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.MatchedEventId.Should().Be(_bahrain.Id);
        found.MatchedByMemory.Should().BeFalse();
    }

    [Fact]
    public async Task AHitOnAnEventThatIsNotInTheLibraryIsIgnored()
    {
        await UsageData(true);
        var file = WriteOutside("zz clip 0001.mkv");
        HubKnows(file, "ev-999999", "lg-000278");

        var scan = await Service.ScanFolderAsync(_outside, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.MatchedEventId.Should().BeNull();
        found.MatchConfidence.Should().BeNull();
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Debug && l.Message.Contains("not in the library"));
    }

    [Fact]
    public async Task AHitInAnotherLeagueThanTheFolderIsIgnoredAndLogged()
    {
        await UsageData(true);
        var file = WriteInLibrary("Formula 1", "Season 2024", "zz clip 0001.mkv");
        HubKnows(file, "ev-200300", "lg-000079");

        var scan = await Service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.FolderLeagueId.Should().Be(_bahrain.LeagueId);
        found.MatchedEventId.Should().BeNull("the name holds nothing that fuzzy matching can use");
        found.MatchConfidence.Should().BeNull();
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Debug && l.Message.Contains("league"));
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Information && l.Message.Contains("1 in another league"));
    }

    [Fact]
    public async Task AHitInAnotherLeagueThanTheFolderFallsToFuzzyMatching()
    {
        await UsageData(true);
        var file = WriteInLibrary("Formula 1", "Season 2024", "Formula 1 2024 Bahrain Grand Prix Race.mkv");
        HubKnows(file, "ev-200300", "lg-000079");

        var scan = await Service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.MatchedEventId.Should().Be(_bahrain.Id, "fuzzy matching finds the race in the folder league");
        found.MatchConfidence.Should().NotBe(LibraryImportService.MatchMemoryConfidence);
    }

    [Fact]
    public async Task ALookupThatNeverAnswersIsCancelledAtTheTimeoutAndTheScanCompletes()
    {
        await UsageData(true);
        _hub.Hang = true;
        var service = CreateService(matchMemoryTimeout: TimeSpan.FromMilliseconds(200));
        var file = WriteInLibrary("Formula 1", "Season 2024", "Formula 1 2024 Bahrain Grand Prix Race.mkv");
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var scan = await service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        clock.Stop();
        _hub.SawCancellation.Should().BeTrue("the timeout cancels the request itself");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4), "the default timeout is 5 s, so this proves the 200 ms one");
        scan.Errors.Should().BeEmpty();
        var found = Find(scan, file);
        found.MatchedEventId.Should().Be(_bahrain.Id, "the scan falls back to fuzzy matching");
        found.MatchConfidence.Should().NotBe(LibraryImportService.MatchMemoryConfidence);
    }

    [Fact]
    public async Task AHitOnAnEventWithAnEmptySeasonAppliesInItsSeasonFolder()
    {
        // An empty Season string is no season, the same as null.
        await UsageData(true);
        var imola = new Event
        {
            Title = "Emilia Romagna Grand Prix - Race", Sport = "Motorsport", LeagueId = _bahrain.LeagueId,
            Season = "", SeasonNumber = 2024, EpisodeNumber = 30, Round = "7",
            EventDate = new DateTime(2024, 5, 19, 13, 0, 0, DateTimeKind.Utc),
            ExternalId = "ev-100007", Status = "completed", Monitored = true,
        };
        _db.Events.Add(imola);
        _db.SaveChanges();
        var file = WriteInLibrary("Formula 1", "Season 2024", "zz race clip 0007.mkv");
        HubKnows(file, "ev-100007", "lg-000278");

        var scan = await Service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.FolderSeason.Should().Be("2024");
        found.MatchedEventId.Should().Be(imola.Id);
        found.MatchConfidence.Should().Be(LibraryImportService.MatchMemoryConfidence);
    }

    [Fact]
    public async Task AFailedLookupLeavesTheFuzzyResults()
    {
        await UsageData(true);
        _hub.Throw = true;
        var file = WriteInLibrary("Formula 1", "Season 2024", "Formula 1 2024 Bahrain Grand Prix Race 1080p WEB-DL.mkv");

        var scan = await Service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        _hub.LookupStems.Should().ContainSingle("the scan asked, and the hub failed");
        scan.Errors.Should().BeEmpty();
        var found = Find(scan, file);
        found.MatchedEventId.Should().Be(_bahrain.Id);
        found.MatchConfidence.Should().NotBe(90);
    }

    [Fact]
    public async Task FourHundredFiftyFilesGoInThreeLookupRequests()
    {
        await UsageData(true);
        var files = Enumerable.Range(1, 450).Select(i => WriteOutside($"zz clip {i:D4}.mkv")).ToList();

        await Service.ScanFolderAsync(_outside, options: ScanOptions.Interactive);

        _hub.LookupStems.Select(s => s.Count).Should().Equal(200, 200, 50);
        _hub.LookupStems.SelectMany(s => s).Should().BeEquivalentTo(files.Select(StemOf));
    }

    [Fact]
    public async Task WithUsageDataOffTheScanMakesNoHttpRequestAtAll()
    {
        await UsageData(false);
        var file = WriteOutside("zz clip 0001.mkv");
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_outside, options: ScanOptions.Interactive);

        _hub.Requests.Should().Be(0);
        Find(scan, file).MatchedEventId.Should().BeNull();
    }

    [Fact]
    public async Task ABackgroundLibraryScanNeverAsks()
    {
        await UsageData(true);
        var file = WriteOutside("zz clip 0001.mkv");
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_outside, options: ScanOptions.LibraryBackground);

        _hub.LookupStems.Should().BeEmpty();
        Find(scan, file).MatchedEventId.Should().BeNull();
    }

    [Fact]
    public async Task AHitOnAnEventThatHasAFileIsJudgedByTheUpgradeRule()
    {
        await UsageData(true);
        _bahrain.HasFile = true;
        var heldPath = WriteInLibrary("Formula 1", "Season 2024", "Formula 1 - S2024E10 - Bahrain Grand Prix - Race - WEBDL-1080p.mkv");
        _db.EventFiles.Add(new EventFile
        {
            EventId = _bahrain.Id, FilePath = heldPath, Quality = "WEBDL-1080p", Exists = true,
            OriginalTitle = "Formula 1 - S2024E10 - Bahrain Grand Prix - Race - WEBDL-1080p",
        });
        _bahrain.FilePath = heldPath;
        _db.SaveChanges();
        var file = WriteOutside("zz clip 2024 0001 HDTV-720p.mkv");
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_outside, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.MatchedEventId.Should().Be(_bahrain.Id);
        found.MatchConfidence.Should().Be(90);
        found.Rejections.Should().ContainSingle().Which.Should().Contain("Not an upgrade");
    }

    [Fact]
    public async Task AFileWithASidecarIdIsNotLookedUp()
    {
        await UsageData(true);
        var withId = WriteOutside("zz clip 0001.mkv");
        var without = WriteOutside("zz clip 0002.mkv");
        _sidecars[withId] = new NfoSidecar(null, null, "ev-100002");

        var scan = await Service.ScanFolderAsync(_outside, options: ScanOptions.Interactive);

        _hub.LookupStems.Should().ContainSingle().Which.Should().Equal(StemOf(without));
        Find(scan, withId).MatchedEventId.Should().Be(_saudi.Id);
    }

    [Fact]
    public async Task AHitOnAnEventThatAnIdTokenFileClaimedIsIgnored()
    {
        await UsageData(true);
        var tokened = WriteOutside("Bahrain race sportarr-ev-100001.mkv");
        var other = WriteOutside("zz clip 0001.mkv");
        HubKnows(other, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_outside, options: ScanOptions.Interactive);

        _hub.LookupStems.Should().ContainSingle().Which.Should().Equal(StemOf(other));
        Find(scan, tokened).MatchedEventId.Should().Be(_bahrain.Id);
        Find(scan, other).MatchedEventId.Should().NotBe(_bahrain.Id);
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Information && l.Message.Contains("1 already claimed"));
    }

    [Fact]
    public async Task AnIgnoredFileIsListedButItsStemIsNotSent()
    {
        // The Library Import page lists ignored files, so it scans with them.
        await UsageData(true);
        var ignored = WriteOutside("zz clip 0001.mkv");
        var other = WriteOutside("zz clip 0002.mkv");
        _db.Blocklist.Add(new BlocklistItem
        {
            Title = "zz clip 0001", FilePath = ignored, Reason = BlocklistReason.ManualBlock,
        });
        _db.SaveChanges();

        var scan = await Service.ScanFolderAsync(_outside, includeIgnoredFiles: true, options: ScanOptions.Interactive);

        Find(scan, ignored).Should().NotBeNull("the page still lists the ignored file");
        _hub.LookupStems.Should().ContainSingle().Which.Should().Equal(StemOf(other));
    }

    private Event AddBahrain2025()
    {
        var race = new Event
        {
            Title = "Bahrain Grand Prix - Race", Sport = "Motorsport", LeagueId = _bahrain.LeagueId, Season = "2025",
            SeasonNumber = 2025, EpisodeNumber = 10, Round = "1", EventDate = new DateTime(2025, 4, 13, 15, 0, 0, DateTimeKind.Utc),
            ExternalId = "ev-110001", Status = "completed", Monitored = true,
        };
        _db.Events.Add(race);
        _db.SaveChanges();
        return race;
    }

    [Fact]
    public async Task AHitFromAnotherSeasonThanTheFolderFallsToFuzzyMatching()
    {
        await UsageData(true);
        var bahrain2025 = AddBahrain2025();
        var file = WriteInLibrary("Formula 1", "Season 2025", "Bahrain Grand Prix Race.mkv");
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.FolderSeason.Should().Be("2025");
        found.MatchedEventId.Should().Be(bahrain2025.Id, "fuzzy matching finds the race of the folder season");
        found.MatchConfidence.Should().NotBe(90);
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Information && l.Message.Contains("1 disagree on season, year or session"));
    }

    [Fact]
    public async Task AHitThatOnlyTheFolderSeasonRejectsIsVetoed()
    {
        await UsageData(true);
        AddBahrain2025();
        // The name says 2024, so only the folder season disagrees with the hit.
        var file = WriteInLibrary("Formula 1", "Season 2025", "zz clip 2024 0001.mkv");
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        Find(scan, file).MatchedEventId.Should().NotBe(_bahrain.Id);
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Debug && l.Message.Contains("Match memory") && l.Message.Contains("season 2025"));
    }

    [Fact]
    public async Task AHitFromAnotherYearThanThePathIsVetoed()
    {
        await UsageData(true);
        var file = WriteOutside(Path.Combine("2023", "zz clip 0001.mkv"));
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_outside, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.FolderLeagueId.Should().BeNull();
        found.MatchedEventId.Should().NotBe(_bahrain.Id);
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Debug && l.Message.Contains("Match memory") && l.Message.Contains("2023"));
    }

    [Fact]
    public async Task AHitOnAnotherSessionThanTheFileNamesIsVetoed()
    {
        await UsageData(true);
        var file = WriteOutside("zz Bahrain Qualifying 0001.mkv");
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_outside, options: ScanOptions.Interactive);

        Find(scan, file).MatchedEventId.Should().NotBe(_bahrain.Id);
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Debug && l.Message.Contains("Match memory") && l.Message.Contains("session"));
    }

    [Fact]
    public async Task AHitThatAgreesOnSeasonYearAndSessionApplies()
    {
        await UsageData(true);
        AddBahrain2025();
        var file = WriteInLibrary("Formula 1", "Season 2024", "zz race clip 0001.mkv");
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.FolderSeason.Should().Be("2024");
        found.MatchedEventId.Should().Be(_bahrain.Id);
        found.MatchConfidence.Should().Be(90);
    }

    // This name gives the same stem every year, so a hit can name the event of another year.
    private const string YearlessName = "Bahrain Grand Prix - Race 1080p.mkv";

    [Fact]
    public async Task AYearlessNameInAFlatFolderDoesNotTakeTheHit()
    {
        await UsageData(true);
        AddBahrain2025();
        var file = WriteInLibrary("Formula 1", YearlessName);
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.FolderSeason.Should().BeNull();
        found.MatchConfidence.Should().NotBe(90);
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Debug && l.Message.Contains("Match memory") && l.Message.Contains("names no year"));
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Information && l.Message.Contains("1 name no year"));
    }

    [Fact]
    public async Task AYearlessNameOutsideTheRootsDoesNotTakeTheHit()
    {
        await UsageData(true);
        var file = WriteOutside(YearlessName);
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_outside, options: ScanOptions.Interactive);

        Find(scan, file).MatchConfidence.Should().NotBe(90);
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Information && l.Message.Contains("1 name no year"));
    }

    [Fact]
    public async Task AYearlessNameInAnotherSeasonFolderGetsTheSeasonVeto()
    {
        await UsageData(true);
        var bahrain2025 = AddBahrain2025();
        var file = WriteInLibrary("Formula 1", "Season 2025", YearlessName);
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.MatchedEventId.Should().Be(bahrain2025.Id, "fuzzy matching finds the race of the folder season");
        found.MatchConfidence.Should().NotBe(90);
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Information && l.Message.Contains("1 disagree on season, year or session"));
    }

    [Fact]
    public async Task AYearlessNameInTheRightSeasonFolderTakesTheHit()
    {
        await UsageData(true);
        AddBahrain2025();
        var file = WriteInLibrary("Formula 1", "Season 2024", YearlessName);
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.MatchedEventId.Should().Be(_bahrain.Id);
        found.MatchConfidence.Should().Be(90);
    }

    [Fact]
    public async Task AYearlessNameWithASidecarAiredDateInTheRightYearTakesTheHit()
    {
        await UsageData(true);
        AddBahrain2025();
        var file = WriteInLibrary("Formula 1", YearlessName);
        _sidecars[file] = new NfoSidecar(new DateTime(2024, 3, 2), null, null);
        HubKnows(file, "ev-100001", "lg-000278");

        var scan = await Service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var found = Find(scan, file);
        found.MatchedEventId.Should().Be(_bahrain.Id);
        found.MatchConfidence.Should().Be(90);
    }

    [Fact]
    public async Task NoLookupLineGoesAboveDebugPerStem()
    {
        await UsageData(true);
        var files = Enumerable.Range(1, 3).Select(i => WriteOutside($"zz clip 2024 {i:D4}.mkv")).ToList();
        HubKnows(files[0], "ev-100001", "lg-000278");
        HubKnows(files[1], "ev-999999", "lg-000278");

        await Service.ScanFolderAsync(_outside, options: ScanOptions.Interactive);

        var summary = _log.Lines.Where(l => l.Level >= LogLevel.Information && l.Message.Contains("match memory")).ToList();
        summary.Should().ContainSingle().Which.Message.Should()
            .Contain("3 stems").And.Contain("2 hits").And.Contain("1 applied").And.Contain("1 not in the library");
    }

    private sealed class HubHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private int _requests;
        public Dictionary<string, (string EventId, string LeagueId)> Answers { get; } = new(StringComparer.Ordinal);
        public List<List<string>> LookupStems { get; } = new();
        public bool Throw { get; set; }
        // The lookup waits until its token cancels it.
        public bool Hang { get; set; }
        public bool SawCancellation { get; private set; }
        public int Requests => Volatile.Read(ref _requests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            if (!request.RequestUri!.AbsolutePath.EndsWith("/api/metadata/match/by-name", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var stems = JsonDocument.Parse(body).RootElement.GetProperty("stems")
                .EnumerateArray().Select(e => e.GetString()!).ToList();
            lock (_gate)
                LookupStems.Add(stems);
            if (Throw)
                throw new HttpRequestException("hub down");
            if (Hang)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    SawCancellation = true;
                    throw;
                }
            }

            var matches = stems
                .Where(Answers.ContainsKey)
                .ToDictionary(s => s, s => new { eventId = Answers[s].EventId, leagueId = Answers[s].LeagueId, installs = 3 });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { matches }), Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class CaptureLogger : ILogger<LibraryImportService>
    {
        private readonly object _gate = new();
        private readonly List<(LogLevel Level, string Message)> _lines = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Lines
        {
            get { lock (_gate) return _lines.ToList(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
                _lines.Add((logLevel, formatter(state, exception)));
        }
    }
}
