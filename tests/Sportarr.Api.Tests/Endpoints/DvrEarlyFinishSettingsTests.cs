using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Services;
using Sportarr.Api.Validators;

namespace Sportarr.Api.Tests.Endpoints;

public class DvrEarlyFinishSettingsTests
{
    [Fact]
    public async Task ExistingConfigDefaultsToDisabledWithFiveMinuteBuffer()
    {
        await using var server = await SettingsServer.StartAsync();

        var settings = await server.Client.GetFromJsonAsync<JsonElement>("api/dvr/settings");

        Assert.False(settings.GetProperty("earlyFinishGuardEnabled").GetBoolean());
        Assert.Equal(5, settings.GetProperty("earlyFinishBufferMinutes").GetInt32());
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    public async Task SavedBufferIsClampedAndPersisted(int requested, int expected)
    {
        await using var server = await SettingsServer.StartAsync();

        using var response = await server.Client.PutAsJsonAsync("api/dvr/settings", new
        {
            earlyFinishGuardEnabled = true,
            earlyFinishBufferMinutes = requested
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await server.Client.GetFromJsonAsync<JsonElement>("api/dvr/settings");
        Assert.True(settings.GetProperty("earlyFinishGuardEnabled").GetBoolean());
        Assert.Equal(expected, settings.GetProperty("earlyFinishBufferMinutes").GetInt32());
        var xml = await File.ReadAllTextAsync(Path.Combine(server.DataPath, "config.xml"));
        Assert.Contains("<DvrEarlyFinishGuardEnabled>true</DvrEarlyFinishGuardEnabled>", xml);
        Assert.Contains($"<DvrEarlyFinishBufferMinutes>{expected}</DvrEarlyFinishBufferMinutes>", xml);
    }

    [Theory]
    [InlineData("\"earlyFinishGuardEnabled\":\"true\"")]
    [InlineData("\"earlyFinishGuardEnabled\":1")]
    [InlineData("\"earlyFinishGuardEnabled\":null")]
    [InlineData("\"earlyFinishBufferMinutes\":\"5\"")]
    [InlineData("\"earlyFinishBufferMinutes\":null")]
    [InlineData("\"earlyFinishBufferMinutes\":true")]
    [InlineData("\"earlyFinishBufferMinutes\":5.5")]
    [InlineData("\"earlyFinishBufferMinutes\":2147483648")]
    public async Task InvalidTypesDoNotChangeEarlierSettings(string invalidField)
    {
        await using var server = await SettingsServer.StartAsync();
        var before = await File.ReadAllTextAsync(Path.Combine(server.DataPath, "config.xml"));
        var body = "{\"recordingPath\":\"/should-not-save\"," + invalidField + "}";

        using var response = await server.Client.PutAsync("api/dvr/settings",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var settings = await server.Client.GetFromJsonAsync<JsonElement>("api/dvr/settings");
        Assert.Equal("", settings.GetProperty("recordingPath").GetString());
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(server.DataPath, "config.xml")));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("true")]
    public async Task InvalidDocumentsReturnBadRequest(string body)
    {
        await using var server = await SettingsServer.StartAsync();

        using var response = await server.Client.PutAsync("api/dvr/settings",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(90, 60)]
    public async Task ReadingHandEditedConfigReturnsEffectiveBuffer(int stored, int expected)
    {
        await using var server = await SettingsServer.StartAsync(stored);

        var settings = await server.Client.GetFromJsonAsync<JsonElement>("api/dvr/settings");

        Assert.Equal(expected, settings.GetProperty("earlyFinishBufferMinutes").GetInt32());
    }

    [Fact]
    public async Task IndexerConflictPolicyDefaultsToAskAndSavesKeepBetter()
    {
        await using var server = await SettingsServer.StartAsync();

        var before = await server.Client.GetFromJsonAsync<JsonElement>("api/dvr/settings");
        Assert.Equal("Ask", before.GetProperty("indexerConflictPolicy").GetString());

        using var response = await server.Client.PutAsJsonAsync("api/dvr/settings", new
        {
            indexerConflictPolicy = "KeepBetter"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await server.Client.GetFromJsonAsync<JsonElement>("api/dvr/settings");
        Assert.Equal("KeepBetter", after.GetProperty("indexerConflictPolicy").GetString());
        var xml = await File.ReadAllTextAsync(Path.Combine(server.DataPath, "config.xml"));
        Assert.Contains("<DvrIndexerConflictPolicy>KeepBetter</DvrIndexerConflictPolicy>", xml);
    }

    [Theory]
    [InlineData("\"indexerConflictPolicy\":\"keepbetter\"")]
    [InlineData("\"indexerConflictPolicy\":\"Newest\"")]
    [InlineData("\"indexerConflictPolicy\":\"\"")]
    [InlineData("\"indexerConflictPolicy\":null")]
    [InlineData("\"indexerConflictPolicy\":1")]
    public async Task InvalidIndexerConflictPolicyDoesNotChangeEarlierSettings(string invalidField)
    {
        await using var server = await SettingsServer.StartAsync();
        var before = await File.ReadAllTextAsync(Path.Combine(server.DataPath, "config.xml"));
        var body = "{\"recordingPath\":\"/should-not-save\"," + invalidField + "}";

        using var response = await server.Client.PutAsync("api/dvr/settings",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(server.DataPath, "config.xml")));
    }

    private sealed class SettingsServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public string DataPath { get; }
        public HttpClient Client { get; }

        private SettingsServer(WebApplication app, string dataPath)
        {
            _app = app;
            DataPath = dataPath;
            Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        }

        public static async Task<SettingsServer> StartAsync(int? storedBuffer = null)
        {
            var dataPath = Path.Combine(Path.GetTempPath(), $"dvr-settings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dataPath);
            var bufferXml = storedBuffer.HasValue
                ? $"<DvrEarlyFinishBufferMinutes>{storedBuffer.Value}</DvrEarlyFinishBufferMinutes>"
                : "";
            await File.WriteAllTextAsync(Path.Combine(dataPath, "config.xml"),
                $"<Config><SettingsUpgradeLevel>999</SettingsUpgradeLevel>{bufferXml}</Config>");
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = dataPath
            });
            builder.Services.AddSingleton<ConfigService>();
            builder.Services.AddValidatorsFromAssemblyContaining<ScheduleDvrRecordingRequestValidator>();
            foreach (var type in new[]
            {
                typeof(DvrRecordingService), typeof(DvrQualityScoreCalculator), typeof(SportarrDbContext),
                typeof(EventDvrService), typeof(IptvOrgSyncService), typeof(EventChannelResolverService),
                typeof(DvrAutoSchedulerService), typeof(DvrChannelReresolveService), typeof(FFmpegRecorderService),
                typeof(DiskSpaceService)
            })
            {
                builder.Services.AddScoped(type, _ => throw new InvalidOperationException("Settings must not resolve unrelated services."));
            }
            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.MapDvrEndpoints();
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
