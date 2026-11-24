using System.Net.Http.Json;
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

public class EventTypeFolderSettingsTests
{
    [Fact]
    public async Task Settings_endpoint_round_trips_event_type_folders()
    {
        await using var server = await SettingsServer.StartAsync();
        var settings = JsonNode.Parse(await server.Client.GetStringAsync("api/settings"))!.AsObject();
        var media = JsonNode.Parse(settings["mediaManagementSettings"]!.GetValue<string>())!.AsObject();
        media["createEventTypeFolders"]!.GetValue<bool>().Should().BeFalse();

        media["createEventTypeFolders"] = true;
        settings["mediaManagementSettings"] = media.ToJsonString();
        using var response = await server.Client.PutAsJsonAsync("api/settings", settings);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());

        var saved = JsonNode.Parse(await server.Client.GetStringAsync("api/settings"))!.AsObject();
        var savedMedia = JsonNode.Parse(saved["mediaManagementSettings"]!.GetValue<string>())!.AsObject();
        savedMedia["createEventTypeFolders"]!.GetValue<bool>().Should().BeTrue();
    }

    private sealed class SettingsServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _dataPath;
        public HttpClient Client { get; }

        private SettingsServer(WebApplication app, string dataPath)
        {
            _app = app;
            _dataPath = dataPath;
            Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        }

        public static async Task<SettingsServer> StartAsync()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), $"event-type-settings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dataPath);
            await File.WriteAllTextAsync(Path.Combine(dataPath, "config.xml"),
                "<Config><SettingsUpgradeLevel>999</SettingsUpgradeLevel></Config>");

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = dataPath
            });
            var databaseName = $"event-type-settings-{Guid.NewGuid():N}";
            builder.Services.AddSingleton<ConfigService>();
            builder.Services.AddDbContext<SportarrDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
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
            Directory.Delete(_dataPath, recursive: true);
        }
    }
}
