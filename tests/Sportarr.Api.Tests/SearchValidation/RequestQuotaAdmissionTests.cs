using System.Net;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Startup;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(IndexerStatusFixtureCollection.Name)]
public sealed class RequestQuotaAdmissionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task PacingCancellationReturnsPromptly()
    {
        var limiter = new RateLimitService(NullLogger<RateLimitService>.Instance);
        await limiter.WaitAndPulseAsync("fixture.invalid", "17", TimeSpan.Zero);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var clock = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            limiter.WaitAndPulseAsync("fixture.invalid", "17", TimeSpan.FromMilliseconds(300), cancellation.Token));

        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(200), $"Cancellation took {clock.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task CancellationDuringPacingDoesNotReserveQuotaOrReachTransport()
    {
        await using var fixture = await StatusFixture.CreateAsync(5);
        fixture.Row.RequestDelayMs = 300;
        var limiter = new RateLimitService(NullLogger<RateLimitService>.Instance);
        await limiter.WaitAndPulseAsync("fixture.invalid", fixture.Row.Id.ToString(), TimeSpan.Zero);
        var terminal = new ReplyHandler();
        using var client = new HttpMessageInvoker(
            new IndexerQueryQuotaHandler(fixture.Services, limiter) { InnerHandler = terminal });
        using var request = IndexerQueryRequest.Create(fixture.Row, fixture.Row.Url,
            new IndexerQueryContext(fixture.Row.Id));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        var error = await Assert.ThrowsAsync<IndexerQueryAdmissionException>(
            () => client.SendAsync(request, cancellation.Token));
        Assert.Equal(QueryAdmissionFailure.Cancelled, error.Kind);

        Assert.Equal(0, terminal.Sends);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Empty(await db.IndexerStatuses.ToListAsync());
    }

    [Fact]
    public async Task VariableReservationLatencyCannotShortenDispatchPacing()
    {
        await using var fixture = await StatusFixture.CreateAsync(5);
        fixture.Row.RequestDelayMs = 1000;
        fixture.Factory.SaveDelays.Enqueue(700);
        fixture.Factory.SaveDelays.Enqueue(0);
        var limiter = new RateLimitService(NullLogger<RateLimitService>.Instance);
        var terminal = new ReplyHandler();
        using var client = new HttpMessageInvoker(
            new IndexerQueryQuotaHandler(fixture.Services, limiter) { InnerHandler = terminal });

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = IndexerQueryRequest.Create(fixture.Row, fixture.Row.Url,
                new IndexerQueryContext(fixture.Row.Id));
            using var response = await client.SendAsync(request, CancellationToken.None);
        }

        Assert.Equal(2, terminal.Arrivals.Count);
        Assert.True(terminal.Arrivals[1] - terminal.Arrivals[0] >= TimeSpan.FromMilliseconds(950),
            $"Dispatches were only {(terminal.Arrivals[1] - terminal.Arrivals[0]).TotalMilliseconds:F0}ms apart");
    }

    [Fact]
    public async Task ConcurrentReservationsCreateOneStatusAndSpendOnlyOneSlot()
    {
        await using var fixture = await StatusFixture.CreateAsync(1);
        using var cancellation = new CancellationTokenSource();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Enumerable.Range(0, 8).Select(async _ =>
        {
            await start.Task;
            try { await fixture.Status.ReserveQueryAttemptAsync(fixture.Row.Id, cancellation.Token); return true; }
            catch (IndexerQueryAdmissionException ex) when (ex.Kind == QueryAdmissionFailure.Denied) { return false; }
        }).ToArray();
        Exception? primaryFailure = null;
        try
        {
            start.SetResult();
            var outcomes = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Single(outcomes.Where(admitted => admitted));
            await using var db = fixture.Factory.CreateDbContext();
            var row = Assert.Single(await db.IndexerStatuses.ToListAsync());
            Assert.Equal(1, row.QueriesThisHour);
            Assert.Equal(0, row.QueryFailures);
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                // Keep the fixture database until every reservation has stopped.
                await Task.WhenAll(pending);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception drainFailure) when (primaryFailure != null)
            {
                output.WriteLine("Reservation drain also failed: " + drainFailure);
            }
        }
    }

    [Fact]
    public async Task AlreadyCancelledAdmissionDoesNotCreateStatusOrReachTransport()
    {
        await using var fixture = await StatusFixture.CreateAsync(1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var terminal = new ReplyHandler();
        using var client = new HttpMessageInvoker(new IndexerQueryQuotaHandler(fixture.Services) { InnerHandler = terminal });
        using var request = IndexerQueryRequest.Create(fixture.Row, fixture.Row.Url, new IndexerQueryContext(fixture.Row.Id));
        var error = await Assert.ThrowsAsync<IndexerQueryAdmissionException>(() => client.SendAsync(request, cancellation.Token));
        Assert.Equal(QueryAdmissionFailure.Cancelled, error.Kind);
        Assert.Equal(0, terminal.Sends);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Empty(await db.IndexerStatuses.ToListAsync());
    }

    [Fact]
    public async Task FailedReservationSaveDoesNotReachTransportOrPersistCount()
    {
        await using var fixture = await StatusFixture.CreateAsync(1);
        fixture.Factory.FailSave = true;
        var terminal = new ReplyHandler();
        using var client = new HttpMessageInvoker(new IndexerQueryQuotaHandler(fixture.Services) { InnerHandler = terminal });
        using var request = IndexerQueryRequest.Create(fixture.Row, fixture.Row.Url, new IndexerQueryContext(fixture.Row.Id));
        var error = await Assert.ThrowsAsync<IndexerQueryAdmissionException>(() => client.SendAsync(request, CancellationToken.None));
        Assert.Equal(QueryAdmissionFailure.Persistence, error.Kind);
        Assert.Equal(0, terminal.Sends);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Empty(await db.IndexerStatuses.ToListAsync());
    }

    [Fact]
    public async Task UnmarkedDiagnosticDoesNotReserveExhaustedRowQuota()
    {
        await using var fixture = await StatusFixture.CreateAsync(0);
        var terminal = new ReplyHandler();
        using var client = new HttpMessageInvoker(new IndexerQueryQuotaHandler(fixture.Services) { InnerHandler = terminal });
        using var request = new HttpRequestMessage(HttpMethod.Get, fixture.Row.Url);
        using var response = await client.SendAsync(request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, terminal.Sends);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Empty(await db.IndexerStatuses.ToListAsync());
    }

    [Fact]
    public async Task AdmissionReadsChangedRowLimitInsteadOfDetachedConfiguration()
    {
        await using var fixture = await StatusFixture.CreateAsync(5);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var current = await db.Indexers.SingleAsync();
            current.QueryLimit = 0;
            await db.SaveChangesAsync();
        }
        var error = await Assert.ThrowsAsync<IndexerQueryAdmissionException>(
            () => fixture.Status.ReserveQueryAttemptAsync(fixture.Row.Id));
        Assert.Equal(QueryAdmissionFailure.Denied, error.Kind);
        await using var read = fixture.Factory.CreateDbContext();
        Assert.Empty(await read.IndexerStatuses.ToListAsync());
    }

    [Fact]
    public async Task HealthOnlyOutcomesKeepReservationWindowAndActiveCooldown()
    {
        await using var fixture = await StatusFixture.CreateAsync(10);
        await fixture.Status.ReserveQueryAttemptAsync(fixture.Row.Id);
        await fixture.Status.RecordRateLimitedAsync(fixture.Row.Id, TimeSpan.FromMinutes(3));
        await using var beforeDb = fixture.Factory.CreateDbContext();
        var before = await beforeDb.IndexerStatuses.AsNoTracking().SingleAsync();

        await fixture.Status.RecordFailureHealthAsync(fixture.Row.Id, "HTTP 500");
        await fixture.Status.RecordFailureHealthAsync(fixture.Row.Id, "connection refused");
        await fixture.Status.RecordSuccessHealthAsync(fixture.Row.Id);
        await using var afterDb = fixture.Factory.CreateDbContext();
        var after = await afterDb.IndexerStatuses.AsNoTracking().SingleAsync();
        Assert.Equal(1, after.QueriesThisHour);
        Assert.Equal(before.HourResetTime, after.HourResetTime);
        Assert.Equal(before.RateLimitedUntil, after.RateLimitedUntil);
        Assert.Equal(0, after.QueryFailures);
        Assert.Equal(0, after.ConnectionErrors);
        Assert.NotNull(after.LastSuccess);
    }

    [Fact]
    public async Task LongRetryAfterKeepsIndexerUnavailableUntilServerDeadline()
    {
        await using var fixture = await StatusFixture.CreateAsync(null);
        await fixture.Status.RecordRateLimitedAsync(fixture.Row.Id, TimeSpan.FromHours(3));

        await using var db = fixture.Factory.CreateDbContext();
        var status = await db.IndexerStatuses.AsNoTracking().SingleAsync();
        Assert.InRange(status.RateLimitedUntil!.Value,
            DateTime.UtcNow.AddHours(2).AddMinutes(55),
            DateTime.UtcNow.AddHours(3).AddMinutes(5));

        var error = await Assert.ThrowsAsync<IndexerQueryAdmissionException>(
            () => fixture.Status.ReserveQueryAttemptAsync(fixture.Row.Id));
        Assert.Equal(QueryAdmissionFailure.Denied, error.Kind);
        Assert.Equal(0, status.QueriesThisHour);
    }

    [Fact]
    public async Task ExcessiveRetryAfterRemainsBoundedToOneDay()
    {
        await using var fixture = await StatusFixture.CreateAsync(null);
        await fixture.Status.RecordRateLimitedAsync(fixture.Row.Id, TimeSpan.FromDays(3));

        await using var db = fixture.Factory.CreateDbContext();
        var status = await db.IndexerStatuses.AsNoTracking().SingleAsync();
        Assert.InRange(status.RateLimitedUntil!.Value,
            DateTime.UtcNow.AddHours(23).AddMinutes(55),
            DateTime.UtcNow.AddDays(1).AddMinutes(5));
    }

    [Fact]
    public async Task LaterShort429CannotShortenAnActiveLongCooldown()
    {
        await using var fixture = await StatusFixture.CreateAsync(null);
        await fixture.Status.RecordRateLimitedAsync(fixture.Row.Id, TimeSpan.FromHours(3));
        await fixture.Status.RecordRateLimitedAsync(fixture.Row.Id, TimeSpan.FromMinutes(5));

        await using var db = fixture.Factory.CreateDbContext();
        var status = await db.IndexerStatuses.AsNoTracking().SingleAsync();
        Assert.True(status.RateLimitedUntil > DateTime.UtcNow.AddHours(2).AddMinutes(55));
    }

    [Fact]
    public async Task LegacyOutcomeStillCountsForUnmigratedCallers()
    {
        await using var fixture = await StatusFixture.CreateAsync(null);
        await fixture.Status.RecordSuccessAsync(fixture.Row.Id);
        await fixture.Status.RecordFailureAsync(fixture.Row.Id, "HTTP 500");
        await fixture.Status.RecordFailureAsync(fixture.Row.Id, "connection refused");
        await using var db = fixture.Factory.CreateDbContext();
        var state = await db.IndexerStatuses.SingleAsync();
        Assert.Equal(3, state.QueriesThisHour);
        Assert.Equal(1, state.QueryFailures);
        Assert.Equal(1, state.ConnectionErrors);
    }

    [Theory]
    [InlineData(IndexerType.Torznab)]
    [InlineData(IndexerType.Newznab)]
    public async Task Caps429EscapesWithoutNegativeCacheAndRetainsRetryAfter(IndexerType protocol)
    {
        var terminal = new ReplyHandler { StatusCode = HttpStatusCode.TooManyRequests };
        using var http = new HttpClient(terminal);
        var row = new Indexer { Id = 91, Type = protocol, Name = "Caps control", Url = "http://fixture.invalid/" + Guid.NewGuid(), ApiPath = "/api", ApiKey = "fixture" };
        var torznab = new TorznabClient(http, NullLogger<TorznabClient>.Instance, queryContext: new IndexerQueryContext(row.Id));
        var newznab = new NewznabClient(http, NullLogger<NewznabClient>.Instance, queryContext: new IndexerQueryContext(row.Id));
        async Task Search()
        {
            if (protocol == IndexerType.Torznab) await torznab.SearchAsync(row, "query", sportarrId: "ev-2336155");
            else await newznab.SearchAsync(row, "query", sportarrId: "ev-2336155");
        }
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var error = await Assert.ThrowsAsync<IndexerRateLimitException>(Search);
            Assert.Equal(TimeSpan.FromSeconds(45), error.RetryAfter);
        }
        Assert.Equal(2, terminal.Sends);
        Assert.All(terminal.Paths, path => Assert.Contains("t=caps", path));
    }

    [Theory]
    [InlineData(IndexerType.Torznab)]
    [InlineData(IndexerType.Newznab)]
    public async Task CapsLocalDenialEscapesWithoutNegativeCache(IndexerType protocol)
    {
        var terminal = new ReplyHandler { Deny = true };
        using var http = new HttpClient(terminal);
        var row = new Indexer { Id = 92, Type = protocol, Name = "Denied caps", Url = "http://fixture.invalid/" + Guid.NewGuid(), ApiPath = "/api", ApiKey = "fixture" };
        var torznab = new TorznabClient(http, NullLogger<TorznabClient>.Instance, queryContext: new IndexerQueryContext(row.Id));
        var newznab = new NewznabClient(http, NullLogger<NewznabClient>.Instance, queryContext: new IndexerQueryContext(row.Id));
        async Task Search()
        {
            if (protocol == IndexerType.Torznab) await torznab.SearchAsync(row, "query", sportarrId: "ev-2336155");
            else await newznab.SearchAsync(row, "query", sportarrId: "ev-2336155");
        }
        for (var attempt = 0; attempt < 2; attempt++)
            await Assert.ThrowsAsync<IndexerQueryAdmissionException>(Search);
        Assert.Equal(2, terminal.Sends);
        Assert.All(terminal.Paths, path => Assert.Contains("t=caps", path));
    }

    [Theory]
    [InlineData(IndexerType.Torznab)]
    [InlineData(IndexerType.Newznab)]
    public async Task DirectClientsWithRowIdsUseNamedChainWithoutQuotaDependencies(IndexerType protocol)
    {
        var terminal = new ReplyHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRateLimitService, RateLimitService>();
        services.AddTransient<RateLimitHandler>();
        services.AddSportarrHttpClients();
        services.AddHttpClient("IndexerClient").ConfigurePrimaryHttpMessageHandler(() => terminal);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("IndexerClient");
        var row = new Indexer { Id = 93, Type = protocol, Name = "Direct client", Url = "http://fixture.invalid/" + Guid.NewGuid(), ApiPath = "/api", ApiKey = "fixture", RequestDelayMs = 1 };
        List<ReleaseSearchResult> rows;
        if (protocol == IndexerType.Torznab)
            rows = await new TorznabClient(http, NullLogger<TorznabClient>.Instance)
                .SearchAsync(row, "query", sportarrId: "ev-2336155");
        else
            rows = await new NewznabClient(http, NullLogger<NewznabClient>.Instance)
                .SearchAsync(row, "query", sportarrId: "ev-2336155");
        Assert.Empty(rows);
        Assert.Equal(2, terminal.Sends);
        Assert.Contains("t=caps", terminal.Paths[0]);
        Assert.Contains("t=search", terminal.Paths[1]);
    }

    private sealed class ReplyHandler : HttpMessageHandler
    {
        public int Sends { get; private set; }
        public bool Deny { get; init; }
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
        public List<string> Paths { get; } = new();
        public List<DateTime> Arrivals { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sends++;
            Arrivals.Add(DateTime.UtcNow);
            Paths.Add(request.RequestUri!.PathAndQuery);
            if (Deny) throw new IndexerQueryAdmissionException(92, QueryAdmissionFailure.Denied, "Fixture denial");
            var response = new HttpResponseMessage(StatusCode) { Content = new StringContent("<rss><channel /></rss>") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
            return Task.FromResult(response);
        }
    }

    private sealed class StatusFixture : IAsyncDisposable
    {
        private readonly string _directory;
        public TestFactory Factory { get; }
        public ServiceProvider Services { get; }
        public IndexerStatusService Status { get; }
        public Indexer Row { get; } = new() { Name = "Admission control", Type = IndexerType.Newznab, Enabled = true, Url = "http://fixture.invalid/api" };

        private StatusFixture(string directory, TestFactory factory)
        {
            _directory = directory;
            Factory = factory;
            Status = new IndexerStatusService(factory, NullLogger<IndexerStatusService>.Instance);
            Services = new ServiceCollection().AddSingleton(Status).BuildServiceProvider();
        }

        public static async Task<StatusFixture> CreateAsync(int? limit)
        {
            var directory = Path.Combine(Path.GetTempPath(), "quota-admission-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var options = new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "fixture.db") + ";Pooling=False;Default Timeout=5").Options;
            var fixture = new StatusFixture(directory, new TestFactory(options));
            try
            {
                await using var db = fixture.Factory.CreateDbContext();
                await db.Database.EnsureCreatedAsync();
                fixture.Row.QueryLimit = limit;
                db.Indexers.Add(fixture.Row);
                await db.SaveChangesAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            Services.Dispose();
            Directory.Delete(_directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestFactory(DbContextOptions<SportarrDbContext> options) : IDbContextFactory<SportarrDbContext>
    {
        public bool FailSave { get; set; }
        public Queue<int> SaveDelays { get; } = new();
        public SportarrDbContext CreateDbContext() => new TestContext(options, this);
        public Task<SportarrDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    private sealed class TestContext(DbContextOptions<SportarrDbContext> options, TestFactory factory) : SportarrDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (factory.FailSave) throw new InvalidOperationException("Injected reservation save failure");
            return SaveAsync(cancellationToken);
        }

        private async Task<int> SaveAsync(CancellationToken cancellationToken)
        {
            if (factory.SaveDelays.TryDequeue(out var delay) && delay > 0)
                await Task.Delay(delay, cancellationToken);
            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
