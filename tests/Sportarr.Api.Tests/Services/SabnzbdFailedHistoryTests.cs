using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

public class SabnzbdFailedHistoryTests
{
    private sealed class SabHistoryHandler(
        string historySlots,
        string queueSlots = "[]") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var mode = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["mode"];
            var body = mode == "history"
                ? "{\"history\":{\"slots\":" + historySlots + "}}"
                : "{\"queue\":{\"slots\":" + queueSlots + "}}";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }

    private static DownloadClient Config() => new()
    {
        Name = "NZBdav",
        Type = DownloadClientType.NZBdav,
        Host = "nzbdav",
        Port = 3000,
        ApiKey = "key",
        Category = "sports"
    };

    private static SabnzbdClient Client(string historySlots, string queueSlots = "[]") =>
        new(
            new HttpClient(new SabHistoryHandler(historySlots, queueSlots)),
            NullLogger<SabnzbdClient>.Instance);

    private static string FailedSlotJson(string nzoId, string? storage, string failMessage = "Missing articles: ...")
    {
        var storageJson = storage is null ? "null" : $"\"{storage}\"";
        return "[{" +
               $"\"nzo_id\": \"{nzoId}\"," +
               "\"name\": \"Missing.Articles.Release\"," +
               "\"status\": \"Failed\"," +
               "\"category\": \"sports\"," +
               "\"bytes\": 1234," +
               $"\"storage\": {storageJson}," +
               $"\"fail_message\": \"{failMessage}\"" +
               "}]";
    }

    private const string CompletedNullStorage = """
        [{
            "nzo_id": "nzo-wait",
            "name": "Completed.Waiting",
            "status": "Completed",
            "category": "sports",
            "bytes": 2000,
            "storage": null
        }]
        """;

    private const string CompletedWithStorage = """
        [{
            "nzo_id": "nzo-ready",
            "name": "Completed.Ready",
            "status": "Completed",
            "category": "sports",
            "bytes": 3000,
            "storage": "/downloads/complete/x"
        }]
        """;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AFailedDownloadStaysFailedWhenTheClientSendsNoStorage(string? storage)
    {
        var status = await Client(FailedSlotJson("nzo-failed", storage))
            .GetDownloadStatusAsync(Config(), "nzo-failed", "sports");

        status.Should().NotBeNull();
        status!.Status.Should().Be("failed");
        status.ErrorMessage.Should().Contain("Missing articles");
    }

    [Fact]
    public async Task ACompletedDownloadWaitsUntilTheClientSendsStorage()
    {
        var status = await Client(CompletedNullStorage)
            .GetDownloadStatusAsync(Config(), "nzo-wait", "sports");

        status.Should().NotBeNull();
        status!.Status.Should().Be("downloading");
        status.SavePath.Should().BeNull();
    }

    [Fact]
    public async Task ACompletedDownloadWithStorageCompletes()
    {
        var status = await Client(CompletedWithStorage)
            .GetDownloadStatusAsync(Config(), "nzo-ready", "sports");

        status.Should().NotBeNull();
        status!.Status.Should().Be("completed");
        status.SavePath.Should().Be("/downloads/complete/x");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AScriptFailureWithoutStorageStaysFailed(string? storage)
    {
        var status = await Client(FailedSlotJson("nzo-script", storage, "Post-processing script failed"))
            .GetDownloadStatusAsync(Config(), "nzo-script", "sports");

        status.Should().NotBeNull();
        status!.Status.Should().Be("failed");
    }
}
