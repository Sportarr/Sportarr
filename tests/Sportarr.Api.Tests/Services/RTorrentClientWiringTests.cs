using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Deleting a torrent's files translates the path rTorrent reports and then
/// requires it to sit inside a folder this client downloads into. Both halves
/// need the path mapping service.
///
/// The constructor took the service and dropped it, which no compiler
/// complains about. A remote rTorrent then had no mapping and no mapped root,
/// so the torrent was erased, its files were left on disk, and the call still
/// reported success.
/// </summary>
public class RTorrentClientWiringTests
{
    private static object? FieldValue(RTorrentClient client, string name) =>
        typeof(RTorrentClient)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(client);

    [Fact]
    public void The_path_mapping_service_is_kept()
    {
        var mapping = Mock.Of<IRemotePathMappingService>();

        var client = new RTorrentClient(new HttpClient(), NullLogger<RTorrentClient>.Instance, mapping);

        FieldValue(client, "_pathMappingService")
            .Should().BeSameAs(mapping, "deletion cannot translate or confine a path without it");
    }

    [Fact]
    public void The_client_still_works_without_one()
    {
        var client = new RTorrentClient(new HttpClient(), NullLogger<RTorrentClient>.Instance);

        FieldValue(client, "_pathMappingService").Should().BeNull();
    }
}
