using System.Net;
using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// NZBdav's SABnzbd emulator accepts category from the query string and/or a
/// "category" alias, not only the standard multipart "cat" field.
/// </summary>
public class SabnzbdNzbDavCategoryTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Upload;
        public string? UploadBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath != "/api")
            {
                var nzb = "<?xml version=\"1.0\"?><nzb><file subject=\"x\">" + new string('x', 200) + "</file></nzb>";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(nzb) };
            }

            Upload = request;
            UploadBody = request.Content is not null ? await request.Content.ReadAsStringAsync(ct) : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":true,\"nzo_ids\":[\"5ef134ef-4425-4044-a619-acb8dc9e6440\"]}")
            };
        }
    }

    [Fact]
    public async Task AddFile_MirrorsCategoryInQueryStringAndFormAliases()
    {
        var handler = new CapturingHandler();
        var client = new SabnzbdClient(new HttpClient(handler), Mock.Of<ILogger<SabnzbdClient>>());
        var config = new DownloadClient { Name = "NZBdav", Type = DownloadClientType.Sabnzbd, Host = "nzbdav", Port = 3000, ApiKey = "k3y" };

        var nzoId = await client.AddNzbAsync(config, "http://indexer/get/abc.nzb", "sports", "EPL.Match");

        nzoId.Should().Be("5ef134ef-4425-4044-a619-acb8dc9e6440");
        handler.Upload!.RequestUri!.Query.Should().Contain("cat=sports").And.Contain("category=sports");
        handler.UploadBody.Should().Contain("name=cat").And.Contain("name=category");
    }
}
