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
/// An import that the user chose reports its file matches once per batch,
/// after the save. Nothing leaves the app when Send Anonymous Usage Data
/// is off, and an automatic import never reports.
/// </summary>
public class ImportSubmitsMatchMemoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _incoming;
    private readonly SportarrDbContext _db;
    private readonly ConfigService _config;
    private readonly HttpClient _http;
    private readonly MemoryCache _cache;
    private readonly RecordingHandler _handler = new();
    private NfoSidecar? _sidecar;
    private readonly Dictionary<string, string> _embeddedIds = new();

    public ImportSubmitsMatchMemoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sportarr-match-memory-tests-" + Guid.NewGuid().ToString("N"));
        _incoming = Path.Combine(_tempDir, "incoming");
        Directory.CreateDirectory(_incoming);
        Directory.CreateDirectory(Path.Combine(_tempDir, "library"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sportarr:DataPath"] = Path.Combine(_tempDir, "data"),
            ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json",
        }).Build();
        _config = new ConfigService(configuration, Mock.Of<ILogger<ConfigService>>());
        _db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _http = new HttpClient(_handler);
        _cache = new MemoryCache(new MemoryCacheOptions());
        Service = CreateService(configuration);
    }

    private LibraryImportService Service { get; }

    private LibraryImportService CreateService(IConfiguration configuration)
    {
        // The probe stands in for ffprobe. It gives the embedded tag that a test sets.
        var inspector = new Mock<MediaFileInspector>(Mock.Of<ILogger<MediaFileInspector>>());
        inspector.Setup(i => i.InspectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) =>
                new MediaFileInspection { FilePath = path, SportarrId = _embeddedIds.GetValueOrDefault(path) });
        var fileParser = new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>(), inspector.Object);
        var api = new SportarrApiClient(_http, Mock.Of<ILogger<SportarrApiClient>>(), configuration, _config, _cache);
        return new LibraryImportService(
            _db,
            Mock.Of<ILogger<LibraryImportService>>(),
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
            SidecarReader = _ => _sidecar,
        };
    }

    public void Dispose()
    {
        _db.Dispose();
        _http.Dispose();
        _cache.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private (Event first, Event second) Seed()
    {
        var root = new RootFolder { Path = Path.Combine(_tempDir, "library") };
        _db.RootFolders.Add(root);
        _db.SaveChanges();
        var nfl = new League { Name = "NFL", Sport = "American Football", ExternalId = "lg-000032", RootFolderId = root.Id };
        _db.Leagues.Add(nfl);
        _db.SaveChanges();
        var first = new Event
        {
            Title = "Carolina Panthers vs Cleveland Browns", Sport = "American Football", Season = "2025",
            EventDate = new DateTime(2025, 8, 8), LeagueId = nfl.Id, ExternalId = "ev-312923",
            SeasonNumber = 2025, EpisodeNumber = 6, Status = "completed", Monitored = true,
        };
        var second = new Event
        {
            Title = "Baltimore Ravens vs Detroit Lions", Sport = "American Football", Season = "2025",
            EventDate = new DateTime(2025, 9, 22), LeagueId = nfl.Id, ExternalId = "ev-313015",
            SeasonNumber = 2025, EpisodeNumber = 97, Status = "completed", Monitored = true,
        };
        _db.Events.AddRange(first, second);
        _db.SaveChanges();
        return (first, second);
    }

    private string Write(string name)
    {
        var path = Path.Combine(_incoming, name);
        File.WriteAllBytes(path, new byte[64 * 1024]);
        return path;
    }

    private async Task<List<JsonElement>> SentSuggestions()
    {
        if (Service.LastMatchReport != null)
            await Service.LastMatchReport;
        return _handler.Bodies
            .SelectMany(body => JsonDocument.Parse(body).RootElement.GetProperty("suggestions").EnumerateArray())
            .ToList();
    }

    private async Task UsageData(bool on) => (await _config.GetConfigAsync()).SendAnonymousUsageData = on;

    [Fact]
    public async Task APageImportOfTwoFilesSendsOneBatchWithBothMatches()
    {
        await UsageData(true);
        var (first, second) = Seed();
        var kept = Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");
        var picked = Write("Ravens Lions highlights.mkv");

        var result = await Service.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = kept, EventId = first.Id, SuggestedEventId = first.Id, ReportMatch = true, ImportMode = "copy" },
            new() { FilePath = picked, EventId = second.Id, SuggestedEventId = null, ReportMatch = true, ImportMode = "copy" },
        });

        result.Imported.Should().HaveCount(2);
        var sent = await SentSuggestions();
        _handler.Uris.Should().ContainSingle()
            .Which.AbsoluteUri.Should().Be("https://metadata.invalid/api/metadata/suggestions/file-matches");
        sent.Should().HaveCount(2);
        sent[0].GetProperty("eventId").GetString().Should().Be("ev-312923");
        sent[0].GetProperty("leagueId").GetString().Should().Be("lg-000032");
        sent[0].GetProperty("stemDisplay").GetString().Should().Be("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP");
        sent[0].GetProperty("season").GetString().Should().Be("2025");
        sent[0].GetProperty("kind").GetString().Should().Be("confirmed");
        sent[1].GetProperty("eventId").GetString().Should().Be("ev-313015");
        sent[1].GetProperty("kind").GetString().Should().Be("manual", "the user mapped an unmatched file by hand");
        var body = JsonDocument.Parse(_handler.Bodies.Single()).RootElement;
        body.GetProperty("installId").GetString().Should().Be((await _config.GetConfigAsync()).AnalyticsInstanceId);
    }

    [Fact]
    public async Task APageImportOfAnotherEventThanTheSuggestionIsManual()
    {
        await UsageData(true);
        var (first, second) = Seed();
        var file = Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");

        await Service.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = file, EventId = second.Id, SuggestedEventId = first.Id, ReportMatch = true, ImportMode = "copy" },
        });

        (await SentSuggestions()).Should().ContainSingle().Which.GetProperty("kind").GetString().Should().Be("manual");
    }

    [Fact]
    public async Task WithUsageDataOffNothingIsSent()
    {
        await UsageData(false);
        var (first, second) = Seed();
        var kept = Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");
        var picked = Write("Ravens Lions highlights.mkv");

        var result = await Service.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = kept, EventId = first.Id, SuggestedEventId = first.Id, ReportMatch = true, ImportMode = "copy" },
            new() { FilePath = picked, EventId = second.Id, ReportMatch = true, ImportMode = "copy" },
        });

        result.Imported.Should().HaveCount(2);
        Service.LastMatchReport.Should().BeNull();
        _handler.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAutomaticUpgradeOnlyImportNeverReports()
    {
        await UsageData(true);
        var (first, _) = Seed();
        var file = Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");

        var result = await Service.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = file, EventId = first.Id, OnlyIfUpgrade = true, ImportMode = "copy" },
        });

        result.Imported.Should().ContainSingle();
        Service.LastMatchReport.Should().BeNull();
        _handler.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task AFileMatchedByItsSidecarIdNeverReports()
    {
        await UsageData(true);
        var (first, _) = Seed();
        var file = Write("Panthers Browns.mkv");
        _sidecar = new NfoSidecar(null, "Carolina Panthers vs Cleveland Browns", "ev-312923");

        var result = await Service.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = file, EventId = first.Id, SuggestedEventId = first.Id, ReportMatch = true, ImportMode = "copy" },
        });

        result.Imported.Should().ContainSingle();
        _handler.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task AFileNamedWithASportarrIdNeverReports()
    {
        await UsageData(true);
        var (first, _) = Seed();
        var file = Write("NFL - S2025E06 - Panthers vs Browns - sportarr-ev-312923.mkv");

        var result = await Service.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = file, EventId = first.Id, SuggestedEventId = first.Id, ReportMatch = true, ImportMode = "copy" },
        });

        result.Imported.Should().ContainSingle();
        _handler.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedImportIsNotReported()
    {
        await UsageData(true);
        var (first, _) = Seed();

        var result = await Service.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = Path.Combine(_incoming, "missing.mkv"), EventId = first.Id, ReportMatch = true },
        });

        result.Failed.Should().ContainSingle();
        _handler.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAcceptedPendingImportTheUserEditedReportsManual()
    {
        await UsageData(true);
        var (first, _) = Seed();
        var file = Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");
        var pending = new PendingImport
        {
            DownloadId = "disk-1", Title = "NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP", FilePath = file,
            SuggestedEventId = first.Id, SuggestionConfidence = FileMatchSuggestionCollector.UserChoiceConfidence,
        };

        await Service.ImportFilesAsync(new List<FileImportRequest>
        {
            new()
            {
                FilePath = file, EventId = first.Id, ReportMatch = true, ImportMode = "copy",
                SuggestedEventId = FileMatchSuggestionCollector.SuggestionBeforeUserChoice(pending),
            },
        });

        (await SentSuggestions()).Should().ContainSingle().Which.GetProperty("kind").GetString().Should().Be("manual");
    }

    [Fact]
    public async Task AnAcceptedPendingImportLeftUnchangedReportsConfirmed()
    {
        await UsageData(true);
        var (first, _) = Seed();
        var file = Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");
        var pending = new PendingImport
        {
            DownloadId = "disk-1", Title = "NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP", FilePath = file,
            SuggestedEventId = first.Id, SuggestionConfidence = 72,
        };

        await Service.ImportFilesAsync(new List<FileImportRequest>
        {
            new()
            {
                FilePath = file, EventId = first.Id, ReportMatch = true, ImportMode = "copy",
                SuggestedEventId = FileMatchSuggestionCollector.SuggestionBeforeUserChoice(pending),
            },
        });

        (await SentSuggestions()).Should().ContainSingle().Which.GetProperty("kind").GetString().Should().Be("confirmed");
    }

    [Fact]
    public async Task ReportFileMatchesSendsNothingWhenUsageDataIsOff()
    {
        await UsageData(false);

        await Service.ReportFileMatchesAsync(new[]
        {
            new FileMatchSuggestion("lg-000032", "ev-312923", "panthers browns", "Panthers Browns", "2025", "manual"),
        });

        Service.LastMatchReport.Should().BeNull();
        _handler.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task AFileMatchedByItsEmbeddedTagNeverReports()
    {
        await UsageData(true);
        var (first, _) = Seed();
        var file = Write("Panthers Browns.mkv");
        _embeddedIds[file] = "ev-312923";

        var result = await Service.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = file, EventId = first.Id, SuggestedEventId = first.Id, ReportMatch = true, ImportMode = "copy" },
        });

        result.Imported.Should().ContainSingle();
        _handler.Bodies.Should().BeEmpty();
    }

    private PendingImport Pending(string file, int eventId, int confidence) => new()
    {
        DownloadId = "disk-1", Title = Path.GetFileNameWithoutExtension(file), FilePath = file,
        SuggestedEventId = eventId, SuggestionConfidence = confidence,
    };

    [Fact]
    public async Task AnAcceptAtTheUserChoiceConfidenceReportsManual()
    {
        await UsageData(true);
        var (first, _) = Seed();
        var file = Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");

        await Service.ReportAcceptedPendingImportAsync(
            Pending(file, first.Id, FileMatchSuggestionCollector.UserChoiceConfidence), file, file, null);

        var sent = (await SentSuggestions()).Should().ContainSingle().Subject;
        sent.GetProperty("kind").GetString().Should().Be("manual");
        sent.GetProperty("eventId").GetString().Should().Be("ev-312923");
    }

    [Fact]
    public async Task AnAcceptOfAnAutomaticHundredReportsConfirmed()
    {
        await UsageData(true);
        var (first, _) = Seed();
        var file = Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");

        await Service.ReportAcceptedPendingImportAsync(Pending(file, first.Id, 100), file, file, null);

        (await SentSuggestions()).Should().ContainSingle().Which.GetProperty("kind").GetString().Should().Be("confirmed");
    }

    [Fact]
    public async Task AnAcceptedFileWithAnEmbeddedTagNeverReports()
    {
        await UsageData(true);
        var (first, _) = Seed();
        var source = Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");
        var imported = Path.Combine(_tempDir, "library", "NFL - S2025E06 - Carolina Panthers vs Cleveland Browns.mkv");
        File.WriteAllBytes(imported, new byte[1024]);
        // The tag moves with the file, so the probe reads the imported copy.
        _embeddedIds[imported] = "ev-312923";

        await Service.ReportAcceptedPendingImportAsync(Pending(source, first.Id, 72), source, imported, null);

        Service.LastMatchReport.Should().BeNull();
        _handler.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAcceptedFileWithASidecarIdNeverReports()
    {
        await UsageData(true);
        var (first, _) = Seed();
        var file = Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");

        await Service.ReportAcceptedPendingImportAsync(Pending(file, first.Id, 72), file, file, "ev-312923");

        _handler.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAcceptWithUsageDataOffSendsNothing()
    {
        await UsageData(false);
        var (first, _) = Seed();
        var file = Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");

        await Service.ReportAcceptedPendingImportAsync(Pending(file, first.Id, 72), file, file, null);

        Service.LastMatchReport.Should().BeNull();
        _handler.Bodies.Should().BeEmpty();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<string> Bodies { get; } = new();
        public List<Uri> Uris { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.RequestUri!.AbsolutePath.EndsWith("/suggestions/file-matches", StringComparison.Ordinal))
            {
                lock (_gate)
                {
                    Bodies.Add(body);
                    Uris.Add(request.RequestUri);
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"accepted":1}""", Encoding.UTF8, "application/json"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }
}
