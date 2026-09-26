using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Services;

public class PendingReleaseReaperPersistenceTests
{
    [Theory]
    [InlineData(1, false, false)]
    [InlineData(3, true, false)]
    [InlineData(6, true, false)]
    [InlineData(3, false, true)]
    [InlineData(3, false, false, true)]
    [InlineData(3, false, false, true, true)]
    public async Task OwnershipSaveFailureDoesNotAddTheReleaseTwiceOnNextPass(int failedSaves, bool refuseRemoval, bool failAfterCommit, bool conflictingRecovery = false, bool differingCase = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "sportarr-reaper-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var failure = new FirstOwnerSaveFailure { FailuresRemaining = failedSaves, FailAfterCommit = failAfterCommit };
        var transport = new ReaperTransport { RefuseRemoval = refuseRemoval };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = directory })
            .Build());
        services.AddDbContext<SportarrDbContext>(options => options
            .UseSqlite("Data Source=" + Path.Combine(directory, "reaper.db") + ";Pooling=False")
            .AddInterceptors(failure));
        services.AddSingleton<ConfigService>();
        services.AddSingleton<RssSyncService>();
        services.AddSingleton<DownloadOwnershipCoordinator>();
        services.AddSingleton<IHttpClientFactory>(transport);
        services.AddSingleton(transport.CreateClient("default"));
        services.AddSingleton(Mock.Of<IRemotePathMappingService>());
        services.AddScoped<DownloadClientService>();
        services.AddScoped<NotificationService>();
        await using var provider = services.BuildServiceProvider();

        try
        {
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
                await db.Database.EnsureCreatedAsync();
                var league = new League { Name = "Fixture League", Sport = "Fighting", Monitored = true };
                var evt = new Event { Title = "Fixture 100", Sport = "Fighting", League = league,
                    EventDate = DateTime.UtcNow.AddDays(-1), Monitored = true, Status = "Completed" };
                var client = new DownloadClient { Name = "Fixture SAB", Type = DownloadClientType.Sabnzbd,
                    Host = "client.invalid", Port = 8080, ApiKey = "fixture", Enabled = true, Category = "sportarr" };
                var pending = new PendingRelease { Event = evt, Title = "Fixture.100.1080p.WEB-DL",
                    Guid = "fixture-release", DownloadUrl = "http://source.invalid/release.nzb",
                    Indexer = "Fixture source", Protocol = "Usenet", Size = 1_000_000_000,
                    Quality = "WEBDL-1080p", QualityScore = 300, Score = 300,
                    PublishDate = DateTime.UtcNow.AddHours(-2), ReleasableAt = DateTime.UtcNow.AddMinutes(-1) };
                db.AddRange(league, evt, client, pending);
                await db.SaveChangesAsync();
            }

            failure.Armed = true;
            var reaper = new PendingReleaseReaperService(provider, NullLogger<PendingReleaseReaperService>.Instance);
            for (var pass = 0; pass < (conflictingRecovery ? 1 : failedSaves); pass++)
            {
                try { await RunPassAsync(reaper); }
                catch (InvalidOperationException)
                {
                    Assert.True(provider.GetRequiredService<DownloadOwnershipCoordinator>().HasAcquisitions);
                    Assert.Equal(1, transport.Adds);
                }
            }
            if (conflictingRecovery)
            {
                using var conflictScope = provider.CreateScope();
                var conflictDb = conflictScope.ServiceProvider.GetRequiredService<SportarrDbContext>();
                var original = await conflictDb.Events.SingleAsync();
                var other = new Event { Id = original.Id + 256, Title = "Unrelated event", Sport = "Fighting",
                    LeagueId = original.LeagueId, Monitored = true, EventDate = original.EventDate };
                conflictDb.Events.Add(other);
                var conflict = new DownloadQueueItem { EventId = other.Id, DownloadClientId = 1,
                    DownloadId = differingCase ? "REAPER-JOB-1" : "reaper-job-1", Title = "Conflicting owner", Part = "Main Card", Status = DownloadStatus.Downloading };
                conflictDb.DownloadQueue.Add(conflict);
                var unrelated = new PendingRelease { Event = other, Title = "Fixture.Other.Prelims.1080p.WEB-DL",
                    Part = "Prelims", Guid = "unrelated-release", DownloadUrl = "http://source.invalid/release.nzb",
                    Protocol = "Usenet", Quality = "WEBDL-1080p", ReleasableAt = DateTime.UtcNow.AddMinutes(-1) };
                conflictDb.PendingReleases.Add(unrelated);
                await conflictDb.SaveChangesAsync();
                await RunPassAsync(reaper);
                var coordinator = provider.GetRequiredService<DownloadOwnershipCoordinator>();
                Assert.False(coordinator.HasAcquisitions);
                using (var external = coordinator.TryEnterExternalDecision()) Assert.NotNull(external);
                using var unrelatedGate = await coordinator.EnterEventDecisionAsync(other.Id).WaitAsync(TimeSpan.FromSeconds(1));
                Assert.Equal(2, transport.Adds);
                Assert.True(coordinator.IsQuarantined(1, "reaper-job-1"));
                Assert.Null(coordinator.TryEnterExternalDecision(1, "reaper-job-1"));
                Assert.Equal(PendingReleaseStatus.Pending, (await conflictDb.PendingReleases.AsNoTracking().SingleAsync(p => p.EventId == original.Id)).Status);
                conflictDb.DownloadQueue.Remove(conflict);
                await conflictDb.SaveChangesAsync();
                using (var otherAcquisition = await coordinator.EnterAcquisitionAsync())
                {
                    await RunPassAsync(reaper);
                    Assert.True(coordinator.IsQuarantined(1, "reaper-job-1"));
                    Assert.False(await conflictDb.DownloadQueue.AnyAsync(q => q.EventId == original.Id));
                }
            }
            await RunPassAsync(reaper);

            using var verification = provider.CreateScope();
            var read = verification.ServiceProvider.GetRequiredService<SportarrDbContext>();
            Assert.Equal(conflictingRecovery ? 2 : 1, transport.Adds);
            Assert.Equal(0, transport.Removals);
            Assert.Equal(conflictingRecovery ? 2 : 1, await read.DownloadQueue.CountAsync());
            Assert.All(await read.PendingReleases.AsNoTracking().ToListAsync(), p => Assert.Equal(PendingReleaseStatus.Released, p.Status));
            Assert.False(provider.GetRequiredService<DownloadOwnershipCoordinator>().IsQuarantined(1, "reaper-job-1"));
            Assert.False(provider.GetRequiredService<DownloadOwnershipCoordinator>().HasAcquisitions);
        }
        finally
        {
            transport.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task RunPassAsync(PendingReleaseReaperService reaper)
    {
        var method = typeof(PendingReleaseReaperService).GetMethod("ReapAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await ((Task)method.Invoke(reaper, new object[] { CancellationToken.None })!).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class FirstOwnerSaveFailure : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int FailuresRemaining { get; set; }
        public bool FailAfterCommit { get; init; }
        private bool _ownerSeen;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var addsOwner = eventData.Context?.ChangeTracker.Entries<DownloadQueueItem>()
                .Any(entry => entry.State == EntityState.Added) == true;
            if (Armed && addsOwner) _ownerSeen = true;
            if (Armed && addsOwner && !FailAfterCommit && FailuresRemaining-- > 0)
                throw new InvalidOperationException("Injected first ownership save failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (Armed && _ownerSeen && FailAfterCommit && FailuresRemaining-- > 0)
                throw new InvalidOperationException("Injected failure after ownership commit");
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class ReaperTransport : HttpMessageHandler, IHttpClientFactory, IDisposable
    {
        private readonly HttpClient _client;
        public int Adds { get; private set; }
        public int Removals { get; private set; }
        public bool RefuseRemoval { get; init; }

        public ReaperTransport() => _client = new HttpClient(this, disposeHandler: false);
        public HttpClient CreateClient(string name) => _client;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.Host == "source.invalid")
            {
                var nzb = "<?xml version=\"1.0\"?><nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\"><file poster=\"fixture\" subject=\"fixture\" date=\"1599004800\"><groups><group>alt.test</group></groups><segments><segment bytes=\"4096\" number=\"1\">fixture@invalid</segment></segments></file></nzb>";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(nzb, Encoding.UTF8, "application/x-nzb") });
            }

            var query = request.RequestUri!.Query;
            if (request.Method == HttpMethod.Post)
            {
                Adds++;
                return Task.FromResult(Json($"{{\"status\":true,\"nzo_ids\":[\"reaper-job-{Adds}\"]}}"));
            }
            if (query.Contains("name=delete") || query.Contains("name=remove"))
            {
                Removals++;
                if (RefuseRemoval) return Task.FromResult(Json("{\"status\":false}"));
                return Task.FromResult(Json("{\"status\":true,\"nzo_ids\":[\"reaper-job-1\"]}"));
            }
            return Task.FromResult(Json("{\"status\":true}"));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        public new void Dispose() => _client.Dispose();
    }
}
