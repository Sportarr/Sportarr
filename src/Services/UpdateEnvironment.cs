using System.Diagnostics;

namespace Sportarr.Api.Services;

/// <summary>
/// How the update helper starts Sportarr again. The names match the
/// helper's --restart values.
/// </summary>
public enum UpdateRestartMode
{
    Process,
    Systemd,
    WindowsService
}

/// <summary>
/// The restart mode, the service name, and a reason when the built-in
/// update cannot work with this service setup.
/// </summary>
public sealed record UpdateRestartTarget(UpdateRestartMode Mode, string ServiceName, string? Problem);

/// <summary>
/// Facts about the computer and the process that decide if the built-in
/// update can run. Tests use a fake.
/// </summary>
public interface IUpdateEnvironment
{
    string InstallDir { get; }
    string WorkingDirectory { get; }
    string? ProcessPath { get; }
    IReadOnlyList<string> Arguments { get; }
    int ProcessId { get; }
    DateTime ProcessStartUtc { get; }
    string? Os { get; }
    string? Arch { get; }
    bool IsContainer { get; }
    bool IsDirectoryWritable(string path);
    long? FreeSpace(string path);

    /// <summary>The mount point or drive that holds the path.</summary>
    string VolumeOf(string path);
    /// <summary>
    /// Finds how Sportarr runs. The answer is kept. "refresh" looks again,
    /// for example after a service file change.
    /// </summary>
    UpdateRestartTarget DetectRestart(bool refresh = false);
}

public sealed class SystemUpdateEnvironment : IUpdateEnvironment
{
    // TEST ONLY. The end-to-end update test sets this environment variable.
    // It runs Sportarr in a container, where the built-in update is
    // otherwise refused. No setting, API or page can change it.
    public const string AllowContainerVariable = "SPORTARR_UPDATE_ALLOW_CONTAINER";

    private readonly ILogger<SystemUpdateEnvironment> _logger;
    private UpdateRestartTarget? _restart;

    public SystemUpdateEnvironment(ILogger<SystemUpdateEnvironment> logger)
    {
        _logger = logger;
        using var current = Process.GetCurrentProcess();
        ProcessId = current.Id;
        ProcessStartUtc = current.StartTime.ToUniversalTime();
    }

    public string InstallDir => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    // Read once at start. A relative data path depends on it.
    public string WorkingDirectory { get; } = Directory.GetCurrentDirectory();
    public string? ProcessPath => Environment.ProcessPath;
    public IReadOnlyList<string> Arguments => Environment.GetCommandLineArgs().Skip(1).ToList();
    public int ProcessId { get; }
    public DateTime ProcessStartUtc { get; }
    public string? Os => LatestReleaseClient.CurrentOs();
    public string? Arch => LatestReleaseClient.CurrentArch();

    public bool IsContainer
    {
        get
        {
            if (string.Equals(Environment.GetEnvironmentVariable(AllowContainerVariable), "true", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (File.Exists("/.dockerenv") || File.Exists("/run/.containerenv"))
                return true;
            return CgroupLooksLikeContainer(ReadFile("/proc/1/cgroup"));
        }
    }

    /// <summary>
    /// An application container shows its runtime in the cgroup path. An
    /// LXC system container does not count. It runs a full system with
    /// systemd, so the built-in update works there.
    /// </summary>
    internal static bool CgroupLooksLikeContainer(string? cgroup)
    {
        if (string.IsNullOrEmpty(cgroup))
            return false;
        return new[] { "docker", "kubepods", "containerd", "libpod" }
            .Any(marker => cgroup.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsDirectoryWritable(string path)
    {
        try
        {
            var probe = Path.Combine(path, $".sportarr-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public string VolumeOf(string path)
    {
        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try
        {
            return DriveInfo.GetDrives()
                .Select(d => d.RootDirectory.FullName)
                .Where(root => full.StartsWith(root, comparison) &&
                               (full.Length == root.Length || root.EndsWith(Path.DirectorySeparatorChar) || full[root.Length] == Path.DirectorySeparatorChar))
                .OrderByDescending(root => root.Length)
                .FirstOrDefault() ?? Path.GetPathRoot(full) ?? full;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Path.GetPathRoot(full) ?? full;
        }
    }

    public long? FreeSpace(string path)
    {
        try { return new DriveInfo(Path.GetFullPath(path)).AvailableFreeSpace; }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return null; }
    }

    public UpdateRestartTarget DetectRestart(bool refresh = false) =>
        refresh || _restart == null ? _restart = DetectRestartCore() : _restart;

    private UpdateRestartTarget DetectRestartCore()
    {
        if (OperatingSystem.IsWindows())
        {
            return IsWindowsService()
                ? new UpdateRestartTarget(UpdateRestartMode.WindowsService, WindowsServiceName(ProcessId) ?? "Sportarr", null)
                : new UpdateRestartTarget(UpdateRestartMode.Process, "", null);
        }

        // launchd stops every process that a job started when the job ends,
        // so the helper would stop with Sportarr. A launchd job has launchd,
        // process 1, as its parent. A start from a terminal has the shell.
        if (OperatingSystem.IsMacOS() && GetParentProcessId() == 1)
        {
            return new UpdateRestartTarget(UpdateRestartMode.Process, "",
                "Sportarr runs under launchd, which stops the update helper together with Sportarr. Use the Script or External mechanism.");
        }

        if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INVOCATION_ID")))
        {
            // A scope, a user manager or an unknown layout gets the plain
            // restart. Only a system service gets the systemd restart.
            var cgroup = ReadFile("/proc/self/cgroup");
            if (IsUserManagerCgroup(cgroup))
            {
                // A user manager has its own settings and its own rights.
                // The checks below do not cover it, so refuse.
                return new UpdateRestartTarget(UpdateRestartMode.Process, "",
                    "Sportarr runs as a systemd user service. The built-in update does not support user services. Run Sportarr as a system service, or use the Script or External mechanism.");
            }
            var unit = SystemdUnitFromCgroup(cgroup);
            if (unit == null)
                return new UpdateRestartTarget(UpdateRestartMode.Process, "", null);

            var settings = ReadSystemdSettings(unit);
            return new UpdateRestartTarget(UpdateRestartMode.Systemd, unit, SystemdProblem(unit, settings));
        }

        return new UpdateRestartTarget(UpdateRestartMode.Process, "", null);
    }

    /// <summary>
    /// The helper must outlive Sportarr, and systemd must start Sportarr
    /// again after it exits with a failure code.
    /// </summary>
    internal static string? SystemdProblem(string unit, IReadOnlyDictionary<string, string>? settings)
    {
        if (settings == null)
            return $"Sportarr could not read the settings of the {unit} service with systemctl.";

        settings.TryGetValue("KillMode", out var killMode);
        settings.TryGetValue("Restart", out var restart);
        if (!string.Equals(killMode, "process", StringComparison.OrdinalIgnoreCase))
            return $"Add KillMode=process to the {unit} service, so that the update helper keeps running when Sportarr stops.";

        // Sportarr exits with code 1 after the swap. Only these two rules
        // restart a service after that exit code.
        if (restart is not ("on-failure" or "always"))
            return $"Add Restart=on-failure to the {unit} service, so that systemd starts Sportarr again after the update.";

        foreach (var key in new[] { "SuccessExitStatus", "RestartPreventExitStatus" })
        {
            if (settings.TryGetValue(key, out var statuses) &&
                statuses.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("1"))
                return $"Remove exit code 1 from {key} in the {unit} service. Sportarr exits with code 1 so that systemd starts the new version.";
        }

        // A failed new version restarts again and again until the rollback.
        // The pause between starts must be long enough, or systemd hits its
        // start limit and gives up before the old version is back. The rule
        // has two parts. RestartSec is at least 2 seconds. The start limit is
        // off, or the burst of starts at that pause takes longer than the
        // limit interval.
        settings.TryGetValue("RestartUSec", out var restartText);
        var restartDelay = ParseSystemdTimeSpan(restartText);
        if (restartDelay == null || restartDelay < TimeSpan.FromSeconds(2))
            return $"Add RestartSec=5 to the {unit} service, so that systemd waits between starts while the update rolls back.";

        settings.TryGetValue("StartLimitBurst", out var burstText);
        settings.TryGetValue("StartLimitIntervalUSec", out var intervalText);
        var burst = int.TryParse(burstText, out var b) ? b : 5;
        var interval = intervalText == "infinity" ? TimeSpan.MaxValue : ParseSystemdTimeSpan(intervalText) ?? TimeSpan.FromSeconds(10);
        var limitOff = burst == 0 || interval == TimeSpan.Zero;
        if (!limitOff && (interval == TimeSpan.MaxValue || restartDelay.Value * burst <= interval))
            return $"The start limit of the {unit} service can stop systemd during a rollback. Raise RestartSec, or lower StartLimitIntervalSec, so that {burst} starts take longer than the interval.";
        return null;
    }

    private static bool IsWindowsService()
    {
#if WINDOWS
        return Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService();
#else
        return false;
#endif
    }

    /// <summary>
    /// Finds the name of the service that runs this process. The installer
    /// calls it Sportarr, but a user can install it under another name.
    /// </summary>
    private string? WindowsServiceName(int pid)
    {
        try
        {
            var output = RunTool("tasklist.exe", new[] { "/svc", "/fi", $"PID eq {pid}", "/fo", "csv", "/nh" });
            return output == null ? null : ServiceNameFromTasklist(output);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Update] tasklist failed");
            return null;
        }
    }

    /// <summary>
    /// Reads the service name from one line of "tasklist /svc /fo csv",
    /// for example "Sportarr.exe","1234","Sportarr".
    /// </summary>
    internal static string? ServiceNameFromTasklist(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(line))
            return null;
        var fields = line.Split("\",\"");
        if (fields.Length < 3)
            return null;
        var name = fields[2].Trim('"', ' ', '\r');
        return name.Length == 0 || name.Equals("N/A", StringComparison.OrdinalIgnoreCase) || name.Contains(',')
            ? null
            : name;
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getppid")]
    private static extern int GetParentProcessIdNative();

    private static int GetParentProcessId()
    {
        try { return GetParentProcessIdNative(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return -1; }
    }

    /// <summary>
    /// True when a systemd system service runs Sportarr. The start and the
    /// update use this one rule, so that both agree.
    /// </summary>
    public static bool IsSystemdSystemService() =>
        OperatingSystem.IsLinux() &&
        IsSystemdSystemService(Environment.GetEnvironmentVariable("INVOCATION_ID"), ReadFile("/proc/self/cgroup"));

    internal static bool IsSystemdSystemService(string? invocationId, string? cgroup) =>
        !string.IsNullOrEmpty(invocationId) && !IsUserManagerCgroup(cgroup) && SystemdUnitFromCgroup(cgroup) != null;

    /// <summary>
    /// True when a systemd user manager (user@1000.service) runs Sportarr,
    /// as a user service or in a scope under it.
    /// </summary>
    internal static bool IsUserManagerCgroup(string? cgroup) =>
        !string.IsNullOrEmpty(cgroup) &&
        cgroup.Split('\n').Any(l => l.Contains("/user@", StringComparison.Ordinal) &&
                                    l.Split('/').Any(seg => seg.StartsWith("user@", StringComparison.Ordinal) && seg.EndsWith(".service", StringComparison.Ordinal)) &&
                                    !l.TrimEnd().EndsWith(".scope", StringComparison.Ordinal));

    /// <summary>
    /// Reads a systemd time span as systemctl show prints it, for example
    /// "100ms", "5s" or "1min 30s". Returns null for text it cannot read.
    /// "0" is zero.
    /// </summary>
    internal static TimeSpan? ParseSystemdTimeSpan(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (text.Trim() == "0")
            return TimeSpan.Zero;
        var total = TimeSpan.Zero;
        foreach (var part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var digits = new string(part.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            var unit = part[digits.Length..];
            if (!double.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                return null;
            total += unit switch
            {
                "us" => TimeSpan.FromMicroseconds(value),
                "ms" => TimeSpan.FromMilliseconds(value),
                "s" or "" => TimeSpan.FromSeconds(value),
                "min" => TimeSpan.FromMinutes(value),
                "h" => TimeSpan.FromHours(value),
                "d" => TimeSpan.FromDays(value),
                _ => TimeSpan.MinValue
            };
            if (total < TimeSpan.Zero)
                return null;
        }
        return total;
    }

    /// <summary>
    /// Returns the system service that runs Sportarr, from the last segment
    /// of the cgroup path. A scope, a service under a user manager
    /// (user@1000.service) or any other layout returns null.
    /// </summary>
    internal static string? SystemdUnitFromCgroup(string? cgroup)
    {
        if (string.IsNullOrEmpty(cgroup))
            return null;
        var line = cgroup.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("0::", StringComparison.Ordinal))
            ?? cgroup.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(l => l.Contains("name=systemd"));
        if (line == null)
            return null;
        var path = line[(line.IndexOf(':', line.IndexOf(':') + 1) + 1)..].Trim();
        if (path.Contains("/user@", StringComparison.Ordinal))
            return null;
        var last = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return last != null && last.EndsWith(".service", StringComparison.Ordinal) ? last : null;
    }

    private IReadOnlyDictionary<string, string>? ReadSystemdSettings(string unit)
    {
        try
        {
            var output = RunTool("systemctl", new[] { "show", unit, "-p", "KillMode", "-p", "Restart", "-p", "SuccessExitStatus", "-p", "RestartPreventExitStatus", "-p", "RestartUSec", "-p", "StartLimitBurst", "-p", "StartLimitIntervalUSec" });
            if (output == null)
                return null;
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Update] systemctl show failed");
            return null;
        }
    }

    /// <summary>
    /// Runs a tool and returns its output, or null when it fails or does not
    /// end within five seconds. The output is read while the tool runs, so
    /// a full pipe cannot block it.
    /// </summary>
    internal static string? RunTool(string tool, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi);
        if (process == null)
            return null;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            return null;
        }
        process.WaitForExit();
        _ = errors.Result;
        return process.ExitCode == 0 ? output.Result : null;
    }

    private static string? ReadFile(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception) { return null; }
    }
}
