using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The shared save roots of a qBittorrent client are read once for a while,
/// and they belong to real qBittorrent clients only.
/// </summary>
public class QBittorrentSharedSavePathsTests
{
    public QBittorrentSharedSavePathsTests() => QBittorrentClient.ResetSharedSavePathsCache();

    private static DownloadClient Config(DownloadClientType type, int id) => new()
    {
        Id = id,
        Name = "client",
        Type = type,
        Host = "localhost",
        Port = 8080,
        Category = "sportarr",
        ApiKey = "test-key",
    };

    [Fact]
    public async Task TheRootsAreReadOnceAndIncludeTheTorrentsOwnSavePath()
    {
        var server = new Server();
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);
        var config = Config(DownloadClientType.QBittorrent, 901);

        var first = await client.GetAllDownloadsByCategoryAsync(config, "sportarr");
        var second = await client.GetAllDownloadsByCategoryAsync(config, "sportarr");

        server.PreferenceReads.Should().Be(1);
        server.CategoryReads.Should().Be(1);
        first.Single().SharedSavePaths.Should().Contain("/downloads").And.Contain("/downloads/sportarr");
        second.Single().SharedSavePaths.Should().Equal(first.Single().SharedSavePaths);
    }

    [Fact]
    public async Task AFailedReadIsNotKept()
    {
        var server = new Server { FailPreferences = true };
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);
        var config = Config(DownloadClientType.QBittorrent, 902);

        await client.GetAllDownloadsByCategoryAsync(config, "sportarr");
        server.FailPreferences = false;
        var later = await client.GetAllDownloadsByCategoryAsync(config, "sportarr");

        server.PreferenceReads.Should().Be(2);
        later.Single().SharedSavePaths.Should().Contain("/downloads");
    }

    [Fact]
    public async Task OtherClientTypesGetNoSharedSavePaths()
    {
        var server = new Server();
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.GetAllDownloadsByCategoryAsync(Config(DownloadClientType.Decypharr, 903), "sportarr");

        result.Single().SharedSavePaths.Should().BeEmpty();
        server.PreferenceReads.Should().Be(0);
    }

    private sealed class Server : HttpMessageHandler
    {
        public int PreferenceReads { get; private set; }
        public int CategoryReads { get; private set; }
        public bool FailPreferences { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response = path switch
            {
                "/api/v2/torrents/info" => new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[]
                    {
                        new QBittorrentTorrent
                        {
                            Hash = "0123456789abcdef0123456789abcdef01234567", Name = "NFL.2026.Week.5.720p.WEB",
                            Category = "sportarr", State = "stalledUP", Progress = 1,
                            SavePath = "/downloads/sportarr", ContentPath = "/downloads/sportarr/NFL.2026.Week.5.720p.WEB",
                        }
                    })
                },
                "/api/v2/app/preferences" => Preferences(),
                "/api/v2/torrents/categories" => Categories(),
                _ => new(HttpStatusCode.OK) { Content = new StringContent("Ok.") },
            };
            return Task.FromResult(response);
        }

        private HttpResponseMessage Preferences()
        {
            PreferenceReads++;
            return FailPreferences
                ? new(HttpStatusCode.InternalServerError)
                : new(HttpStatusCode.OK) { Content = new StringContent("{\"save_path\":\"/downloads\"}") };
        }

        private HttpResponseMessage Categories()
        {
            CategoryReads++;
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"sportarr\":{\"name\":\"sportarr\",\"savePath\":\"/downloads/sportarr\"}}")
            };
        }
    }
}
