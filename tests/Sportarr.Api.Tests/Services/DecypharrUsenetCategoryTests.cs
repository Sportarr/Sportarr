using System.Net;
using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #241: Sportarr logged "Category: sportarr" but Decypharr
/// received an empty category, so completed downloads landed in the download
/// root instead of the category folder and never auto-imported. Decypharr's
/// SABnzbd emulator only reads parameters from the query string, the same
/// quirk already documented for mode and output, and the category was only
/// being sent as a multipart form field.
/// </summary>
public class DecypharrUsenetCategoryTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Post;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
            {
                var nzb = "<?xml version=\"1.0\"?><nzb><file subject=\"x\">" + new string('x', 200) + "</file></nzb>";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(nzb)
                });
            }

            Post = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":true,\"nzo_ids\":[\"SABnzbd_nzo_123\"]}")
            });
        }
    }

    [Fact]
    public async Task CategoryTravelsInTheQueryString()
    {
        var handler = new CapturingHandler();
        var client = new SabnzbdClient(new HttpClient(handler), Mock.Of<ILogger<SabnzbdClient>>());
        var config = new DownloadClient { Name = "Decypharr", Type = DownloadClientType.DecypharrUsenet, Host = "decypharr", Port = 8282 };

        var nzoId = await client.AddNzbForDecypharrAsync(config, "http://indexer/get/abc.nzb", "sportarr");

        nzoId.Should().Be("SABnzbd_nzo_123");
        handler.Post.Should().NotBeNull();
        handler.Post!.RequestUri!.Query.Should().Contain("category=sportarr",
            "Decypharr reads the parameter name category, from the query string first");
        handler.Post.RequestUri.Query.Should().Contain("cat=sportarr",
            "real SABnzbd calls the same thing cat, so both names travel");
        handler.Post.RequestUri.Query.Should().Contain("mode=addfile");
    }
}
