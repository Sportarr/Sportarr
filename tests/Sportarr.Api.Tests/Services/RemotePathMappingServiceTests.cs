using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Covers RemotePathMappingService.RemapRemoteToLocalAsync, the shared
/// translation used by import and download monitoring when a download client
/// reports paths from another host or container. Pins down host matching,
/// longest-prefix-wins ordering, trailing-slash tolerance, Windows-style
/// separators, and the segment-boundary rule (a mapping for /data must not
/// claim /database/...).
/// </summary>
public class RemotePathMappingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public RemotePathMappingServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private SportarrDbContext CreateDb()
    {
        var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(_connection)
            .Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static RemotePathMappingService CreateService(SportarrDbContext db)
        => new(db, NullLogger<RemotePathMappingService>.Instance);

    private static RemotePathMapping Mapping(string host, string remotePath, string localPath)
        => new() { Host = host, RemotePath = remotePath, LocalPath = localPath };

    [Fact]
    public async Task Returns_path_unchanged_when_no_mappings_exist()
    {
        using var db = CreateDb();
        var service = CreateService(db);

        var result = await service.RemapRemoteToLocalAsync("seedbox", "/home/user/downloads/file.mkv");

        result.Should().Be("/home/user/downloads/file.mkv");
    }

    [Fact]
    public async Task Returns_path_unchanged_when_no_mapping_matches_the_host()
    {
        using var db = CreateDb();
        db.RemotePathMappings.Add(Mapping("other-box", "/downloads", "/data/downloads"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RemapRemoteToLocalAsync("seedbox", "/downloads/file.mkv");

        result.Should().Be("/downloads/file.mkv");
    }

    [Fact]
    public async Task Remaps_a_file_under_the_mapped_remote_path()
    {
        using var db = CreateDb();
        db.RemotePathMappings.Add(Mapping("seedbox", "/home/user/downloads", "/data/seedbox"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RemapRemoteToLocalAsync("seedbox", "/home/user/downloads/race/file.mkv");

        result.Should().Be("/data/seedbox/race/file.mkv");
    }

    [Fact]
    public async Task Matches_the_host_case_insensitively()
    {
        using var db = CreateDb();
        db.RemotePathMappings.Add(Mapping("SeedBox", "/downloads", "/data/seedbox"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RemapRemoteToLocalAsync("seedbox", "/downloads/file.mkv");

        result.Should().Be("/data/seedbox/file.mkv");
    }

    [Fact]
    public async Task Exact_match_returns_the_local_base_path()
    {
        using var db = CreateDb();
        db.RemotePathMappings.Add(Mapping("seedbox", "/downloads", "/data/seedbox"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RemapRemoteToLocalAsync("seedbox", "/downloads");

        result.Should().Be("/data/seedbox");
    }

    [Fact]
    public async Task Tolerates_trailing_slashes_on_both_the_mapping_and_the_input()
    {
        using var db = CreateDb();
        db.RemotePathMappings.Add(Mapping("seedbox", "/downloads/", "/data/seedbox/"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RemapRemoteToLocalAsync("seedbox", "/downloads/file.mkv/");

        result.Should().Be("/data/seedbox/file.mkv");
    }

    [Fact]
    public async Task Longest_remote_prefix_wins_when_mappings_nest()
    {
        using var db = CreateDb();
        db.RemotePathMappings.Add(Mapping("seedbox", "/downloads", "/data/general"));
        db.RemotePathMappings.Add(Mapping("seedbox", "/downloads/sports", "/data/sports"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RemapRemoteToLocalAsync("seedbox", "/downloads/sports/file.mkv");

        result.Should().Be("/data/sports/file.mkv");
    }

    [Fact]
    public async Task Does_not_match_across_path_segment_boundaries()
    {
        using var db = CreateDb();
        db.RemotePathMappings.Add(Mapping("seedbox", "/data", "/local/data"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RemapRemoteToLocalAsync("seedbox", "/database/file.mkv");

        result.Should().Be("/database/file.mkv");
    }

    [Fact]
    public async Task Translates_windows_style_remote_paths()
    {
        using var db = CreateDb();
        db.RemotePathMappings.Add(Mapping("winbox", @"C:\Downloads", "/data/downloads"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RemapRemoteToLocalAsync("winbox", @"C:\Downloads\race\file.mkv");

        result.Should().Be(Path.Combine("/data/downloads", "race", "file.mkv"));
    }

    [Fact]
    public async Task Returns_empty_path_unchanged()
    {
        using var db = CreateDb();
        db.RemotePathMappings.Add(Mapping("seedbox", "/downloads", "/data/seedbox"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RemapRemoteToLocalAsync("seedbox", "");

        result.Should().Be("");
    }
}
