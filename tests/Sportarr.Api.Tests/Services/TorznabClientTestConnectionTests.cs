using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Covers TorznabClient.TestConnectionAsync lenient handling. Some Prowlarr-managed
/// indexers answer t=caps with an HTML error page instead of XML; the test must fall
/// back to RSS/search rather than throwing an XmlException and rejecting the indexer.
/// </summary>
public class TorznabClientTestConnectionTests
{
    private const string CapsXml = "<?xml version=\"1.0\"?><caps><searching><search available=\"yes\"/></searching></caps>";
    private const string RssXml = "<?xml version=\"1.0\"?><rss version=\"2.0\"><channel><title>Feed</title></channel></rss>";
    private const string HtmlError = "<!doctype html><html><head><title>500</title></head><body>error</body></html>";

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<string, (HttpStatusCode Status, string Body)> _capsResponder;
        private readonly Func<string, (HttpStatusCode Status, string Body)> _searchResponder;

        public RoutingHandler(
            Func<string, (HttpStatusCode, string)> caps,
            Func<string, (HttpStatusCode, string)> search)
        {
            _capsResponder = caps;
            _searchResponder = search;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri?.Query ?? "";
            var (status, body) = query.Contains("t=caps") ? _capsResponder(query) : _searchResponder(query);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/xml")
            });
        }
    }

    private static TorznabClient BuildClient(
        Func<string, (HttpStatusCode, string)> caps,
        Func<string, (HttpStatusCode, string)> search)
    {
        var http = new HttpClient(new RoutingHandler(caps, search));
        return new TorznabClient(http, NullLogger<TorznabClient>.Instance);
    }

    private static Indexer Config() => new()
    {
        Id = 1,
        Name = "Racing For Me",
        Url = "http://prowlarr:9696/torznab/all",
        ApiPath = "/api",
        ApiKey = "abc",
    };

    [Fact]
    public async Task Caps_ReturnsValidXml_IsSuccess()
    {
        var client = BuildClient(
            caps: _ => (HttpStatusCode.OK, CapsXml),
            search: _ => (HttpStatusCode.OK, RssXml));

        (await client.TestConnectionAsync(Config())).Should().BeTrue();
    }

    [Fact]
    public async Task Caps_ReturnsHtml_FallsBackToRss_IsSuccess()
    {
        // The reported case: t=caps answers with an HTML error page. The old code threw
        // an XmlException here and failed the test; now it falls back to RSS/search.
        var client = BuildClient(
            caps: _ => (HttpStatusCode.OK, HtmlError),
            search: _ => (HttpStatusCode.OK, RssXml));

        (await client.TestConnectionAsync(Config())).Should().BeTrue();
    }

    [Fact]
    public async Task Caps_ServerError_FallsBackToRss_IsSuccess()
    {
        var client = BuildClient(
            caps: _ => (HttpStatusCode.InternalServerError, HtmlError),
            search: _ => (HttpStatusCode.OK, RssXml));

        (await client.TestConnectionAsync(Config())).Should().BeTrue();
    }

    [Fact]
    public async Task Caps_AndRss_BothHtml_IsFailure()
    {
        var client = BuildClient(
            caps: _ => (HttpStatusCode.OK, HtmlError),
            search: _ => (HttpStatusCode.OK, HtmlError));

        (await client.TestConnectionAsync(Config())).Should().BeFalse();
    }

    [Fact]
    public async Task Rss_ReturnsErrorRoot_IsFailure()
    {
        // A Torznab <error> (e.g. bad apikey) must not be accepted as a working feed.
        var client = BuildClient(
            caps: _ => (HttpStatusCode.OK, HtmlError),
            search: _ => (HttpStatusCode.OK, "<?xml version=\"1.0\"?><error code=\"100\" description=\"Invalid API Key\"/>"));

        (await client.TestConnectionAsync(Config())).Should().BeFalse();
    }
}
