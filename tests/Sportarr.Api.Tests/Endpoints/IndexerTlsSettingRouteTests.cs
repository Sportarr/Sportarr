using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Endpoints;

public sealed class IndexerTlsSettingRouteTests
{
    [Fact]
    public async Task PrivateOptInRoundTripsAndPublicAddressIsRejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sportarr-indexer-tls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Services.AddDbContext<SportarrDbContext>(options =>
                options.UseSqlite("Data Source=" + Path.Combine(directory, "test.db") + ";Pooling=False"));
            builder.Services.AddHttpClient();
            builder.Services.AddSingleton<IndexerSearchService>(_ => null!);
            await using var app = builder.Build();
            app.MapIndexerEndpoints();
            using (var scope = app.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<SportarrDbContext>().Database.EnsureCreatedAsync();
            await app.StartAsync();
            var client = app.GetTestClient();

            var created = await client.PostAsJsonAsync("/api/indexer", Payload("https://172.24.8.1/prowlarr", true));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var id = createdJson.RootElement.GetProperty("id").GetInt32();

            var saved = await client.GetFromJsonAsync<JsonElement>($"/api/indexer/{id}");
            var setting = saved.GetProperty("fields").EnumerateArray()
                .Single(field => field.GetProperty("name").GetString() == "disableSslCertificateValidation");
            Assert.Equal("true", setting.GetProperty("value").GetString());

            var rejected = await client.PutAsJsonAsync($"/api/indexer/{id}", Payload("https://example.com", true));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var after = await client.GetFromJsonAsync<JsonElement>($"/api/indexer/{id}");
            var baseUrl = after.GetProperty("fields").EnumerateArray()
                .Single(field => field.GetProperty("name").GetString() == "baseUrl");
            Assert.Equal("https://172.24.8.1/prowlarr", baseUrl.GetProperty("value").GetString());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static object Payload(string url, bool disableValidation) => new
    {
        name = "Local Prowlarr",
        implementation = "Torznab",
        fields = new object[]
        {
            new { name = "baseUrl", value = url },
            new { name = "apiPath", value = "/29/api" },
            new { name = "disableSslCertificateValidation", value = disableValidation.ToString().ToLowerInvariant() }
        }
    };
}
