using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The Library Import preview shows the name a file gets. A file matched to a
/// part gets the part token, the same way the import names it.
/// </summary>
public class LibraryPreviewPartTokenTests
{
    private const string FileName = "03 - UFC 310 Fokartya.1080p.WEB-DL.mkv";

    [Fact]
    public async Task PreviewWithANamedPartWithoutANumberGetsTheNumber()
    {
        await using var fixture = await Fixture.CreateAsync();

        var preview = await fixture.Service.BuildDestinationPreviewForEventAsync(fixture.EventId, FileName, "Main Card");

        Assert.Contains("E03 - pt3 - ", preview);
    }

    [Fact]
    public async Task PreviewWithAPartShowsThePartToken()
    {
        await using var fixture = await Fixture.CreateAsync();

        var preview = await fixture.Service.BuildDestinationPreviewForEventAsync(
            fixture.EventId, FileName, "Main Card", 3);

        Assert.Contains("E03 - pt3 - ", preview);
    }

    [Fact]
    public async Task PreviewWithoutAPartHasNoPartToken()
    {
        await using var fixture = await Fixture.CreateAsync();

        var preview = await fixture.Service.BuildDestinationPreviewForEventAsync(fixture.EventId, FileName);

        Assert.Contains("E03 - UFC 310 Pantoja vs Asakura", preview);
        Assert.DoesNotContain(" - pt", preview);
    }

    [Fact]
    public async Task PreviewDropsThePartForAFullEventOnlyEvent()
    {
        await using var fixture = await Fixture.CreateAsync(monitoredParts: "Full Event");

        var preview = await fixture.Service.BuildDestinationPreviewForEventAsync(
            fixture.EventId, FileName, "Main Card", 3);

        Assert.DoesNotContain(" - pt", preview);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly SqliteConnection _connection;
        private readonly HttpClient _http;
        private readonly MemoryCache _cache;
        private readonly SportarrDbContext _db;

        private Fixture(string directory, SqliteConnection connection, HttpClient http, MemoryCache cache,
            SportarrDbContext db, LibraryImportService service, int eventId)
        {
            _directory = directory;
            _connection = connection;
            _http = http;
            _cache = cache;
            _db = db;
            Service = service;
            EventId = eventId;
        }

        public LibraryImportService Service { get; }
        public int EventId { get; }

        public static async Task<Fixture> CreateAsync(string? monitoredParts = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"preview-part-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = directory,
                ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json"
            }).Build();
            var config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.Leagues.Add(new League { Id = 1, Name = "UFC", Sport = "Fighting", ExternalId = "lg-000079" });
            db.MediaManagementSettings.Add(new MediaManagementSettings
            {
                RenameEvents = true,
                StandardFileFormat = "{Series} - {Season}{Episode}{Part} - {Event Title}"
            });
            var evt = new Event
            {
                Id = 1,
                LeagueId = 1,
                Title = "UFC 310 Pantoja vs Asakura",
                Sport = "Fighting",
                ExternalId = "ev-1000003",
                EventDate = new DateTime(2024, 12, 7, 23, 0, 0),
                Season = "2024",
                SeasonNumber = 2024,
                EpisodeNumber = 3,
                MonitoredParts = monitoredParts
            };
            db.Events.Add(evt);
            await db.SaveChangesAsync();

            // The metadata service is not reachable here, so the stored episode number is kept.
            var http = new HttpClient(new UnavailableHandler());
            var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new SportarrApiClient(http, NullLogger<SportarrApiClient>.Instance, configuration, config, cache);
            var resolver = new EpisodeNumberResolver(db, api, NullLogger<EpisodeNumberResolver>.Instance);
            var parser = new MediaFileParser(NullLogger<MediaFileParser>.Instance);
            var service = new LibraryImportService(
                db,
                NullLogger<LibraryImportService>.Instance,
                parser,
                new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
                new FileNamingService(NullLogger<FileNamingService>.Instance),
                new EventPartDetector(NullLogger<EventPartDetector>.Instance),
                config,
                resolver,
                new DiskSpaceService(NullLogger<DiskSpaceService>.Instance),
                new CustomFormatService(parser),
                null!,
                null!);
            return new Fixture(directory, connection, http, cache, db, service, evt.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
            _http.Dispose();
            _cache.Dispose();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }
    }

    private sealed class UnavailableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
    }
}
