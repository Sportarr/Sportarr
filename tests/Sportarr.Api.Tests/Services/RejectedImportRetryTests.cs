using System.Reflection;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Retry a rejected import when the upgrade decision changes.
/// </summary>
public class RejectedImportRetryTests
{
    private const string Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-Fixture";

    [Fact]
    public async Task RecordingGrowthDoesNotChangeTheRejectedImportDecision()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            multipart: false, relational: true, mediaRoot: "/data/e2e-lib");
        await HoldRecordingAsync(rig);
        var row = await CompletedDownloadAsync(rig);
        var importer = rig.Services.GetRequiredService<FileImportService>();
        var before = await importer.GetRejectedImportDecisionSignatureAsync(row);
        var held = await rig.Db.EventFiles.SingleAsync(f => f.EventId == rig.Event.Id);
        held.Size += 4096;
        await rig.Db.SaveChangesAsync();

        var after = await importer.GetRejectedImportDecisionSignatureAsync(row);

        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData("WEBDL-720p")]
    [InlineData("Unknown")]
    public async Task RejectedImportRetriesOnlyWhenTheUpgradeInputsChange(string savedQuality)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            multipart: false, relational: true, mediaRoot: "/data/e2e-lib");
        await SetReplaceRecordingsAsync(rig, false);
        await HoldRecordingAsync(rig);
        var row = await CompletedDownloadAsync(rig);
        row.Quality = savedQuality;
        await rig.Db.SaveChangesAsync();
        var logger = new RetryLogCounter();
        using var wake = new DownloadMonitorWakeSignal();
        using var monitor = new EnhancedDownloadMonitorService(rig.Services, logger, wake);

        await PollAsync(monitor);

        var rejected = await ReloadAsync(rig, row.Id);
        Assert.NotNull(rejected);
        Assert.Equal(DownloadStatus.ImportWarning, rejected!.Status);

        // A dozen polls with the job still complete in the client.
        for (var poll = 0; poll < 12; poll++)
            await PollAsync(monitor);
        rejected = await ReloadAsync(rig, row.Id);
        Assert.NotNull(rejected);
        Assert.Equal(DownloadStatus.ImportWarning, rejected!.Status);
        Assert.Equal(1, logger.RetryCount);

        await SetReplaceRecordingsAsync(rig, true);
        await PollAsync(monitor);
        Assert.Equal(2, logger.RetryCount);

        var imported = await ReloadAsync(rig, row.Id);
        Assert.NotNull(imported);
        Assert.Equal(DownloadStatus.Imported, imported!.Status);
        var file = await rig.Db.EventFiles.AsNoTracking().SingleAsync(f => f.EventId == rig.Event.Id && f.Exists);
        Assert.False(file.IsIptvRecording);
        Assert.Contains("720", file.Quality, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("Unknown", file.Quality);
    }

    [Fact]
    public async Task GrabbingAJobTheQueueAlreadyTracksReusesItsRow()
    {
        // The client hands back the job id it already holds (a qBittorrent
        // duplicate add). That job is the rejected row's, so the grab rearms
        // that row instead of tracking the same job twice.
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            multipart: false, relational: true, mediaRoot: "/data/e2e-lib");
        await SetReplaceRecordingsAsync(rig, false);
        await HoldRecordingAsync(rig);
        var row = await CompletedDownloadAsync(rig, downloadId: "part-job-1");
        await PollAsync(rig);
        Assert.Equal(DownloadStatus.ImportWarning, (await ReloadAsync(rig, row.Id))!.Status);

        await SetReplaceRecordingsAsync(rig, true);
        using var response = await rig.Client.PostAsJsonAsync("/api/release/push", new
        {
            title = Title,
            downloadUrl = "http://part-source.invalid/one.nzb",
            protocol = "usenet",
            indexer = "Part fixture",
            size = 2_000_000_000L,
            publishDate = DateTime.UtcNow.AddMinutes(-5),
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var result = JsonDocument.Parse(body);
        Assert.True(result.RootElement[0].GetProperty("approved").GetBoolean(), body);
        var tracked = await rig.Db.DownloadQueue.AsNoTracking().ToListAsync();
        var only = Assert.Single(tracked);
        Assert.Equal(row.Id, only.Id);
        Assert.Equal(DownloadStatus.Queued, only.Status);
        Assert.Null(only.ErrorMessage);

        await PollAsync(rig);
        Assert.Equal(DownloadStatus.Imported, (await ReloadAsync(rig, row.Id))!.Status);
    }

    internal static async Task PollAsync(PartIdentityIntegrationHarness rig)
    {
        using var wake = new DownloadMonitorWakeSignal();
        using var monitor = new EnhancedDownloadMonitorService(rig.Services,
            NullLogger<EnhancedDownloadMonitorService>.Instance, wake);
        await PollAsync(monitor);
    }

    private static async Task PollAsync(EnhancedDownloadMonitorService monitor)
    {
        var monitorPass = typeof(EnhancedDownloadMonitorService).GetMethod("MonitorDownloadsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)monitorPass.Invoke(monitor, new object?[] { CancellationToken.None })!;
    }

    private sealed class RetryLogCounter : ILogger<EnhancedDownloadMonitorService>
    {
        public int RetryCount { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopScope();
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).Contains("Rejected import now qualifies", StringComparison.Ordinal))
                RetryCount++;
        }

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    internal static async Task<DownloadQueueItem?> ReloadAsync(PartIdentityIntegrationHarness rig, int id) =>
        await rig.Db.DownloadQueue.AsNoTracking().SingleOrDefaultAsync(q => q.Id == id);

    internal static async Task SetReplaceRecordingsAsync(PartIdentityIntegrationHarness rig, bool on)
    {
        var configService = rig.Services.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        config.DvrReplaceRecordingsWithIndexerReleases = on;
        await configService.SaveConfigAsync(config);
    }

    internal static async Task HoldRecordingAsync(PartIdentityIntegrationHarness rig)
    {
        var root = await rig.Db.RootFolders.Select(r => r.Path).SingleAsync();
        var path = Path.Combine(root, "UFC 9999 Event WEBDL-1080p.mkv");
        await File.WriteAllBytesAsync(path, new byte[4096]);
        rig.Event.HasFile = true;
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id, FilePath = path, Size = 4096, Quality = "WEBDL-1080p",
            IsIptvRecording = true, Exists = true,
        });
        await rig.Db.SaveChangesAsync();
    }

    // A download the client reports complete, with its file on disk.
    internal static async Task<DownloadQueueItem> CompletedDownloadAsync(PartIdentityIntegrationHarness rig,
        string downloadId = "rejected-job")
    {
        var root = await rig.Db.RootFolders.Select(r => r.Path).SingleAsync();
        var folder = Path.Combine(Path.GetDirectoryName(root)!, "incoming", downloadId);
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, Title + ".mkv"), new byte[4096]);
        rig.Transport.CompletedDownloadId = downloadId;
        rig.Transport.CompletedDownloadPath = folder;

        var client = await rig.Db.DownloadClients.SingleAsync();
        var row = new DownloadQueueItem
        {
            EventId = rig.Event.Id, Title = Title, DownloadId = downloadId, DownloadClientId = client.Id,
            GrabCategory = "sportarr", Quality = "WEBDL-720p", Protocol = "Usenet",
            Status = DownloadStatus.Downloading, Progress = 50, Added = DateTime.UtcNow.AddHours(-1),
        };
        rig.Db.DownloadQueue.Add(row);
        await rig.Db.SaveChangesAsync();
        return row;
    }
}
