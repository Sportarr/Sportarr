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
