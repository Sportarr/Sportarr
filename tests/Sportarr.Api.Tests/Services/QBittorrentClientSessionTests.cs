using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

[Collection(WallClockFixtureCollection.Name)]
public class QBittorrentClientSessionTests
{
    private static DownloadClient Config() => new()
    {
        Name = "qbit-test",
        Type = DownloadClientType.QBittorrent,
        Host = "localhost",
        Port = 8080,
        Username = "user",
        Password = "password"
    };

    [Fact]
    public async Task GetTorrentsAsync_ReauthenticatesAfterServerRestart()
    {
        var server = new RestartingQbittorrentHandler();
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        (await client.GetTorrentsAsync(Config())).Should().HaveCount(1);
        server.Restart();

        (await client.GetTorrentsAsync(Config())).Should().HaveCount(1);
        server.LoginCount.Should().Be(2);
    }

    [Fact]
    public async Task DeleteTorrentAsync_ReauthenticatesBeforeRetryingRejectedDelete()
    {
        var server = new RestartingQbittorrentHandler();
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        (await client.TestConnectionAsync(Config())).Should().BeTrue();
        server.Restart();

        (await client.DeleteTorrentAsync(Config(), "abc123")).Should().BeTrue();
        server.LoginCount.Should().Be(2);
        server.AcceptedDeletes.Should().Be(1);
    }

    [Fact]
    public async Task ParallelRequestsAfterRestart_ShareOneNewLogin()
    {
        var server = new RestartingQbittorrentHandler();
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        (await client.GetTorrentsAsync(Config())).Should().HaveCount(1);
        server.Restart();
        server.HoldRestartLogin();

        var first = client.GetTorrentsAsync(Config());
        await server.RestartLoginStarted.WaitAsync(TimeSpan.FromSeconds(5));
        var second = client.GetTorrentsAsync(Config());
        await Task.Delay(100);
        server.ReleaseRestartLogin();

        (await first).Should().HaveCount(1);
        (await second).Should().HaveCount(1);
        server.LoginCount.Should().Be(2);
    }

    [Fact]
    public async Task GetTorrentsAsync_QueuedLoginRespectsPollDeadline()
    {
        var server = new RestartingQbittorrentHandler();
        server.HoldRestartLogin();
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var first = client.GetTorrentsAsync(Config());
        await server.RestartLoginStarted.WaitAsync(TimeSpan.FromSeconds(5));
        var second = client.GetTorrentsAsync(Config());

        (await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(18)))).Should().Be(second);
        (await second).Should().BeNull();
        (await first).Should().BeNull();
        server.ReleaseRestartLogin();
    }

    [Fact]
    public async Task GetTorrentsAsync_CancelledReloginDisposesRejectedResponse()
    {
        var server = new RestartingQbittorrentHandler();
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        (await client.GetTorrentsAsync(Config())).Should().HaveCount(1);
        server.Restart();
        server.HoldRestartLogin();
        server.RejectedResponseDelay = TimeSpan.FromSeconds(7);

        var pending = client.GetTorrentsAsync(Config());
        await server.RestartLoginStarted.WaitAsync(TimeSpan.FromSeconds(10));

        (await pending).Should().BeNull();
        server.RejectedResponseDisposed.Should().BeTrue();
        server.ReleaseRestartLogin();
    }

    private sealed class RestartingQbittorrentHandler : HttpMessageHandler
    {
        private int _session = 1;
        private TaskCompletionSource? _restartLoginRelease;
        private readonly TaskCompletionSource _restartLoginStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int LoginCount { get; private set; }
        public int AcceptedDeletes { get; private set; }
        public bool RejectedResponseDisposed => _rejectedContent?.WasDisposed == true;
        public TimeSpan RejectedResponseDelay { get; set; }
        public Task RestartLoginStarted => _restartLoginStarted.Task;
        private TrackingContent? _rejectedContent;

        public void Restart() => _session++;
        public void HoldRestartLogin() => _restartLoginRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseRestartLogin() => _restartLoginRelease?.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/auth/login")
            {
                LoginCount++;
                if (_restartLoginRelease != null)
                {
                    _restartLoginStarted.TrySetResult();
                    await _restartLoginRelease.Task.WaitAsync(cancellationToken);
                }
                var login = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                login.Headers.TryAddWithoutValidation("Set-Cookie", $"SID=session-{_session}; path=/");
                return login;
            }

            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join(";", values)
                : string.Empty;
            if (!cookie.Contains($"SID=session-{_session}", StringComparison.Ordinal))
            {
                if (RejectedResponseDelay > TimeSpan.Zero)
                    await Task.Delay(RejectedResponseDelay, cancellationToken);
                _rejectedContent = new TrackingContent();
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = _rejectedContent };
            }

            if (path == "/api/v2/torrents/info")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new QBittorrentTorrent { Hash = "abc123", Name = "Test" } })
                };

            if (path == "/api/v2/app/version")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("5.1.0") };

            if (path == "/api/v2/torrents/delete")
            {
                AcceptedDeletes++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private sealed class TrackingContent : ByteArrayContent
        {
            public TrackingContent() : base(Array.Empty<byte>()) { }

            public bool WasDisposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
