using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A remote path arrives from the download client, so one carrying ".."
/// segments can combine into somewhere outside the mapping. Whatever sits
/// there would be treated as the download's contents and imported, renamed or
/// moved, so a result that leaves the local base is refused.
/// </summary>
public class RemotePathMappingConfinementTests
{
    private static RemotePathMappingService CreateService(params RemotePathMapping[] mappings)
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new SportarrDbContext(options);
        db.RemotePathMappings.AddRange(mappings);
        db.SaveChanges();

        return new RemotePathMappingService(db, NullLogger<RemotePathMappingService>.Instance);
    }

    private static RemotePathMapping Mapping(string remote, string local) => new()
    {
        Host = "downloader",
        RemotePath = remote,
        LocalPath = local
    };

    [Fact]
    public async Task A_path_inside_the_mapping_is_remapped()
    {
        var service = CreateService(Mapping("/remote/downloads", "/local/downloads"));

        var result = await service.RemapRemoteToLocalAsync("downloader", "/remote/downloads/event.mkv");

        result.Should().Be(Path.Combine("/local/downloads", "event.mkv"));
    }

    [Fact]
    public async Task A_path_climbing_out_of_the_mapping_is_refused()
    {
        var service = CreateService(Mapping("/remote/downloads", "/local/downloads"));
        const string escaping = "/remote/downloads/../../etc/passwd";

        var result = await service.RemapRemoteToLocalAsync("downloader", escaping);

        result.Should().Be(escaping, "an escaping path is left unmapped rather than resolved");
    }

    [Fact]
    public async Task A_sibling_differing_only_by_case_is_refused_where_case_matters()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            // These hosts genuinely treat the two names as one folder, so
            // there is nothing to escape into.
            return;
        }

        var service = CreateService(Mapping("/remote/downloads", "/local/downloads"));
        const string sibling = "/remote/downloads/../Downloads/event.mkv";

        var result = await service.RemapRemoteToLocalAsync("downloader", sibling);

        result.Should().Be(sibling,
            "/local/Downloads is a different folder from /local/downloads on a case-sensitive host");
    }
}
