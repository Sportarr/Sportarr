using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Sportarr.Api.Endpoints;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// The season search reads its options from a JSON body. The quality profile
/// in the body was ignored before, because the endpoint only read the query
/// string.
/// </summary>
public class SeasonSearchRequestTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BodyGivesTheProfileAndTheCustomText(bool chunked)
    {
        var options = await ReadAsync(chunked
            ? JsonContent.Create(new { qualityProfileId = 7, customQuery = "  F1 2026 Hungarian GP  " })
            : new StringContent("{\"qualityProfileId\":7,\"customQuery\":\"  F1 2026 Hungarian GP  \"}",
                Encoding.UTF8, "application/json"));

        options.qualityProfileId.Should().Be(7);
        options.customQuery.Should().Be("F1 2026 Hungarian GP");
    }

    [Fact]
    public async Task QueryStringProfileWorksWithoutABody()
    {
        var options = await ReadAsync(null, "?qualityProfileId=4");

        options.qualityProfileId.Should().Be(4);
        options.customQuery.Should().BeNull();
    }

    [Fact]
    public async Task BodyProfileWinsOverTheQueryString()
    {
        var options = await ReadAsync(JsonContent.Create(new { qualityProfileId = 9 }), "?qualityProfileId=4");

        options.qualityProfileId.Should().Be(9);
    }

    [Fact]
    public async Task NullBodyProfileFallsBackToTheQueryString()
    {
        var options = await ReadAsync(JsonContent.Create(new { qualityProfileId = (int?)null }), "?qualityProfileId=4");

        options.qualityProfileId.Should().Be(4);
    }

    [Theory]
    [InlineData("{\"customQuery\":\"   \"}")]
    [InlineData("{\"customQuery\":12}")]
    [InlineData("not json")]
    [InlineData("[]")]
    public async Task BlankOrUnreadableTextGivesNoCustomQuery(string body)
    {
        var options = await ReadAsync(new StringContent(body, Encoding.UTF8, "application/json"));

        options.customQuery.Should().BeNull();
        options.qualityProfileId.Should().BeNull();
    }

    private static async Task<(int? qualityProfileId, string? customQuery)> ReadAsync(HttpContent? content, string query = "")
    {
        await using var app = WebApplication.CreateBuilder().Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapPost("/", async (HttpContext context, int? qualityProfileId) =>
        {
            var request = await SearchAndCalendarEndpoints.ReadSeasonSearchRequestAsync(context.Request, qualityProfileId);
            return Results.Json(new { request.QualityProfileId, request.CustomQuery });
        });
        await app.StartAsync();

        using var client = new HttpClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, app.Urls.Single() + query)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = content
        };
        using var response = await client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var profile = body.RootElement.GetProperty("qualityProfileId");
        var custom = body.RootElement.GetProperty("customQuery");
        return (profile.ValueKind == JsonValueKind.Null ? null : profile.GetInt32(),
            custom.ValueKind == JsonValueKind.Null ? null : custom.GetString());
    }
}
