using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Services;
using AppTaskStatus = Sportarr.Api.Models.TaskStatus;

namespace Sportarr.Api.Tests.SearchValidation;

/// <summary>
/// A search queue entry also writes a task row so it shows in System > Tasks.
/// The task runner must leave that row to the search queue. Otherwise the
/// same search runs twice and can grab twice.
/// </summary>
[Collection(IndexerStatusFixtureCollection.Name)]
public sealed class QueuedSearchRunsOnceTests
{
    [Fact]
    public async Task TaskRunnerDoesNotRunASearchThatWaitsInTheSearchQueue()
    {
        await using var rig = await CompetitionDateRouteHarness.CreateAsync(0);
        rig.Transport.Results = _ => new[] { rig.Release(0) };
        var searchQueue = new SearchQueueService(
            rig.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SearchQueueService>.Instance);

        // Hold the search queue processor, so the search stays queued.
        var processing = (SemaphoreSlim)typeof(SearchQueueService)
            .GetField("_processingLock", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.True(await processing.WaitAsync(TimeSpan.FromSeconds(30)));
        try
        {
            var queued = await searchQueue.QueueSearchAsync(rig.Event.Id);
            Assert.Equal(SearchQueueStatus.Queued, queued.Status);
            Assert.NotNull(queued.TaskId);

            // Any other task wakes the task runner.
            var probe = await rig.Services.GetRequiredService<TaskService>()
                .QueueTaskAsync("Probe", "QueuedSearchProbe");
            await WaitForEndAsync(rig, probe.Id);

            using var scope = rig.Services.CreateScope();
            var row = await scope.ServiceProvider.GetRequiredService<SportarrDbContext>().Tasks
                .AsNoTracking().SingleAsync(t => t.Id == queued.TaskId);
            Assert.Equal(AppTaskStatus.Queued, row.Status);
            Assert.Null(row.Started);
            Assert.Empty(rig.Transport.Searches);
        }
        finally
        {
            await searchQueue.ClearPendingSearchesAsync();
            processing.Release();
        }
    }

    [Fact]
    public async Task TaskRunnerDoesNotRunASearchThatWaitsForASearchSlot()
    {
        await using var rig = await CompetitionDateRouteHarness.CreateAsync(0);
        rig.Transport.Results = _ => new[] { rig.Release(0) };
        var searchQueue = new SearchQueueService(
            rig.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SearchQueueService>.Instance);

        // Take every search slot. The queue takes the search out of its
        // pending list and then waits here.
        var slots = (SemaphoreSlim)typeof(SearchQueueService)
            .GetField("_searchSemaphore", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var held = 0;
        try
        {
            var slotCount = searchQueue.GetQueueStatus().MaxConcurrent;
            for (; held < slotCount; held++)
                Assert.True(await slots.WaitAsync(TimeSpan.FromSeconds(30)));

            var queued = await searchQueue.QueueSearchAsync(rig.Event.Id);
            await WaitUntilAsync(() => searchQueue.GetQueueStatus().PendingCount == 0);

            var probe = await rig.Services.GetRequiredService<TaskService>()
                .QueueTaskAsync("Probe", "QueuedSearchProbe");
            await WaitForEndAsync(rig, probe.Id);

            var row = await RowAsync(rig, queued.TaskId!.Value);
            Assert.Equal(AppTaskStatus.Queued, row.Status);
            Assert.Empty(rig.Transport.Searches);
        }
        finally
        {
            for (; held > 0; held--) slots.Release();
        }
        await WaitUntilAsync(() => searchQueue.GetQueueStatus().ActiveCount == 0, seconds: 60);
    }

    [Fact]
    public async Task TaskRunnerDoesNotFailASearchTheSearchQueueIsRunning()
    {
        await using var rig = await CompetitionDateRouteHarness.CreateAsync(0);
        var searchQueue = new SearchQueueService(
            rig.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SearchQueueService>.Instance);
        var processing = (SemaphoreSlim)typeof(SearchQueueService)
            .GetField("_processingLock", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.True(await processing.WaitAsync(TimeSpan.FromSeconds(30)));
        try
        {
            var queued = await searchQueue.QueueSearchAsync(rig.Event.Id);

            // The search queue marks its row Running when the search starts.
            using (var scope = rig.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
                var running = await db.Tasks.SingleAsync(t => t.Id == queued.TaskId);
                running.Status = AppTaskStatus.Running;
                running.Started = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            var probe = await rig.Services.GetRequiredService<TaskService>()
                .QueueTaskAsync("Probe", "QueuedSearchProbe");
            await WaitForEndAsync(rig, probe.Id);

            // The runner treated a row with no runner token as an orphan and failed it.
            var row = await RowAsync(rig, queued.TaskId!.Value);
            Assert.Equal(AppTaskStatus.Running, row.Status);
            Assert.Null(row.Ended);
            Assert.Empty(rig.Transport.Searches);
        }
        finally
        {
            await searchQueue.ClearPendingSearchesAsync();
            processing.Release();
        }
    }

    [Fact]
    public async Task ClearedSearchRowIsCancelledBeforeTheQueueLetsItGo()
    {
        var capture = new CancelCapture();
        await using var rig = await Sportarr.Api.Tests.Services.PartIdentityIntegrationHarness.CreateAsync(
            multipart: false, interceptors: new Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] { capture });
        var searchQueue = new SearchQueueService(
            rig.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SearchQueueService>.Instance);
        var processing = (SemaphoreSlim)typeof(SearchQueueService)
            .GetField("_processingLock", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.True(await processing.WaitAsync(TimeSpan.FromSeconds(30)));
        try
        {
            var queued = await searchQueue.QueueSearchAsync(rig.Event.Id);

            Assert.Equal(1, await searchQueue.ClearPendingSearchesAsync());

            // While the row was still Queued, the queue still claimed it.
            Assert.Equal(new[] { true }, capture.ClaimedWhenCancelled);
            Assert.DoesNotContain(queued.Id, SearchQueueService.UnfinishedSearchIds());
            Assert.Equal(AppTaskStatus.Cancelled,
                (await rig.Db.Tasks.AsNoTracking().SingleAsync(t => t.Id == queued.TaskId)).Status);
        }
        finally
        {
            processing.Release();
        }
    }

    /// <summary>
    /// Records, for each task row saved as Cancelled, whether the search
    /// queue still claimed it at that moment.
    /// </summary>
    private sealed class CancelCapture : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public List<bool> ClaimedWhenCancelled { get; } = new();

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            foreach (var row in eventData.Context!.ChangeTracker.Entries<Sportarr.Api.Models.AppTask>())
            {
                if (row.State == EntityState.Modified && row.Entity.Status == AppTaskStatus.Cancelled
                    && row.Entity.CancellationId != null)
                    ClaimedWhenCancelled.Add(SearchQueueService.UnfinishedSearchIds().Contains(row.Entity.CancellationId));
            }
            return ValueTask.FromResult(result);
        }
    }

    private static async Task<Sportarr.Api.Models.AppTask> RowAsync(CompetitionDateRouteHarness rig, int taskId)
    {
        using var scope = rig.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SportarrDbContext>().Tasks
            .AsNoTracking().SingleAsync(t => t.Id == taskId);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The search queue did not reach the expected state.");
            await Task.Delay(100);
        }
    }

    private static async Task WaitForEndAsync(CompetitionDateRouteHarness rig, int taskId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = rig.Services.CreateScope();
            var task = await scope.ServiceProvider.GetRequiredService<SportarrDbContext>().Tasks
                .AsNoTracking().SingleAsync(t => t.Id == taskId);
            if (task.Ended != null) return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Task {taskId} did not finish.");
    }
}
