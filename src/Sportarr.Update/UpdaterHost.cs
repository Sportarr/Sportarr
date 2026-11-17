using System.Diagnostics;

namespace Sportarr.Update;

/// <summary>
/// The process id and start time that the app writes to updates/app.pid.
/// The start time makes sure that a reused process id is not stopped.
/// </summary>
public sealed record AppProcessId(int Pid, long StartTicksUtc)
{
    public static AppProcessId? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var lines = File.ReadAllLines(path);
            return lines.Length >= 2 &&
                   int.TryParse(lines[0], out var pid) &&
                   long.TryParse(lines[1], out var ticks)
                ? new AppProcessId(pid, ticks)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}

/// <summary>
/// The parts of the helper that touch processes, services and the network.
/// Tests use a fake.
/// </summary>
public interface IUpdaterHost
{
    /// <summary>
    /// Returns true when the process has stopped within the timeout. A
    /// process id that now belongs to another process counts as stopped.
    /// </summary>
    Task<bool> WaitForExitAsync(AppProcessId app, TimeSpan timeout);

    /// <summary>Starts the app. Returns a handle that tells if it stopped early.</summary>
    IStartedApp StartApp(string executable, IReadOnlyList<string> arguments, string workingDirectory);

    /// <summary>Returns true when the URL answers with a success code.</summary>
    Task<bool> PingAsync(string url);

    Task DelayAsync(TimeSpan delay);

    bool IsRunning(AppProcessId app);

    Task<bool> StartServiceAsync(string serviceName);

    /// <summary>Stops the service and waits until it reports STOPPED.</summary>
    Task<bool> StopServiceAsync(string serviceName, TimeSpan timeout);

    /// <summary>
    /// Stops the app process, but only when the id still belongs to a
    /// Sportarr process that started at the recorded time.
    /// </summary>
    Task<bool> KillAppAsync(AppProcessId app);

    /// <summary>
    /// Pauses the app process (SIGSTOP), so that it writes nothing while
    /// the helper restores files. Unix only. Returns false elsewhere.
    /// </summary>
    bool PauseApp(AppProcessId app);

    /// <summary>Lets a paused process run again (SIGCONT). Unix only.</summary>
    bool ResumeApp(AppProcessId app);

    /// <summary>The systemd ActiveState of the unit, for example "failed".</summary>
    Task<string?> SystemdStateAsync(string unit);

    DateTime UtcNow { get; }
}

public interface IStartedApp
{
    bool HasExited { get; }

    /// <summary>Stops the process and waits for it to end.</summary>
    Task KillAsync();
}

public sealed class SystemUpdaterHost : IUpdaterHost
{
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(30);
    private readonly UpdateLog _log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public SystemUpdaterHost(UpdateLog log) => _log = log;

    public DateTime UtcNow => DateTime.UtcNow;

    public async Task<bool> WaitForExitAsync(AppProcessId app, TimeSpan timeout)
    {
        var process = FindApp(app);
        if (process == null)
            return true; // It has already stopped, or the id is someone else's now.

        using (process)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }

    public IStartedApp StartApp(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        var process = Process.Start(psi) ?? throw new InvalidOperationException($"{executable} did not start");
        _log.Write($"Started {executable} (pid {process.Id}) in {workingDirectory}");
        return new StartedProcess(process);
    }

    public async Task<bool> PingAsync(string url)
    {
        try
        {
            using var response = await _http.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public Task DelayAsync(TimeSpan delay) => Task.Delay(delay);

    public bool IsRunning(AppProcessId app) => FindApp(app) is { } process && Dispose(process);

    public async Task<bool> StartServiceAsync(string serviceName) => (await RunScAsync("start", serviceName)).ExitCode == 0;

    public async Task<bool> StopServiceAsync(string serviceName, TimeSpan timeout)
    {
        await RunScAsync("stop", serviceName);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var (exitCode, output) = await RunScAsync("query", serviceName);
            if (exitCode == 0 && output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
                return true;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        _log.Write($"The {serviceName} service did not stop within {timeout.TotalSeconds:0} seconds");
        return false;
    }

    public async Task<bool> KillAppAsync(AppProcessId app)
    {
        var process = FindApp(app);
        if (process == null)
        {
            _log.Write($"Pid {app.Pid} is not a running Sportarr process. Nothing to stop");
            return false;
        }

        using (process)
        {
            _log.Write($"Stopping pid {app.Pid}");
            if (!TryKill(process, app.Pid))
                return false;
            using var cts = new CancellationTokenSource(KillWait);
            try
            {
                await process.WaitForExitAsync(cts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                _log.Write($"Pid {app.Pid} did not stop within {KillWait.TotalSeconds:0} seconds");
                return false;
            }
        }
    }

    /// <summary>
    /// Stops the process and its children. The helper can be a child of the
    /// process, and then a tree kill throws, because it would end the helper
    /// too. The process alone is stopped then.
    /// </summary>
    private bool TryKill(Process process, int pid)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (InvalidOperationException) when (HasExited(process))
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            // The helper is in the tree.
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log.Write($"Could not stop pid {pid} with its children: {ex.Message}");
        }

        try
        {
            process.Kill();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (HasExited(process))
                return true;
            _log.Write($"Could not stop pid {pid}: {ex.Message}");
            return false;
        }
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    public bool ResumeApp(AppProcessId app)
    {
        if (OperatingSystem.IsWindows())
            return false;
        var signal = OperatingSystem.IsMacOS() ? 19 : 18; // SIGCONT
        var resumed = SendSignal(app.Pid, signal) == 0;
        _log.Write(resumed ? $"Resumed pid {app.Pid}" : $"Could not resume pid {app.Pid}");
        return resumed;
    }

    public bool PauseApp(AppProcessId app)
    {
        if (OperatingSystem.IsWindows())
            return false;
        using var process = FindApp(app);
        if (process == null)
            return false;
        const int SigStop = 19; // Linux. macOS uses 17.
        var signal = OperatingSystem.IsMacOS() ? 17 : SigStop;
        var paused = SendSignal(app.Pid, signal) == 0;
        _log.Write(paused ? $"Paused pid {app.Pid}" : $"Could not pause pid {app.Pid}");
        return paused;
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SendSignal(int pid, int signal);

    public async Task<string?> SystemdStateAsync(string unit)
    {
        var (exitCode, output) = await RunToolAsync("systemctl", new[] { "show", unit, "-p", "ActiveState", "--value" });
        return exitCode == 0 ? output.Trim() : null;
    }

    // The name check skips the helper itself. The start time check skips a
    // process that got the same id later. A start time of zero is unknown.
    private static Process? FindApp(AppProcessId app)
    {
        try
        {
            var process = Process.GetProcessById(app.Pid);
            var nameOk = process.ProcessName.StartsWith("Sportarr", StringComparison.OrdinalIgnoreCase) &&
                         !process.ProcessName.StartsWith("Sportarr.Update", StringComparison.OrdinalIgnoreCase);
            var startOk = app.StartTicksUtc == 0 ||
                          Math.Abs(process.StartTime.ToUniversalTime().Ticks - app.StartTicksUtc) < TimeSpan.TicksPerSecond * 2;
            if (nameOk && startOk && !process.HasExited)
                return process;
            process.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool Dispose(Process process)
    {
        process.Dispose();
        return true;
    }

    private async Task<(int ExitCode, string Output)> RunScAsync(string verb, string serviceName)
    {
        var result = await RunToolAsync("sc.exe", new[] { verb, serviceName });
        if (verb != "query")
            _log.Write($"sc {verb} {serviceName} exited {result.ExitCode}");
        return result;
    }

    // Reads the output while the tool runs, and stops a tool that hangs.
    private async Task<(int ExitCode, string Output)> RunToolAsync(string tool, IEnumerable<string> arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(tool)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);
            using var process = Process.Start(psi);
            if (process == null)
                return (-1, "");
            var output = process.StandardOutput.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                _log.Write($"{tool} did not answer within 30 seconds");
                return (-1, "");
            }
            return (process.ExitCode, await output);
        }
        catch (Exception ex)
        {
            _log.Write($"{tool} failed: {ex.Message}");
            return (-1, "");
        }
    }

    private sealed class StartedProcess : IStartedApp
    {
        private readonly Process _process;

        public StartedProcess(Process process) => _process = process;

        public bool HasExited
        {
            get
            {
                try { return _process.HasExited; }
                catch (InvalidOperationException) { return true; }
            }
        }

        public async Task KillAsync()
        {
            try { _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { return; }
            using var cts = new CancellationTokenSource(KillWait);
            try { await _process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { /* The caller logs the next step. */ }
        }
    }
}
