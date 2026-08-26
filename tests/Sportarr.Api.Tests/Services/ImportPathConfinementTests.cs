using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Imports are confined to the folders downloads are supposed to land in, so a
/// path the client reports cannot pull an arbitrary file into the pipeline.
/// The client's download directory is an override though, and leaving it blank
/// to use the client's own default is the ordinary setup. Library roots say
/// where imports are written, not where downloads arrive, so confining against
/// them alone refused every normal import.
/// </summary>
public class ImportPathConfinementTests : IDisposable
{
    private readonly string _tempDir;

    public ImportPathConfinementTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sportarr-confinement-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static SportarrDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SportarrDbContext(options);
    }

    private string MakeFile(string relativeDir, string name)
    {
        var dir = Path.Combine(_tempDir, relativeDir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private static ProvideImportItemService CreateService(SportarrDbContext db)
    {
        var mapping = new Mock<IRemotePathMappingService>();
        mapping.Setup(m => m.RemapRemoteToLocalAsync(It.IsAny<string>(), It.IsAny<string>()))
               .ReturnsAsync((string _, string remote) => remote);

        return new ProvideImportItemService(db, mapping.Object,
            NullLogger<ProvideImportItemService>.Instance);
    }

    [Fact]
    public async Task A_client_using_its_own_default_directory_can_still_import()
    {
        using var db = CreateDb();

        // The library root is configured, as it always is. On its own it says
        // nothing about where this client puts its downloads.
        db.RootFolders.Add(new RootFolder { Path = Path.Combine(_tempDir, "library") });
        var client = new DownloadClient { Name = "qBittorrent", Host = "localhost", Directory = null };
        db.DownloadClients.Add(client);
        await db.SaveChangesAsync();

        var file = MakeFile("downloads/complete", "event.mkv");
        var download = new DownloadQueueItem { Title = "Event", DownloadId = "abc", DownloadClientId = client.Id, DownloadClient = client };

        var item = await CreateService(db).ProvideImportItemAsync(download, file);

        item.IsValid.Should().BeTrue(
            "a blank download directory means the client keeps its own default, which is a supported setup");
    }

    [Fact]
    public async Task A_path_outside_a_configured_download_directory_is_refused()
    {
        using var db = CreateDb();

        db.RootFolders.Add(new RootFolder { Path = Path.Combine(_tempDir, "library") });
        var client = new DownloadClient
        {
            Name = "qBittorrent",
            Host = "localhost",
            Directory = Path.Combine(_tempDir, "downloads")
        };
        db.DownloadClients.Add(client);
        await db.SaveChangesAsync();

        var stray = MakeFile("elsewhere", "someone-elses-file.mkv");
        var download = new DownloadQueueItem { Title = "Event", DownloadId = "abc", DownloadClientId = client.Id, DownloadClient = client };

        var item = await CreateService(db).ProvideImportItemAsync(download, stray);

        item.IsValid.Should().BeFalse("the client says where its downloads land and this is not under it");
    }

    [Fact]
    public async Task A_path_inside_a_configured_download_directory_is_accepted()
    {
        using var db = CreateDb();

        db.RootFolders.Add(new RootFolder { Path = Path.Combine(_tempDir, "library") });
        var client = new DownloadClient
        {
            Name = "qBittorrent",
            Host = "localhost",
            Directory = Path.Combine(_tempDir, "downloads")
        };
        db.DownloadClients.Add(client);
        await db.SaveChangesAsync();

        var file = MakeFile("downloads/complete", "event.mkv");
        var download = new DownloadQueueItem { Title = "Event", DownloadId = "abc", DownloadClientId = client.Id, DownloadClient = client };

        var item = await CreateService(db).ProvideImportItemAsync(download, file);

        item.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Another_hosts_mapping_does_not_confine_this_client()
    {
        using var db = CreateDb();

        db.RootFolders.Add(new RootFolder { Path = Path.Combine(_tempDir, "library") });
        db.RemotePathMappings.Add(new RemotePathMapping
        {
            Host = "some-other-box",
            RemotePath = "/remote/downloads",
            LocalPath = Path.Combine(_tempDir, "other")
        });
        var client = new DownloadClient { Name = "Deluge", Host = "localhost", Directory = null };
        db.DownloadClients.Add(client);
        await db.SaveChangesAsync();

        var file = MakeFile("downloads/complete", "event.mkv");
        var download = new DownloadQueueItem { Title = "Event", DownloadId = "abc", DownloadClientId = client.Id, DownloadClient = client };

        var item = await CreateService(db).ProvideImportItemAsync(download, file);

        item.IsValid.Should().BeTrue(
            "a mapping belonging to a different host says nothing about where this client downloads");
    }

    [Fact]
    public async Task A_remote_path_mapping_alone_is_enough_to_confine()
    {
        using var db = CreateDb();

        db.RootFolders.Add(new RootFolder { Path = Path.Combine(_tempDir, "library") });
        db.RemotePathMappings.Add(new RemotePathMapping
        {
            Host = "localhost",
            RemotePath = "/remote/downloads",
            LocalPath = Path.Combine(_tempDir, "downloads")
        });
        var client = new DownloadClient { Name = "Deluge", Host = "localhost", Directory = null };
        db.DownloadClients.Add(client);
        await db.SaveChangesAsync();

        var stray = MakeFile("elsewhere", "someone-elses-file.mkv");
        var download = new DownloadQueueItem { Title = "Event", DownloadId = "abc", DownloadClientId = client.Id, DownloadClient = client };

        var item = await CreateService(db).ProvideImportItemAsync(download, stray);

        item.IsValid.Should().BeFalse("the mapping says where this host's downloads land");
    }
}
