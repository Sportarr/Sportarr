using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Validators;

namespace Sportarr.Api.Tests.Endpoints;

public class EpgDownloadLimitSettingsTests
{
    [Fact]
    public async Task Settings_endpoint_uses_the_default_when_the_config_element_is_missing()
    {
        await using var server = await SettingsServer.StartAsync(null);

        using var response = await server.Client.GetAsync("api/settings");
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue("the endpoint returned {0}", body);
        var settings = JsonSerializer.Deserialize<JsonElement>(body);

        settings.GetProperty("epgMaxDownloadSizeMb").GetInt32()
            .Should().Be(Config.DefaultEpgMaxDownloadSizeMb);
    }

    [Fact]
    public async Task Settings_endpoint_returns_the_configured_limit()
    {
        await using var server = await SettingsServer.StartAsync(300);

        using var response = await server.Client.GetAsync("api/settings");
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue("the endpoint returned {0}", body);
        var settings = JsonSerializer.Deserialize<JsonElement>(body);

        settings.TryGetProperty("epgMaxDownloadSizeMb", out var limit).Should().BeTrue();
        limit.GetInt32().Should().Be(300);
    }

    [Fact]
    public async Task Settings_endpoint_caps_the_saved_limit_at_the_safety_ceiling()
    {
        await using var server = await SettingsServer.StartAsync(300);
        var settings = JsonNode.Parse(await server.Client.GetStringAsync("api/settings"))!.AsObject();
        settings["epgMaxDownloadSizeMb"] = 700;

        using var response = await server.Client.PutAsJsonAsync("api/settings", settings);

        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue("the endpoint returned {0}", body);
        var saved = JsonNode.Parse(await server.Client.GetStringAsync("api/settings"))!.AsObject();
        saved["epgMaxDownloadSizeMb"]!.GetValue<int>().Should().Be(512);
        (await File.ReadAllTextAsync(Path.Combine(server.DataPath, "config.xml")))
            .Should().Contain("<EpgMaxDownloadSizeMb>512</EpgMaxDownloadSizeMb>");
    }

    private sealed class SettingsServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        public string DataPath { get; }

        private SettingsServer(WebApplication app, string dataPath)
        {
            _app = app;
            DataPath = dataPath;
            Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        }

        public static async Task<SettingsServer> StartAsync(int? storedLimit)
        {
            var dataPath = Path.Combine(Path.GetTempPath(), $"epg-limit-settings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dataPath);
            var limitElement = storedLimit.HasValue
                ? $"<EpgMaxDownloadSizeMb>{storedLimit.Value}</EpgMaxDownloadSizeMb>"
                : "";
            await File.WriteAllTextAsync(
                Path.Combine(dataPath, "config.xml"),
                $"<Config><SettingsUpgradeLevel>999</SettingsUpgradeLevel>{limitElement}</Config>");

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = dataPath
            });
            builder.Services.AddSingleton<ConfigService>();
            builder.Services.AddDbContext<SportarrDbContext>(options =>
                options.UseInMemoryDatabase($"epg-limit-settings-{Guid.NewGuid():N}"));
            builder.Services.AddValidatorsFromAssemblyContaining<ScheduleDvrRecordingRequestValidator>();
            builder.Services.AddScoped<SimpleAuthService>();
            builder.Services.AddScoped<FileFormatManager>();
            // The settings page asks how this install updates.
            builder.Services.AddSingleton<IUpdateMethodSource>(new FakeUpdateMethodSource());

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.MapSettingsEndpoints();
            await app.StartAsync();
            return new SettingsServer(app, dataPath);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
            Directory.Delete(DataPath, recursive: true);
        }
    }
}
