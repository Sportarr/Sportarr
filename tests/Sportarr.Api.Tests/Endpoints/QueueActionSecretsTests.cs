using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Models;
using Sportarr.Api.Tests.Services;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// The queue actions load the row with its download client. The client holds
/// the password and the API key, so a response must never carry the row as
/// it is.
/// </summary>
public class QueueActionSecretsTests
{
    private const string Password = "fixture-client-password";
    private const string ApiKey = "fixture-client-api-key";

    [Theory]
    [InlineData("pause", DownloadStatus.Downloading, DownloadStatus.Paused)]
    [InlineData("resume", DownloadStatus.Paused, DownloadStatus.Downloading)]
    public async Task APauseOrResumeAnswersWithoutTheClientSecrets(string action, DownloadStatus before, DownloadStatus after)
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync(rename: false, multipart: false,
            title: "Team A vs Team B", sport: "American Football", leagueName: "NFL");
        var row = await AddQueueRowAsync(rig, before);

        using var response = await rig.Client.PostAsync($"/api/queue/{row.Id}/{action}", null);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        AssertNoSecrets(body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("id").GetInt32().Should().Be(row.Id);
        json.RootElement.GetProperty("status").GetInt32().Should().Be((int)after);
    }

    [Fact]
    public async Task AForcedImportAnswersWithoutTheClientSecrets()
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync(rename: false, multipart: false,
            title: "Team A vs Team B", sport: "American Football", leagueName: "NFL");
        rig.Event.ExternalId = "ev-9900301";
        var root = (await rig.Db.RootFolders.SingleAsync()).Path;
        var path = Path.Combine(Path.GetDirectoryName(root)!, "incoming",
            "NFL.2020.09.01.Team.A.vs.Team.B.720p.WEB-DL.H264.{sportarr-ev-9900301}.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)65, 4096).ToArray());
        rig.Transport.JobPath = path;
        var row = await AddQueueRowAsync(rig, DownloadStatus.Completed);

        using var response = await rig.Client.PostAsync($"/api/queue/{row.Id}/import", null);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        AssertNoSecrets(body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("id").GetInt32().Should().Be(row.Id);
        json.RootElement.GetProperty("status").GetInt32().Should().Be((int)DownloadStatus.Imported);
    }

    private static async Task<DownloadQueueItem> AddQueueRowAsync(PackMemberLifecycleHarness rig, DownloadStatus status)
    {
        var client = await rig.Db.DownloadClients.SingleAsync();
        client.Username = "fixture-user";
        client.Password = Password;
        client.ApiKey = ApiKey;
        var row = new DownloadQueueItem
        {
            EventId = rig.Event.Id,
            Event = rig.Event,
            DownloadClientId = client.Id,
            DownloadClient = client,
            DownloadId = "owned-pack-job",
            Title = "NFL.2020.09.01.Team.A.vs.Team.B.720p.WEB-DL.H264",
            Quality = "WEBDL-720p",
            Protocol = "Torrent",
            Status = status,
            Progress = status == DownloadStatus.Completed ? 100 : 40,
            Size = 4096,
            Downloaded = status == DownloadStatus.Completed ? 4096 : 1024
        };
        rig.Db.DownloadQueue.Add(row);
        await rig.Db.SaveChangesAsync();
        return row;
    }

    /// <summary>Checks every level of the body, not only the top.</summary>
    private static void AssertNoSecrets(string body)
    {
        body.Should().NotContain(Password);
        body.Should().NotContain(ApiKey);
        using var json = JsonDocument.Parse(body);
        PropertyNames(json.RootElement).Should().NotContain(name =>
            name.Equals("password", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("apiKey", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> PropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var name in PropertyNames(property.Value))
                    yield return name;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            foreach (var name in PropertyNames(item))
                yield return name;
        }
    }
}
