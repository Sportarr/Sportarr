using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class RTorrentClientEndpointTests
{
    private sealed class DigestChallengeServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        private readonly string _challengeScheme;
        private readonly bool _redirectToCanonicalPath;

        public DigestChallengeServer(string challengeScheme = "Digest", bool redirectToCanonicalPath = false)
        {
            _challengeScheme = challengeScheme;
            _redirectToCanonicalPath = redirectToCanonicalPath;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serve = ServeAsync();
        }

        public int Port { get; }
        public bool SawDigestAuthorization { get; private set; }
        public bool SawBasicAuthorization { get; private set; }
        public bool SawBasicOnFirstRequest { get; private set; }
        public int RequestCount { get; private set; }

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                using var stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                string? line;
                string? authorization = null;
                var requestLine = await reader.ReadLineAsync(_stop.Token);
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_stop.Token)))
                {
                    if (line.StartsWith("Authorization: ", StringComparison.OrdinalIgnoreCase))
                        authorization = line[15..];
                }

                RequestCount++;
                if (_redirectToCanonicalPath && requestLine?.Contains(" /RPC2 ", StringComparison.Ordinal) == true)
                {
                    var redirect = "HTTP/1.1 307 Temporary Redirect\r\nLocation: /RPC2/\r\nConnection: close\r\nContent-Length: 0\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(redirect), _stop.Token);
                    continue;
                }
                var digestAuthorization = authorization?.StartsWith("Digest ", StringComparison.Ordinal) == true;
                var basicAuthorization = authorization == "Basic dXNlcjpzZWNyZXQ=";
                SawDigestAuthorization |= digestAuthorization;
                SawBasicAuthorization |= basicAuthorization;
                SawBasicOnFirstRequest |= RequestCount == 1 && basicAuthorization;
                var authorized = _challengeScheme == "Digest" ? digestAuthorization : basicAuthorization;
                var body = authorized
                    ? "<?xml version=\"1.0\"?><methodResponse><params><param><value><string>0.9.8</string></value></param></params></methodResponse>"
                    : string.Empty;
                var status = authorized ? "200 OK" : "401 Unauthorized";
                var headers = $"HTTP/1.1 {status}\r\nConnection: close\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n";
                if (!authorized)
                    headers += _challengeScheme == "Digest"
                        ? "WWW-Authenticate: Digest realm=\"ruTorrent\", nonce=\"test-nonce\", algorithm=MD5, qop=\"auth\"\r\n"
                        : "WWW-Authenticate: Basic realm=\"ruTorrent\"\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(headers + "\r\n" + body), _stop.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try { await _serve; }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            _stop.Dispose();
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public RecordingHandler(string? responseBody = null)
        {
            _responseBody = responseBody ??
                "<?xml version=\"1.0\"?><methodResponse><params><param><value><string>0.9.8</string></value></param></params></methodResponse>";
        }

        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public HttpMethod? Method { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Method = request.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "text/xml")
            });
        }
    }

    [Theory]
    [InlineData("/RPC2", "/RPC2")]
    [InlineData("/RPC2 ", "/RPC2")]
    [InlineData(" RPC2 ", "/RPC2")]
    [InlineData("/rutorrent/RPC2", "/rutorrent/RPC2")]
    [InlineData("/rutorrent", "/rutorrent/RPC2")]
    [InlineData("rutorrent", "/rutorrent/RPC2")]
    [InlineData("/RPC2/", "/RPC2")]
    [InlineData(null, "/rutorrent/RPC2")]
    [InlineData("", "/rutorrent/RPC2")]
    [InlineData("   ", "/rutorrent/RPC2")]
    public async Task UsesConfiguredRpcEndpointWithoutDuplicatingOrDroppingThePath(
        string? urlBase, string expectedPath)
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new RTorrentClient(httpClient, NullLogger<RTorrentClient>.Instance);
        var config = new DownloadClient
        {
            Name = "Test rTorrent",
            Type = DownloadClientType.RTorrent,
            Host = "seedbox.example",
            Port = 8443,
            UseSsl = true,
            UrlBase = urlBase
        };

        var connected = await client.TestConnectionAsync(config);

        connected.Should().BeTrue();
        handler.RequestUri.Should().Be(new Uri($"https://seedbox.example:8443{expectedPath}"));
        handler.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task RejectsAnHtmlPageReturnedByTheWrongEndpoint()
    {
        using var handler = new RecordingHandler("<html><body>Sign in</body></html>");
        using var httpClient = new HttpClient(handler);
        var client = new RTorrentClient(httpClient, NullLogger<RTorrentClient>.Instance);
        var config = new DownloadClient
        {
            Name = "Test rTorrent", Type = DownloadClientType.RTorrent,
            Host = "seedbox.example", Port = 8443, UseSsl = true, UrlBase = "/RPC2"
        };

        var connected = await client.TestConnectionAsync(config);

        connected.Should().BeFalse();
    }

    [Fact]
    public async Task DoesNotSendAnotherConfigsBasicCredentials()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new RTorrentClient(httpClient, NullLogger<RTorrentClient>.Instance);
        var authenticated = new DownloadClient
        {
            Name = "First", Type = DownloadClientType.RTorrent,
            Host = "seedbox.example", Port = 8443, UseSsl = true, UrlBase = "/first/RPC2",
            Username = "user", Password = "secret"
        };
        var anonymous = new DownloadClient
        {
            Name = "Second", Type = DownloadClientType.RTorrent,
            Host = "seedbox.example", Port = 8443, UrlBase = "/second/RPC2"
        };

        (await client.TestConnectionAsync(authenticated)).Should().BeTrue();
        handler.Authorization.Should().StartWith("Basic ");

        (await client.TestConnectionAsync(anonymous)).Should().BeTrue();
        handler.RequestUri.Should().Be(new Uri("http://seedbox.example:8443/second/RPC2"));
        handler.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task ConnectsWhenXmlRpcEndpointRequiresDigestAuthentication()
    {
        await using var server = new DigestChallengeServer();
        using var httpClient = new HttpClient();
        var client = new RTorrentClient(httpClient, NullLogger<RTorrentClient>.Instance);
        var config = new DownloadClient
        {
            Name = "Digest test", Type = DownloadClientType.RTorrent,
            Host = "127.0.0.1", Port = server.Port, UrlBase = "/RPC2",
            Username = "user", Password = "secret"
        };

        (await client.TestConnectionAsync(config)).Should().BeTrue();
        server.SawDigestAuthorization.Should().BeTrue();
        server.SawBasicAuthorization.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectsWhenDigestChallengeFollowsSameOriginRedirect()
    {
        await using var server = new DigestChallengeServer(redirectToCanonicalPath: true);
        using var httpClient = new HttpClient();
        var client = new RTorrentClient(httpClient, NullLogger<RTorrentClient>.Instance);
        var config = new DownloadClient
        {
            Name = "Redirected Digest test", Type = DownloadClientType.RTorrent,
            Host = "127.0.0.1", Port = server.Port, UrlBase = "/RPC2",
            Username = "user", Password = "secret"
        };

        (await client.TestConnectionAsync(config)).Should().BeTrue();
        server.SawDigestAuthorization.Should().BeTrue();
    }

    [Fact]
    public async Task WaitsForBasicChallengeBeforeSendingPasswordOverHttp()
    {
        await using var server = new DigestChallengeServer("Basic");
        using var httpClient = new HttpClient();
        var client = new RTorrentClient(httpClient, NullLogger<RTorrentClient>.Instance);
        var config = new DownloadClient
        {
            Name = "Basic test", Type = DownloadClientType.RTorrent,
            Host = "127.0.0.1", Port = server.Port, UrlBase = "/RPC2",
            Username = "user", Password = "secret"
        };

        (await client.TestConnectionAsync(config)).Should().BeTrue();
        server.SawBasicOnFirstRequest.Should().BeFalse();
        server.SawBasicAuthorization.Should().BeTrue();
    }
}
