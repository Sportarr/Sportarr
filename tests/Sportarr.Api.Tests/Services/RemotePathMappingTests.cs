using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The remote path comes from the download client. One carrying ".." segments
/// combined into somewhere outside the mapped folder, and whatever sat there
/// was then treated as the download's contents, so it could be imported,
/// renamed or moved.
/// </summary>
public class RemotePathMappingTests
{
    private static SportarrDbContext Db()
    {
        var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.RemotePathMappings.Add(new RemotePathMapping
        {
            Host = "seedbox",
            RemotePath = "/remote/downloads",
            LocalPath = "/local/downloads",
        });
        db.SaveChanges();
        return db;
    }

    private static RemotePathMappingService Svc(SportarrDbContext db) =>
        new(db, Mock.Of<ILogger<RemotePathMappingService>>());

    [Fact]
    public async Task MapsAPathUnderTheRemoteRoot()
    {
        var mapped = await Svc(Db()).RemapRemoteToLocalAsync("seedbox", "/remote/downloads/NFL/game.mkv");

        mapped.Should().Be(Path.Combine("/local/downloads", "NFL", "game.mkv"));
    }

    [Fact]
    public async Task RefusesAPathThatClimbsOutOfTheMapping()
    {
        const string hostile = "/remote/downloads/../../etc/passwd";

        var mapped = await Svc(Db()).RemapRemoteToLocalAsync("seedbox", hostile);

        mapped.Should().Be(hostile, "an escaping path is left unmapped rather than pointed at the host filesystem");
        mapped.Should().NotContain("local");
    }

    [Fact]
    public async Task LeavesAPathUnderAnotherRootAlone()
    {
        var mapped = await Svc(Db()).RemapRemoteToLocalAsync("seedbox", "/elsewhere/file.mkv");

        mapped.Should().Be("/elsewhere/file.mkv");
    }

    [Fact]
    public async Task DoesNotClaimASiblingFolderWithTheSamePrefix()
    {
        var mapped = await Svc(Db()).RemapRemoteToLocalAsync("seedbox", "/remote/downloads-old/file.mkv");

        mapped.Should().Be("/remote/downloads-old/file.mkv");
    }
}
