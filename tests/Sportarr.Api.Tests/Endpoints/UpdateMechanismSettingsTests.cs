using System.Net;
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
using Sportarr.Api.Services;
using Sportarr.Api.Validators;

namespace Sportarr.Api.Tests.Endpoints;

public class UpdateMechanismSettingsTests
{
    [Theory]
    [InlineData("Apt", "external")]
    [InlineData("BuiltIn", "builtin")]
    public async Task Apt_shows_as_external_without_rewriting_the_config(string stored, string shown)
    {
        await using var server = await SettingsServer.StartAsync(stored);

        var update = await GetUpdateSettingsAsync(server);

        update["mechanism"]!.GetValue<string>().Should().Be(shown);
        (await File.ReadAllTextAsync(server.ConfigPath))
            .Should().Contain($"<UpdateMechanism>{stored}</UpdateMechanism>");
    }

    [Theory]
    [InlineData("SCRIPT", "Script")]
    [InlineData("docker", "Docker")]
    [InlineData("External", "External")]
    [InlineData("builtin", "BuiltIn")]
    public async Task Saves_the_four_mechanisms_in_any_case(string sent, string saved)
    {
        await using var server = await SettingsServer.StartAsync("Docker");

        using var response = await PutMechanismAsync(server, sent);

        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue("the endpoint returned {0}", body);
        (await File.ReadAllTextAsync(server.ConfigPath))
            .Should().Contain($"<UpdateMechanism>{saved}</UpdateMechanism>");
    }

    [Theory]
    [InlineData("apt")]
    [InlineData("selfupdate")]
    [InlineData("")]
    public async Task Rejects_any_other_mechanism(string sent)
    {
        await using var server = await SettingsServer.StartAsync("Docker");
        var before = await File.ReadAllTextAsync(server.ConfigPath);

        using var response = await PutMechanismAsync(server, sent);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await File.ReadAllTextAsync(server.ConfigPath)).Should().Be(before);
    }

    private static async Task<JsonObject> GetUpdateSettingsAsync(SettingsServer server)
    {
        var settings = JsonNode.Parse(await server.Client.GetStringAsync("api/settings"))!.AsObject();
        return JsonNode.Parse(settings["updateSettings"]!.GetValue<string>())!.AsObject();
    }

    private static async Task<HttpResponseMessage> PutMechanismAsync(SettingsServer server, string mechanism)
    {
        var settings = JsonNode.Parse(await server.Client.GetStringAsync("api/settings"))!.AsObject();
        var update = JsonNode.Parse(settings["updateSettings"]!.GetValue<string>())!.AsObject();
        update["mechanism"] = mechanism;
        settings["updateSettings"] = update.ToJsonString();
        return await server.Client.PutAsJsonAsync("api/settings", settings);
    }

    private sealed class SettingsServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _dataPath;
        public HttpClient Client { get; }
        public string ConfigPath => Path.Combine(_dataPath, "config.xml");

        private SettingsServer(WebApplication app, string dataPath)
        {
            _app = app;
            _dataPath = dataPath;
            Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        }

        public static async Task<SettingsServer> StartAsync(string storedMechanism)
        {
            var dataPath = Path.Combine(Path.GetTempPath(), $"update-mechanism-settings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dataPath);
            await File.WriteAllTextAsync(
                Path.Combine(dataPath, "config.xml"),
                $"<Config><SettingsUpgradeLevel>999</SettingsUpgradeLevel><UpdateMechanism>{storedMechanism}</UpdateMechanism></Config>");

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = dataPath
            });
            builder.Services.AddSingleton<ConfigService>();
            builder.Services.AddDbContext<SportarrDbContext>(options =>
                options.UseInMemoryDatabase($"update-mechanism-settings-{Guid.NewGuid():N}"));
            builder.Services.AddValidatorsFromAssemblyContaining<ScheduleDvrRecordingRequestValidator>();
            builder.Services.AddScoped<SimpleAuthService>();
            builder.Services.AddScoped<FileFormatManager>();

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
