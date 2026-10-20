using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Endpoints;

public class SonarrSeriesEndpointsTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sportarr-series-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration["Sportarr:DataPath"] = _directory;
        builder.Services.AddDbContext<SportarrDbContext>(options =>
            options.UseSqlite("Data Source=" + Path.Combine(_directory, "test.db") + ";Pooling=False"));
        builder.Services.AddSingleton<ConfigService>();
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient<SportarrApiClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new CatalogHandler());
        builder.Services.AddHttpClient<NotificationService>();
        builder.Services.AddScoped<LeagueAddService>();
        builder.Services.AddSingleton<TaskService>();
        // Unused routes in these groups still need their service types registered.
        builder.Services.AddSingleton<IMetadataWriterService>(_ => null!);
        // Exercise manual import's filename fallback without a media inspection pipeline.
        builder.Services.AddScoped(sp => new LibraryImportService(
            sp.GetRequiredService<SportarrDbContext>(), sp.GetRequiredService<ILogger<LibraryImportService>>(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!));
        builder.Services.AddSingleton<FileImportService>(_ => null!);
        _app = builder.Build();
        _app.MapSonarrSeriesEndpoints();
        _app.MapSonarrCalendarEndpoint();
        _app.MapSonarrWantedEndpoint();
        _app.MapSonarrCommandEndpoints();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.RootFolders.Add(new RootFolder { Path = _directory });
            db.Leagues.Add(new League
            {
                Id = 1, Name = "NHL", Sport = "Ice Hockey", ExternalId = "lg-000041", Monitored = true,
                PosterUrl = "https://example.test/poster.jpg", LogoUrl = "https://example.test/logo.png"
            });
            db.Events.Add(new Event
            {
                Title = "Philadelphia Flyers vs Boston Bruins", Sport = "Ice Hockey", LeagueId = 1,
                EventDate = new DateTime(2026, 10, 20, 19, 0, 0, DateTimeKind.Utc),
                Season = "2026-2027", SeasonNumber = 2026, Monitored = true
            });
            await db.SaveChangesAsync();
        }
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [Theory]
    [InlineData("/api/v3/series")]
    [InlineData("/api/v3/series/1")]
    [InlineData("/api/v3/series/lookup?term=tvdb:900000041")]
    [InlineData("/api/v3/series/lookup?term=NHL")]
    [InlineData("/api/v3/series/lookup?term=tvdb:900000042")]
    public async Task SeriesReads_CarryRequiredFieldsAndTypes(string route)
    {
        var result = await _client.GetFromJsonAsync<JsonElement>(route);
        var series = result.ValueKind == JsonValueKind.Array ? result.EnumerateArray().ToArray() : new[] { result };
        series.Should().NotBeEmpty();
        foreach (var entry in series)
            AssertSeries(entry);
    }

    [Theory]
    [InlineData("/api/v3/series/1")]
    [InlineData("/api/v3/series")]
    [InlineData("/api/v3/series/editor")]
    public async Task SeriesUpdates_ReturnDecodableSeries(string route)
    {
        var response = await _client.PutAsJsonAsync(route, new
        {
            id = 1, seriesIds = new[] { 1 }, monitored = false, tags = new[] { 7 }, applyTags = "add"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        AssertSeries(result.ValueKind == JsonValueKind.Array ? result[0] : result);
    }

    [Fact]
    public async Task SeriesAdd_ReturnsDecodableSeries()
    {
        var response = await _client.PostAsJsonAsync("/api/v3/series", new { tvdbId = 900000042 });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        AssertSeries(await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    [Theory]
    [InlineData(true, 2026)]
    [InlineData(false, 2026)]
    [InlineData(false, null)]
    public async Task Calendar_EmbedsDecodableSeriesAndTheEventSeason(bool hasPoster, int? seasonNumber)
    {
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            var league = await db.Leagues.SingleAsync();
            league.PosterUrl = hasPoster ? league.PosterUrl : null;
            var evt = await db.Events.SingleAsync();
            evt.SeasonNumber = seasonNumber;
            await db.SaveChangesAsync();
        }

        var calendar = await _client.GetFromJsonAsync<JsonElement>(
            "/api/v3/calendar?start=2026-10-19&end=2026-10-21&includeSeries=true");
        calendar.GetArrayLength().Should().Be(1);
        var episode = calendar[0];
        foreach (var field in new[] { "id", "seriesId", "episodeFileId", "tvdbId", "seasonNumber", "episodeNumber" })
            episode.GetProperty(field).GetInt32().Should().BeGreaterThanOrEqualTo(0);
        foreach (var field in new[] { "hasFile", "monitored", "unverifiedSceneNumbering" })
            episode.GetProperty(field).GetBoolean();
        episode.GetProperty("airDateUtc").GetDateTimeOffset().Offset.Should().Be(TimeSpan.Zero);
        var series = episode.GetProperty("series");
        AssertSeries(series);
        series.GetProperty("seasons").GetArrayLength().Should().Be(1);
        series.GetProperty("seasons")[0].GetProperty("seasonNumber").GetInt32().Should().Be(2026);
        series.GetProperty("seasons")[0].GetProperty("monitored").GetBoolean().Should().BeTrue();
        series.GetProperty("images").EnumerateArray().Count(i => i.GetProperty("coverType").GetString() == "poster")
            .Should().Be(hasPoster ? 1 : 0);
    }

    [Fact]
    public async Task Wanted_EmbeddedSeriesIsDecodable()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            (await db.Events.SingleAsync()).EventDate = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }
        var result = await _client.GetFromJsonAsync<JsonElement>("/api/v3/wanted/missing?includeSeries=true");
        AssertSeries(result.GetProperty("records")[0].GetProperty("series"));
    }

    [Fact]
    public async Task ManualImport_EmbeddedSeriesUsesAnIsoTimestamp()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "NHL - S2026E01 - Game.mkv"), "video");
        var result = await _client.GetFromJsonAsync<JsonElement>(
            "/api/v3/manualimport?folder=" + Uri.EscapeDataString(_directory));
        var series = result[0].GetProperty("series");
        AssertSeries(series);
        series.GetProperty("firstAired").GetDateTimeOffset().Offset.Should().Be(TimeSpan.Zero);
    }

    // Required stored properties in Ruddarr's Series, Season and MediaImage models.
    private static void AssertSeries(JsonElement series)
    {
        foreach (var field in new[] { "title", "sortTitle", "status", "seriesType", "added" })
            series.GetProperty(field).ValueKind.Should().Be(JsonValueKind.String, field);
        series.GetProperty("status").GetString().Should().Be("continuing");
        series.GetProperty("seriesType").GetString().Should().Be("standard");
        series.GetProperty("added").GetDateTimeOffset();
        foreach (var field in new[] { "tvdbId", "year", "runtime" })
            series.GetProperty(field).GetInt32().Should().BeGreaterThanOrEqualTo(0);
        foreach (var field in new[] { "ended", "seasonFolder", "useSceneNumbering", "monitored" })
            series.GetProperty(field).GetBoolean();
        series.GetProperty("ended").GetBoolean().Should().BeFalse();
        foreach (var tag in series.GetProperty("tags").EnumerateArray())
            tag.GetInt32();
        foreach (var genre in series.GetProperty("genres").EnumerateArray())
            genre.ValueKind.Should().Be(JsonValueKind.String);
        foreach (var image in series.GetProperty("images").EnumerateArray())
        {
            image.GetProperty("coverType").ValueKind.Should().Be(JsonValueKind.String);
            image.GetProperty("url").ValueKind.Should().Be(JsonValueKind.String);
            image.GetProperty("remoteUrl").ValueKind.Should().Be(JsonValueKind.String);
        }
        foreach (var season in series.GetProperty("seasons").EnumerateArray())
        {
            season.GetProperty("seasonNumber").GetInt32();
            season.GetProperty("monitored").GetBoolean();
            if (season.TryGetProperty("statistics", out var stats))
                AssertStatistics(stats, false);
        }
        if (series.TryGetProperty("statistics", out var statistics))
            AssertStatistics(statistics, true);
    }

    private static void AssertStatistics(JsonElement stats, bool series)
    {
        foreach (var field in new[] { "episodeFileCount", "episodeCount", "totalEpisodeCount", "sizeOnDisk" })
            stats.GetProperty(field).GetInt64();
        if (series)
        {
            stats.GetProperty("seasonCount").GetInt32();
            stats.GetProperty("percentOfEpisodes").GetDouble();
        }
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app != null)
            await _app.DisposeAsync();
        Directory.Delete(_directory, true);
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var league = new League { Name = "NBA", Sport = "Basketball", ExternalId = "lg-000042", SportFormat = "Team vs Team" };
            var content = request.RequestUri!.AbsolutePath.Contains("/search/")
                ? JsonContent.Create(new { data = new { search = new[] { league } } })
                : JsonContent.Create(new { data = new { lookup = new[] { league } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
