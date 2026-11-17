using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class UpdateShutdownTests : IDisposable
{
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), $"update-shutdown-{Guid.NewGuid():N}");

    public UpdateShutdownTests() => Directory.CreateDirectory(_dataPath);

    public void Dispose() => Directory.Delete(_dataPath, recursive: true);

    [Fact]
    public void Shutdown_kills_an_FFmpeg_process_that_is_still_running()
    {
        if (OperatingSystem.IsWindows())
            return;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = _dataPath })
            .Build();
        var recorder = new FFmpegRecorderService(
            NullLogger<FFmpegRecorderService>.Instance,
            new ConfigService(configuration, NullLogger<ConfigService>.Instance),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

        // A stand-in for FFmpeg that would run for a long time.
        using var process = Process.Start(new ProcessStartInfo("sleep", "300") { UseShellExecute = false })!;
        recorder.TrackForTest(1, process, Path.Combine(_dataPath, "rec.ts"));

        recorder.KillRemainingProcesses().Should().Be(1);

        process.HasExited.Should().BeTrue();
    }

    [Fact]
    public void Start_markers_tell_the_helper_the_pid_and_the_migration()
    {
        UpdateStartupMarkers.WriteStarting(_dataPath);
        var updates = Path.Combine(_dataPath, "updates");

        var pidLines = File.ReadAllLines(Path.Combine(updates, UpdateStartupMarkers.PidFileName));
        int.Parse(pidLines[0]).Should().Be(Environment.ProcessId);
        long.Parse(pidLines[1]).Should().BeGreaterThan(0);
        File.ReadAllText(Path.Combine(updates, UpdateStartupMarkers.StartingFileName)).Should().Be("starting");

        UpdateStartupMarkers.WriteMigrating(_dataPath);
        File.ReadAllText(Path.Combine(updates, UpdateStartupMarkers.StartingFileName)).Should().Be("migrating");

        UpdateStartupMarkers.ClearStarting(_dataPath);
        File.Exists(Path.Combine(updates, UpdateStartupMarkers.StartingFileName)).Should().BeFalse();
    }

    [Fact]
    public void The_helper_reads_the_pid_file_that_the_app_writes()
    {
        UpdateStartupMarkers.WriteStarting(_dataPath);

        var pid = Sportarr.Update.AppProcessId.Read(Path.Combine(_dataPath, "updates", UpdateStartupMarkers.PidFileName));

        pid!.Pid.Should().Be(Environment.ProcessId);
        using var current = Process.GetCurrentProcess();
        pid.StartTicksUtc.Should().Be(current.StartTime.ToUniversalTime().Ticks);
    }
}
