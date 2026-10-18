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
    public async Task FallbackRecordingKeepsPartAndImportMode()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var failed = fixture.Recording;
        failed.Status = DvrRecordingStatus.Failed;
        failed.PartName = "Main Card";
        failed.ImportMode = "move";
        failed.ScheduledStart = DateTime.UtcNow.AddHours(1);
        failed.ScheduledEnd = failed.ScheduledStart.AddHours(3);
        var backup = new IptvChannel
        {
            Id = 2,
            SourceId = failed.Channel!.SourceId,
            Name = "Backup",
            StreamUrl = "https://iptv.invalid/backup"
        };
        fixture.Db.IptvChannels.Add(backup);
        failed.FallbackChannelIds = "[2]";
        await fixture.Db.SaveChangesAsync();

        var rotatedId = await fixture.Service.TryRescheduleOnFallbackAsync(failed, "stream failed");

        rotatedId.Should().NotBeNull();
        var rotated = await fixture.Db.DvrRecordings.SingleAsync(recording => recording.Id == rotatedId);
        rotated.PartName.Should().Be("Main Card");
        rotated.ImportMode.Should().Be("move");
    }

    [Fact]
    public async Task RetryKeepsAUsableFailedCaptureUntilTheUserMovesIt()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var savedPath = fixture.CreateRecordingFile("failed-capture.ts");
        fixture.Recording.Status = DvrRecordingStatus.Failed;
        fixture.Recording.OutputPath = savedPath;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.StartRecordingAsync(fixture.Recording.Id);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("saved capture");
        fixture.Recording.OutputPath.Should().Be(savedPath);
        File.Exists(savedPath).Should().BeTrue();
    }

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
        var ffprobePath = await InstallFakeFfprobeReporting720pAsync();

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
    public async Task CompletedImportScoresTheRecordingAgainstTheProfilesCustomFormats()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var fixture = await NamingFixture.CreateAsync();
        var format = new CustomFormat { Name = "720p", Specifications = new List<FormatSpecification>
        {
            new() { Name = "Title", Implementation = "ReleaseTitleSpecification", Required = true,
                Fields = new Dictionary<string, object> { { "value", "720p" } } },
        } };
        fixture.Db.CustomFormats.Add(format);
        await fixture.Db.SaveChangesAsync();
        var profile = new QualityProfile { Name = "DVR fixture" };
        profile.FormatItems.Add(new ProfileFormatItem { FormatId = format.Id, Score = 25 });
        fixture.Db.QualityProfiles.Add(profile);
        await fixture.Db.SaveChangesAsync();
        fixture.Recording.Event!.League!.QualityProfileId = profile.Id;
        fixture.Recording.OutputPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        fixture.Recording.Method = DvrRecordingMethod.Catchup;
        await fixture.Db.SaveChangesAsync();
        var ffprobePath = await InstallFakeFfprobeReporting720pAsync();
        using var formatCache = new CustomFormatMatchCache(NullLogger<CustomFormatMatchCache>.Instance);
        var evaluator = new ReleaseEvaluator(NullLogger<ReleaseEvaluator>.Instance,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance), formatCache);

        try
        {
            var imported = await fixture.CreateEventDvrService(evaluator).ImportCompletedRecordingAsync(fixture.Recording.Id);

            imported.Should().BeTrue();
            var recording = await fixture.Db.DvrRecordings.SingleAsync();
            recording.CustomFormatScore.Should().Be(25,
                "the probed 720p recording matches the profile's 720p custom format");
        }
        finally
        {
            File.Delete(ffprobePath);
        }
    }

    /// <summary>
    /// Puts an ffprobe next to the test assembly that reports a 1280x720 h264
    /// file with stereo AAC; the caller deletes it.
    /// </summary>
    private static async Task<string> InstallFakeFfprobeReporting720pAsync()
    {
        var ffprobePath = Path.Combine(AppContext.BaseDirectory, "ffprobe");
        File.Exists(ffprobePath).Should().BeFalse("the test must not replace a real ffprobe binary");
        await File.WriteAllTextAsync(ffprobePath,
            "#!/bin/sh\n" +
            "printf '%s\\n' '{\"streams\":[{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":1280,\"height\":720,\"avg_frame_rate\":\"60/1\"},{\"codec_type\":\"audio\",\"codec_name\":\"aac\",\"channels\":2}],\"format\":{\"format_name\":\"mov,mp4\"}}'\n");
        File.SetUnixFileMode(ffprobePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return ffprobePath;
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

    [Fact]
    public async Task ADirectImportMarksTheFileAsAnIptvRecording()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        fixture.Recording.OutputPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        await fixture.Db.SaveChangesAsync();

        var imported = await fixture.CreateEventDvrService()
            .ImportCompletedRecordingAsync(fixture.Recording.Id);

        imported.Should().BeTrue();
        fixture.Db.ChangeTracker.Clear();
        (await fixture.Db.EventFiles.SingleAsync()).IsIptvRecording.Should().BeTrue();
    }

    [Fact]
    public async Task CompletedRecordingAlreadyLinkedByPathSettlesWithoutAddingAnotherFile()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var path = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        fixture.Recording.OutputPath = path;
        fixture.Db.EventFiles.Add(new EventFile
        {
            EventId = fixture.Recording.EventId!.Value,
            FilePath = path,
            Quality = "HDTV-1080p",
            Exists = false
        });
        await fixture.Db.SaveChangesAsync();

        var imported = await fixture.CreateEventDvrService()
            .ImportCompletedRecordingAsync(fixture.Recording.Id);

        imported.Should().BeTrue();
        fixture.Db.ChangeTracker.Clear();
        (await fixture.Db.EventFiles.CountAsync()).Should().Be(1);
        (await fixture.Db.EventFiles.SingleAsync()).Exists.Should().BeTrue();
        (await fixture.Db.Events.SingleAsync()).HasFile.Should().BeTrue();
        (await fixture.Db.Events.SingleAsync()).FilePath.Should().Be(path);
        var recording = await fixture.Db.DvrRecordings.SingleAsync();
        recording.Status.Should().Be(DvrRecordingStatus.Imported);
        recording.ImportedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task BulkImportQueuesLateRecordingForManualChoiceWhenIndexerOwnsItsPart()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var recordingPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        var indexerPath = fixture.CreateRecordingFile("Fixture - WEBDL-1080p.mkv");
        fixture.Recording.OutputPath = recordingPath;
        fixture.Db.EventFiles.Add(new EventFile
        {
            EventId = fixture.Recording.EventId!.Value,
            FilePath = indexerPath,
            Quality = "WEBDL-1080p",
            Added = fixture.Recording.ScheduledEnd.AddMinutes(-1),
            Exists = true
        });
        await fixture.Db.SaveChangesAsync();

        var service = fixture.CreateEventDvrService();
        var imported = await service.ImportAllCompletedRecordingsAsync();
        (await service.ImportAllCompletedRecordingsAsync()).Should().Be(0);

        imported.Should().Be(0);
        File.Exists(recordingPath).Should().BeTrue();
        fixture.Db.ChangeTracker.Clear();
        (await fixture.Db.EventFiles.SingleAsync()).FilePath.Should().Be(indexerPath);
        (await fixture.Db.DvrRecordings.SingleAsync()).Status.Should().Be(DvrRecordingStatus.Completed);
        var pending = await fixture.Db.PendingImports.SingleAsync();
        pending.FilePath.Should().Be(recordingPath);
        pending.SuggestedEventId.Should().Be(fixture.Recording.EventId);
        pending.Status.Should().Be(PendingImportStatus.Pending);

        pending.Status = PendingImportStatus.Importing;
        await fixture.Db.SaveChangesAsync();
        (await service.ImportAllCompletedRecordingsAsync()).Should().Be(0);
        (await fixture.Db.PendingImports.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task BulkImportDoesNotQueueAnotherRecordingAsAnIndexerConflict()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var recordingPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        var heldPath = fixture.CreateRecordingFile("Fixture - HDTV-720p.DVR.ts");
        fixture.Recording.OutputPath = recordingPath;
        fixture.Db.EventFiles.Add(new EventFile
        {
            EventId = fixture.Recording.EventId!.Value,
            FilePath = heldPath,
            Quality = "HDTV-720p",
            Source = "IPTV",
            Exists = true
        });
        await fixture.Db.SaveChangesAsync();

        var service = fixture.CreateEventDvrService();
        (await service.ImportAllCompletedRecordingsAsync()).Should().Be(0);
        (await service.ImportAllCompletedRecordingsAsync()).Should().Be(0);
        (await fixture.Db.PendingImports.CountAsync()).Should().Be(0);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(heldPath).Should().BeTrue();
    }

    [Fact]
    public async Task DirectImportDoesNotQueueAnotherRecordingAsAChoice()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var recordingPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        var heldPath = fixture.CreateRecordingFile("Fixture - HDTV-720p.DVR.ts");
        fixture.Recording.OutputPath = recordingPath;
        fixture.Db.EventFiles.Add(new EventFile
        {
            EventId = fixture.Recording.EventId!.Value,
            FilePath = heldPath,
            Quality = "HDTV-720p",
            Source = "IPTV",
            IsIptvRecording = true,
            Exists = true
        });
        await fixture.Db.SaveChangesAsync();

        (await fixture.CreateEventDvrService().ImportCompletedRecordingAsync(fixture.Recording.Id)).Should().BeFalse();
        (await fixture.Db.PendingImports.CountAsync()).Should().Be(0);
        (await fixture.Db.EventFiles.SingleAsync()).FilePath.Should().Be(heldPath);
    }

    [Fact]
    public async Task MissingIndexerFileKeepsItsRowAndQueuesTheRecordingForChoice()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var recordingPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        var missingPath = Path.Combine(fixture.Directory, "temporarily-unavailable.mkv");
        fixture.Recording.OutputPath = recordingPath;
        var missingFile = new EventFile
        {
            EventId = fixture.Recording.EventId!.Value,
            FilePath = missingPath,
            Quality = "WEBDL-1080p",
            Added = fixture.Recording.ScheduledEnd.AddMinutes(-1),
            Exists = false,
            MissingSince = DateTime.UtcNow
        };
        fixture.Db.EventFiles.Add(missingFile);
        await fixture.Db.SaveChangesAsync();

        var service = fixture.CreateEventDvrService();
        (await service.ImportCompletedRecordingAsync(fixture.Recording.Id)).Should().BeFalse();
        (await service.ImportAllCompletedRecordingsAsync()).Should().Be(0);
        fixture.Db.ChangeTracker.Clear();
        (await fixture.Db.EventFiles.SingleAsync()).FilePath.Should().Be(missingPath);
        (await fixture.Db.DvrRecordings.SingleAsync()).Status.Should().Be(DvrRecordingStatus.Completed);
        (await fixture.Db.DvrRecordings.SingleAsync()).ImportedAt.Should().BeNull();
        File.Exists(recordingPath).Should().BeTrue();
        var choice = await fixture.Db.PendingImports.SingleAsync();
        choice.DownloadId.Should().Be($"dvr-{fixture.Recording.Id}");
        choice.Status.Should().Be(PendingImportStatus.Pending);
        choice.ErrorMessage.Should().Contain("missing from disk");
    }

    [Fact]
    public async Task ExistingIndexerFileTakesPriorityOverAnOlderMissingRow()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var recordingPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        var livePath = fixture.CreateRecordingFile("Fixture - WEBDL-1080p.mkv");
        fixture.Recording.OutputPath = recordingPath;
        fixture.Db.EventFiles.AddRange(
            new EventFile
            {
                EventId = fixture.Recording.EventId!.Value,
                FilePath = Path.Combine(fixture.Directory, "missing.mkv"), Quality = "WEBDL-720p",
                Added = fixture.Recording.ScheduledEnd.AddMinutes(-10), Exists = false
            },
            new EventFile
            {
                EventId = fixture.Recording.EventId!.Value,
                FilePath = livePath, Quality = "WEBDL-1080p",
                Added = fixture.Recording.ScheduledEnd.AddMinutes(-5), Exists = true
            });
        await fixture.Db.SaveChangesAsync();

        (await fixture.CreateEventDvrService().ImportCompletedRecordingAsync(fixture.Recording.Id)).Should().BeFalse();
        var pending = await fixture.Db.PendingImports.SingleAsync();
        pending.ErrorMessage.Should().NotContain("missing from disk");
        pending.ErrorMessage.Should().Contain("WEBDL-1080p");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterIndexerWithSourcePrecedenceSettlesRecordingWithoutRetry(bool markedAsExisting)
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var recordingPath = fixture.CreateRecordingFile("Fixture - HDTV-1080p.DVR.mp4");
        var livePath = fixture.CreateRecordingFile("Fixture - WEBDL-1080p.mkv");
        fixture.Recording.OutputPath = recordingPath;
        var configService = fixture.CreateConfigService();
        var config = await configService.GetConfigAsync();
        config.DvrReplaceRecordingsWithIndexerReleases = true;
        await configService.SaveConfigAsync(config);
        fixture.Db.EventFiles.Add(new EventFile
        {
            EventId = fixture.Recording.EventId!.Value,
            FilePath = livePath, Quality = "WEBDL-1080p",
            Added = fixture.Recording.ScheduledEnd.AddMinutes(5), Exists = markedAsExisting
        });
        await fixture.Db.SaveChangesAsync();

        var service = fixture.CreateEventDvrService();
        (await service.ImportCompletedRecordingAsync(fixture.Recording.Id)).Should().BeFalse();
        (await service.ImportAllCompletedRecordingsAsync()).Should().Be(0);
        (await fixture.Db.PendingImports.CountAsync()).Should().Be(0);
        (await fixture.Db.DvrRecordings.SingleAsync()).Status.Should().Be(DvrRecordingStatus.Ignored);
        (await fixture.Db.EventFiles.SingleAsync()).Exists.Should().BeTrue();
        File.Exists(recordingPath).Should().BeTrue();
    }

    [Fact]
    public async Task FailedCaptureIsKeptInRecoveryFolderOutsideTheLibraryScan()
    {
        await using var fixture = await NamingFixture.CreateAsync();
        var path = Path.Combine(fixture.Directory, ".Fixture - HDTV-1080p.DVR.ts");
        await File.WriteAllBytesAsync(path, new byte[2 * 1024 * 1024]);
        fixture.Recording.OutputPath = path;
        fixture.Recording.Status = DvrRecordingStatus.Recording;
        fixture.Recording.ScheduledStart = DateTime.UtcNow.AddMinutes(-10);
        fixture.Recording.ScheduledEnd = DateTime.UtcNow.AddHours(1);
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.HandleRecorderExitAsync(fixture.Recording.Id, 1, "stream ended");

        fixture.Db.ChangeTracker.Clear();
        var recording = await fixture.Db.DvrRecordings.SingleAsync();
        recording.Status.Should().Be(DvrRecordingStatus.Failed);
        recording.OutputPath.Should().Contain(".sportarr-dvr-recovery");
        File.Exists(path).Should().BeFalse();
        File.Exists(recording.OutputPath).Should().BeTrue();
        Sportarr.Api.Helpers.LibraryPathFilter.IsExcluded(recording.OutputPath).Should().BeTrue();
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

        public EventDvrService CreateEventDvrService(ReleaseEvaluator? releaseEvaluator = null)
        {
            return new EventDvrService(
                NullLogger<EventDvrService>.Instance,
                Db,
                Service,
                null!,
                null!,
                new FFmpegRecorderService(NullLogger<FFmpegRecorderService>.Instance, GetConfig(), null!),
                releaseEvaluator!,
                null!,
                null!,
                GetConfig(),
                null!);
        }

        public ConfigService CreateConfigService() => GetConfig();

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
