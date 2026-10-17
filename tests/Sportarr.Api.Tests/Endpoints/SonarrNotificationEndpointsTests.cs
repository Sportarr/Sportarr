using System.Reflection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Endpoints;

public class SonarrNotificationEndpointsTests
{
    [Fact]
    public async Task V3RoutesKeepDownloadTriggerAndDisabledStateAcrossUpdate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sportarr-notification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Services.AddDbContext<SportarrDbContext>(options =>
                options.UseSqlite("Data Source=" + Path.Combine(directory, "test.db") + ";Pooling=False"));
            builder.Services.AddSingleton<CustomFormatMatchCache>(_ => null!);
            builder.Services.AddSingleton<ImportListService>(_ => null!);
            await using var app = builder.Build();
            app.MapSonarrConfigEndpoints();
            using (var scope = app.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<SportarrDbContext>().Database.EnsureCreatedAsync();
            await app.StartAsync();
            var client = app.GetTestClient();

            var created = await client.PostAsJsonAsync("/api/v3/notification", new
            {
                name = "Webhook", implementation = "Webhook", enabled = false,
                onDownload = true, onUpgrade = false,
                fields = new[] { new { name = "webhook", value = "https://example.test/hook" } }
            });
            created.StatusCode.Should().Be(HttpStatusCode.OK);
            using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var id = createdJson.RootElement.GetProperty("id").GetInt32();

            var updated = await client.PutAsJsonAsync($"/api/v3/notification/{id}", new
            {
                name = "Webhook", implementation = "Webhook", onDownload = true, onUpgrade = false,
                fields = new[] { new { name = "webhook", value = "https://example.test/hook" } }
            });
            updated.StatusCode.Should().Be(HttpStatusCode.OK);

            var saved = await client.GetFromJsonAsync<JsonElement>($"/api/v3/notification/{id}");
            saved.GetProperty("enabled").GetBoolean().Should().BeFalse();
            saved.GetProperty("onDownload").GetBoolean().Should().BeTrue();
            saved.GetProperty("onUpgrade").GetBoolean().Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void V3ReadReportsStoredTriggerAndEnabledValues()
    {
        var notification = new Notification
        {
            Name = "Webhook",
            Implementation = "Webhook",
            Enabled = false,
            ConfigJson = "{\"webhook\":\"https://example.test/hook\",\"onDownload\":false,\"onUpgrade\":true}"
        };

        var response = Invoke<object>("ToV3Notification", notification);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));

        json.RootElement.GetProperty("enabled").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("onDownload").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("onUpgrade").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void V3WritePersistsTopLevelDownloadTriggerForDispatch()
    {
        const string request = """
            {"name":"Webhook","implementation":"Webhook","enabled":true,
             "onDownload":true,"onUpgrade":false,
             "fields":[{"name":"webhook","value":"https://example.test/hook"}]}
            """;

        var notification = Invoke<Notification>("ParseV3Notification", request);
        using var config = JsonDocument.Parse(notification.ConfigJson);

        config.RootElement.GetProperty("webhook").GetString().Should().Be("https://example.test/hook");
        config.RootElement.GetProperty("onDownload").GetBoolean().Should().BeTrue();
        config.RootElement.GetProperty("onUpgrade").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void V3ReadDoesNotClaimAnUnsetDownloadTriggerIsEnabled()
    {
        var response = Invoke<object>("ToV3Notification", new Notification
        {
            Name = "Legacy webhook",
            Implementation = "Webhook",
            ConfigJson = "{\"webhook\":\"https://example.test/hook\"}"
        });
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));

        json.RootElement.GetProperty("onDownload").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void V3WriteTopLevelFalseOverridesFieldTrue()
    {
        const string request = """
            {"name":"Webhook","implementation":"Webhook","onDownload":false,
             "fields":[{"name":"onDownload","value":true}]}
            """;

        var notification = Invoke<Notification>("ParseV3Notification", request);
        using var config = JsonDocument.Parse(notification.ConfigJson);

        config.RootElement.GetProperty("onDownload").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void V3UpdateKeepsDisabledStateWhenClientOmitsEnabled()
    {
        const string request = """
            {"name":"Webhook","implementation":"Webhook","onDownload":true}
            """;

        var notification = Invoke<Notification>("ParseV3Notification", request, false);

        notification.Enabled.Should().BeFalse();
    }

    private static T Invoke<T>(string name, params object[] arguments)
    {
        var method = typeof(SonarrConfigEndpoints)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(candidate => candidate.Name == name &&
                candidate.GetParameters().Length == arguments.Length);
        method.Should().NotBeNull();
        return (T)method!.Invoke(null, arguments)!;
    }
}
