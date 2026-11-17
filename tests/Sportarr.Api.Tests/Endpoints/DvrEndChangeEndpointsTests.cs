using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Middleware;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Validators;

namespace Sportarr.Api.Tests.Endpoints;

// Recording ids are unique to this class. The service keeps the extension
// budget in static state.
public class DvrEndChangeEndpointsTests
{
    [Fact]
    public async Task ExtendReturnsTheNewEndAndTheTotalExtension()
    {
        await using var server = await EndChangeServer.StartAsync();
        var end = DateTime.UtcNow.AddHours(1);
        await server.AddAsync(44101, DvrRecordingStatus.Recording, end);

        using var response = await server.Client.PostAsJsonAsync("api/dvr/recordings/44101/extend", new { minutes = 15 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(end.AddMinutes(15), body.GetProperty("scheduledEnd").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));
        Assert.Equal(15, body.GetProperty("totalExtensionMinutes").GetInt32());
        Assert.Equal(120, body.GetProperty("extensionCapMinutes").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"minutes\":\"5\"}")]
    [InlineData("{\"minutes\":5.5}")]
    [InlineData("{\"minutes\":null}")]
    [InlineData("{\"minutes\":0}")]
    [InlineData("{\"minutes\":241}")]
    public async Task ExtendRejectsABadBody(string json)
    {
        await using var server = await EndChangeServer.StartAsync();
        await server.AddAsync(44102, DvrRecordingStatus.Recording, DateTime.UtcNow.AddHours(1));

        using var response = await server.Client.PostAsync("api/dvr/recordings/44102/extend",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRecordingReturnsNotFound()
    {
        await using var server = await EndChangeServer.StartAsync();

        using var extend = await server.Client.PostAsJsonAsync("api/dvr/recordings/44199/extend", new { minutes = 5 });
        using var finish = await server.Client.PostAsync("api/dvr/recordings/44199/finish", null);

        Assert.Equal(HttpStatusCode.NotFound, extend.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, finish.StatusCode);
    }

    [Fact]
    public async Task AFinishedRecordingReturnsConflict()
    {
        await using var server = await EndChangeServer.StartAsync();
        await server.AddAsync(44103, DvrRecordingStatus.Completed, DateTime.UtcNow.AddMinutes(-10));

        using var extend = await server.Client.PostAsJsonAsync("api/dvr/recordings/44103/extend", new { minutes = 5 });
        using var finish = await server.Client.PostAsJsonAsync("api/dvr/recordings/44103/finish", new { afterMinutes = 0 });

        Assert.Equal(HttpStatusCode.Conflict, extend.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, finish.StatusCode);
    }

    [Fact]
    public async Task ReachingTheCapReturnsConflictAndSaysSo()
    {
        await using var server = await EndChangeServer.StartAsync();
        await server.AddAsync(44104, DvrRecordingStatus.Recording, DateTime.UtcNow.AddHours(1));
        using (var first = await server.Client.PostAsJsonAsync("api/dvr/recordings/44104/extend", new { minutes = 120 }))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var response = await server.Client.PostAsJsonAsync("api/dvr/recordings/44104/extend", new { minutes = 5 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("capReached").GetBoolean());
        Assert.Contains("cap", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task FinishAcceptsAnEmptyBodyAndADelay()
    {
        await using var server = await EndChangeServer.StartAsync();
        await server.AddAsync(44105, DvrRecordingStatus.Recording, DateTime.UtcNow.AddHours(1));
        await server.AddAsync(44106, DvrRecordingStatus.Recording, DateTime.UtcNow.AddHours(1));

        using var now = await server.Client.PostAsync("api/dvr/recordings/44105/finish", null);
        using var later = await server.Client.PostAsJsonAsync("api/dvr/recordings/44106/finish", new { afterMinutes = 10 });

        Assert.Equal(HttpStatusCode.OK, now.StatusCode);
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        var body = await later.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(DateTime.UtcNow.AddMinutes(10), body.GetProperty("finishAt").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("{\"afterMinutes\":-1}")]
    [InlineData("{\"afterMinutes\":61}")]
    [InlineData("{\"afterMinutes\":\"5\"}")]
    [InlineData("not json")]
    public async Task FinishRejectsABadDelay(string json)
    {
        await using var server = await EndChangeServer.StartAsync();
        await server.AddAsync(44107, DvrRecordingStatus.Recording, DateTime.UtcNow.AddHours(1));

        using var response = await server.Client.PostAsync("api/dvr/recordings/44107/finish",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/dvr/recordings/1/extend")]
    [InlineData("/api/dvr/recordings/1/finish")]
    public void EndChangeRoutesRequireAuthentication(string path)
    {
        Assert.False(DynamicAuthenticationMiddleware.IsPublicPath(path, "POST"));
    }

    [Theory]
    [InlineData("Session", "forms login")]
    [InlineData("Forms", "forms login")]
    [InlineData("ApiKey", "API key")]
    [InlineData(null, "unknown")]
    public void TheLogNamesHowTheCallerSignedIn(string? authenticationType, string expected)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        if (authenticationType != null)
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(authenticationType));

        Assert.Equal(expected, DvrEndpoints.DescribeCaller(context));
    }

    private sealed class EndChangeServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly SqliteConnection _connection;
        private readonly string _dataPath;
        public HttpClient Client { get; }

        private EndChangeServer(WebApplication app, SqliteConnection connection, string dataPath)
        {
            _app = app;
            _connection = connection;
            _dataPath = dataPath;
            Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        }

        private SportarrDbContext CreateDb() =>
            new(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(_connection).Options);

        public async Task AddAsync(int id, DvrRecordingStatus status, DateTime end)
        {
            await using var db = CreateDb();
            db.DvrRecordings.Add(new DvrRecording
            {
                Id = id, ChannelId = 1, Title = $"Recording {id}", Status = status, Method = DvrRecordingMethod.Live,
                ScheduledStart = DateTime.UtcNow.AddHours(-1), ScheduledEnd = end, PostPadding = 0,
                ActualStart = status == DvrRecordingStatus.Recording ? DateTime.UtcNow.AddHours(-1) : null
            });
            await db.SaveChangesAsync();
        }

        public static async Task<EndChangeServer> StartAsync()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), $"dvr-end-change-api-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dataPath);
            await File.WriteAllTextAsync(Path.Combine(dataPath, "config.xml"),
                "<Config><SettingsUpgradeLevel>999</SettingsUpgradeLevel></Config>");

            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options;
            await using (var seed = new SportarrDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync();
                seed.IptvSources.Add(new IptvSource { Id = 1, Name = "Fixture", Url = "https://iptv.invalid/list" });
                seed.IptvChannels.Add(new IptvChannel { Id = 1, SourceId = 1, Name = "Fixture", StreamUrl = "https://iptv.invalid/stream" });
                await seed.SaveChangesAsync();
            }

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = dataPath
            });
            builder.Services.AddSingleton<ConfigService>();
            builder.Services.AddValidatorsFromAssemblyContaining<ScheduleDvrRecordingRequestValidator>();
            builder.Services.AddScoped(_ => new SportarrDbContext(options));
            builder.Services.AddSingleton(sp => new FFmpegRecorderService(
                NullLogger<FFmpegRecorderService>.Instance, sp.GetRequiredService<ConfigService>(), null!));
            builder.Services.AddScoped(sp => new DvrRecordingService(NullLogger<DvrRecordingService>.Instance,
                sp.GetRequiredService<SportarrDbContext>(), sp.GetRequiredService<FFmpegRecorderService>(), null!,
                sp.GetRequiredService<ConfigService>(), null!, null!, null!, null!, null!, new DvrEarlyFinishGuard()));
            foreach (var type in new[]
            {
                typeof(DvrQualityScoreCalculator), typeof(EventDvrService), typeof(IptvOrgSyncService),
                typeof(EventChannelResolverService), typeof(DvrAutoSchedulerService),
                typeof(DvrChannelReresolveService), typeof(DiskSpaceService)
            })
            {
                builder.Services.AddScoped(type, _ => throw new InvalidOperationException("These tests must not resolve unrelated services."));
            }
            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.MapDvrEndpoints();
            await app.StartAsync();
            return new EndChangeServer(app, connection, dataPath);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
            await _connection.DisposeAsync();
            Directory.Delete(_dataPath, recursive: true);
        }
    }
}
