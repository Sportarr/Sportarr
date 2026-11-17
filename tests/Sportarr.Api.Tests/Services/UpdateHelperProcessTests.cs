using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Sportarr.Update;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Runs the real update helper as a child of a real stand-in for Sportarr.
/// The stand-in ignores the stop, so the helper must stop its own parent.
/// This runs only inside the Linux test container.
/// </summary>
public class UpdateHelperProcessTests : IDisposable
{
    // Generous limits, so that a busy machine cannot fail the test. A
    // healthy run ends in a few seconds, because each wait ends as soon as
    // its condition is true. Only the two exit waits always run in full,
    // because the stand-in never stops by itself.
    private const int ExitWaitSeconds = 2;
    private const int ExtraExitWaitSeconds = 2;
    private const int HealthWaitSeconds = 120;
    private static readonly TimeSpan TestLimit = TimeSpan.FromMinutes(4);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"update-helper-process-{Guid.NewGuid():N}");
    private readonly ITestOutputHelper _output;

    public UpdateHelperProcessTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task The_helper_stops_a_parent_that_ignores_the_stop_and_finishes_the_update()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/bin/sh"))
            return;

        var helperDll = typeof(UpdateRunner).Assembly.Location;
        File.Exists(Path.ChangeExtension(helperDll, ".runtimeconfig.json")).Should().BeTrue("the helper must be able to run on its own");

        var install = Path.Combine(_root, "install");
        var staging = Path.Combine(_root, "data", "updates", "staging");
        var data = Path.Combine(_root, "data");
        // Each started app records its pid, so that the test can stop it.
        // It runs long enough for a slow machine to see it answer.
        var appPids = Path.Combine(_root, "app-pids");
        WriteScript(Path.Combine(install, "Sportarr"), $"#!/bin/sh\necho old\necho $$ >> '{appPids}'\nexec sleep 300\n");
        // Only the new version leaves this file. The health answer waits for
        // it, so a restart of the wrong program fails the test.
        var newStarted = Path.Combine(_root, "new-version-started");
        WriteScript(Path.Combine(staging, "Sportarr"), $"#!/bin/sh\necho new\necho $$ >> '{appPids}'\ntouch '{newStarted}'\nexec sleep 300\n");
        WriteScript(Path.Combine(staging, "Sportarr.Update", "Sportarr.Update"), "#!/bin/sh\n");

        // The stand-in is a copy of the shell named Sportarr, so the helper
        // takes it for Sportarr. It ignores SIGTERM and never exits by itself.
        var stub = Path.Combine(_root, "stub", "Sportarr");
        Directory.CreateDirectory(Path.GetDirectoryName(stub)!);
        File.Copy("/bin/sh", stub);
        File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        using var health = new TcpListener(IPAddress.Loopback, 0);
        health.Start();
        var port = ((IPEndPoint)health.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TestLimit);
        var server = ServeAsync(health, () => File.Exists(newStarted), cts.Token);

        var helperArgs = string.Join(' ', new[]
        {
            "--pid", "$$", "--install-dir", install, "--staging-dir", staging, "--data-dir", data,
            "--app-exe", Path.Combine(install, "Sportarr"), "--health-url", $"http://127.0.0.1:{port}/ping",
            "--version", "9.9.9.9", "--previous-version", "1.0.0.1",
            "--exit-timeout", $"{ExitWaitSeconds}", "--extra-exit-wait", $"{ExtraExitWaitSeconds}",
            "--health-timeout", $"{HealthWaitSeconds}"
        }.Select(a => a == "$$" ? a : $"'{a}'"));
        var script = $"trap '' TERM INT; dotnet '{helperDll}' {helperArgs}; sleep 300";

        using var parent = Process.Start(new ProcessStartInfo(stub) { ArgumentList = { "-c", script }, UseShellExecute = false })!;
        var resultPath = Path.Combine(data, "updates", UpdateResultFile.FileName);
        var logPath = Path.Combine(data, "logs", "update.log");
        try
        {
            var stopped = await WaitForAsync(() => parent.HasExited, TestLimit);
            var finished = await WaitForAsync(() => File.Exists(resultPath), TestLimit);

            stopped.Should().BeTrue($"the helper must stop its parent.{Diagnostics(logPath)}");
            finished.Should().BeTrue($"the helper must write its result.{Diagnostics(logPath)}");
            var log = File.ReadAllText(logPath);
            File.ReadAllText(resultPath).Should().Contain("status=Succeeded", Diagnostics(logPath));
            log.Should().Contain("Stopping Sportarr").And.NotContain("could not stop");
            File.ReadAllText(Path.Combine(install, "Sportarr")).Should().Contain("echo new");
            File.Exists(newStarted).Should().BeTrue("the helper starts the new version");
        }
        catch
        {
            _output.WriteLine(Diagnostics(logPath));
            throw;
        }
        finally
        {
            if (!parent.HasExited)
                parent.Kill(entireProcessTree: true);
            StopApps(appPids);
            cts.Cancel();
            health.Stop();
            try { await server; } catch (Exception) { }
        }
    }

    private static void StopApps(string pidFile)
    {
        if (!File.Exists(pidFile))
            return;
        foreach (var line in File.ReadAllLines(pidFile))
        {
            if (!int.TryParse(line, out var pid))
                continue;
            try
            {
                using var app = Process.GetProcessById(pid);
                app.Kill();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
        }
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                return false;
            await Task.Delay(100);
        }
        return true;
    }

    private static string Diagnostics(string logPath) =>
        File.Exists(logPath)
            ? $"{Environment.NewLine}Helper log:{Environment.NewLine}{File.ReadAllText(logPath)}"
            : $"{Environment.NewLine}The helper wrote no log.";

    // Answers 200, as /ping does, once the new version has started. Until
    // then it answers 503.
    private static async Task ServeAsync(TcpListener listener, Func<bool> ready, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            var buffer = new byte[4096];
            await stream.ReadAsync(buffer, cancellationToken);
            var response = ready()
                ? "HTTP/1.1 200 OK\r\nContent-Length: 4\r\nConnection: close\r\n\r\npong"u8.ToArray()
                : "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response, cancellationToken);
        }
    }

    private static void WriteScript(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
