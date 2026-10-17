using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public class CachePolicyAutomaticRetentionDiagnosticsTests
{
    private readonly ITestOutputHelper _output;

    public CachePolicyAutomaticRetentionDiagnosticsTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task OriginalLowConfidenceFixtureRecordsTheActualWarmRejectionReason()
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        using var logs = new CapturedLogs();
        rig.Services.GetRequiredService<ILoggerFactory>().AddProvider(logs);
        await rig.RetentionAsync(10);
        var release = rig.Release(ageDays: 30);
        var scorer = rig.Services.GetRequiredService<ReleaseMatchScorer>();
        Assert.Null(scorer.GetSportPrefix(rig.Event.League!.Name, rig.Event.Sport));
        Assert.Equal(45, scorer.CalculateMatchScore(release.Title, rig.Event));
        rig.Transport.Results = _ => new[] { release };

        var cold = await rig.AutomaticAsync();

        Assert.Contains("No approved releases found", cold.Message);
        Assert.Contains(logs.Messages, message => message.Contains("retention: 10 days"));
        logs.Messages.Clear();

        var warm = await rig.AutomaticAsync();

        _output.WriteLine("Cold result: " + cold.Message);
        _output.WriteLine("Warm result: " + warm.Message);
        foreach (var message in logs.Messages) _output.WriteLine(message);
        Assert.Single(rig.Transport.Searches);
        if (warm.Message.Contains("none have sufficient match confidence"))
        {
            Assert.Contains(logs.Messages, message => message.Contains("1/1 releases approved by quality/part validation"));
            Assert.Contains(logs.Messages, message => message.Contains("Top score: 45"));
        }
        else
        {
            Assert.Contains("No approved releases found", warm.Message);
            Assert.Contains(logs.Messages, message => message.Contains("retention: 10 days"));
        }
        Assert.Null(warm.SelectedRelease);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
    }

    [Fact]
    public async Task HighConfidenceOldReleaseMustStillFailRetentionAfterCacheReuse()
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        using var logs = new CapturedLogs();
        rig.Services.GetRequiredService<ILoggerFactory>().AddProvider(logs);
        await rig.RetentionAsync(10);
        var release = HighConfidenceRelease(rig, 30);
        rig.Transport.Results = _ => new[] { release };
        var cold = await rig.AutomaticAsync();
        Assert.Contains("No approved releases found", cold.Message);
        Assert.Contains(logs.Messages, message => message.Contains("retention: 10 days"));
        Assert.Null(cold.SelectedRelease);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
        logs.Messages.Clear();

        var warm = await rig.AutomaticAsync();

        _output.WriteLine("High-confidence warm result: " + warm.Message);
        _output.WriteLine("Selected release: " + warm.SelectedRelease);
        _output.WriteLine("Descriptor attempts: " + rig.Transport.DescriptorAttempts);
        foreach (var message in logs.Messages) _output.WriteLine(message);
        Assert.Single(rig.Transport.Searches);
        Assert.Contains("No approved releases found", warm.Message);
        Assert.Contains(logs.Messages, message => message.Contains("retention: 10 days"));
        Assert.Null(warm.SelectedRelease);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
    }

    [Fact]
    public async Task HighConfidenceRecentControlReachesSelectionAndDescriptorAttempt()
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        await rig.RetentionAsync(10);
        var release = HighConfidenceRelease(rig, 1);
        rig.Transport.Results = _ => new[] { release };

        var result = await rig.AutomaticAsync();

        Assert.Single(rig.Transport.Searches);
        Assert.Equal(release.Title, result.SelectedRelease);
        Assert.Equal(1, rig.Transport.DescriptorAttempts);
        // The transport returns 410. This is selection evidence, not a download.
        Assert.False(result.Success);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
    }

    private static ReleaseSearchResult HighConfidenceRelease(CachePolicyHttpHarness rig, int ageDays)
    {
        var release = rig.Release(ageDays: ageDays);
        release.Title = "FIFA.World.Cup." + release.Title;
        Assert.True(rig.Services.GetRequiredService<ReleaseMatchScorer>().CalculateMatchScore(release.Title, rig.Event) >= 50,
            "The retention fixture must clear the actual automatic confidence gate.");
        return release;
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(Messages);
        public void Dispose() { }

        private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
        }
    }
}
