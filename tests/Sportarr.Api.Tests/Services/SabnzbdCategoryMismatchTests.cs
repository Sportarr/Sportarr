using System.Net;
using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// When Sportarr polls by the nzo_id it received at grab time, a category
/// mismatch means the client mis-filed the job (NZBdav/Decypharr quirk), not
/// that another app owns the download. The status should still be returned.
/// </summary>
public class SabnzbdCategoryMismatchTests
{
    private sealed class HistoryMismatchHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!.ToString();

            if (uri.Contains("mode=queue"))
            {
                return Task.FromResult(Json("{\"queue\":{\"slots\":[]}}"));
            }

            if (uri.Contains("mode=history"))
            {
                return Task.FromResult(Json("""
                {
                    "history": {
                        "slots": [{
                            "nzo_id": "5ef134ef-4425-4044-a619-acb8dc9e6440",
                            "name": "EPL.26-27-Matchday.1-Arsenal.FC.vs.Coventry.City.2160p.4K.UHD-SDX",
                            "status": "Completed",
                            "category": "uncategorized",
                            "bytes": 16575673310,
                            "storage": "/usenet/completed/uncategorized/EPL.26-27-Matchday.1-Arsenal.FC.vs.Coventry.City.2160p.4K.UHD-SDX"
                        }]
                    }
                }
                """));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body) };
    }

    [Fact]
    public async Task GetDownloadStatus_StillReturnsCompleted_WhenCategoryWasMisFiled()
    {
        var client = new SabnzbdClient(
            new HttpClient(new HistoryMismatchHandler()),
            Mock.Of<ILogger<SabnzbdClient>>());
        var config = new DownloadClient
        {
            Name = "NZBdav",
            Type = DownloadClientType.Sabnzbd,
            Host = "nzbdav",
            Port = 3000,
            ApiKey = "k3y",
            Category = "sports"
        };

        var status = await client.GetDownloadStatusAsync(
            config,
            "5ef134ef-4425-4044-a619-acb8dc9e6440",
            "sports");

        status.Should().NotBeNull();
        status!.Status.Should().Be("completed");
        status.SavePath.Should().Contain("Coventry.City");
    }

    [Fact]
    public async Task FindDownloadByTitle_ReturnsCompleted_WhenHistoryCategoryDiffers()
    {
        var client = new SabnzbdClient(
            new HttpClient(new HistoryMismatchHandler()),
            Mock.Of<ILogger<SabnzbdClient>>());
        var config = new DownloadClient
        {
            Name = "NZBdav",
            Type = DownloadClientType.Sabnzbd,
            Host = "nzbdav",
            Port = 3000,
            ApiKey = "k3y",
            Category = "sports"
        };

        var (status, downloadId) = await client.FindDownloadByTitleAsync(
            config,
            "EPL.26-27-Matchday.1-Arsenal.FC.vs.Coventry.City.2160p.4K.UHD-SDX",
            "sports");

        status.Should().NotBeNull();
        downloadId.Should().Be("5ef134ef-4425-4044-a619-acb8dc9e6440");
        status!.Status.Should().Be("completed");
    }
}
