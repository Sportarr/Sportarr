using System.Diagnostics;
using System.Globalization;

namespace Sportarr.Api.Services;

/// <summary>
/// Small files in updates/ that tell the update helper how a start goes.
/// app.pid holds the process id and start time, so the helper stops the
/// right process. "starting" exists until the app is ready. It holds
/// "migrating" while database migrations run, so the helper waits longer
/// and knows that the database changed.
/// </summary>
public enum RestoreWait
{
    /// <summary>No restore marker was there.</summary>
    NoRestore,

    /// <summary>A marker of a stopped helper was there and is gone now.</summary>
    StaleRemoved,

    /// <summary>A live helper restored, and the start waited for it.</summary>
    WaitedForHelper,

    /// <summary>The wait reached its limit. The marker is gone now.</summary>
    TimedOut
}

public static class UpdateStartupMarkers
{
    public const string PidFileName = "app.pid";
    public const string StartingFileName = "starting";

    // Written when migrations run. The app never removes it. The helper
    // removes it before each start, so after a failed start it tells the
    // helper that the database changed, even when the migration was quick.
    public const string MigratedFileName = "migrated";

    public const string RestoringFileName = "restoring";

    public const string HelperPidFileName = "helper.pid";

    // The helper writes the version whose files are in the install folder,
    // when it swaps them in and when it puts the old ones back.
    public const string InstalledVersionFileName = "installed-version";

    // Short waits between tries of a file move. Tests set it lower.
    internal static TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    internal const int MoveAttempts = 6;

    /// <summary>
    /// Waits while the update helper restores the database. A start in that
    /// time would open a file that is half replaced. The marker names the
    /// helper process. When that process is gone, the marker is left over
    /// from a crash and goes away at once. Otherwise the wait ends at the
    /// limit. The limit stays short, because a Windows service must report
    /// that it runs within 30 seconds. Outside systemd the marker then goes
    /// away and the start goes on. Under systemd the marker of a live
    /// helper stays, because the start exits and the helper still works.
    /// </summary>
    public static RestoreWait WaitWhileRestoring(string dataPath, TimeSpan limit, TimeSpan? pollInterval = null, bool underSystemd = false)
    {
        var marker = Path.Combine(dataPath, "updates", RestoringFileName);
        var deadline = DateTime.UtcNow + limit;
        var told = false;
        while (File.Exists(marker))
        {
            // Markers are written in one step, so an empty one is left over
            // from an older version or a full disk. It counts as still
            // being written until the limit.
            var content = ReadText(marker);
            if (content == null)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Try(() => File.Delete(marker));
                    return told ? RestoreWait.TimedOut : RestoreWait.StaleRemoved;
                }
                Thread.Sleep(pollInterval ?? TimeSpan.FromMilliseconds(500));
                continue;
            }

            if (!IsRecordedProcessAlive(marker))
            {
                // The helper ended while its marker was there.
                Console.WriteLine("[Sportarr] Removing a restore marker that a stopped update helper left behind");
                Try(() => File.Delete(marker));
                return told ? RestoreWait.WaitedForHelper : RestoreWait.StaleRemoved;
            }
            if (DateTime.UtcNow >= deadline)
            {
                if (underSystemd)
                {
                    Console.WriteLine($"[Sportarr] The update helper still restores after {limit.TotalSeconds:0} seconds. Exiting, so that systemd starts again later");
                    return RestoreWait.TimedOut;
                }
                Console.WriteLine($"[Sportarr] The update helper still restores after {limit.TotalSeconds:0} seconds. Starting anyway");
                Try(() => File.Delete(marker));
                return RestoreWait.TimedOut;
            }
            if (!told)
            {
                Console.WriteLine("[Sportarr] The update helper restores the database. Waiting for it");
                told = true;
            }
            Thread.Sleep(pollInterval ?? TimeSpan.FromMilliseconds(500));
        }
        return told ? RestoreWait.WaitedForHelper : RestoreWait.NoRestore;
    }

    /// <summary>
    /// The first steps of a start. Returns true when the caller must exit
    /// with code 1.
    /// The pid file comes first, so that a helper that restores the
    /// database can pause and stop this process. Then the start waits for a
    /// restore. Under systemd, a start that waited for a live helper exits.
    /// This process may have loaded the program files from before the
    /// restore. systemd then starts the files that are in place now.
    /// Under systemd, a start also exits when the installed version is not
    /// the version of this process and the helper that wrote it still runs.
    /// That happens in a crash loop that loaded the new files just before
    /// the helper put the old ones back. In every other case the file is
    /// old news. It goes away, so that it can never lock out a start after
    /// a manual install.
    /// </summary>
    public static bool PrepareStart(string dataPath, TimeSpan restoreLimit, bool underSystemd, string runningVersion, TimeSpan? pollInterval = null)
    {
        WritePid(dataPath);
        var wait = WaitWhileRestoring(dataPath, restoreLimit, pollInterval, underSystemd);
        if (underSystemd && wait is RestoreWait.WaitedForHelper or RestoreWait.TimedOut)
        {
            Console.WriteLine("[Sportarr] Exiting after the restore, so that systemd starts the restored version");
            return true;
        }

        var installedPath = Path.Combine(dataPath, "updates", InstalledVersionFileName);
        var installed = ReadText(installedPath);
        if (installed != null)
        {
            var mismatch = !string.Equals(installed, runningVersion, StringComparison.OrdinalIgnoreCase);
            if (mismatch && underSystemd && IsHelperRunning(dataPath))
            {
                Console.WriteLine($"[Sportarr] This process is version {runningVersion}, but the update helper has installed version {installed}. Exiting, so that systemd starts the installed version");
                return true;
            }
            if (mismatch)
                Console.WriteLine($"[Sportarr] This process is version {runningVersion}. The update helper last installed version {installed}, but it no longer runs");
            Try(() => File.Delete(installedPath));
        }

        WriteStarting(dataPath);
        return false;
    }

    /// <summary>
    /// Removes the installed-version file once this process runs that
    /// version. The guard above is then no longer needed.
    /// </summary>
    public static void ClearInstalledVersionIfCurrent(string dataPath, string runningVersion)
    {
        var path = Path.Combine(dataPath, "updates", InstalledVersionFileName);
        if (string.Equals(ReadText(path), runningVersion, StringComparison.OrdinalIgnoreCase))
            Try(() => File.Delete(path));
    }

    public static void WritePid(string dataPath)
    {
        Try(() =>
        {
            using var current = Process.GetCurrentProcess();
            var start = current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
            WriteAtomic(Path.Combine(dataPath, "updates", PidFileName), $"{current.Id}\n{start}\n");
        });
    }

    /// <summary>
    /// Writes a small file in one step, with a temporary file and a rename.
    /// A reader never sees a half-written file.
    /// </summary>
    public static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, text);
            // On Windows a reader can hold the target open for a moment.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temp, path, overwrite: true);
                    return;
                }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < MoveAttempts)
                {
                    Thread.Sleep(RetryDelay);
                }
            }
        }
        catch
        {
            try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>Returns the trimmed text, or null when the file is missing, empty or busy.</summary>
    public static string? ReadText(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var text = File.ReadAllText(path).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads "pid" and "start time in UTC ticks" from a marker file and
    /// tells if that process still runs. A file without them counts as a
    /// process that is gone.
    /// </summary>
    public static bool IsRecordedProcessAlive(string markerPath)
    {
        try
        {
            var lines = File.ReadAllLines(markerPath);
            if (lines.Length < 2 || !int.TryParse(lines[0], out var pid) || !long.TryParse(lines[1], out var ticks))
                return false;
            using var process = Process.GetProcessById(pid);
            return !process.HasExited &&
                   Math.Abs(process.StartTime.ToUniversalTime().Ticks - ticks) < TimeSpan.TicksPerSecond * 2;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>True while the update helper that wrote helper.pid still runs.</summary>
    public static bool IsHelperRunning(string dataPath) =>
        IsRecordedProcessAlive(Path.Combine(dataPath, "updates", HelperPidFileName));

    public static void WriteStarting(string dataPath)
    {
        WritePid(dataPath);
        Try(() => WriteAtomic(Path.Combine(dataPath, "updates", StartingFileName), "starting"));
    }

    public static void WriteMigrating(string dataPath) =>
        Try(() =>
        {
            var dir = Path.Combine(dataPath, "updates");
            WriteAtomic(Path.Combine(dir, MigratedFileName), "migrated");
            WriteAtomic(Path.Combine(dir, StartingFileName), "migrating");
        });

    public static void ClearStarting(string dataPath) =>
        Try(() => File.Delete(Path.Combine(dataPath, "updates", StartingFileName)));

    // A marker is a help for the helper. A failure to write it must never
    // stop Sportarr from starting.
    private static void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
