using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Services;

internal sealed class RegrabMissingHttpHarness : IAsyncDisposable
{
    // The search queue shares event identities across fixture apps.
    private static int _nextEventId = 2_000_000;
    private readonly PartIdentityIntegrationHarness _services;
    private readonly SqliteConnection _connection;
    private readonly WebApplication _app;
    public SportarrDbContext Db { get; }
    public HttpClient Client { get; }
    public string DirectoryPath { get; }
    public Event Event { get; private set; } = null!;
    public Task<IDisposable> EnterEventDecisionAsync() => _services.Services.GetRequiredService<DownloadClientService>().EnterEventDecisionAsync(Event.Id);
    public int ClientAdds => _services.Transport.ClientAdds;

    private RegrabMissingHttpHarness(PartIdentityIntegrationHarness services, SqliteConnection connection,
        SportarrDbContext db, WebApplication app, string directory)
    {
        _services = services; _connection = connection; Db = db; _app = app;
        DirectoryPath = directory; Client = app.GetTestClient();
    }

    public static async Task<RegrabMissingHttpHarness> CreateAsync(Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null)
    {
        var services = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = Path.Combine(Path.GetTempPath(), "sportarr-regrab-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var connection = new SqliteConnection("DataSource=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection);
        if (interceptor != null) options.AddInterceptors(interceptor);
        var db = new SportarrDbContext(options.Options);
        await db.Database.EnsureCreatedAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(db);
        foreach (var type in new[] { typeof(ConfigService), typeof(AutomaticSearchService), typeof(NotificationService),
            typeof(DownloadClientService), typeof(IMetadataWriterService) })
            builder.Services.AddSingleton(type, services.Services.GetRequiredService(type));
        builder.Services.AddSingleton<SearchQueueService>();
        // The sibling routes must bind services but must not execute in these tests.
        foreach (var type in new[] { typeof(EventDvrService), typeof(EventStreamService), typeof(FileRenameService) })
            builder.Services.AddSingleton(type, _ => throw new InvalidOperationException("Unexpected sibling endpoint service: " + type.Name));
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
        var app = builder.Build(); app.MapEventEndpoints(); app.MapHistoryEndpoints(); await app.StartAsync();
        var rig = new RegrabMissingHttpHarness(services, connection, db, app, directory);
        var league = new League { Name = "UFC", Sport = "Fighting", Monitored = true };
        db.Leagues.Add(league); await db.SaveChangesAsync();
        rig.Event = new Event { Id = Interlocked.Increment(ref _nextEventId), Title = "UFC 9999", Sport = "Fighting", LeagueId = league.Id,
            EventDate = new DateTime(2020, 9, 1, 20, 0, 0, DateTimeKind.Utc), HasFile = true };
        db.Events.Add(rig.Event);
        db.DownloadClients.Add(new DownloadClient { Name = "Owned fixture", Type = DownloadClientType.Sabnzbd,
            Host = "part-client.invalid", Port = 8080, Category = "sportarr", ApiKey = "fixture", Enabled = true });
        await db.SaveChangesAsync(); return rig;
    }

    public async Task<EventFile> AddFileAsync(string name = "main-card.mkv", bool onDisk = true, bool exists = true, string part = "Main Card")
    {
        var path = Path.Combine(DirectoryPath, name);
        if (onDisk) await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'h', 4096).ToArray());
        var file = new EventFile { EventId = Event.Id, FilePath = path, Size = 4096, Quality = "WEBDL-720p",
            Exists = exists, PartName = part, PartNumber = part == "Main Card" ? 3 : 2 };
        Db.EventFiles.Add(file); Event.FilePath = path; Event.FileSize = file.Size; Event.Quality = file.Quality;
        await Db.SaveChangesAsync(); return file;
    }

    public GrabHistory History(string? path, string name = "owned", int? eventId = null) => new()
    {
        EventId = eventId ?? Event.Id, Title = "UFC.9999.Main.Card.720p.WEB-DL-" + name,
        PartName = "Main Card", DestinationPath = path, FileExists = true, WasImported = true,
        ImportedAt = DateTime.UtcNow.AddDays(-1), GrabbedAt = DateTime.UtcNow.AddDays(-2),
        DownloadUrl = "http://part-source.invalid/" + name + ".nzb", Guid = "history-" + name,
        Protocol = "Usenet", Indexer = "Owned fixture", Quality = "WEBDL-720p", Size = 4096
    };

    public async Task<JsonElement> ReadAsync(string path)
    {
        using var response = await Client.GetAsync(path); return await BodyAsync(response, HttpStatusCode.OK);
    }

    public async Task<JsonElement> DeleteAsync(EventFile file, HttpStatusCode expected = HttpStatusCode.OK, int? eventId = null,
        int? fileId = null, string blocklistAction = "none")
    {
        using var response = await Client.DeleteAsync(
            $"/api/events/{eventId ?? file.EventId}/files/{fileId ?? file.Id}?blocklistAction={blocklistAction}");
        return await BodyAsync(response, expected);
    }

    public async Task<JsonElement> BatchAsync(string query = "?limit=1")
    {
        using var response = await Client.PostAsJsonAsync("/api/grab-history/regrab-missing" + query, new { });
        return await BodyAsync(response, HttpStatusCode.OK);
    }

    public async Task SetMultiPartAsync(bool enabled)
    {
        var service = _services.Services.GetRequiredService<ConfigService>();
        var config = await service.GetConfigAsync(); config.EnableMultiPartEpisodes = enabled; await service.SaveConfigAsync(config);
    }

    public async Task SetRecycleBinAsync(string path)
    {
        var service = _services.Services.GetRequiredService<ConfigService>();
        var config = await service.GetConfigAsync(); config.RecycleBin = path; await service.SaveConfigAsync(config);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose(); await _app.DisposeAsync(); await Db.DisposeAsync(); await _connection.DisposeAsync();
        await _services.DisposeAsync(); if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
    }
}
