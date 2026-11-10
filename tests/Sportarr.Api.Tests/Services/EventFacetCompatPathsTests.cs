using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #380. The v3 series route (Maintainerr and other tools) and the
/// import lists also monitor events. Both must follow the league's facet
/// rules, or an outside tool monitors the races the user excluded.
/// </summary>
public class EventFacetCompatPathsTests : IAsyncLifetime
{
    private const string WomenOnly = "[{\"gender\":[\"Women\"]}]";
    private static readonly DateTime RaceDay = new(2026, 12, 19, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sportarr-facets-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["Sportarr:DataPath"] = _directory;
        builder.Services.AddDbContext<SportarrDbContext>(options =>
            options.UseSqlite("Data Source=" + Path.Combine(_directory, "test.db") + ";Pooling=False"));
        builder.Services.AddSingleton<ConfigService>();
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient<SportarrApiClient>();
        builder.Services.AddHttpClient<NotificationService>();
        builder.Services.AddScoped<LeagueAddService>();
        builder.Services.AddSingleton<TaskService>();
        // Unused routes in this group still need their service types registered.
        builder.Services.AddSingleton<IMetadataWriterService>(_ => null!);
        builder.Services.AddScoped(sp => new LibraryImportService(
            sp.GetRequiredService<SportarrDbContext>(), sp.GetRequiredService<ILogger<LibraryImportService>>(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!));
        builder.Services.AddSingleton<FileImportService>(_ => null!);
        _app = builder.Build();
        _app.MapSonarrSeriesEndpoints();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.RootFolders.Add(new RootFolder { Path = _directory });
            db.Leagues.Add(new League
            {
                Id = 1, Name = "FIS Alpine Ski World Cup", Sport = "Skiing", SportFormat = "EventSport",
                ExternalId = "lg-001231", Monitored = false, MonitoredFacets = WomenOnly
            });
            foreach (var (title, offset) in new[]
            {
                ("Mens Downhill at Val Gardena", 0), ("Womens Downhill at Val d'Isere", 0), ("Alpine Team Event at Are", 1)
            })
            {
                db.Events.Add(new Event
                {
                    Title = title, Sport = "Skiing", LeagueId = 1, EventDate = RaceDay.AddDays(offset),
                    Season = "2026-2027", SeasonNumber = 2026, Monitored = false
                });
            }
            await db.SaveChangesAsync();
        }
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        try { Directory.Delete(_directory, true); } catch (IOException) { }
    }

    private async Task<Dictionary<string, bool>> MonitoredAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        return await db.Events.AsNoTracking().ToDictionaryAsync(e => e.Title, e => e.Monitored);
    }

    [Fact]
    public async Task V3SeasonMonitor_KeepsTheExcludedEventsUnmonitored()
    {
        var response = await _client.PutAsJsonAsync("/api/v3/series/1", new
        {
            id = 1, seasons = new[] { new { seasonNumber = 2026, monitored = true } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var monitored = await MonitoredAsync();
        monitored["Mens Downhill at Val Gardena"].Should().BeFalse();
        monitored["Womens Downhill at Val d'Isere"].Should().BeTrue();
        monitored["Alpine Team Event at Are"].Should().BeTrue();
    }

    [Fact]
    public async Task V3SeriesMonitor_KeepsTheExcludedEventsUnmonitored()
    {
        var response = await _client.PutAsJsonAsync("/api/v3/series/1", new { id = 1, monitored = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var monitored = await MonitoredAsync();
        monitored["Mens Downhill at Val Gardena"].Should().BeFalse();
        monitored["Womens Downhill at Val d'Isere"].Should().BeTrue();
    }

    [Fact]
    public async Task ImportListRemonitor_KeepsTheExcludedEventsUnmonitored()
    {
        int listId;
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            var list = new ImportList
            {
                Name = "Ski feed", ListType = ImportListType.RSS, Url = "http://feed.invalid/ski.xml",
                Enabled = true, MonitorEvents = true
            };
            db.ImportLists.Add(list);
            await db.SaveChangesAsync();
            listId = list.Id;
        }

        var feed = new StringBuilder("<?xml version=\"1.0\"?><rss version=\"2.0\"><channel>");
        foreach (var title in new[] { "Mens Downhill at Val Gardena", "Womens Downhill at Val d'Isere" })
            feed.Append($"<item><title>{System.Security.SecurityElement.Escape(title)}</title><pubDate>2026-12-19T12:00:00Z</pubDate></item>");
        feed.Append("</channel></rss>");
        var service = new ImportListService(NullLogger<ImportListService>.Instance,
            _app.Services.GetRequiredService<IServiceScopeFactory>(), new FeedFactory(feed.ToString()));

        var (success, message, _) = await service.SyncImportListAsync(listId);

        success.Should().BeTrue(message);
        var monitored = await MonitoredAsync();
        monitored["Mens Downhill at Val Gardena"].Should().BeFalse("the league rules monitor women only");
        monitored["Womens Downhill at Val d'Isere"].Should().BeTrue();
    }

    private sealed class FeedFactory : HttpMessageHandler, IHttpClientFactory
    {
        private readonly string _feed;
        public FeedFactory(string feed) => _feed = feed;
        public HttpClient CreateClient(string name) => new(this, false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_feed, Encoding.UTF8, "application/rss+xml")
            });
    }
}
