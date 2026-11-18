using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.MotorsportLive;

namespace Sportarr.Api.Tests.Services.MotorsportLive;

/// <summary>
/// The Overtime Guard and the Early Finish Guard with series timing. The
/// livescore feed returns nothing, as it does for motorsport today.
/// </summary>
public class DvrSeriesTimingGuardTests
{
    [Fact]
    public async Task LiveSessionExtendsAtTheScheduledEnd()
    {
        await using var f = await GuardFixture.CreateAsync();
        var end = await f.EndAtAsync(TimeSpan.FromMinutes(-1));
        f.Series.Status = GuardFixture.Live();

        Assert.True(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));

        var row = await f.ReadRowAsync();
        Assert.Equal(end.AddMinutes(10), row.ScheduledEnd);
        Assert.Equal(end, row.OriginalScheduledEnd);
        Assert.Equal(1, f.Series.Calls);
    }

    [Fact]
    public async Task LiveSessionStopsExtendingAtTheCap()
    {
        await using var f = await GuardFixture.CreateAsync(config => config.DvrOvertimeMaxExtensionMinutes = 30);
        var end = await f.EndAtAsync(TimeSpan.FromMinutes(-1), usedMinutes: 26);
        f.Series.Status = GuardFixture.Live();

        Assert.True(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        Assert.Equal(end.AddMinutes(4), (await f.ReadRowAsync()).ScheduledEnd);

        await f.EndAtAsync(TimeSpan.FromMinutes(-1), usedMinutes: 30);
        Assert.False(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
    }

    [Fact]
    public async Task FinishedSessionEndsTheRecordingAfterTheBuffer()
    {
        await using var f = await GuardFixture.CreateAsync(config => config.DvrEarlyFinishBufferMinutes = 5);
        await f.EndAtAsync(TimeSpan.FromHours(1));

        f.Series.Status = GuardFixture.Finished(DateTimeOffset.UtcNow.AddMinutes(-2));
        Assert.False(await f.Service.IsReadyToStopAsync(f.Recording));

        f.Series.Status = GuardFixture.Finished(DateTimeOffset.UtcNow.AddMinutes(-6));
        Assert.True(await f.Service.IsReadyToStopAsync(f.Recording));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task UnconfirmedOrEarlierFinishDoesNotEndTheRecording(bool confirmed, bool duringRecording)
    {
        await using var f = await GuardFixture.CreateAsync(config => config.DvrEarlyFinishBufferMinutes = 0);
        await f.EndAtAsync(TimeSpan.FromHours(1));
        f.Series.Status = GuardFixture.Finished(DateTimeOffset.UtcNow.AddMinutes(-30), confirmed, duringRecording);

        Assert.False(await f.Service.IsReadyToStopAsync(f.Recording));
    }

    [Fact]
    public async Task SessionThatJustFinishedKeepsTheBufferAtTheScheduledEnd()
    {
        await using var f = await GuardFixture.CreateAsync(config => config.DvrEarlyFinishBufferMinutes = 5);
        var end = await f.EndAtAsync(TimeSpan.FromSeconds(-30));
        f.Series.Status = GuardFixture.Finished(DateTimeOffset.UtcNow.AddMinutes(-2));

        Assert.True(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        Assert.Equal(end.AddMinutes(3), (await f.ReadRowAsync()).ScheduledEnd);
    }

    [Fact]
    public async Task FinishBufferIsNotKeptWhenEarlyFinishGuardIsOff()
    {
        await using var f = await GuardFixture.CreateAsync(config =>
        {
            config.DvrEarlyFinishGuardEnabled = false;
            config.DvrEarlyFinishBufferMinutes = 5;
        });
        var end = await f.EndAtAsync(TimeSpan.FromSeconds(-30));
        f.Series.Status = GuardFixture.Finished(DateTimeOffset.UtcNow.AddMinutes(-2));

        Assert.False(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        Assert.Equal(end, (await f.ReadRowAsync()).ScheduledEnd);
    }

    [Theory]
    [InlineData(-5, true)]
    [InlineData(5, false)]
    [InlineData(null, false)]
    public async Task LateStartExtendsOnlyAfterTheScheduledSessionStart(int? startMinutes, bool extends)
    {
        await using var f = await GuardFixture.CreateAsync();
        var end = await f.EndAtAsync(TimeSpan.FromMinutes(-1));
        f.Series.Status = new MotorsportLiveStatus(MotorsportLivePhase.NotStarted, "motogp.com",
            "POR MGP Grand Prix (RAC)", null, false, false, false,
            startMinutes.HasValue ? DateTimeOffset.UtcNow.AddMinutes(startMinutes.Value) : null);

        Assert.Equal(extends, await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        Assert.Equal(extends ? end.AddMinutes(10) : end, (await f.ReadRowAsync()).ScheduledEnd);
    }

    [Fact]
    public async Task LateStartStopsExtendingAtTheCap()
    {
        await using var f = await GuardFixture.CreateAsync(config => config.DvrOvertimeMaxExtensionMinutes = 30);
        await f.EndAtAsync(TimeSpan.FromMinutes(-1), usedMinutes: 30);
        f.Series.Status = new MotorsportLiveStatus(MotorsportLivePhase.NotStarted, "motogp.com",
            "POR MGP Grand Prix (RAC)", null, false, false, false, DateTimeOffset.UtcNow.AddMinutes(-40));

        Assert.False(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
    }

    [Fact]
    public async Task UnknownStatusChangesNothing()
    {
        await using var f = await GuardFixture.CreateAsync();
        var end = await f.EndAtAsync(TimeSpan.FromMinutes(-1));
        f.Series.Status = MotorsportLiveStatus.Unknown("motogp.com");

        Assert.False(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        Assert.Equal(end, (await f.ReadRowAsync()).ScheduledEnd);

        await f.EndAtAsync(TimeSpan.FromHours(1));
        Assert.False(await f.Service.IsReadyToStopAsync(f.Recording));
    }

    [Fact]
    public async Task SettingOffAsksNoSourceAndChangesNothing()
    {
        await using var f = await GuardFixture.CreateAsync(config => config.DvrMotorsportSeriesTimingEnabled = false);
        var end = await f.EndAtAsync(TimeSpan.FromMinutes(-1));
        f.Series.Status = GuardFixture.Live();

        Assert.False(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        Assert.Equal(end, (await f.ReadRowAsync()).ScheduledEnd);

        await f.EndAtAsync(TimeSpan.FromHours(1));
        f.Series.Status = GuardFixture.Finished(DateTimeOffset.UtcNow.AddMinutes(-30));
        Assert.False(await f.Service.IsReadyToStopAsync(f.Recording));
        Assert.Equal(0, f.Series.Calls);
    }

    [Fact]
    public async Task GuardsThatAreOffIgnoreTheSource()
    {
        await using var f = await GuardFixture.CreateAsync(config =>
        {
            config.DvrOvertimeGuardEnabled = false;
            config.DvrEarlyFinishGuardEnabled = false;
        });
        var end = await f.EndAtAsync(TimeSpan.FromMinutes(-1));
        f.Series.Status = GuardFixture.Live();
        Assert.False(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        Assert.Equal(end, (await f.ReadRowAsync()).ScheduledEnd);

        await f.EndAtAsync(TimeSpan.FromHours(1));
        f.Series.Status = GuardFixture.Finished(DateTimeOffset.UtcNow.AddMinutes(-30));
        Assert.False(await f.Service.IsReadyToStopAsync(f.Recording));
        Assert.Equal(0, f.Series.Calls);
    }

    [Fact]
    public async Task LeagueWithoutATimingSourceIsNotAsked()
    {
        await using var f = await GuardFixture.CreateAsync(leagueName: "Premier League");
        await f.EndAtAsync(TimeSpan.FromMinutes(-1));
        f.Series.Status = GuardFixture.Live();

        Assert.False(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        Assert.Equal(0, f.Series.Calls);
    }

    [Fact]
    public async Task FreshLivescoreEntryIsUsedBeforeSeriesTiming()
    {
        await using var f = await GuardFixture.CreateAsync(livescore: () => new DvrLiveScore
        {
            EventId = "ev-1856931", LeagueId = "lg-000046", Status = "Live",
            Source = "thesportsdb:livescore", SourceFetchedAt = DateTimeOffset.UtcNow
        });
        var end = await f.EndAtAsync(TimeSpan.FromMinutes(-1));
        f.Series.Status = MotorsportLiveStatus.Unknown("motogp.com");

        Assert.True(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        Assert.Equal(end.AddMinutes(10), (await f.ReadRowAsync()).ScheduledEnd);
        Assert.Equal(0, f.Series.Calls);
    }

    [Fact]
    public async Task SchedulerListsAFinishedSessionForAStop()
    {
        await using var f = await GuardFixture.CreateAsync(config => config.DvrEarlyFinishBufferMinutes = 1);
        await f.EndAtAsync(TimeSpan.FromHours(1));
        f.Series.Status = GuardFixture.Finished(DateTimeOffset.UtcNow.AddMinutes(-2));

        var due = await f.Service.GetRecordingsToStopAsync();

        Assert.Contains(due, r => r.Id == f.Recording.Id);
    }

    [Fact]
    public async Task RealStatusServiceNeedsTwoFetchesAMinuteApartBeforeTheStop()
    {
        var provider = new StepProvider();
        var service = new MotorsportLiveStatusService(new[] { provider }, NullLogger<MotorsportLiveStatusService>.Instance);
        await using var f = await GuardFixture.CreateAsync(config => config.DvrEarlyFinishBufferMinutes = 1, series: service);
        await f.EndAtAsync(TimeSpan.FromHours(1));
        var now = DateTimeOffset.UtcNow;
        var sessionStart = new DateTimeOffset(DateTime.SpecifyKind(f.Recording.ScheduledStart, DateTimeKind.Utc));

        provider.Next = new(MotorsportLivePhase.Live, "POR:MGP:Race:", "POR MGP Grand Prix (RAC)", null, now.AddMinutes(-50), sessionStart);
        Assert.False(await f.Service.IsReadyToStopAsync(f.Recording));
        provider.Next = provider.Next with { FetchedAt = now.AddMinutes(-4) };
        Assert.False(await f.Service.IsReadyToStopAsync(f.Recording));

        // The first fetch that shows the finish. The buffer has passed, but
        // one fetch is not enough.
        provider.Next = provider.Next with { Phase = MotorsportLivePhase.Finished, FetchedAt = now.AddMinutes(-3) };
        Assert.False(await f.Service.IsReadyToStopAsync(f.Recording));

        provider.Next = provider.Next with { FetchedAt = now };
        Assert.True(await f.Service.IsReadyToStopAsync(f.Recording));
    }

    private sealed class StepProvider : IMotorsportLiveProvider
    {
        public MotorsportSourceState? Next { get; set; }
        public string Source => "motogp.com";
        public bool Supports(MotorsportSeries series) => true;
        public Task<MotorsportSourceState?> GetSessionStateAsync(MotorsportSessionQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(Next);
    }

    [Fact]
    public async Task ColdLookupAtTheScheduledEndHasTimeForTwoRequests()
    {
        // A cold MotoGP lookup reads the calendar and then the event page.
        // Six seconds in total must still extend a live race.
        await using var f = await GuardFixture.CreateAsync();
        var end = await f.EndAtAsync(TimeSpan.FromMinutes(-1));
        f.Series.Status = GuardFixture.Live();
        f.Series.Delay = TimeSpan.FromSeconds(6);

        Assert.True(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        Assert.Equal(end.AddMinutes(10), (await f.ReadRowAsync()).ScheduledEnd);
    }

    [Fact]
    public async Task HangingSourcesUseOnlyThePassLimitAtTheScheduledEnd()
    {
        // One service instance serves one scheduler pass. The lookups share
        // one limit, and the last lookup gets only the time that is left.
        await using var f = await GuardFixture.CreateAsync();
        var end = await f.EndAtAsync(TimeSpan.FromMinutes(-1));
        f.Service.OvertimeSeriesLookupLimit = TimeSpan.FromSeconds(4);
        f.Service.OvertimeSeriesPassLimit = TimeSpan.FromSeconds(6);
        f.Series.Status = GuardFixture.Live();
        f.Series.Delay = TimeSpan.FromMinutes(5);

        var first = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        first.Stop();
        var second = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        second.Stop();
        Assert.Equal(2, f.Series.Calls);
        Assert.True(first.Elapsed >= TimeSpan.FromSeconds(3.9), $"first lookup took {first.Elapsed}");
        Assert.True(second.Elapsed < TimeSpan.FromSeconds(3.5), $"second lookup took {second.Elapsed}");

        Assert.False(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        Assert.Equal(2, f.Series.Calls);
        Assert.Equal(end, (await f.ReadRowAsync()).ScheduledEnd);
    }

    [Fact]
    public async Task TimeOutsideTheLookupsDoesNotUseThePassLimit()
    {
        // A stop can take minutes. A later recording in the same pass must
        // still ask the source.
        await using var f = await GuardFixture.CreateAsync();
        var end = await f.EndAtAsync(TimeSpan.FromMinutes(-1));
        f.Service.OvertimeSeriesPassLimit = TimeSpan.FromSeconds(1);
        f.Series.Status = null;

        Assert.False(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        f.Series.Status = GuardFixture.Live();
        Assert.True(await f.Service.ShouldExtendForOvertimeAsync(f.Recording));
        Assert.Equal(2, f.Series.Calls);
        Assert.Equal(end.AddMinutes(10), (await f.ReadRowAsync()).ScheduledEnd);
    }

    private sealed class FakeSeries : IMotorsportLiveStatusService
    {
        public MotorsportLiveStatus? Status { get; set; }
        public TimeSpan Delay { get; set; }
        public int Calls { get; private set; }

        public MotorsportSeries? ResolveSeries(League? league) =>
            MotorsportSeriesMap.Resolve(league?.Name, league?.AlternateName);

        public async Task<MotorsportLiveStatus?> GetStatusAsync(Event evt, DvrRecording recording, CancellationToken cancellationToken)
        {
            Calls++;
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);
            return Status;
        }
    }

    private sealed class LivescoreHandler : HttpMessageHandler
    {
        private readonly Func<DvrLiveScore>? _entry;
        public LivescoreHandler(Func<DvrLiveScore>? entry) => _entry = entry;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = _entry == null ? "{\"data\":[]}" : JsonSerializer.Serialize(new { data = new[] { _entry() } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class GuardFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly MemoryCache _cache;
        private readonly HttpClient _http;
        private readonly string _directory;

        public SportarrDbContext Db { get; }
        public DvrRecording Recording { get; }
        public DvrRecordingService Service { get; }
        public FakeSeries Series { get; }

        private GuardFixture(SqliteConnection connection, MemoryCache cache, HttpClient http, string directory,
            SportarrDbContext db, DvrRecording recording, DvrRecordingService service, FakeSeries series)
        {
            _connection = connection;
            _cache = cache;
            _http = http;
            _directory = directory;
            Db = db;
            Recording = recording;
            Service = service;
            Series = series;
        }

        public static MotorsportLiveStatus Live() =>
            new(MotorsportLivePhase.Live, "motogp.com", "QAT MGP Grand Prix (RAC)", null, false, false, false);

        public static MotorsportLiveStatus Finished(DateTimeOffset at, bool confirmed = true, bool duringRecording = true) =>
            new(MotorsportLivePhase.Finished, "motogp.com", "QAT MGP Grand Prix (RAC)", at, false, confirmed, duringRecording);

        /// <summary>Set the end relative to now and the extension already used.</summary>
        public async Task<DateTime> EndAtAsync(TimeSpan fromNow, int usedMinutes = 0)
        {
            await using var writer = CreateDb();
            var row = await writer.DvrRecordings.SingleAsync();
            row.ScheduledEnd = DateTime.UtcNow.Add(fromNow);
            row.OriginalScheduledEnd = usedMinutes > 0 ? row.ScheduledEnd.AddMinutes(-usedMinutes) : null;
            await writer.SaveChangesAsync();
            await Db.Entry(Recording).ReloadAsync();
            return row.ScheduledEnd;
        }

        public async Task<DvrRecording> ReadRowAsync()
        {
            await using var reader = CreateDb();
            return await reader.DvrRecordings.AsNoTracking().SingleAsync();
        }

        private SportarrDbContext CreateDb() =>
            new(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(_connection).Options);

        public static async Task<GuardFixture> CreateAsync(Action<Config>? configure = null, string leagueName = "MotoGP",
            IMotorsportLiveStatusService? series = null, Func<DvrLiveScore>? livescore = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"dvr-series-{Guid.NewGuid():N}");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = directory,
                ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json"
            }).Build();
            var config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
            var settings = await config.GetConfigAsync();
            settings.DvrOvertimeGuardEnabled = true;
            settings.DvrOvertimeMaxExtensionMinutes = 120;
            settings.DvrEarlyFinishGuardEnabled = true;
            settings.DvrEarlyFinishBufferMinutes = 5;
            settings.DvrMotorsportSeriesTimingEnabled = true;
            configure?.Invoke(settings);
            await config.SaveConfigAsync(settings);

            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.Leagues.Add(new League { Id = 1, Name = leagueName, Sport = "Motorsport", ExternalId = "lg-000046" });
            db.Events.Add(new Event
            {
                Id = 2, LeagueId = 1, Title = "Qatar GP", Sport = "Motorsport", ExternalId = "ev-1856931",
                EventDate = DateTime.UtcNow.AddHours(-1)
            });
            db.IptvSources.Add(new IptvSource { Id = 1, Name = "Fixture", Url = "https://iptv.invalid/list" });
            db.IptvChannels.Add(new IptvChannel { Id = 1, SourceId = 1, Name = "Fixture", StreamUrl = "https://iptv.invalid/stream" });
            var recording = new DvrRecording
            {
                Id = 1, EventId = 2, ChannelId = 1, Title = "Qatar GP", Method = DvrRecordingMethod.Live,
                Status = DvrRecordingStatus.Recording, ActualStart = DateTime.UtcNow.AddHours(-1),
                ScheduledStart = DateTime.UtcNow.AddHours(-1), ScheduledEnd = DateTime.UtcNow.AddHours(1),
                PostPadding = 0
            };
            db.DvrRecordings.Add(recording);
            await db.SaveChangesAsync();

            var http = new HttpClient(new LivescoreHandler(livescore));
            var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new SportarrApiClient(http, NullLogger<SportarrApiClient>.Instance, configuration, config, cache);
            var recorder = new FFmpegRecorderService(NullLogger<FFmpegRecorderService>.Instance, config, null!);
            var fake = new FakeSeries();
            var service = new DvrRecordingService(NullLogger<DvrRecordingService>.Instance, db,
                recorder, null!, config, null!, null!, null!, api, null!, new DvrEarlyFinishGuard(), series ?? fake);
            return new GuardFixture(connection, cache, http, directory, db, recording, service, fake);
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            _cache.Dispose();
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
