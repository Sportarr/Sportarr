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

    private LibraryImportService CreateService()
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
        var file = WriteOutside("zz clip 0001.mkv");
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
        found.MatchedEventId.Should().NotBe(_ufc300.Id);
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Debug && l.Message.Contains("league"));
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Information && l.Message.Contains("1 in another league"));
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
        var file = WriteOutside("zz clip 0001 HDTV-720p.mkv");
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
    public async Task NoLookupLineGoesAboveDebugPerStem()
    {
        await UsageData(true);
        var files = Enumerable.Range(1, 3).Select(i => WriteOutside($"zz clip {i:D4}.mkv")).ToList();
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
