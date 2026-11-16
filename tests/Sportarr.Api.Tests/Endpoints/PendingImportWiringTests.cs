using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// The pending import routes call the match helpers and the report the way
/// the screens use them. These seams broke silently before, because the
/// helpers had tests and the routes did not.
/// </summary>
// A page import of a recording choice takes the static recording choice gate.
[Collection(DvrChoiceStateCollection.Name)]
public class PendingImportWiringTests
{
    [Fact]
    public async Task ThePutOfANewEventWritesTheUserChoiceConfidence()
    {
        await using var host = await WiringHost.CreateAsync();
        var (first, second) = host.Seed();
        var pending = await host.AddPendingAsync(host.Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv"), first.Id, 72);

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/pending-imports/{pending.Id}/suggestion", new { eventId = second.Id, part = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await host.Db.PendingImports.AsNoTracking().SingleAsync(p => p.Id == pending.Id);
        saved.SuggestedEventId.Should().Be(second.Id);
        saved.SuggestionConfidence.Should().Be(FileMatchSuggestionCollector.UserChoiceConfidence);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("suggestionConfidence").GetInt32().Should().Be(100,
            "the user choice marker never reaches an API client");
    }

    [Fact]
    public async Task ThePutOfAPartAloneKeepsTheConfidence()
    {
        await using var host = await WiringHost.CreateAsync();
        var (first, _) = host.Seed();
        var pending = await host.AddPendingAsync(host.Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv"), first.Id, 72);

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/pending-imports/{pending.Id}/suggestion", new { eventId = first.Id, part = "Second Half" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await host.Db.PendingImports.AsNoTracking().SingleAsync(p => p.Id == pending.Id);
        saved.SuggestionConfidence.Should().Be(72);
        saved.SuggestedPart.Should().Be("Second Half");
    }

    [Fact]
    public async Task ThePendingListAndThePendingRowShowTheUserChoiceAsOneHundred()
    {
        await using var host = await WiringHost.CreateAsync();
        var (first, _) = host.Seed();
        var pending = await host.AddPendingAsync(host.Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv"),
            first.Id, FileMatchSuggestionCollector.UserChoiceConfidence);

        var list = await host.Client.GetFromJsonAsync<JsonElement>("/api/pending-imports");
        var row = await host.Client.GetFromJsonAsync<JsonElement>($"/api/pending-imports/{pending.Id}");

        list.EnumerateArray().Single().GetProperty("suggestionConfidence").GetInt32().Should().Be(100);
        row.GetProperty("suggestionConfidence").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task AnAcceptAfterTheUserPickedAnotherEventReportsManualOnce()
    {
        await using var host = await WiringHost.CreateAsync();
        await host.UsageData(true);
        var (first, second) = host.Seed();
        var pending = await host.AddPendingAsync(host.Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv"), first.Id, 72);

        using (var edit = await host.Client.PutAsJsonAsync(
                   $"/api/pending-imports/{pending.Id}/suggestion", new { eventId = second.Id, part = (string?)null }))
            edit.StatusCode.Should().Be(HttpStatusCode.OK);
        using var accept = await host.Client.PostAsync($"/api/pending-imports/{pending.Id}/accept", null);

        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        using (var body = JsonDocument.Parse(await accept.Content.ReadAsStringAsync()))
            body.RootElement.GetProperty("suggestionConfidence").GetInt32().Should().Be(100);
        var sent = await host.SentSuggestions();
        host.Handler.Bodies.Should().ContainSingle();
        var suggestion = sent.Should().ContainSingle().Subject;
        suggestion.GetProperty("kind").GetString().Should().Be("manual");
        suggestion.GetProperty("eventId").GetString().Should().Be("ev-313015");
    }

    [Fact]
    public async Task AnAcceptOfAnUnchangedSuggestionReportsConfirmedOnce()
    {
        await using var host = await WiringHost.CreateAsync();
        await host.UsageData(true);
        var (first, _) = host.Seed();
        var pending = await host.AddPendingAsync(host.Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv"), first.Id, 72);

        using var accept = await host.Client.PostAsync($"/api/pending-imports/{pending.Id}/accept", null);

        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        var sent = await host.SentSuggestions();
        host.Handler.Bodies.Should().ContainSingle();
        var suggestion = sent.Should().ContainSingle().Subject;
        suggestion.GetProperty("kind").GetString().Should().Be("confirmed");
        suggestion.GetProperty("eventId").GetString().Should().Be("ev-312923");
    }

    [Fact]
    public async Task AnAcceptOfAFileWithAnEmbeddedLeagueTagReportsNothing()
    {
        await using var host = await WiringHost.CreateAsync();
        await host.UsageData(true);
        var (first, _) = host.Seed();
        var file = host.Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");
        host.EmbeddedIds[file] = "lg-000032";
        var pending = await host.AddPendingAsync(file, first.Id, 72);

        using var accept = await host.Client.PostAsync($"/api/pending-imports/{pending.Id}/accept", null);

        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        (await host.SentSuggestions()).Should().BeEmpty();
        host.Handler.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task APageImportClearsTheDiskScanRowOfThatFileFromThePendingList()
    {
        await using var host = await WiringHost.CreateAsync();
        var (first, second) = host.Seed();
        var imported = host.Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");
        var other = host.Write("NFL.2025.Ravens.vs.Lions.1080p.WEB.h264-GRP.mkv");
        var stale = await host.AddPendingAsync(imported, first.Id, 50);
        var kept = await host.AddPendingAsync(other, second.Id, 50);

        var result = await host.Library.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = imported, EventId = first.Id, SuggestedEventId = first.Id, ReportMatch = true, ImportMode = "copy" },
        });

        result.Imported.Should().ContainSingle();
        var list = await host.Client.GetFromJsonAsync<JsonElement>("/api/pending-imports");
        list.EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).Should().Equal(kept.Id);
        var cleared = await host.Db.PendingImports.AsNoTracking().SingleAsync(p => p.Id == stale.Id);
        cleared.Status.Should().Be(PendingImportStatus.Completed);
        cleared.ResolvedAt.Should().NotBeNull();
    }

    // Match season pairs files with events by order, so the user did not pick them.
    [Fact]
    public async Task APageImportOfASeasonMatchPairReportsNothing()
    {
        await using var host = await WiringHost.CreateAsync();
        await host.UsageData(true);
        var (first, second) = host.Seed();
        var paired = host.Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");
        var picked = host.Write("NFL.2025.Ravens.vs.Lions.1080p.WEB.h264-GRP.mkv");

        var result = await host.Library.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = paired, EventId = first.Id, SuggestedEventId = second.Id, ReportMatch = true, PairedBySeasonMatch = true, ImportMode = "copy" },
            new() { FilePath = picked, EventId = second.Id, SuggestedEventId = first.Id, ReportMatch = true, ImportMode = "copy" },
        });

        result.Imported.Should().HaveCount(2);
        (await host.SentSuggestions()).Should().ContainSingle()
            .Which.GetProperty("eventId").GetString().Should().Be("ev-313015", "only the user's own pick is reported");
    }

    [Fact]
    public async Task AFailedPageImportLeavesThePendingRow()
    {
        await using var host = await WiringHost.CreateAsync();
        var (first, _) = host.Seed();
        var file = host.Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");
        var pending = await host.AddPendingAsync(file, first.Id, 50);

        var result = await host.Library.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = file, EventId = 9999, ReportMatch = true, ImportMode = "copy" },
        });

        result.Failed.Should().ContainSingle();
        (await host.Db.PendingImports.AsNoTracking().SingleAsync(p => p.Id == pending.Id))
            .Status.Should().Be(PendingImportStatus.Pending);
    }

    [Fact]
    public async Task ThePendingListCarriesThePackFieldsThatTheRowStores()
    {
        await using var host = await WiringHost.CreateAsync();
        var (first, _) = host.Seed();
        var plain = await host.AddPendingAsync(host.Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv"), first.Id, 50);
        var pack = new PendingImport
        {
            DownloadId = "pack-job", Title = "NFL-2025-Week15", FilePath = Path.Combine(Path.GetTempPath(), "NFL-2025-Week15"),
            Size = 4096, IsPack = true, FileCount = 4, MatchedEventsCount = 3, Status = PendingImportStatus.Pending,
            Detected = DateTime.UtcNow.AddMinutes(1),
        };
        host.Db.PendingImports.Add(pack);
        await host.Db.SaveChangesAsync();

        var list = await host.Client.GetFromJsonAsync<JsonElement>("/api/pending-imports");
        var row = await host.Client.GetFromJsonAsync<JsonElement>($"/api/pending-imports/{pack.Id}");

        var rows = list.EnumerateArray().ToDictionary(r => r.GetProperty("id").GetInt32());
        rows[pack.Id].GetProperty("isPack").GetBoolean().Should().BeTrue();
        rows[pack.Id].GetProperty("fileCount").GetInt32().Should().Be(4);
        rows[pack.Id].GetProperty("matchedEventsCount").GetInt32().Should().Be(3);
        rows[plain.Id].GetProperty("isPack").GetBoolean().Should().BeFalse();
        row.GetProperty("isPack").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task APageImportThatMovesADvrChoiceFileMarksTheRecordingImported()
    {
        await using var host = await WiringHost.CreateAsync();
        var (first, _) = host.Seed();
        var file = host.Write("NFL.2025.Panthers.vs.Browns.720p.HDTV.DVR.mkv");
        var (recording, pending) = await AddDvrChoiceAsync(host, file, first);

        var result = await host.Library.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = file, EventId = first.Id, ImportMode = "move" },
        });

        var imported = result.Imported.Should().ContainSingle().Subject;
        File.Exists(file).Should().BeFalse();
        (await host.Db.PendingImports.AsNoTracking().SingleAsync(p => p.Id == pending.Id))
            .Status.Should().Be(PendingImportStatus.Completed);
        var saved = await host.Db.DvrRecordings.AsNoTracking().SingleAsync(r => r.Id == recording.Id);
        saved.Status.Should().Be(DvrRecordingStatus.Imported);
        saved.ImportedAt.Should().NotBeNull();
        saved.ErrorMessage.Should().BeNull();
        saved.OutputPath.Should().Be(imported, "a move takes the recording's file into the library");
    }

    [Fact]
    public async Task APageImportThatCopiesADvrChoiceFileKeepsTheRecordingOnItsCapture()
    {
        await using var host = await WiringHost.CreateAsync();
        var (first, _) = host.Seed();
        var file = host.Write("NFL.2025.Panthers.vs.Browns.720p.HDTV.DVR.mkv");
        var (recording, _) = await AddDvrChoiceAsync(host, file, first);

        var result = await host.Library.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = file, EventId = first.Id, ImportMode = "copy" },
        });

        result.Imported.Should().ContainSingle().Which.Should().NotBe(file);
        var saved = await host.Db.DvrRecordings.AsNoTracking().SingleAsync(r => r.Id == recording.Id);
        saved.Status.Should().Be(DvrRecordingStatus.Imported);
        saved.ImportedAt.Should().NotBeNull();
        saved.OutputPath.Should().Be(file, "a delete of the recording must remove the capture, not the library copy");
    }

    [Fact]
    public async Task APageImportOfADvrChoiceFileToAnotherEventLeavesTheRecording()
    {
        await using var host = await WiringHost.CreateAsync();
        var (first, second) = host.Seed();
        var file = host.Write("NFL.2025.Panthers.vs.Browns.720p.HDTV.DVR.mkv");
        var (recording, pending) = await AddDvrChoiceAsync(host, file, first);

        var result = await host.Library.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = file, EventId = second.Id, ImportMode = "move" },
        });

        result.Imported.Should().ContainSingle();
        (await host.Db.PendingImports.AsNoTracking().SingleAsync(p => p.Id == pending.Id))
            .Status.Should().Be(PendingImportStatus.Completed);
        await AssertUnchangedAsync(host, recording);
    }

    [Fact]
    public async Task APageImportOfADvrChoiceFileToAnotherPartLeavesTheRecording()
    {
        await using var host = await WiringHost.CreateAsync();
        var (first, _) = host.Seed();
        var file = host.Write("NFL.2025.Panthers.vs.Browns.720p.HDTV.DVR.mkv");
        var (recording, _) = await AddDvrChoiceAsync(host, file, first, part: "Second Half");

        var result = await host.Library.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = file, EventId = first.Id, ImportMode = "move" },
        });

        result.Imported.Should().ContainSingle();
        await AssertUnchangedAsync(host, recording);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task APageImportLeavesARecordingThatChangedSinceTheRowWasWritten(bool pathChanged)
    {
        await using var host = await WiringHost.CreateAsync();
        var (first, _) = host.Seed();
        var file = host.Write("NFL.2025.Panthers.vs.Browns.720p.HDTV.DVR.mkv");
        var (recording, _) = await AddDvrChoiceAsync(host, file, first,
            status: pathChanged ? DvrRecordingStatus.Completed : DvrRecordingStatus.Failed,
            outputPath: pathChanged ? file + ".old" : file);

        var result = await host.Library.ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = file, EventId = first.Id, ImportMode = "move" },
        });

        result.Imported.Should().ContainSingle();
        await AssertUnchangedAsync(host, recording);
    }

    [Fact]
    public async Task APageImportWaitsForRetentionThatResolvesTheSameChoiceAndStillReports()
    {
        await using var host = await WiringHost.CreateAsync();
        await host.UsageData(true);
        var (first, second) = host.Seed();
        var file = host.Write("NFL.2025.Panthers.vs.Browns.720p.HDTV.DVR.mkv");
        var (recording, pending) = await AddDvrChoiceAsync(host, file, first);
        // The downloaded file that the choice compares with. Retention keeps it.
        var downloaded = host.Write("NFL.2025.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv");
        host.Db.EventFiles.Add(new EventFile { EventId = first.Id, FilePath = downloaded, Exists = true, Size = 4096 });
        await host.Db.SaveChangesAsync();
        host.Db.ChangeTracker.Clear();
        // The batch uses host.Db. Retention uses its own context on the same store.
        await using var retentionDb = host.NewContext();

        Task<ImportResult> batch;
        await DvrIndexerChoice.EnterGateAsync();
        try
        {
            batch = host.Library.ImportFilesAsync(new List<FileImportRequest>
            {
                new() { FilePath = file, EventId = second.Id, SuggestedEventId = first.Id, ReportMatch = true, ImportMode = "copy" },
            });
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DvrIndexerChoice.GateWaiters == 0)
            {
                batch.IsCompleted.Should().BeFalse("the batch must wait for the recording choice gate");
                DateTime.UtcNow.Should().BeBefore(deadline, "the batch must reach the gate");
                await Task.Delay(10);
            }
            // Retention keeps the downloaded file and removes the choice row.
            var row = await retentionDb.PendingImports.SingleAsync(p => p.Id == pending.Id);
            (await DvrIndexerChoice.KeepDownloadAsync(retentionDb, row, blocklist: false))
                .Should().Be(DvrIndexerChoice.Outcome.Resolved);
        }
        finally
        {
            DvrIndexerChoice.Gate.Release();
        }

        var result = await batch;
        result.Imported.Should().ContainSingle();
        result.Failed.Should().BeEmpty();
        (await host.Db.PendingImports.AsNoTracking().AnyAsync(p => p.Id == pending.Id)).Should().BeFalse();
        (await host.Db.DvrRecordings.AsNoTracking().SingleAsync(r => r.Id == recording.Id))
            .Status.Should().Be(DvrRecordingStatus.Ignored, "retention decided first, so the batch leaves the recording");
        (await host.SentSuggestions()).Should().ContainSingle()
            .Which.GetProperty("eventId").GetString().Should().Be("ev-313015");
        File.Exists(downloaded).Should().BeTrue();
    }

    /// <summary>A recording and its "dvr-N" choice row, as the DVR service writes them.</summary>
    private static async Task<(DvrRecording Recording, PendingImport Pending)> AddDvrChoiceAsync(
        WiringHost host, string file, Event evt, string? part = null,
        DvrRecordingStatus status = DvrRecordingStatus.Completed, string? outputPath = null)
    {
        var recording = new DvrRecording
        {
            EventId = evt.Id, ChannelId = 1, Title = evt.Title, Status = status, PartName = part,
            OutputPath = outputPath ?? file, Quality = "HDTV-720p", ErrorMessage = "Choose which file to keep.",
            ScheduledStart = DateTime.UtcNow.AddHours(-3), ScheduledEnd = DateTime.UtcNow.AddHours(-1),
        };
        host.Db.DvrRecordings.Add(recording);
        await host.Db.SaveChangesAsync();
        var pending = new PendingImport
        {
            DownloadId = $"dvr-{recording.Id}", Title = Path.GetFileName(file), FilePath = file, Size = 4096,
            SuggestedEventId = evt.Id, SuggestedPart = part, SuggestionConfidence = 100, Protocol = "DVR",
            Status = PendingImportStatus.Pending,
        };
        host.Db.PendingImports.Add(pending);
        await host.Db.SaveChangesAsync();
        host.Db.ChangeTracker.Clear();
        return (recording, pending);
    }

    private static async Task AssertUnchangedAsync(WiringHost host, DvrRecording before)
    {
        var saved = await host.Db.DvrRecordings.AsNoTracking().SingleAsync(r => r.Id == before.Id);
        saved.Status.Should().Be(before.Status);
        saved.ImportedAt.Should().BeNull();
        saved.OutputPath.Should().Be(before.OutputPath);
        saved.ErrorMessage.Should().Be(before.ErrorMessage);
    }

    [Fact]
    public async Task ThePageImportRouteSetsReportMatchAndItSurvivesTheTaskBody()
    {
        var name = Guid.NewGuid().ToString("N");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<SportarrDbContext>(options => options.UseInMemoryDatabase(name));
        builder.Services.AddSingleton<TaskService>();
        // The other library routes must resolve as services. They never run here.
        builder.Services.AddSingleton<LibraryImportService>(_ => throw new InvalidOperationException("Unexpected import service"));
        builder.Services.AddSingleton<SportarrApiClient>(_ => throw new InvalidOperationException("Unexpected metadata client"));
        builder.Services.AddSingleton<ConfigService>(_ => throw new InvalidOperationException("Unexpected config route"));
        await using var app = builder.Build();
        app.MapLibraryEndpoints();
        await app.StartAsync();

        // The client tries to turn the report off. Only the server decides.
        using var response = await app.GetTestClient().PostAsJsonAsync("/api/library/import", new[]
        {
            new { filePath = "/media/missing/file.mkv", eventId = 7, suggestedEventId = 7, reportMatch = false, suggestedByMemory = true, pairedBySeasonMatch = true },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        string? body;
        using (var scope = app.Services.CreateScope())
            body = (await scope.ServiceProvider.GetRequiredService<SportarrDbContext>().Tasks.AsNoTracking()
                .SingleAsync(t => t.CommandName == "LibraryImport")).Body;
        var request = TaskService.ReadLibraryImportRequests(body).Should().ContainSingle().Subject;
        request.ReportMatch.Should().BeTrue();
        request.SuggestedByMemory.Should().BeTrue();
        request.PairedBySeasonMatch.Should().BeTrue();
        request.EventId.Should().Be(7);
        request.SuggestedEventId.Should().Be(7);
    }

    /// <summary>
    /// The pending import routes on an in-memory database, with a hub that
    /// records file match reports and a probe that gives embedded tags.
    /// </summary>
    private sealed class WiringHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _directory;
        private readonly string _incoming;

        private WiringHost(WebApplication app, string directory, RecordingHandler handler, Dictionary<string, string> embeddedIds)
        {
            _app = app;
            _directory = directory;
            _incoming = Path.Combine(directory, "incoming");
            Handler = handler;
            EmbeddedIds = embeddedIds;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }
        public RecordingHandler Handler { get; }
        public Dictionary<string, string> EmbeddedIds { get; }
        public SportarrDbContext Db => _app.Services.GetRequiredService<SportarrDbContext>();
        public LibraryImportService Library => _app.Services.GetRequiredService<LibraryImportService>();
        private ConfigService Config => _app.Services.GetRequiredService<ConfigService>();

        public static async Task<WiringHost> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "sportarr-pending-wiring-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "incoming"));
            Directory.CreateDirectory(Path.Combine(directory, "library"));
            var handler = new RecordingHandler();
            var embeddedIds = new Dictionary<string, string>();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = Path.Combine(directory, "data"),
                ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json",
            });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            var services = builder.Services;
            // One context keeps the routes and the assertions on the same rows.
            services.AddSingleton(new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseInMemoryDatabase(directory).Options));
            services.AddMemoryCache();
            services.AddSingleton(new HttpClient(handler));
            services.AddSingleton(Mock.Of<IHttpClientFactory>());
            services.AddSingleton(Mock.Of<IMetadataWriterService>());
            services.AddSingleton(Mock.Of<IRemotePathMappingService>());
            // The probe stands in for ffprobe. It gives the embedded tag that a test sets.
            var inspector = new Mock<MediaFileInspector>(Mock.Of<ILogger<MediaFileInspector>>());
            inspector.Setup(i => i.InspectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string path, CancellationToken _) =>
                    new MediaFileInspection { FilePath = path, SportarrId = embeddedIds.GetValueOrDefault(path) });
            services.AddSingleton(inspector.Object);
            foreach (var type in new[]
            {
                typeof(ConfigService), typeof(SportarrApiClient), typeof(MediaFileParser), typeof(SportsFileNameParser),
                typeof(FileNamingService), typeof(EventPartDetector), typeof(DiskSpaceService), typeof(CustomFormatService),
                typeof(EpisodeNumberResolver), typeof(NotificationService), typeof(ImportFileSuppressionService),
                typeof(LibraryImportService),
            }) services.AddSingleton(type);
            // The disk accept never moves a download, so it needs no download client.
            services.AddSingleton(sp => new FileImportService(
                sp.GetRequiredService<SportarrDbContext>(), sp.GetRequiredService<MediaFileParser>(),
                sp.GetRequiredService<FileNamingService>(), null!, sp.GetRequiredService<EventPartDetector>(),
                sp.GetRequiredService<ConfigService>(), sp.GetRequiredService<ImportFileSuppressionService>(),
                sp.GetRequiredService<DiskSpaceService>(), sp.GetRequiredService<EpisodeNumberResolver>(),
                sp.GetRequiredService<NotificationService>(), sp.GetRequiredService<CustomFormatService>(),
                sp.GetRequiredService<IRemotePathMappingService>(), sp.GetRequiredService<IMetadataWriterService>(),
                sp.GetRequiredService<ILogger<FileImportService>>()));
            // The sibling routes must resolve as services. They never run here.
            services.AddSingleton<DownloadClientService>(_ => throw new InvalidOperationException("Unexpected download client route"));
            services.AddSingleton<PackImportService>(_ => throw new InvalidOperationException("Unexpected pack route"));
            services.AddSingleton<QueueRemovalService>(_ => throw new InvalidOperationException("Unexpected queue removal route"));
            services.AddSingleton<ImportMatchingService>(_ => throw new InvalidOperationException("Unexpected matching route"));
            services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);

            var app = builder.Build();
            app.MapQueueAndImportEndpoints();
            await app.StartAsync();
            var host = new WiringHost(app, directory, handler, embeddedIds);
            var config = await host.Config.GetConfigAsync();
            config.SkipFreeSpaceCheck = true;
            config.UseHardlinks = false;
            config.SendAnonymousUsageData = false;
            return host;
        }

        /// <summary>A second context on the same in-memory store, as another service uses.</summary>
        public SportarrDbContext NewContext() => new(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(_directory).Options);

        public async Task UsageData(bool on) => (await Config.GetConfigAsync()).SendAnonymousUsageData = on;

        public (Event First, Event Second) Seed()
        {
            var root = new RootFolder { Path = Path.Combine(_directory, "library") };
            Db.RootFolders.Add(root);
            Db.SaveChanges();
            var nfl = new League { Name = "NFL", Sport = "American Football", ExternalId = "lg-000032", RootFolderId = root.Id };
            Db.Leagues.Add(nfl);
            Db.SaveChanges();
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
            Db.Events.AddRange(first, second);
            Db.SaveChanges();
            return (first, second);
        }

        public string Write(string name)
        {
            var path = Path.Combine(_incoming, name);
            File.WriteAllBytes(path, new byte[64 * 1024]);
            return path;
        }

        /// <summary>A pending row as a disk scan writes it.</summary>
        public async Task<PendingImport> AddPendingAsync(string file, int eventId, int confidence)
        {
            var pending = new PendingImport
            {
                DownloadId = $"disk-{Guid.NewGuid():N}", Title = Path.GetFileName(file), FilePath = file,
                Size = new FileInfo(file).Length, SuggestedEventId = eventId, SuggestionConfidence = confidence,
                Status = PendingImportStatus.Pending,
            };
            Db.PendingImports.Add(pending);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return pending;
        }

        public async Task<List<JsonElement>> SentSuggestions()
        {
            if (Library.LastMatchReport != null)
                await Library.LastMatchReport;
            return Handler.Bodies
                .SelectMany(body => JsonDocument.Parse(body).RootElement.GetProperty("suggestions").EnumerateArray())
                .ToList();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.RequestUri!.AbsolutePath.EndsWith("/suggestions/file-matches", StringComparison.Ordinal))
            {
                lock (_gate)
                    Bodies.Add(body);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"accepted":1}""", Encoding.UTF8, "application/json"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }
}
