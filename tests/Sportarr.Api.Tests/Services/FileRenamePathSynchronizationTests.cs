using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Services;

public sealed class FileRenamePathSynchronizationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "sportarr-file-rename-path-tests-" + Guid.NewGuid().ToString("N"));

    public FileRenamePathSynchronizationTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task SeasonRenumberingUpdatesEventAndEventFilePaths()
    {
        await using var rig = CreateRig();
        var oldPath = WriteFile("NFL - S2026E85 - HOU vs BUF.mkv");
        var expectedPath = Path.Combine(_tempDir, "NFL - S2026E57 - HOU vs BUF.mkv");
        var league = NewLeague(1);
        var evt = NewEvent(1, league, 57, oldPath);
        evt.Files.Add(NewFile(1, evt, oldPath));
        rig.Db.AddRange(league, evt, NewSettings());
        await rig.Db.SaveChangesAsync();

        var renamed = await rig.Service.RenameAllFilesInSeasonAsync(league.Id, "2026", numberingOnly: true);

        renamed.Should().Be(1);
        File.Exists(oldPath).Should().BeFalse();
        File.Exists(expectedPath).Should().BeTrue();
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.SingleAsync()).FilePath.Should().Be(expectedPath);
        (await rig.Db.EventFiles.SingleAsync(file => file.Id == 1)).FilePath.Should().Be(expectedPath);
    }

    [Fact]
    public async Task SingleFileRenameRepairsMissingLegacyPathWhenAnotherRowIsStale()
    {
        await using var rig = CreateRig();
        var oldPath = WriteFile("NFL - S2026E85 - HOU vs BUF.mkv");
        var expectedPath = Path.Combine(_tempDir, "NFL - S2026E57 - HOU vs BUF.mkv");
        var league = NewLeague(1);
        var evt = NewEvent(1, league, 57, null);
        evt.HasFile = true;
        evt.Files.Add(NewFile(1, evt, oldPath));
        evt.Files.Add(NewFile(2, evt, Path.Combine(_tempDir, "missing.mkv")));
        rig.Db.AddRange(league, evt);
        await rig.Db.SaveChangesAsync();

        var renamed = await rig.Service.RenameEventFilesAsync(evt.Id, NewSettings());

        renamed.Should().Be(1);
        File.Exists(expectedPath).Should().BeTrue();
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.SingleAsync()).FilePath.Should().Be(expectedPath);
        (await rig.Db.EventFiles.SingleAsync(file => file.Id == 1)).FilePath.Should().Be(expectedPath);
        (await rig.Db.EventFiles.SingleAsync(file => file.Id == 2)).Exists.Should().BeFalse();
    }

    [Fact]
    public async Task RenamingAnotherPartDoesNotReplaceThePrimaryEventPath()
    {
        await using var rig = CreateRig();
        var primaryPath = WriteFile("NFL - S2026E57 - HOU vs BUF.mkv");
        var oldPartPath = WriteFile("NFL - S2026E85 - HOU vs BUF - pt2.mkv");
        var expectedPartPath = Path.Combine(_tempDir, "NFL - S2026E57 - HOU vs BUF - pt2.mkv");
        var league = NewLeague(1);
        var evt = NewEvent(1, league, 57, primaryPath);
        evt.Files.Add(NewFile(1, evt, primaryPath));
        evt.Files.Add(NewFile(2, evt, oldPartPath, partNumber: 2));
        rig.Db.AddRange(league, evt, NewSettings());
        await rig.Db.SaveChangesAsync();

        var renamed = await rig.Service.RenameAllFilesInSeasonAsync(league.Id, "2026", numberingOnly: true);

        renamed.Should().Be(1);
        File.Exists(expectedPartPath).Should().BeTrue();
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.SingleAsync()).FilePath.Should().Be(primaryPath);
        (await rig.Db.EventFiles.SingleAsync(file => file.Id == 2)).FilePath.Should().Be(expectedPartPath);
    }

    [Fact]
    public async Task BatchRenameDoesNotFollowAnotherFilesOldPathWithinTheSameEvent()
    {
        await using var rig = CreateRig();
        var firstOldPath = WriteFile("NFL - S2026E57 - HOU vs BUF.mkv");
        var secondOldPath = WriteFile("NFL - S2026E57 - HOU vs BUF - pt1.mkv");
        var firstExpectedPath = secondOldPath;
        var secondExpectedPath = Path.Combine(_tempDir, "NFL - S2026E57 - HOU vs BUF - pt2.mkv");
        var league = NewLeague(1);
        var evt = NewEvent(1, league, 57, firstOldPath);
        evt.Files.Add(NewFile(1, evt, firstOldPath, partNumber: 1));
        evt.Files.Add(NewFile(2, evt, secondOldPath, partNumber: 2));
        rig.Db.AddRange(league, evt, NewSettings());
        await rig.Db.SaveChangesAsync();

        var renamed = await rig.Service.RenameAllFilesInSeasonAsync(league.Id, "2026");

        renamed.Should().Be(2);
        File.Exists(firstExpectedPath).Should().BeTrue();
        File.Exists(secondExpectedPath).Should().BeTrue();
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.SingleAsync()).FilePath.Should().Be(firstExpectedPath);
    }

    [Fact]
    public async Task LinuxRenameDoesNotMatchASeparatePathThatDiffersOnlyByCase()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        await using var rig = CreateRig();
        var selectedPath = WriteFile("NFL - S2026E85 - HOU VS BUF.MKV");
        var movedPath = WriteFile("nfl - s2026e85 - hou vs buf.mkv");
        var expectedPath = Path.Combine(_tempDir, "NFL - S2026E57 - HOU vs BUF.mkv");
        var league = NewLeague(1);
        var evt = NewEvent(1, league, 57, selectedPath);
        evt.Files.Add(NewFile(1, evt, selectedPath));
        evt.Files.Add(NewFile(2, evt, movedPath));
        rig.Db.AddRange(league, evt);
        await rig.Db.SaveChangesAsync();

        var renamed = await rig.Service.RenameEventFilesAsync(evt.Id, NewSettings(), new[] { 2 });

        renamed.Should().Be(1);
        File.Exists(expectedPath).Should().BeTrue();
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.SingleAsync()).FilePath.Should().Be(selectedPath);
    }

    [Fact]
    public async Task FailedSeasonBatchRestoresEventAndEventFilePaths()
    {
        await using var rig = CreateRig();
        var firstOldPath = WriteFile("NFL - S2026E85 - HOU vs BUF.mkv");
        var secondOldPath = WriteFile("NFL - S2026E86 - NYG vs DAL.mkv");
        var thirdOldPath = WriteFile("NFL - S2026E87 - NYG vs DAL.mkv");
        var blockedPath = Path.Combine(_tempDir, "NFL - S2026E58 - NYG vs DAL.mkv");
        // File.Exists ignores a directory. The final File.Move then fails.
        Directory.CreateDirectory(blockedPath);
        var league = NewLeague(1);
        var first = NewEvent(1, league, 57, firstOldPath);
        var second = NewEvent(2, league, 58, secondOldPath);
        var third = NewEvent(3, league, 59, thirdOldPath);
        first.FilePath = null;
        first.HasFile = true;
        first.Files.Add(NewFile(1, first, firstOldPath));
        second.Files.Add(NewFile(2, second, secondOldPath));
        third.Files.Add(NewFile(3, third, thirdOldPath));
        rig.Db.AddRange(league, first, second, third, NewSettings());
        await rig.Db.SaveChangesAsync();

        var renamed = await rig.Service.RenameAllFilesInSeasonAsync(league.Id, "2026", numberingOnly: true);

        renamed.Should().Be(0);
        File.Exists(firstOldPath).Should().BeTrue();
        File.Exists(secondOldPath).Should().BeTrue();
        File.Exists(thirdOldPath).Should().BeTrue();
        first.FilePath.Should().BeNull();
        first.Files.Single().FilePath.Should().Be(firstOldPath);
        second.FilePath.Should().Be(secondOldPath);
        second.Files.Single().FilePath.Should().Be(secondOldPath);
        third.FilePath.Should().Be(thirdOldPath);
        third.Files.Single().FilePath.Should().Be(thirdOldPath);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.SingleAsync(evt => evt.Id == first.Id)).FilePath.Should().BeNull();
        (await rig.Db.Events.SingleAsync(evt => evt.Id == second.Id)).FilePath.Should().Be(secondOldPath);
        (await rig.Db.Events.SingleAsync(evt => evt.Id == third.Id)).FilePath.Should().Be(thirdOldPath);
        (await rig.Db.EventFiles.SingleAsync(file => file.Id == 1)).FilePath.Should().Be(firstOldPath);
        (await rig.Db.EventFiles.SingleAsync(file => file.Id == 2)).FilePath.Should().Be(secondOldPath);
        (await rig.Db.EventFiles.SingleAsync(file => file.Id == 3)).FilePath.Should().Be(thirdOldPath);
    }

    [Fact]
    public async Task ReassigningOnlyFileClearsSourceAndPopulatesTargetPath()
    {
        await using var rig = CreateRig();
        var oldPath = WriteFile("NFL - S2026E57 - HOU vs BUF.mkv");
        var expectedPath = Path.Combine(_tempDir, "NFL - S2026E58 - NYG vs DAL.mkv");
        var league = NewLeague(1);
        var source = NewEvent(1, league, 57, oldPath);
        var target = NewEvent(2, league, 58, null);
        source.FileSize = 1024;
        source.Quality = "WEBDL-1080p";
        source.Files.Add(NewFile(1, source, oldPath, quality: "WEBDL-1080p"));
        rig.Db.AddRange(league, source, target, NewSettings());
        await rig.Db.SaveChangesAsync();

        var result = await rig.Service.ReassignFileAsync(1, target.Id);

        result.Success.Should().BeTrue(result.Error);
        result.NewPath.Should().Be(expectedPath);
        File.Exists(expectedPath).Should().BeTrue();
        rig.Db.ChangeTracker.Clear();
        var persistedSource = await rig.Db.Events.SingleAsync(evt => evt.Id == source.Id);
        persistedSource.HasFile.Should().BeFalse();
        persistedSource.FilePath.Should().BeNull();
        persistedSource.FileSize.Should().BeNull();
        persistedSource.Quality.Should().BeNull();
        var persistedTarget = await rig.Db.Events.SingleAsync(evt => evt.Id == target.Id);
        persistedTarget.HasFile.Should().BeTrue();
        persistedTarget.FilePath.Should().Be(expectedPath);
        persistedTarget.FileSize.Should().Be(1024);
        persistedTarget.Quality.Should().Be("WEBDL-1080p");
    }

    [Fact]
    public async Task ReassigningFileDoesNotReplaceTargetsExistingPrimaryPath()
    {
        await using var rig = CreateRig();
        var sourcePath = WriteFile("NFL - S2026E57 - HOU vs BUF.mkv", 4096);
        var targetPrimaryPath = WriteFile("NFL - S2026E58 - NYG vs DAL.mkv", 2048);
        var league = NewLeague(1);
        var source = NewEvent(1, league, 57, sourcePath);
        var target = NewEvent(2, league, 58, targetPrimaryPath);
        source.FileSize = 4096;
        source.Quality = "WEBDL-1080p";
        target.FileSize = 2048;
        target.Quality = "HDTV-720p";
        source.Files.Add(NewFile(1, source, sourcePath, partNumber: 2, size: 4096, quality: "WEBDL-1080p"));
        target.Files.Add(NewFile(2, target, targetPrimaryPath, size: 2048, quality: "HDTV-720p"));
        rig.Db.AddRange(league, source, target, NewSettings());
        await rig.Db.SaveChangesAsync();

        var result = await rig.Service.ReassignFileAsync(1, target.Id);

        result.Success.Should().BeTrue(result.Error);
        result.NewPath.Should().EndWith("NFL - S2026E58 - NYG vs DAL - pt2.mkv");
        File.Exists(result.NewPath).Should().BeTrue();
        rig.Db.ChangeTracker.Clear();
        var persistedSource = await rig.Db.Events.SingleAsync(evt => evt.Id == source.Id);
        persistedSource.HasFile.Should().BeFalse();
        persistedSource.FilePath.Should().BeNull();
        persistedSource.FileSize.Should().BeNull();
        persistedSource.Quality.Should().BeNull();
        var persistedTarget = await rig.Db.Events.SingleAsync(evt => evt.Id == target.Id);
        persistedTarget.HasFile.Should().BeTrue();
        persistedTarget.FilePath.Should().Be(targetPrimaryPath);
        persistedTarget.FileSize.Should().Be(2048);
        persistedTarget.Quality.Should().Be("HDTV-720p");
    }

    [Fact]
    public async Task ReassigningOneFightCardPartKeepsTargetWantedForOtherParts()
    {
        await using var rig = CreateRig();
        var sourcePath = WriteFile("UFC - S2026E57 - UFC 998 - pt3.mkv");
        var league = NewLeague(1);
        league.Name = "UFC";
        league.Sport = "Fighting";
        var source = NewEvent(1, league, 57, sourcePath);
        source.Title = "UFC 998";
        source.Sport = "Fighting";
        var target = NewEvent(2, league, 58, null);
        target.Title = "UFC 999";
        target.Sport = "Fighting";
        var mainCard = NewFile(1, source, sourcePath, partNumber: 3);
        mainCard.PartName = "Main Card";
        source.Files.Add(mainCard);
        rig.Db.AddRange(league, source, target, NewSettings());
        await rig.Db.SaveChangesAsync();

        var result = await rig.Service.ReassignFileAsync(mainCard.Id, target.Id);

        result.Success.Should().BeTrue(result.Error);
        File.Exists(result.NewPath).Should().BeTrue();
        rig.Db.ChangeTracker.Clear();
        var persistedTarget = await rig.Db.Events.SingleAsync(evt => evt.Id == target.Id);
        persistedTarget.HasFile.Should().BeFalse();
        (await rig.Db.EventFiles.SingleAsync()).EventId.Should().Be(target.Id);
    }

    [Fact]
    public async Task RenameClearsCompletionWhenTheSelectedFileIsMissing()
    {
        await using var rig = CreateRig();
        var missingPath = Path.Combine(_tempDir, "UFC - S2026E57 - UFC 998 - pt3.mkv");
        var league = NewLeague(1);
        league.Name = "UFC";
        league.Sport = "Fighting";
        var evt = NewEvent(1, league, 57, missingPath);
        evt.Title = "UFC 998";
        evt.Sport = "Fighting";
        var mainCard = NewFile(1, evt, missingPath, partNumber: 3);
        mainCard.PartName = "Main Card";
        evt.Files.Add(mainCard);
        rig.Db.AddRange(league, evt, NewSettings());
        await rig.Db.SaveChangesAsync();

        var renamed = await rig.Service.RenameEventFilesAsync(evt.Id, NewSettings());

        renamed.Should().Be(0);
        rig.Db.ChangeTracker.Clear();
        var persisted = await rig.Db.Events.SingleAsync();
        persisted.HasFile.Should().BeFalse();
        persisted.FilePath.Should().BeNull();
        (await rig.Db.EventFiles.SingleAsync()).Exists.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingHubNumbersDoNotRenumberSeasonWithPartialMedia(bool legacyPathOnly)
    {
        await using var rig = CreateRig();
        var partialPath = WriteFile("UFC - S2026E57 - UFC Fight Night 998 - pt1.mkv");
        var league = NewLeague(1);
        league.Name = "UFC";
        league.Sport = "Fighting";
        var first = NewEvent(1, league, 57, legacyPathOnly ? partialPath : null);
        first.Title = "UFC Fight Night 998";
        first.Sport = "Fighting";
        first.HasFile = false;
        if (!legacyPathOnly)
        {
            var prelims = NewFile(1, first, partialPath, partNumber: 1);
            prelims.PartName = "Prelims";
            first.Files.Add(prelims);
        }
        var second = NewEvent(2, league, 58, null);
        second.Title = "UFC Fight Night 999";
        second.Sport = "Fighting";
        rig.Db.AddRange(league, first, second);
        await rig.Db.SaveChangesAsync();

        var renumbered = await rig.Service.RecalculateEpisodeNumbersAsync(league.Id, "2026");

        renumbered.Should().Be(0);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.OrderBy(evt => evt.Id).Select(evt => evt.EpisodeNumber).ToListAsync())
            .Should().Equal(57, 58);
    }

    private string WriteFile(string name, int size = 1024)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    private static League NewLeague(int id) => new()
    {
        Id = id,
        Name = "NFL",
        Sport = "American Football",
        ExternalId = "lg-1"
    };

    private static Event NewEvent(int id, League league, int episode, string? filePath) => new()
    {
        Id = id,
        Title = episode == 57 ? "HOU vs BUF" : "NYG vs DAL",
        Sport = "American Football",
        League = league,
        LeagueId = league.Id,
        Season = "2026",
        SeasonNumber = 2026,
        EpisodeNumber = episode,
        EventDate = new DateTime(2026, 9, 13, 17, 0, 0, DateTimeKind.Utc),
        HasFile = filePath != null,
        FilePath = filePath
    };

    private static EventFile NewFile(
        int id,
        Event evt,
        string path,
        int? partNumber = null,
        long size = 1024,
        string? quality = null) => new()
    {
        Id = id,
        Event = evt,
        EventId = evt.Id,
        FilePath = path,
        Size = size,
        Quality = quality,
        Exists = true,
        PartName = partNumber.HasValue ? "Part " + partNumber.Value : null,
        PartNumber = partNumber
    };

    private static MediaManagementSettings NewSettings() => new()
    {
        RenameEvents = true,
        StandardFileFormat = "{Series} - {Season}{Episode} - {Event Title}{Part}",
        ReorganizeFolders = false,
        DeleteEmptyFolders = false
    };

    private TestRig CreateRig()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new SportarrDbContext(options);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = _tempDir
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(configuration);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(new ConfigService(configuration, Mock.Of<ILogger<ConfigService>>()));
        var provider = services.BuildServiceProvider();
        var httpClient = new HttpClient(new UnavailableMetadataHandler());
        var configService = provider.GetRequiredService<ConfigService>();
        var notificationService = new NotificationService(
            provider,
            Mock.Of<ILogger<NotificationService>>(),
            httpClient,
            Mock.Of<IHttpClientFactory>());
        var parser = new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>());
        var service = new FileRenameService(
            db,
            new FileNamingService(Mock.Of<ILogger<FileNamingService>>()),
            new SportarrApiClient(
                httpClient,
                Mock.Of<ILogger<SportarrApiClient>>(),
                configuration,
                configService,
                new MemoryCache(new MemoryCacheOptions())),
            Mock.Of<ILogger<FileRenameService>>(),
            new DiskSpaceService(Mock.Of<ILogger<DiskSpaceService>>()),
            new CustomFormatService(parser),
            notificationService,
            Mock.Of<IMetadataWriterService>(),
            configService);
        return new TestRig(db, provider, httpClient, service);
    }

    private sealed class TestRig(
        SportarrDbContext db,
        ServiceProvider provider,
        HttpClient httpClient,
        FileRenameService service) : IAsyncDisposable
    {
        public SportarrDbContext Db { get; } = db;
        public FileRenameService Service { get; } = service;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await provider.DisposeAsync();
            httpClient.Dispose();
        }
    }

    private sealed class UnavailableMetadataHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
