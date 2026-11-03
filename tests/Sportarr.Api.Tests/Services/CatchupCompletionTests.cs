using System.Net;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatchupCompletionTests
{
    [Fact]
    public async Task CompletedCatchupRunsThePostRecordingCommandOnceWithTheFinalPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"catchup-complete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var callLog = Path.Combine(directory, "calls.log");
            var script = Path.Combine(directory, "post-recording.sh");
            await File.WriteAllTextAsync(script,
                "#!/bin/sh\n" +
                $"printf '%s|%s|%s|%s\\n' \"$SPORTARR_RECORDING_PATH\" \"$SPORTARR_RECORDING_ID\" \"$SPORTARR_EVENT_ID\" \"$SPORTARR_DURATION_SECONDS\" >> '{callLog}'\n");
            File.SetUnixFileMode(script,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = directory,
                ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json"
            }).Build();
            var config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
            var appConfig = await config.GetConfigAsync();
            appConfig.DvrRecordingPath = directory;
            appConfig.DvrPostRecordingCommand = script;
            await config.SaveConfigAsync(appConfig);

            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options;
            await using var db = new SportarrDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var league = new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" };
            var evt = new Event
            {
                Id = 1668,
                LeagueId = league.Id,
                League = league,
                Title = "Mexico City Grand Prix - Race",
                Sport = "Motorsport",
                EventDate = new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc),
                SeasonNumber = 2026,
                EpisodeNumber = 101
            };
            var source = new IptvSource { Id = 1, Name = "Provider", Url = "https://iptv.invalid", Type = IptvSourceType.Xtream };
            var channel = new IptvChannel
            {
                Id = 1,
                SourceId = source.Id,
                Source = source,
                Name = "Sky Sports Main Event",
                StreamUrl = "https://iptv.invalid/live/u/p/1.ts",
                HasArchive = true
            };
            var downloaded = Path.Combine(directory, "catchup-download.mkv");
            await File.WriteAllTextAsync(downloaded, "recording");
            var recording = new DvrRecording
            {
                Id = 34,
                EventId = evt.Id,
                Event = evt,
                ChannelId = channel.Id,
                Channel = channel,
                Title = evt.Title,
                Method = DvrRecordingMethod.Catchup,
                Status = DvrRecordingStatus.Recording,
                ScheduledStart = evt.EventDate,
                ScheduledEnd = evt.EventDate.AddMinutes(200),
                Quality = "HDTV-720p",
                OutputPath = downloaded
            };
            db.DvrRecordings.Add(recording);
            db.MediaManagementSettings.Add(new MediaManagementSettings
            {
                Id = 1,
                RenameEvents = true,
                StandardFileFormat = "{Event Title} - {Quality Full}",
                CreateLeagueFolders = false,
                CreateSeasonFolders = false,
                CreateEventFolders = false
            });
            db.Notifications.Add(new Notification
            {
                Name = "Recordings",
                Implementation = "Webhook",
                Enabled = true,
                ConfigJson = "{\"webhook\":\"http://example.com/hook\",\"onRecordingCompleted\":true}"
            });
            await db.SaveChangesAsync();

            var handler = new CountingHandler();
            using var httpClient = new HttpClient(handler);
            await using var notificationServices = new ServiceCollection()
                .AddScoped(_ => new SportarrDbContext(options))
                .AddSingleton(config)
                .BuildServiceProvider();
            var notifications = new NotificationService(
                notificationServices,
                NullLogger<NotificationService>.Instance,
                httpClient,
                new FixedHttpClientFactory(httpClient));

            using var apiHttp = new HttpClient();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new SportarrApiClient(apiHttp, NullLogger<SportarrApiClient>.Instance, configuration, config, cache);
            var recorder = new FFmpegRecorderService(NullLogger<FFmpegRecorderService>.Instance, config, null!);
            var dvrService = new DvrRecordingService(
                NullLogger<DvrRecordingService>.Instance,
                db,
                recorder,
                null!,
                config,
                new FileNamingService(NullLogger<FileNamingService>.Instance),
                new DiskSpaceService(NullLogger<DiskSpaceService>.Instance),
                notifications,
                api,
                new EpisodeNumberResolver(db, api, NullLogger<EpisodeNumberResolver>.Instance),
                new DvrEarlyFinishGuard());
            var eventDvr = new EventDvrService(
                NullLogger<EventDvrService>.Instance,
                db,
                dvrService,
                null!,
                null!,
                recorder,
                null!,
                null!,
                null!,
                config,
                null!);
            await using var scoped = new ServiceCollection()
                .AddSingleton(dvrService)
                .AddSingleton(eventDvr)
                .BuildServiceProvider();
            var catchup = new CatchupDownloadService(scoped, NullLogger<CatchupDownloadService>.Instance);

            await catchup.CompleteDownloadAsync(scoped, db, recording, downloaded, 200, CancellationToken.None);

            var calls = await WaitForCallsAsync(callLog);
            // Give a second run time to show up before the count is checked.
            await Task.Delay(TimeSpan.FromSeconds(1));
            calls = await File.ReadAllLinesAsync(callLog);

            var stored = await db.DvrRecordings.AsNoTracking().SingleAsync();
            stored.Status.Should().Be(DvrRecordingStatus.Imported);
            stored.OutputPath.Should().NotBe(downloaded, "the completed download gets its detected-quality name");
            File.Exists(stored.OutputPath).Should().BeTrue();
            calls.Should().ContainSingle()
                .Which.Should().Be($"{stored.OutputPath}|34|1668|12000");
            handler.RequestCount.Should().Be(1, "the completed notification fires once");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string[]> WaitForCallsAsync(string callLog)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(callLog))
            {
                var lines = await File.ReadAllLinesAsync(callLog);
                if (lines.Length > 0)
                {
                    return lines;
                }
            }
            await Task.Delay(100);
        }
        return Array.Empty<string>();
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
