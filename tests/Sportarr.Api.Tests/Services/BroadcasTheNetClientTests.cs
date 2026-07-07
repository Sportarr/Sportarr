using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class BroadcasTheNetClientTests
{
    private readonly Mock<HttpMessageHandler> _handler;
    private readonly Mock<IRateLimitService> _rateLimit;
    private readonly BroadcasTheNetClient _subject;
    private readonly Indexer _indexer;

    public BroadcasTheNetClientTests()
    {
        _handler = new Mock<HttpMessageHandler>();
        _rateLimit = new Mock<IRateLimitService>();

        _rateLimit
            .Setup(r => r.WaitAndPulseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);

        var httpClient = new HttpClient(_handler.Object);

        _subject = new BroadcasTheNetClient(
            httpClient,
            _rateLimit.Object,
            NullLogger<BroadcasTheNetClient>.Instance);

        _indexer = new Indexer
        {
            Id = 1,
            Name = "BroadcastheNet",
            Url = "https://api.broadcasthe.net",
            ApiKey = "abc"
        };
    }

    private void SetupHttpResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK, string mediaType = "application/json")
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType)
            });
    }

    private void SetupHttpResponse(HttpStatusCode statusCode)
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new ByteArrayContent(Array.Empty<byte>())
            });
    }

    // Captures the outgoing request so tests can assert on the serialized body after the call.
    // Assignment in the callback is synchronous; the body is read (awaited) by the caller.
    private StrongBox<HttpRequestMessage?> SetupCapturingResponse(string content, string mediaType = "application/json")
    {
        var box = new StrongBox<HttpRequestMessage?>(null);
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => box.Value = req)
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType)
            });
        return box;
    }

    private void VerifyRequest(string expectedUrl)
    {
        _handler
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.ToString() == expectedUrl),
                ItExpr.IsAny<CancellationToken>());
    }

    private static string ReadFixture(string relativePath)
    {
        var dir = Path.GetDirectoryName(typeof(BroadcasTheNetClientTests).Assembly.Location)!;
        return File.ReadAllText(Path.Combine(dir, relativePath));
    }

    private static HttpResponseMessage JsonOk(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json") };

    // Builds a BTN getTorrents response with `count` torrents (ids startId..startId+count-1),
    // each release name containing "Formula" and "2026" so it survives FilterByQueryTerms.
    private static string BuildTorrentsJson(int count, int startId)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"id\":\"seq\",\"result\":{\"results\":\"").Append(count).Append("\",\"torrents\":{");
        for (var i = 0; i < count; i++)
        {
            var id = startId + i;
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(id).Append("\":{")
              .Append("\"GroupID\":\"").Append(id).Append("\",")
              .Append("\"TorrentID\":\"").Append(id).Append("\",")
              .Append("\"ReleaseName\":\"Formula1.2026.Round").Append(id).Append(".Race.1080p.WEB-GRP\",")
              .Append("\"Size\":\"1\",\"Time\":\"1700000000\",\"Seeders\":\"5\",\"Leechers\":\"0\",")
              .Append("\"Source\":\"WEB-DL\",\"Codec\":\"H.264\",\"Resolution\":\"1080p\",")
              .Append("\"DownloadURL\":\"https:\\/\\/broadcasthe.net\\/torrents.php?id=").Append(id).Append("\"}");
        }
        sb.Append("}}}");
        return sb.ToString();
    }

    [Fact]
    public async Task should_parse_recent_feed_from_BroadcastheNet()
    {
        var recentFeed = ReadFixture("Services/Fixtures/BroadcastheNet/RecentFeed.json");
        SetupHttpResponse(recentFeed);

        var releases = await _subject.FetchRecentAsync(_indexer);

        releases.Should().HaveCount(2);

        var first = releases.First();
        first.Guid.Should().Be("BTN-123");
        first.Title.Should().Be("Jimmy.Kimmel.2014.09.15.Jane.Fonda.HDTV.x264-aAF");
        first.DownloadUrl.Should().Be("https://broadcasthe.net/torrents.php?action=download&id=123&authkey=123&torrent_pass=123");
        first.InfoUrl.Should().Be("https://broadcasthe.net/torrents.php?id=237457&torrentid=123");
        first.Indexer.Should().Be(_indexer.Name);
        first.PublishDate.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1410902133).UtcDateTime);
        first.Size.Should().Be(505099926);
        first.TorrentInfoHash.Should().Be("123");
        first.Seeders.Should().Be(40);
        first.Leechers.Should().Be(9);

        // Quality metadata from BTN fields
        first.Source.Should().Be("HDTV");
        first.Codec.Should().Be("x264");
        first.Quality.Should().Be("SD");

        VerifyRequest("https://api.broadcasthe.net/");
    }

    [Fact]
    public async Task should_throw_on_bad_request()
    {
        SetupHttpResponse(HttpStatusCode.BadRequest);

        var act = () => _subject.FetchRecentAsync(_indexer);

        await act.Should().ThrowAsync<IndexerRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_throw_on_unauthorized()
    {
        SetupHttpResponse(HttpStatusCode.Unauthorized);

        var act = () => _subject.FetchRecentAsync(_indexer);

        await act.Should().ThrowAsync<IndexerRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_throw_on_not_found()
    {
        SetupHttpResponse(HttpStatusCode.NotFound);

        var act = () => _subject.FetchRecentAsync(_indexer);

        await act.Should().ThrowAsync<IndexerRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_throw_rate_limit_exception_on_service_unavailable_with_call_limit_body()
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Call Limit Exceeded")
            });

        var act = () => _subject.FetchRecentAsync(_indexer);

        await act.Should().ThrowAsync<IndexerRateLimitException>();
    }

    [Fact]
    public async Task should_throw_on_html_response()
    {
        SetupHttpResponse("<html><body>Cloudflare</body></html>", HttpStatusCode.OK, "text/html");

        var act = () => _subject.FetchRecentAsync(_indexer);

        await act.Should().ThrowAsync<IndexerRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task should_throw_on_invalid_api_key_plain_text_response()
    {
        SetupHttpResponse("Error: Invalid API Key");

        var act = () => _subject.FetchRecentAsync(_indexer);

        await act.Should().ThrowAsync<IndexerRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_use_configured_base_url_for_download_urls()
    {
        // Download URLs come from BTN's response, not from the configured base URL —
        // verify that http-configured indexers still get the response URLs as-is.
        var indexerWithHttp = new Indexer
        {
            Id = 2,
            Name = "BroadcastheNet",
            Url = "http://api.broadcasthe.net",
            ApiKey = "abc"
        };

        var recentFeed = ReadFixture("Services/Fixtures/BroadcastheNet/RecentFeed.json");
        SetupHttpResponse(recentFeed);

        var releases = await _subject.FetchRecentAsync(indexerWithHttp);

        releases.Should().HaveCount(2);
        // DownloadURL comes from the BTN JSON payload; the client doesn't rewrite it
        releases.First().DownloadUrl.Should().Be(
            "https://broadcasthe.net/torrents.php?action=download&id=123&authkey=123&torrent_pass=123");

        VerifyRequest("http://api.broadcasthe.net/");
    }

    [Fact]
    public async Task should_return_empty_list_when_result_has_no_torrents()
    {
        var emptyFeed = """{"id":"abc","result":{"Results":0}}""";
        SetupHttpResponse(emptyFeed);

        var releases = await _subject.FetchRecentAsync(_indexer);

        releases.Should().BeEmpty();
    }

    [Fact]
    public async Task should_throw_when_api_returns_error_in_json_rpc_response()
    {
        var errorResponse = """{"id":"abc","error":{"code":-32601,"message":"Method not found"}}""";
        SetupHttpResponse(errorResponse);

        var act = () => _subject.FetchRecentAsync(_indexer);

        await act.Should().ThrowAsync<IndexerRequestException>();
    }

    [Fact]
    public async Task should_set_guid_with_btn_prefix_and_torrent_id()
    {
        var recentFeed = ReadFixture("Services/Fixtures/BroadcastheNet/RecentFeed.json");
        SetupHttpResponse(recentFeed);

        var releases = await _subject.FetchRecentAsync(_indexer);

        releases.Should().OnlyContain(r => r.Guid.StartsWith("BTN-"));

        VerifyRequest("https://api.broadcasthe.net/");
    }

    [Fact]
    public async Task should_send_content_type_without_charset_parameter()
    {
        // BTN's PHP backend only recognizes the exact "application/json-rpc" Content-Type;
        // a "; charset=utf-8" suffix makes it return HTML "ERROR 500" (verified live). The
        // StringContent(...,Encoding.UTF8,mediaType) overload appends the charset, so the
        // client must set the header without it.
        var capturedRequest = SetupCapturingResponse("""{"id":"abc","result":{"Results":0}}""");

        await _subject.FetchRecentAsync(_indexer);

        var contentType = capturedRequest.Value!.Content!.Headers.ContentType!;
        contentType.MediaType.Should().Be("application/json-rpc");
        contentType.CharSet.Should().BeNull();
        contentType.ToString().Should().Be("application/json-rpc");
    }

    [Fact]
    public async Task should_serialize_age_filter_with_literal_angle_bracket()
    {
        // BTN's PHP JSON-RPC backend rejects HTML-escaped '<' (<) with an "ERROR 500"
        // page. The comparator must be sent as a literal '<', never escaped.
        var capturedRequest = SetupCapturingResponse("""{"id":"abc","result":{"Results":0}}""");

        await _subject.FetchRecentAsync(_indexer);

        var body = await capturedRequest.Value!.Content!.ReadAsStringAsync();
        body.Should().Contain("\"Age\":\"<=86400\"");
        body.Should().NotContain("\\u003C");
    }

    [Fact]
    public async Task should_parse_search_results_from_BroadcastheNet()
    {
        // Fixture captured from the live BTN API (getTorrents with Search="Formula%1%2026"),
        // sanitized: authkey/torrent_pass redacted to "123". Exercises string-typed numbers,
        // the string-keyed torrents dictionary, and the unmapped "Genres" field.
        var searchFeed = ReadFixture("Services/Fixtures/BroadcastheNet/SearchFeed.json");
        SetupHttpResponse(searchFeed);

        var releases = await _subject.SearchAsync(_indexer, "Formula 1 2026");

        releases.Should().HaveCount(2);

        var darksport = releases.Single(r => r.Guid == "BTN-2239370");
        darksport.Title.Should().Be("Formula1.2026.British.Grand.Prix.Sprint.Race.1080p.AHDTV.x264-DARKSPORT");
        darksport.InfoUrl.Should().Be("https://broadcasthe.net/torrents.php?id=1119375&torrentid=2239370");
        darksport.DownloadUrl.Should().Be("https://broadcasthe.net/torrents.php?action=download&id=2239370&authkey=123&torrent_pass=123");
        darksport.Indexer.Should().Be(_indexer.Name);
        darksport.Size.Should().Be(6043231121);   // long parsed from JSON string
        darksport.Seeders.Should().Be(36);
        darksport.Leechers.Should().Be(1);
        darksport.TorrentInfoHash.Should().Be("B2F3B6EED252EC69D966F5BE9AF70F243755B238");
        darksport.PublishDate.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1783169410).UtcDateTime);
        darksport.Source.Should().Be("HDTV");
        darksport.Codec.Should().Be("H.264");
        darksport.Quality.Should().Be("1080p");
        darksport.ReleaseGroup.Should().Be("DARKSPORT");
    }

    [Fact]
    public async Task should_search_using_search_field_with_wildcards_and_no_age()
    {
        // BTN's "Name" filter needs a Tvdb/Tvrage companion and a broad "Age"-only query
        // times out with ERROR 500. Use only "Search". BTN matches Series/GroupName metadata
        // ("Formula 1"), so the glued "Formula1" is wildcarded at the letter/digit boundary to
        // "Formula%1%2026" — verified live: "Formula1%2026" -> 0, "Formula%1%2026" -> 264.
        var capturedRequest = SetupCapturingResponse("""{"id":"abc","result":{"Results":0}}""");

        await _subject.SearchAsync(_indexer, "Formula1 2026");

        var body = await capturedRequest.Value!.Content!.ReadAsStringAsync();
        body.Should().Contain("\"Search\":\"Formula%1%2026\"");
        body.Should().NotContain("\"Age\"");
        body.Should().NotContain("\"Name\"");
    }

    [Fact]
    public async Task should_wildcard_letter_digit_boundaries_for_round_queries()
    {
        // Boundary wildcards on both directions (letter->digit and digit->letter) let a round
        // query narrow the search. Verified live: "Formula%1%2026%Round%09" returns 11 results
        // (just Round 09) vs 264 for the whole season.
        var capturedRequest = SetupCapturingResponse("""{"id":"abc","result":{"Results":0}}""");

        await _subject.SearchAsync(_indexer, "Formula1 2026 Round09");

        var body = await capturedRequest.Value!.Content!.ReadAsStringAsync();
        body.Should().Contain("\"Search\":\"Formula%1%2026%Round%09\"");
    }

    [Fact]
    public async Task should_keep_server_matched_results_when_terms_absent_from_release_name()
    {
        // Round-scoped query: BTN matched server-side (the round lives in its GroupName), but
        // "Round09" never appears in the release name. FilterByQueryTerms must not drop the whole
        // set to zero — it defers to the downstream matcher. Regression for the round-search bug.
        var searchFeed = ReadFixture("Services/Fixtures/BroadcastheNet/SearchFeed.json");
        SetupHttpResponse(searchFeed);

        var releases = await _subject.SearchAsync(_indexer, "Formula1 2026 Round09");

        releases.Should().HaveCount(2); // both fixture rows survive despite "Round09" not in titles
    }

    [Fact]
    public async Task should_page_past_the_100_row_limit_until_a_short_page()
    {
        // BTN caps a page at 100 rows. A full first page must trigger a second fetch so matches
        // beyond the first 100 are reachable; a short second page ends pagination.
        _handler
            .Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => JsonOk(BuildTorrentsJson(100, startId: 1)))
            .ReturnsAsync(() => JsonOk(BuildTorrentsJson(20, startId: 101)));

        var releases = await _subject.SearchAsync(_indexer, "Formula 2026", maxResults: 250);

        releases.Should().HaveCount(120); // 100 (full page) + 20 (short page), then stops
        _handler.Protected().Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task should_set_info_url_with_group_and_torrent_id()
    {
        var recentFeed = ReadFixture("Services/Fixtures/BroadcastheNet/RecentFeed.json");
        SetupHttpResponse(recentFeed);

        var releases = await _subject.FetchRecentAsync(_indexer);

        releases.First().InfoUrl.Should().Be("https://broadcasthe.net/torrents.php?id=237457&torrentid=123");

        VerifyRequest("https://api.broadcasthe.net/");
    }
}
