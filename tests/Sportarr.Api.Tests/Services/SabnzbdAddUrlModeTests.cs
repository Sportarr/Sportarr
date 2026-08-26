using System.Net;
using System.Web;
using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #263. When the indexer rate-limits the NZB fetch,
/// Sportarr skips addfile and goes straight to addurl. That request carried
/// mode, output and the api key in both the query string and the form body.
/// CherryPy merges a duplicated parameter into a list, and SABnzbd then
/// crashes with "unhashable type: 'list'" (the same crash #183 described for
/// addfile), so every rate-limited grab failed with HTTP 500. The GET
/// fallback had the same shape of bug for username/password auth, which it
/// appended once itself and once more through SendApiRequestAsync.
/// </summary>
public class SabnzbdAddUrlModeTests
{
    private sealed class RateLimitedIndexerHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Post;
        public string? PostBody;
        public HttpRequestMessage? FallbackGet;
        public bool RejectPost;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var ok = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":true,\"nzo_ids\":[\"SABnzbd_nzo_263\"]}")
            };

            if (request.Method == HttpMethod.Get)
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/api"))
                {
                    FallbackGet = request;
                    return ok;
                }
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            }

            Post = request;
            PostBody = await request.Content!.ReadAsStringAsync(ct);
            return RejectPost ? new HttpResponseMessage(HttpStatusCode.MethodNotAllowed) : ok;
        }
    }

    private static SabnzbdClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Mock.Of<ILogger<SabnzbdClient>>());

    private static string[] Keys(string queryOrForm) =>
        HttpUtility.ParseQueryString(queryOrForm.TrimStart('?')).AllKeys.Where(k => k != null).Select(k => k!).ToArray();

    [Fact]
    public async Task AddUrl_SendsEachParameterOnce_WhenTheFetchIsRateLimited()
    {
        var handler = new RateLimitedIndexerHandler();
        var config = new DownloadClient { Name = "SAB", Type = DownloadClientType.Sabnzbd, Host = "sabnzbd", Port = 8080, ApiKey = "k3y" };

        var nzoId = await Client(handler).AddNzbAsync(config, "http://indexer/get/abc.nzb", "sports", "UFC.330.1080p.WEB");

        nzoId.Should().Be("SABnzbd_nzo_263");
        handler.Post.Should().NotBeNull();

        var query = handler.Post!.RequestUri!.Query;
        query.Should().Contain("mode=addurl").And.Contain("output=json").And.Contain("apikey=k3y");
        handler.PostBody.Should().Contain("name=http").And.Contain("cat=sports").And.Contain("nzbname=UFC.330.1080p.WEB");

        Keys(query).Intersect(Keys(handler.PostBody!)).Should().BeEmpty(
            "CherryPy merges a parameter sent in both places into a list, and SABnzbd crashes on it");
        Keys(query).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task AddUrl_KeepsCredentialsOutOfTheBody_WhenUsingUsernameAndPassword()
    {
        var handler = new RateLimitedIndexerHandler();
        var config = new DownloadClient { Name = "SAB", Type = DownloadClientType.Sabnzbd, Host = "sabnzbd", Port = 8080, Username = "u", Password = "p" };

        await Client(handler).AddNzbAsync(config, "http://indexer/get/abc.nzb", "sports");

        var query = handler.Post!.RequestUri!.Query;
        query.Should().Contain("ma_username=u").And.Contain("ma_password=p");
        Keys(query).Intersect(Keys(handler.PostBody!)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetFallback_SendsCredentialsOnce_WhenTheServerRejectsPost()
    {
        var handler = new RateLimitedIndexerHandler { RejectPost = true };
        var config = new DownloadClient { Name = "NZBdav", Type = DownloadClientType.Sabnzbd, Host = "nzbdav", Port = 3000, Username = "u", Password = "p" };

        var nzoId = await Client(handler).AddNzbAsync(config, "http://indexer/get/abc.nzb", "sports", "UFC.330.1080p.WEB");

        nzoId.Should().Be("SABnzbd_nzo_263");
        handler.FallbackGet.Should().NotBeNull();

        var keys = Keys(handler.FallbackGet!.RequestUri!.Query);
        keys.Should().OnlyHaveUniqueItems("a duplicated ma_username or ma_password fails SABnzbd's credential check");
        keys.Should().Contain(new[] { "mode", "name", "cat", "output", "nzbname", "ma_username", "ma_password" });
    }
}
