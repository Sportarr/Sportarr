using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class DvrCompletedOutputNamingTests
{
    [Fact]
    public async Task CompletedImportUsesProbedQualityInFileAndDatabasePaths()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var fixture = await NamingFixture.CreateAsync();
        var originalPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        fixture.Recording.OutputPath = originalPath;
        fixture.Recording.Method = DvrRecordingMethod.Catchup;
        await fixture.Db.SaveChangesAsync();
        var ffprobePath = Path.Combine(AppContext.BaseDirectory, "ffprobe");
        File.Exists(ffprobePath).Should().BeFalse("the test must not replace a real ffprobe binary");
        await File.WriteAllTextAsync(ffprobePath,
            "#!/bin/sh\n" +
            "printf '%s\\n' '{\"streams\":[{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":1280,\"height\":720,\"avg_frame_rate\":\"60/1\"},{\"codec_type\":\"audio\",\"codec_name\":\"aac\",\"channels\":2}],\"format\":{\"format_name\":\"mov,mp4\"}}'\n");
        File.SetUnixFileMode(ffprobePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var imported = await fixture.CreateEventDvrService().ImportCompletedRecordingAsync(fixture.Recording.Id);

            var expectedPath = Path.Combine(fixture.Directory, "Fixture - HDTV-720p.DVR.mp4");
            imported.Should().BeTrue();
            File.Exists(originalPath).Should().BeFalse();
            File.Exists(expectedPath).Should().BeTrue();
            var recording = await fixture.Db.DvrRecordings.SingleAsync();
            recording.Quality.Should().Be("HDTV-720p");
            recording.OutputPath.Should().Be(expectedPath);
            recording.Status.Should().Be(DvrRecordingStatus.Imported);
            (await fixture.Db.EventFiles.SingleAsync()).FilePath.Should().Be(expectedPath);
            (await fixture.Db.Events.SingleAsync()).FilePath.Should().Be(expectedPath);
        }
        finally
        {
            File.Delete(ffprobePath);
        }
    }

    [Fact]
    public async Task RenamesDirectRecordingWithDetectedQualityAndFinalExtension()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var originalPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        fixture.Recording.OutputPath = originalPath;
        fixture.Recording.Quality = "HDTV-720p";
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.RenameCompletedOutputAsync(fixture.Recording);

        var expectedPath = Path.Combine(fixture.Directory, "Fixture - HDTV-720p.DVR.mp4");
        File.Exists(originalPath).Should().BeFalse();
        File.Exists(expectedPath).Should().BeTrue();
        fixture.Recording.OutputPath.Should().Be(expectedPath);
        (await fixture.Db.DvrRecordings.SingleAsync()).OutputPath.Should().Be(expectedPath);
    }

    [Fact]
    public async Task UsesAFreeNameWhenDetectedQualityNameAlreadyExists()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var originalPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        fixture.CreateRecordingFile("Fixture - HDTV-720p.DVR.mp4");
        fixture.Recording.OutputPath = originalPath;
        fixture.Recording.Quality = "HDTV-720p";
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.RenameCompletedOutputAsync(fixture.Recording);

        var expectedPath = Path.Combine(fixture.Directory, "Fixture - HDTV-720p.DVR (2).mp4");
        File.Exists(originalPath).Should().BeFalse();
        File.Exists(expectedPath).Should().BeTrue();
        fixture.Recording.OutputPath.Should().Be(expectedPath);
    }

    [Fact]
    public async Task KeepsCompletedRecordingInItsExistingDirectory()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var existingDirectory = Path.Combine(fixture.Directory, "existing-location");
        System.IO.Directory.CreateDirectory(existingDirectory);
        var originalPath = Path.Combine(existingDirectory, "Fixture - HDTV-1080p.DVR.mp4");
        await File.WriteAllTextAsync(originalPath, "recording");
        fixture.Recording.OutputPath = originalPath;
        fixture.Recording.Quality = "HDTV-720p";
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.RenameCompletedOutputAsync(fixture.Recording);

        var expectedPath = Path.Combine(existingDirectory, "Fixture - HDTV-720p.DVR.mp4");
        File.Exists(expectedPath).Should().BeTrue();
        fixture.Recording.OutputPath.Should().Be(expectedPath);
    }

    [Fact]
    public async Task UpdatesExistingEventPathsThatReferenceTheRenamedRecording()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var originalPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        fixture.Recording.OutputPath = originalPath;
        fixture.Recording.Quality = "HDTV-720p";
        await fixture.Db.SaveChangesAsync();
        await using (var writer = fixture.CreateDb())
        {
            var eventInfo = await writer.Events.SingleAsync();
            eventInfo.FilePath = originalPath;
            writer.EventFiles.Add(new EventFile
            {
                EventId = fixture.Recording.EventId!.Value,
                FilePath = originalPath,
                Quality = "HDTV-1080p"
            });
            await writer.SaveChangesAsync();
        }

        await fixture.Service.RenameCompletedOutputAsync(fixture.Recording);

        var expectedPath = Path.Combine(fixture.Directory, "Fixture - HDTV-720p.DVR.mp4");
        (await fixture.Db.EventFiles.SingleAsync()).FilePath.Should().Be(expectedPath);
        (await fixture.Db.Events.SingleAsync()).FilePath.Should().Be(expectedPath);
    }

    [Fact]
    public async Task KeepsOriginalFileWhenDetectedQualityRenameFails()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var originalPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        var blockedPath = Path.Combine(fixture.Directory, "Fixture - HDTV-720p.DVR.mp4");
        System.IO.Directory.CreateDirectory(blockedPath);
        fixture.Recording.OutputPath = originalPath;
        fixture.Recording.Quality = "HDTV-720p";
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.RenameCompletedOutputAsync(fixture.Recording);

        File.Exists(originalPath).Should().BeTrue();
        fixture.Recording.OutputPath.Should().Be(originalPath);
        (await fixture.Db.DvrRecordings.SingleAsync()).OutputPath.Should().Be(originalPath);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("copy")]
    [InlineData("hardlink")]
    public async Task LeavesLibraryImportModesForTheLibraryImporter(string importMode)
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var originalPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        fixture.Recording.OutputPath = originalPath;
        fixture.Recording.Quality = "HDTV-720p";
        fixture.Recording.ImportMode = importMode;
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.RenameCompletedOutputAsync(fixture.Recording);

        File.Exists(originalPath).Should().BeTrue();
        fixture.Recording.OutputPath.Should().Be(originalPath);
    }

    [Fact]
    public async Task DirectFightNightPrelimsRecordingKeepsMainCardWanted()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var evt = fixture.Recording.Event!;
        evt.Title = "UFC Fight Night 999";
        evt.Sport = "Fighting";
        evt.League!.Name = "UFC";
        evt.League.Sport = "Fighting";
        fixture.Recording.PartName = "Prelims";
        fixture.Recording.OutputPath = fixture.CreateRecordingFile("UFC Fight Night 999 Prelims.mp4");
        await fixture.Db.SaveChangesAsync();

        var imported = await fixture.CreateEventDvrService()
            .ImportCompletedRecordingAsync(fixture.Recording.Id);

        imported.Should().BeTrue();
        fixture.Db.ChangeTracker.Clear();
        (await fixture.Db.EventFiles.SingleAsync()).PartNumber.Should().Be(1);
        (await fixture.Db.Events.SingleAsync()).HasFile.Should().BeFalse();
    }

    private sealed class NamingFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly MemoryCache _cache;
        private readonly HttpClient _http;
        public string Directory { get; }
        public SportarrDbContext Db { get; }
        public DvrRecording Recording { get; }
        public DvrRecordingService Service { get; }

        private NamingFixture(
            string directory,
            SqliteConnection connection,
            MemoryCache cache,
            HttpClient http,
            SportarrDbContext db,
            DvrRecording recording,
            DvrRecordingService service)
        {
            Directory = directory;
            _connection = connection;
            _cache = cache;
            _http = http;
            Db = db;
            Recording = recording;
            Service = service;
        }

        public string CreateRecordingFile(string filename)
        {
            var path = Path.Combine(Directory, filename);
            File.WriteAllText(path, "recording");
            return path;
        }

        public SportarrDbContext CreateDb()
        {
            return new SportarrDbContext(
                new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(_connection).Options);
        }

        public EventDvrService CreateEventDvrService()
        {
            return new EventDvrService(
                NullLogger<EventDvrService>.Instance,
                Db,
                Service,
                null!,
                null!,
                new FFmpegRecorderService(NullLogger<FFmpegRecorderService>.Instance, GetConfig(), null!),
                null!,
                null!,
                null!,
                GetConfig(),
                null!);
        }

        private ConfigService GetConfig()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = Directory,
                ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json"
            }).Build();
            return new ConfigService(configuration, NullLogger<ConfigService>.Instance);
        }

        public static async Task<NamingFixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"dvr-completed-name-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = directory,
                ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json"
            }).Build();
            var config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
            var appConfig = await config.GetConfigAsync();
            appConfig.DvrRecordingPath = directory;
            await config.SaveConfigAsync(appConfig);

            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            var league = new League { Id = 1, Name = "Fixture League", Sport = "Football" };
            var evt = new Event
            {
                Id = 1,
                LeagueId = league.Id,
                League = league,
                Title = "Fixture",
                Sport = "Football",
                EventDate = new DateTime(2026, 9, 14, 17, 0, 0, DateTimeKind.Utc),
                SeasonNumber = 2026,
                EpisodeNumber = 8
            };
            var source = new IptvSource { Id = 1, Name = "Fixture", Url = "https://iptv.invalid/list" };
            var channel = new IptvChannel
            {
                Id = 1,
                SourceId = source.Id,
                Source = source,
                Name = "Fixture FHD",
                StreamUrl = "https://iptv.invalid/stream"
            };
            var recording = new DvrRecording
            {
                Id = 1,
                EventId = evt.Id,
                Event = evt,
                ChannelId = channel.Id,
                Channel = channel,
                Title = evt.Title,
                Status = DvrRecordingStatus.Completed,
                ScheduledStart = evt.EventDate,
                ScheduledEnd = evt.EventDate.AddHours(3),
                Quality = "HDTV-1080p"
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
            await db.SaveChangesAsync();

            var http = new HttpClient();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new SportarrApiClient(
                http,
                NullLogger<SportarrApiClient>.Instance,
                configuration,
                config,
                cache);
            var recorder = new FFmpegRecorderService(NullLogger<FFmpegRecorderService>.Instance, config, null!);
            var service = new DvrRecordingService(
                NullLogger<DvrRecordingService>.Instance,
                db,
                recorder,
                null!,
                config,
                new FileNamingService(NullLogger<FileNamingService>.Instance),
                new DiskSpaceService(NullLogger<DiskSpaceService>.Instance),
                null!,
                api,
                new EpisodeNumberResolver(db, api, NullLogger<EpisodeNumberResolver>.Instance),
                new DvrEarlyFinishGuard());

            return new NamingFixture(directory, connection, cache, http, db, recording, service);
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            _cache.Dispose();
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
