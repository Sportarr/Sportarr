using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using AppTaskStatus = Sportarr.Api.Models.TaskStatus;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Services;
using Sportarr.Api.Startup;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Issue #404. System > Tasks showed Run Now for 6 of the 15 scheduled
/// tasks. Every task can run on demand now. The two that delete things ask
/// first.
/// </summary>
public class ScheduledTaskTriggerTests
{
    private static readonly string[] NewlyTriggerable =
    {
        "backlog-search", "import-list-sync", "tv-schedule-sync", "trash-sync", "dvr-auto-scheduler",
        "catchup-downloads", "health-check", "housekeeping", "event-retention",
    };

    private static IEnumerable<(string Id, bool Triggerable, string? ConfirmMessage)> ListedTasks()
    {
        var tasks = (Array)typeof(TaskEndpoints)
            .GetField("ScheduledTasks", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetValue(null)!;
        foreach (var task in tasks)
        {
            var type = task.GetType();
            yield return (
                (string)type.GetProperty("Id")!.GetValue(task)!,
                (bool)type.GetProperty("Triggerable")!.GetValue(task)!,
                type.GetProperty("ConfirmMessage")?.GetValue(task) as string);
        }
    }

    [Fact]
    public void EveryScheduledTaskHasRunNow()
    {
        var tasks = ListedTasks().ToList();

        tasks.Should().HaveCount(15);
        tasks.Where(t => !t.Triggerable).Select(t => t.Id).Should().BeEmpty();
    }

    [Fact]
    public void OnlyTheTasksThatDeleteThingsAskFirst()
    {
        ListedTasks().Where(t => !string.IsNullOrWhiteSpace(t.ConfirmMessage)).Select(t => t.Id)
            .Should().BeEquivalentTo("housekeeping", "event-retention");
    }

    private static ServiceProvider BuildApp(string dataPath)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSportarrBackgroundServices();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<SportarrDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddSingleton(new ConfigService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = dataPath }).Build(),
            NullLogger<ConfigService>.Instance));
        return services.BuildServiceProvider();
    }

    private static async Task<AppTask> WaitForTaskAsync(ServiceProvider provider, string commandName, Func<AppTask, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.CommandName == commandName);
            if (task != null && done(task)) return task;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"{commandName} did not reach the expected state");
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task EveryNewTriggerQueuesATaskForTheTaskList()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), $"run-now-{Guid.NewGuid():N}");
        try
        {
            await using var provider = BuildApp(dataPath);

            foreach (var id in NewlyTriggerable)
            {
                var result = await TaskEndpoints.TriggerScheduledTaskAsync(id, provider);

                result.Should().BeAssignableTo<IStatusCodeHttpResult>()
                    .Which.StatusCode.Should().Be(StatusCodes.Status200OK, id);
            }

            using var scope = provider.CreateScope();
            var names = await scope.ServiceProvider.GetRequiredService<SportarrDbContext>().Tasks
                .Select(t => t.Name).ToListAsync();
            names.Should().HaveCount(NewlyTriggerable.Length, "each Run Now leaves one row in the task list");
        }
        finally
        {
            if (Directory.Exists(dataPath)) Directory.Delete(dataPath, recursive: true);
        }
    }

    [Fact]
    public async Task ARunNowShowsInHistoryWhenItEnds()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), $"run-now-{Guid.NewGuid():N}");
        try
        {
            await using var provider = BuildApp(dataPath);

            (await TaskEndpoints.TriggerScheduledTaskAsync("catchup-downloads", provider))
                .Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status200OK);

            var task = await WaitForTaskAsync(provider, "CatchupDownloads",
                t => t.Status is AppTaskStatus.Completed or AppTaskStatus.Failed);
            task.Status.Should().Be(AppTaskStatus.Completed, task.Exception);
            task.Name.Should().Be("Catchup Downloads");
            task.Started.Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(dataPath)) Directory.Delete(dataPath, recursive: true);
        }
    }

    [Fact]
    public async Task RunNowIsRefusedWhileTheScheduledPassRuns()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), $"run-now-{Guid.NewGuid():N}");
        try
        {
            await using var provider = BuildApp(dataPath);
            var gate = (ScheduledPassGate)typeof(CatchupDownloadService)
                .GetField("_passGate", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(provider.GetRequiredService<CatchupDownloadService>())!;
            var release = new TaskCompletionSource();
            var scheduled = gate.RunScheduledAsync(() => release.Task, TimeSpan.Zero, CancellationToken.None);

            var result = await TaskEndpoints.TriggerScheduledTaskAsync("catchup-downloads", provider);

            result.Should().BeAssignableTo<IStatusCodeHttpResult>()
                .Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
            using (var scope = provider.CreateScope())
            {
                (await scope.ServiceProvider.GetRequiredService<SportarrDbContext>().Tasks.CountAsync())
                    .Should().Be(0, "a refused Run Now leaves no failed row in the history");
            }
            release.SetResult();
            await scheduled;
        }
        finally
        {
            if (Directory.Exists(dataPath)) Directory.Delete(dataPath, recursive: true);
        }
    }

    private sealed class BlockingPass : IManualPassService
    {
        public ScheduledPassGate PassGate { get; } = new();
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool TakesToken { get; init; }

        public async Task PassAsync(CancellationToken cancellationToken)
        {
            if (TakesToken) await Release.Task.WaitAsync(cancellationToken);
            else await Release.Task;
        }
    }

    [Fact]
    public async Task AQueuedTaskStillStartsWhileALongRunNowPassRuns()
    {
        // Review finding. The task queue runs one task at a time, so a long
        // pass in it held up event searches, imports and league syncs.
        var dataPath = Path.Combine(Path.GetTempPath(), $"run-now-{Guid.NewGuid():N}");
        try
        {
            await using var provider = BuildApp(dataPath);
            var tasks = provider.GetRequiredService<TaskService>();
            var pass = new BlockingPass();
            (await tasks.StartManualPassAsync("Backlog Search", "BacklogSearch", pass)).Should().NotBeNull();

            await tasks.QueueTaskAsync("Test", "TestTask");

            var queued = await WaitForTaskAsync(provider, "TestTask", t => t.Started != null);
            queued.Status.Should().NotBe(AppTaskStatus.Queued);
            var manual = await WaitForTaskAsync(provider, "BacklogSearch", _ => true);
            manual.Status.Should().Be(AppTaskStatus.Running, "the long pass still runs beside the queue");

            pass.Release.SetResult();
            (await WaitForTaskAsync(provider, "BacklogSearch", t => t.Status != AppTaskStatus.Running))
                .Status.Should().Be(AppTaskStatus.Completed);
        }
        finally
        {
            if (Directory.Exists(dataPath)) Directory.Delete(dataPath, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, AppTaskStatus.Cancelled)]
    [InlineData(false, AppTaskStatus.Completed)]
    public async Task CancelStopsAPassThatTakesATokenAndWaitsForOneThatDoesNot(bool takesToken, AppTaskStatus final)
    {
        var dataPath = Path.Combine(Path.GetTempPath(), $"run-now-{Guid.NewGuid():N}");
        try
        {
            await using var provider = BuildApp(dataPath);
            var tasks = provider.GetRequiredService<TaskService>();
            var pass = new BlockingPass { TakesToken = takesToken };
            var task = await tasks.StartManualPassAsync("Health Check", "HealthCheck", pass);

            (await tasks.CancelTaskAsync(task!.Id)).Should().BeTrue();

            if (!takesToken)
            {
                await Task.Delay(200);
                (await tasks.GetTaskAsync(task.Id))!.Status.Should().Be(AppTaskStatus.Aborting,
                    "the row must not claim Cancelled while the pass still runs");
                pass.Release.SetResult();
            }
            (await WaitForTaskAsync(provider, "HealthCheck", t => t.Ended != null)).Status.Should().Be(final);
        }
        finally
        {
            if (Directory.Exists(dataPath)) Directory.Delete(dataPath, recursive: true);
        }
    }

    [Fact]
    public async Task ARunNowWhoseSetupFailsIsRecordedAndLetsTheGateGo()
    {
        // The second database scope is the one the pass loads its row with.
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<SportarrDbContext>().UseInMemoryDatabase(databaseName).Options;
        var resolutions = 0;
        var services = new ServiceCollection().AddLogging();
        services.AddScoped(_ => Interlocked.Increment(ref resolutions) == 2
            ? throw new InvalidOperationException("database unavailable")
            : new SportarrDbContext(options));
        await using var provider = services.BuildServiceProvider();
        var tasks = new TaskService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<TaskService>.Instance);
        var pass = new BlockingPass();

        var task = await tasks.StartManualPassAsync("Health Check", "HealthCheck", pass);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        AppTask? saved;
        do
        {
            await Task.Delay(50);
            await using var db = new SportarrDbContext(options);
            saved = await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == task!.Id);
        } while (saved.Status == AppTaskStatus.Running && DateTime.UtcNow < deadline);

        saved.Status.Should().Be(AppTaskStatus.Failed);
        saved.Message.Should().Be("The task could not start");
        (await tasks.CancelTaskAsync(task!.Id)).Should().BeFalse("the task is no longer tracked");
        pass.PassGate.IsRunning.Should().BeFalse("the gate is free for the next pass");
    }

    [Fact]
    public async Task TheQueueIgnoresARunNowRowItDoesNotOwn()
    {
        // A Run Now row is visible from its first save. The queue used to
        // take such a row for an orphan and mark it failed.
        var dataPath = Path.Combine(Path.GetTempPath(), $"run-now-{Guid.NewGuid():N}");
        try
        {
            await using var provider = BuildApp(dataPath);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
                db.Tasks.Add(new AppTask { Name = "Backlog Search", CommandName = "BacklogSearch", Status = AppTaskStatus.Running, Started = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var tasks = provider.GetRequiredService<TaskService>();

            await tasks.QueueTaskAsync("Test", "TestTask");

            await WaitForTaskAsync(provider, "TestTask", t => t.Started != null);
            (await WaitForTaskAsync(provider, "BacklogSearch", _ => true)).Status.Should().Be(AppTaskStatus.Running);
        }
        finally
        {
            if (Directory.Exists(dataPath)) Directory.Delete(dataPath, recursive: true);
        }
    }

    [Fact]
    public async Task RestartMarksAStoppingRunNowAsCancelledAndLeavesQueueRows()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), $"run-now-{Guid.NewGuid():N}");
        try
        {
            await using var provider = BuildApp(dataPath);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
                db.Tasks.AddRange(
                    new AppTask { Name = "Housekeeping", CommandName = "Housekeeping", Status = AppTaskStatus.Aborting, Started = DateTime.UtcNow },
                    new AppTask { Name = "RSS Sync", CommandName = "RssSync", Status = AppTaskStatus.Aborting, Started = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }

            await provider.GetRequiredService<TaskService>().RecoverAndResumeAsync();

            (await WaitForTaskAsync(provider, "Housekeeping", _ => true)).Status.Should().Be(AppTaskStatus.Cancelled);
            (await WaitForTaskAsync(provider, "RssSync", _ => true)).Status.Should().Be(AppTaskStatus.Aborting);
        }
        finally
        {
            if (Directory.Exists(dataPath)) Directory.Delete(dataPath, recursive: true);
        }
    }

    [Fact]
    public async Task AFailedManualRunDoesNotSkipTheNextTick()
    {
        var gate = new ScheduledPassGate();
        await FluentActions.Awaiting(() => gate.RunManualAsync(_ => throw new InvalidOperationException("boom"), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        (await gate.RunScheduledAsync(() => Task.CompletedTask, TimeSpan.FromHours(1), CancellationToken.None))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(typeof(BacklogSearchService))]
    [InlineData(typeof(ImportListSyncService))]
    [InlineData(typeof(TvScheduleSyncService))]
    [InlineData(typeof(TrashSyncBackgroundService))]
    [InlineData(typeof(DvrAutoSchedulerService))]
    [InlineData(typeof(CatchupDownloadService))]
    [InlineData(typeof(HealthCheckMonitorService))]
    [InlineData(typeof(HousekeepingService))]
    [InlineData(typeof(EventRetentionService))]
    public async Task RunNowReachesTheInstanceTheHostRuns(Type serviceType)
    {
        // A second instance would have its own gate, and a manual run could
        // then overlap the scheduled pass.
        var services = new ServiceCollection().AddLogging();
        services.AddSportarrBackgroundServices();
        await using var provider = services.BuildServiceProvider();

        // Build only the hosted wrappers made by a factory. The other hosted
        // services need dependencies this test does not register.
        var hosted = new List<object>();
        foreach (var descriptor in services.Where(d =>
            d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) && d.ImplementationFactory != null))
        {
            try { hosted.Add(descriptor.ImplementationFactory!(provider)); }
            catch (InvalidOperationException) { }
        }

        hosted.Should().Contain(provider.GetRequiredService(serviceType));
    }

    [Fact]
    public async Task AnUnknownTaskIsRefused()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();

        var result = await TaskEndpoints.TriggerScheduledTaskAsync("not-a-task", provider);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task AManualRunDoesNotStartWhileAPassRuns()
    {
        var gate = new ScheduledPassGate();
        var release = new TaskCompletionSource();
        var scheduled = gate.RunScheduledAsync(() => release.Task, TimeSpan.Zero, CancellationToken.None);

        (await gate.RunManualAsync(_ => Task.CompletedTask, CancellationToken.None)).Should().BeFalse();

        release.SetResult();
        (await scheduled).Should().BeTrue();
        (await gate.RunManualAsync(_ => Task.CompletedTask, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task AScheduledTickDuringAManualRunIsSkipped()
    {
        // The reviewer's case. The timer fired during a manual Backlog Search,
        // and the scheduled pass then ran a second full sweep right after it.
        var gate = new ScheduledPassGate();
        var release = new TaskCompletionSource();
        var manual = gate.RunManualAsync(_ => release.Task, CancellationToken.None);

        var scheduledRan = false;
        (await gate.RunScheduledAsync(() => { scheduledRan = true; return Task.CompletedTask; },
            TimeSpan.FromMinutes(30), CancellationToken.None)).Should().BeFalse();

        release.SetResult();
        await manual;
        scheduledRan.Should().BeFalse();
    }

    [Fact]
    public async Task AScheduledTickSoonAfterAManualRunIsSkipped()
    {
        var now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        var gate = new ScheduledPassGate(() => now);
        await gate.RunManualAsync(_ => Task.CompletedTask, CancellationToken.None);
        var runs = 0;

        now = now.AddMinutes(10);
        (await gate.RunScheduledAsync(() => { runs++; return Task.CompletedTask; },
            TimeSpan.FromMinutes(30), CancellationToken.None)).Should().BeFalse("the manual run counts as this tick");

        now = now.AddMinutes(25);
        (await gate.RunScheduledAsync(() => { runs++; return Task.CompletedTask; },
            TimeSpan.FromMinutes(30), CancellationToken.None)).Should().BeTrue();
        runs.Should().Be(1);
    }

    [Fact]
    public async Task StoppingCancelsAManualRunAndWaitsForIt()
    {
        var gate = new ScheduledPassGate();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = false;
        var manual = gate.RunManualAsync(async ct =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { ended = true; }
        }, CancellationToken.None);
        // A full suite run keeps the thread pool busy, so the waits are long.
        await started.Task.WaitAsync(TimeSpan.FromSeconds(60));

        await gate.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));

        ended.Should().BeTrue("stop waits until the pass has ended");
        await FluentActions.Awaiting(() => manual).Should().ThrowAsync<OperationCanceledException>(
            "the task queue records a cancelled run, not a failure");
    }
}
