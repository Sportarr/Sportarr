using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.MotorsportLive;
using Sportarr.Api.Startup;

namespace Sportarr.Api.Tests.Services.MotorsportLive;

public class DvrSeriesTimingBudgetTests
{
    [Fact]
    public async Task SlowSeriesSourceDoesNotResetALivescoreConfirmation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dvr-budget-{Guid.NewGuid():N}");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sportarr:DataPath"] = directory,
            ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json"
        }).Build();
        var config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
        var settings = await config.GetConfigAsync();
        settings.DvrEarlyFinishGuardEnabled = true;
        settings.DvrEarlyFinishBufferMinutes = 0;
        settings.DvrMotorsportSeriesTimingEnabled = true;
        await config.SaveConfigAsync(settings);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Leagues.AddRange(
            new League { Id = 1, Name = "MotoGP", Sport = "Motorsport", ExternalId = "lg-000046" },
            new League { Id = 2, Name = "Premier League", Sport = "Soccer", ExternalId = "lg-000001" });
        db.IptvSources.Add(new IptvSource { Id = 1, Name = "Fixture", Url = "https://iptv.invalid/list" });
        db.IptvChannels.Add(new IptvChannel { Id = 1, SourceId = 1, Name = "Fixture", StreamUrl = "https://iptv.invalid/stream" });
        var started = DateTime.UtcNow.AddHours(-1);
        // Three motorsport recordings come first in the list. Their source
        // hangs for longer than the whole scheduler budget.
        for (var id = 1; id <= 3; id++)
        {
            db.Events.Add(new Event { Id = 10 + id, LeagueId = 1, Title = "Qatar GP", Sport = "Motorsport",
                ExternalId = $"ev-10{id}", EventDate = started });
            db.DvrRecordings.Add(new DvrRecording
            {
                Id = id, EventId = 10 + id, ChannelId = 1, Title = "Qatar GP", Method = DvrRecordingMethod.Live,
                Status = DvrRecordingStatus.Recording, ActualStart = started, ScheduledStart = started,
                ScheduledEnd = DateTime.UtcNow.AddHours(1), PostPadding = 0
            });
        }
        db.Events.Add(new Event { Id = 20, LeagueId = 2, Title = "Fixture", Sport = "Soccer",
            ExternalId = "ev-000020", EventDate = started });
        var soccer = new DvrRecording
        {
            Id = 4, EventId = 20, ChannelId = 1, Title = "Fixture", Method = DvrRecordingMethod.Live,
            Status = DvrRecordingStatus.Recording, ActualStart = started, ScheduledStart = started,
            ScheduledEnd = DateTime.UtcNow.AddHours(1), PostPadding = 0
        };
        db.DvrRecordings.Add(soccer);
        await db.SaveChangesAsync();

        // The soccer match was already seen final once, 65 seconds ago.
        var guard = new DvrEarlyFinishGuard();
        var previous = DateTimeOffset.UtcNow.AddSeconds(-65);
        Assert.False(guard.Observe(soccer, Final(previous), "ev-000020", "lg-000001", previous, 0));

        using var http = new HttpClient(new LivescoreHandler());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var api = new SportarrApiClient(http, NullLogger<SportarrApiClient>.Instance, configuration, config, cache);
        var recorder = new FFmpegRecorderService(NullLogger<FFmpegRecorderService>.Instance, config, null!);
        var service = new DvrRecordingService(NullLogger<DvrRecordingService>.Instance, db,
            recorder, null!, config, null!, null!, null!, api, null!, guard, new HangingSeries());

        var due = await service.GetRecordingsToStopAsync().WaitAsync(TimeSpan.FromSeconds(40));

        Assert.Contains(due, r => r.Id == 4);
        Assert.DoesNotContain(due, r => r.Id is 1 or 2 or 3);
        Directory.Delete(directory, recursive: true);
    }

    private static DvrLiveScore Final(DateTimeOffset fetched) => new()
    {
        EventId = "ev-000020", LeagueId = "lg-000001", Status = "FT", Progress = "Final",
        Source = "thesportsdb:livescore", SourceFetchedAt = fetched,
        RequestOrigin = "https://metadata.invalid/api/v2/json"
    };

    private sealed class LivescoreHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.RequestUri!.AbsolutePath.EndsWith("/lg-000001")
                ? JsonSerializer.Serialize(new { data = new[] { Final(DateTimeOffset.UtcNow) } })
                : "{\"data\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class HangingSeries : IMotorsportLiveStatusService
    {
        public MotorsportSeries? ResolveSeries(League? league) =>
            MotorsportSeriesMap.Resolve(league?.Name, league?.AlternateName);

        public async Task<MotorsportLiveStatus?> GetStatusAsync(Event evt, DvrRecording recording, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }
}

public class MotorsportTimingFetcherTests
{
    [Fact]
    public async Task FetchThatOnlyTheCallerCutsIsNotKeptAsAFailure()
    {
        // The second step of a lookup can run out of the caller's time while
        // the source is healthy. The next lookup must ask again at once.
        var handler = new CountingHangingHandler();
        var time = new ManualTime(DateTimeOffset.Parse("2026-11-08T16:00:00Z"));
        var fetcher = MotorsportProviders.Fetcher(handler, time);
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fetcher.GetAsync("https://timing.invalid/page", MotorsportTimingFetcher.LiveTtl, caller.Token));
        handler.Hang = false;
        var again = await fetcher.GetAsync("https://timing.invalid/page", MotorsportTimingFetcher.LiveTtl, default);

        Assert.NotNull(again);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task RecentGoodPageStandsInForAFailedRequest()
    {
        var handler = new FixtureHandler();
        var ok = true;
        handler.Raw("/page", () => ok
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"live\":true}") }
            : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var time = new ManualTime(DateTimeOffset.Parse("2026-11-08T16:00:00Z"));
        var fetcher = MotorsportProviders.Fetcher(handler, time);
        var first = await fetcher.GetAsync("https://timing.invalid/page", MotorsportTimingFetcher.LiveTtl, default);

        ok = false;
        time.Now = time.Now.AddSeconds(80);
        var during = await fetcher.GetAsync("https://timing.invalid/page", MotorsportTimingFetcher.LiveTtl, default);
        // The failure is now cached. The stand-in still answers from it.
        time.Now = time.Now.AddSeconds(30);
        var cachedFailure = await fetcher.GetAsync("https://timing.invalid/page", MotorsportTimingFetcher.LiveTtl, default);
        time.Now = time.Now.AddMinutes(4);
        var later = await fetcher.GetAsync("https://timing.invalid/page", MotorsportTimingFetcher.LiveTtl, default);

        // The stand-in keeps its own fetch time, so it never counts as a new
        // confirmation.
        Assert.Equal(first!.FetchedAt, during!.FetchedAt);
        Assert.Equal(first.FetchedAt, cachedFailure!.FetchedAt);
        // The first call, the failed call, and the call after the grace period.
        Assert.Equal(3, handler.Requests.Count);
        Assert.Null(later);
    }

    [Fact]
    public void GzipBodyThatDecodesTooLargeIsRefused()
    {
        var bomb = MotorsportFixtures.Gzip(new string('0', 11 * 1024 * 1024));

        Assert.Throws<InvalidDataException>(() => MotorsportTimingFetcher.Decode(bomb));
    }

    [Fact]
    public void TimingClientLimitsTheResponseSize()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSportarrHttpClients();
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(MotorsportTimingFetcher.HttpClientName);

        Assert.Equal(5 * 1024 * 1024, client.MaxResponseContentBufferSize);
    }

    private sealed class CountingHangingHandler : HttpMessageHandler
    {
        public int Calls;
        public bool Hang = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            if (Hang)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
