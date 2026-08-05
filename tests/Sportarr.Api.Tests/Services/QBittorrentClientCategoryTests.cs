using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// GetTorrentStatusAsync is the hash lookup behind download-monitoring status
/// polling. qBittorrent is commonly shared with other *arr-style apps via
/// category, and a torrent's hash never disappears when it's just reassigned to
/// another app's category - only "not found by hash" ever removed a queue item,
/// so a recategorized torrent was tracked by Sportarr forever. These tests cover
/// the category scoping that makes a mismatched category report as not-found,
/// using the category the item was actually grabbed under (expectedCategory,
/// sourced from DownloadQueueItem.GrabCategory) rather than the client's current
/// default Category - a league bound to a per-root-folder category override
/// must not be mistaken for another app's download just because it differs from
/// the client's own default.
/// </summary>
public class QBittorrentClientCategoryTests
{
    private const string Hash = "abc123def456";

    private static QBittorrentClient CreateClient(List<QBittorrentTorrent> torrents)
    {
        var handler = new FakeTorrentsInfoHandler(torrents);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        return new QBittorrentClient(httpClient, NullLogger<QBittorrentClient>.Instance);
    }

    private static DownloadClient CreateConfig(string category) => new()
    {
        Name = "test",
        Type = DownloadClientType.QBittorrent,
        Host = "localhost",
        Port = 8080,
        Category = category,
        ApiKey = "test-key", // Bearer auth - skips the login round trip entirely
    };

    private static QBittorrentTorrent MakeTorrent(string category) => new()
    {
        Hash = Hash,
        Name = "Some Release",
        Category = category,
        ContentPath = "/downloads/some-release", // avoids the GetTorrentFilesAsync fallback call
    };

    [Fact]
    public async Task GetTorrentStatusAsync_CategoryReassignedToOtherApp_ReturnsNull()
    {
        var client = CreateClient([MakeTorrent("radarr")]);
        var config = CreateConfig("sportarr");

        var result = await client.GetTorrentStatusAsync(config, Hash, expectedCategory: "sportarr");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTorrentStatusAsync_CategoryStillMatches_ReturnsStatus()
    {
        var client = CreateClient([MakeTorrent("sportarr")]);
        var config = CreateConfig("sportarr");

        var result = await client.GetTorrentStatusAsync(config, Hash, expectedCategory: "sportarr");

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTorrentStatusAsync_NoExpectedOrConfiguredCategory_MatchesByHashOnly()
    {
        var client = CreateClient([MakeTorrent("radarr")]);
        var config = CreateConfig(category: "");

        var result = await client.GetTorrentStatusAsync(config, Hash, expectedCategory: null);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTorrentStatusAsync_ExpectedCategoryFallsBackToClientDefault_WhenGrabCategoryUnset()
    {
        // Legacy DownloadQueueItem rows (created before GrabCategory existed) pass
        // expectedCategory: null - status polling must fall back to the client's
        // current default Category, not skip scoping entirely.
        var client = CreateClient([MakeTorrent("radarr")]);
        var config = CreateConfig("sportarr");

        var result = await client.GetTorrentStatusAsync(config, Hash, expectedCategory: null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTorrentStatusAsync_GrabbedUnderRootFolderCategoryOverride_IsNotMistakenForAnotherApp()
    {
        // The download client's own default Category is "sportarr", but this
        // particular event's root folder overrode the category to "sportarr-4k"
        // at grab time. Comparing against the client's live default (the
        // pre-GrabCategory design) would have wrongly nulled this out as if
        // another app had taken it over - it never left Sportarr's control.
        var client = CreateClient([MakeTorrent("sportarr-4k")]);
        var config = CreateConfig("sportarr");

        var result = await client.GetTorrentStatusAsync(config, Hash, expectedCategory: "sportarr-4k");

        result.Should().NotBeNull();
    }

    private class FakeTorrentsInfoHandler(List<QBittorrentTorrent> torrents) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.Contains("torrents/info"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(torrents),
                };
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
