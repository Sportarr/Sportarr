using System.Net;
using FluentAssertions;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Endpoints;

public class IptvStreamRedirectTests
{
    [Theory]
    [InlineData(302, 502)]
    [InlineData(307, 502)]
    [InlineData(300, 300)]
    [InlineData(304, 304)]
    [InlineData(404, 404)]
    public void ConvertsUnresolvedRedirectsToBadGateway(int upstreamStatusCode, int expectedStatusCode)
    {
        IptvEndpoints.GetProxyResponseStatusCode(upstreamStatusCode)
            .Should().Be(expectedStatusCode);
    }

    [Fact]
    public async Task FollowsRedirectsBeforeReturningFinalStreamResponse()
    {
        var requestedUris = new List<Uri>();
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);
            if (requestedUris.Count == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Headers = { Location = new Uri("/live/final.ts", UriKind.Relative) }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        using var response = await IptvEndpoints.SendStreamRequestAsync(
            client,
            new Uri("https://provider.example/start"),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.RequestMessage!.RequestUri.Should().Be(new Uri("https://provider.example/live/final.ts"));
        requestedUris.Should().Equal(
            new Uri("https://provider.example/start"),
            new Uri("https://provider.example/live/final.ts"));
    }

    [Fact]
    public async Task RewritesRedirectedMasterAndVariantPlaylistsAgainstTheirUpstreamUris()
    {
        var requestedUris = new List<Uri>();
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);
            return requestedUris.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://cdn.example.net/live/master.m3u8") }
                },
                2 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000\nvariants/high/index.m3u8\n")
                    {
                        Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.apple.mpegurl") }
                    }
                },
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("#EXTM3U\n#EXTINF:2,\nsegments/segment001.ts\n")
                    {
                        Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.apple.mpegurl") }
                    }
                }
            };
        }));

        var initialUri = new Uri("https://provider.example/start");
        using var masterResponse = await IptvEndpoints.SendStreamRequestAsync(
            client,
            initialUri,
            CancellationToken.None);

        var master = await IptvEndpoints.ReadAndRewriteHlsPlaylistAsync(
            masterResponse,
            initialUri,
            channelId: 24,
            logger: null,
            CancellationToken.None);

        var variantProxyUrl = master
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.Contains("/api/iptv/stream/url?url=", StringComparison.Ordinal));
        var variantUri = new Uri(Uri.UnescapeDataString(
            variantProxyUrl[(variantProxyUrl.IndexOf("url=", StringComparison.Ordinal) + 4)..]
                .Split('&')[0]));
        variantUri.Should().Be(new Uri("https://cdn.example.net/live/variants/high/index.m3u8"));

        using var variantResponse = await IptvEndpoints.SendStreamRequestAsync(
            client,
            variantUri,
            CancellationToken.None);
        var variant = await IptvEndpoints.ReadAndRewriteHlsPlaylistAsync(
            variantResponse,
            variantUri,
            channelId: 24,
            logger: null,
            CancellationToken.None);

        var segmentProxyUrl = variant
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.Contains("/api/iptv/stream/url?url=", StringComparison.Ordinal));
        var segmentUri = new Uri(Uri.UnescapeDataString(
            segmentProxyUrl[(segmentProxyUrl.IndexOf("url=", StringComparison.Ordinal) + 4)..]
                .Split('&')[0]));

        segmentUri.Should().Be(new Uri("https://cdn.example.net/live/variants/high/segments/segment001.ts"));
        requestedUris.Should().Equal(
            new Uri("https://provider.example/start"),
            new Uri("https://cdn.example.net/live/master.m3u8"),
            new Uri("https://cdn.example.net/live/variants/high/index.m3u8"));
    }

    [Fact]
    public async Task ChannelProbeFollowsRedirectsInsteadOfReportingFound()
    {
        var requests = new List<(HttpMethod Method, Uri Uri)>();
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add((request.Method, request.RequestUri!));
            if (request.Method == HttpMethod.Head && requests.Count == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Headers = { Location = new Uri("/live/final", UriKind.Relative) }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        using var response = await IptvSourceService.ProbeChannelAsync(
            client,
            new Uri("https://provider.example/start"),
            "VLC/3.0.18 LibVLC/3.0.18",
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        requests.Should().Equal(
            (HttpMethod.Head, new Uri("https://provider.example/start")),
            (HttpMethod.Head, new Uri("https://provider.example/live/final")));
    }

    [Fact]
    public async Task StopsFollowingAfterTheRedirectLimit()
    {
        var requestCount = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri($"/live/{requestCount}", UriKind.Relative) }
            };
        }));

        using var response = await IptvEndpoints.SendStreamRequestAsync(
            client,
            new Uri("https://provider.example/start"),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        requestCount.Should().Be(11);
    }

    [Fact]
    public async Task DoesNotFollowNonHttpRedirects()
    {
        var requestCount = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("file:///etc/passwd") }
            };
        }));

        using var response = await IptvEndpoints.SendStreamRequestAsync(
            client,
            new Uri("https://provider.example/start"),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        requestCount.Should().Be(1);
    }

    [Fact]
    public async Task DoesNotTreatNonRedirectThreeHundredStatusesAsRedirects()
    {
        var requestCount = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.MultipleChoices)
            {
                Headers = { Location = new Uri("https://provider.example/choice") }
            };
        }));

        using var response = await IptvEndpoints.SendStreamRequestAsync(
            client,
            new Uri("https://provider.example/start"),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.MultipleChoices);
        requestCount.Should().Be(1);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
