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
using Sportarr.Api.Services;
using Sportarr.Api.Validators;

namespace Sportarr.Api.Tests.Endpoints;

public class AutomaticUpdateSettingsTests
{
    private const string ContainerReason = "Sportarr runs in a container. Pull the new image and recreate the container to update.";
    private const string FolderReason = "Sportarr cannot write to its install folder /opt/sportarr.";

    [Theory]
    [InlineData(UpdateMechanisms.BuiltIn, true, null, null)]
    [InlineData(UpdateMechanisms.BuiltIn, false, FolderReason, null)]
    [InlineData(UpdateMechanisms.Docker, false, ContainerReason, null)]
    [InlineData(UpdateMechanisms.Script, true, null, "/opt/sportarr-update/run.sh")]
    public async Task Get_returns_the_method_and_if_automatic_updates_can_run(string mechanism, bool available, string? reason, string? scriptPath)
    {
        await using var server = await SettingsServer.StartAsync(
            "<AutomaticUpdates>true</AutomaticUpdates>", new UpdateMethod(mechanism, available, reason, scriptPath));

        var update = await GetUpdateSettingsAsync(server);

        update["mechanism"]!.GetValue<string>().Should().Be(mechanism.ToLowerInvariant());
        update["automaticUpdates"]!.GetValue<bool>().Should().BeTrue();
        update.ContainsKey("automatic").Should().BeFalse("v4.1.12 used that name for the tick that did nothing");
        update["automaticAvailable"]!.GetValue<bool>().Should().Be(available);
        update["automaticUnavailableReason"]?.GetValue<string>().Should().Be(reason);
        if (reason == null)
            update["automaticUnavailableReason"].Should().BeNull();
        // The page shows the script path. It cannot change it.
        update["scriptPath"]?.GetValue<string>().Should().Be(scriptPath);
        if (scriptPath == null)
            update["scriptPath"].Should().BeNull();
    }

    [Theory]
    [InlineData("", "false")]
    [InlineData("<AutomaticUpdates>false</AutomaticUpdates>", "false")]
    [InlineData("<AutomaticUpdates>true</AutomaticUpdates>", "true")]
    public async Task A_v4_1_12_page_never_changes_the_tick(string stored, string kept)
    {
        // A General page loaded from v4.1.12 sends its own update block.
        // Its "automatic" is the old tick that did nothing.
        await using var server = await SettingsServer.StartAsync(
            "<UpdateAutomatically>true</UpdateAutomatically><UpdateMechanism>BuiltIn</UpdateMechanism>" + stored);

        using var response = await PutRawUpdateAsync(server,
            """{"branch":"main","automatic":true,"mechanism":"builtin","scriptPath":""}""");

        response.IsSuccessStatusCode.Should().BeTrue();
        (await File.ReadAllTextAsync(server.ConfigPath)).Should().Contain($"<AutomaticUpdates>{kept}</AutomaticUpdates>");
        (await GetUpdateSettingsAsync(server))["automaticUpdates"]!.GetValue<bool>().Should().Be(kept == "true");
    }

    [Theory]
    [InlineData("BuiltIn")]
    [InlineData("Docker")]
    [InlineData("Script")]
    public async Task An_old_automatic_tick_shows_as_off(string oldMechanism)
    {
        // v4.1.12 and earlier stored this tick, but it did nothing.
        await using var server = await SettingsServer.StartAsync(
            $"<UpdateAutomatically>true</UpdateAutomatically><UpdateMechanism>{oldMechanism}</UpdateMechanism>");

        (await GetUpdateSettingsAsync(server))["automaticUpdates"]!.GetValue<bool>().Should().BeFalse();
    }

    [Theory]
    [InlineData("docker")]
    [InlineData("script")]
    [InlineData("external")]
    [InlineData("apt")]
    [InlineData("")]
    public async Task A_posted_mechanism_is_ignored(string posted)
    {
        await using var server = await SettingsServer.StartAsync("<UpdateMechanism>Docker</UpdateMechanism>");

        using var response = await PutUpdateAsync(server, update =>
        {
            update["mechanism"] = posted;
            update["automaticAvailable"] = false;
            update["automaticUnavailableReason"] = "posted";
        });

        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue("the endpoint returned {0}", body);
        var update = await GetUpdateSettingsAsync(server);
        update["mechanism"]!.GetValue<string>().Should().Be("builtin");
        update["automaticAvailable"]!.GetValue<bool>().Should().BeTrue();
        var xml = await File.ReadAllTextAsync(server.ConfigPath);
        xml.Should().Contain("<UpdateMechanism>Docker</UpdateMechanism>", "the old value is not used and not changed");
        xml.Should().NotContain("<UpdateScriptPath>/");
    }

    [Fact]
    public async Task Ticking_the_box_and_saving_turns_automatic_updates_on()
    {
        await using var server = await SettingsServer.StartAsync("");

        using var response = await PutUpdateAsync(server, update => update["automaticUpdates"] = true);

        response.IsSuccessStatusCode.Should().BeTrue();
        (await File.ReadAllTextAsync(server.ConfigPath)).Should().Contain("<AutomaticUpdates>true</AutomaticUpdates>");
        (await GetUpdateSettingsAsync(server))["automaticUpdates"]!.GetValue<bool>().Should().BeTrue();
    }

    [Theory]
    [InlineData("<UpdateAutomatically>true</UpdateAutomatically>")]
    [InlineData("<UpdateAutomatically>true</UpdateAutomatically><UpdateMechanism>BuiltIn</UpdateMechanism>")]
    [InlineData("")]
    public async Task A_round_trip_from_another_page_leaves_automatic_off(string stored)
    {
        // Other settings pages send back the update block they were shown.
        await using var server = await SettingsServer.StartAsync(stored);

        using var response = await PutUpdateAsync(server, _ => { });

        response.IsSuccessStatusCode.Should().BeTrue();
        (await File.ReadAllTextAsync(server.ConfigPath)).Should().Contain("<AutomaticUpdates>false</AutomaticUpdates>");
        (await GetUpdateSettingsAsync(server))["automaticUpdates"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task A_save_keeps_the_script_path_from_config_xml()
    {
        await using var server = await SettingsServer.StartAsync("<UpdateScriptPath>/scripts/update.sh</UpdateScriptPath>");

        // An older page sends an empty script path. The page sends back the shown one.
        using var response = await PutUpdateAsync(server, update => update["scriptPath"] = "");
        response.IsSuccessStatusCode.Should().BeTrue();
        using var changed = await PutUpdateAsync(server, update => update["scriptPath"] = "/tmp/other.sh");

        changed.IsSuccessStatusCode.Should().BeTrue();
        (await File.ReadAllTextAsync(server.ConfigPath)).Should().Contain("<UpdateScriptPath>/scripts/update.sh</UpdateScriptPath>");
    }

    private static async Task<JsonObject> GetUpdateSettingsAsync(SettingsServer server)
    {
        var settings = JsonNode.Parse(await server.Client.GetStringAsync("api/settings"))!.AsObject();
        return JsonNode.Parse(settings["updateSettings"]!.GetValue<string>())!.AsObject();
    }

    // Sends back the settings the page was shown, after the change.
    private static async Task<HttpResponseMessage> PutUpdateAsync(SettingsServer server, Action<JsonObject> change)
    {
        var settings = JsonNode.Parse(await server.Client.GetStringAsync("api/settings"))!.AsObject();
        var update = JsonNode.Parse(settings["updateSettings"]!.GetValue<string>())!.AsObject();
        change(update);
        settings["updateSettings"] = update.ToJsonString();
        return await server.Client.PutAsJsonAsync("api/settings", settings);
    }

    // Sends the settings the page was shown, with this update block instead.
    private static async Task<HttpResponseMessage> PutRawUpdateAsync(SettingsServer server, string updateBlock)
    {
        var settings = JsonNode.Parse(await server.Client.GetStringAsync("api/settings"))!.AsObject();
        settings["updateSettings"] = updateBlock;
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

        // "stored" is the XML inside <Config>.
        public static async Task<SettingsServer> StartAsync(string stored, UpdateMethod? method = null)
        {
            var dataPath = Path.Combine(Path.GetTempPath(), $"automatic-update-settings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dataPath);
            await File.WriteAllTextAsync(
                Path.Combine(dataPath, "config.xml"),
                $"<Config><SettingsUpgradeLevel>999</SettingsUpgradeLevel>{stored}</Config>");

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = dataPath
            });
            builder.Services.AddSingleton<ConfigService>();
            builder.Services.AddSingleton<IUpdateMethodSource>(new FakeUpdateMethodSource(method));
            builder.Services.AddDbContext<SportarrDbContext>(options =>
                options.UseInMemoryDatabase($"automatic-update-settings-{Guid.NewGuid():N}"));
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
