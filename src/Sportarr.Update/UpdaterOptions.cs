namespace Sportarr.Update;

public enum RestartMode
{
    /// <summary>Start the app executable again with the same arguments.</summary>
    Process,

    /// <summary>The app runs as a systemd service that restarts it after a failure exit.</summary>
    Systemd,

    /// <summary>The app runs as a Windows service. The helper starts the service again.</summary>
    WindowsService
}

/// <summary>
/// The command line the app gives the helper. Each value is a pair such as
/// "--pid 1234". "--app-arg" can occur more than once.
/// </summary>
public sealed class UpdaterOptions
{
    public int ParentPid { get; init; }

    // The start time of the parent in UTC ticks. Zero means unknown. It
    // makes sure that a reused process id is never taken for Sportarr.
    public long ParentStartTicks { get; init; }
    public AppProcessId Parent => new(ParentPid, ParentStartTicks);
    public string InstallDir { get; init; } = "";
    public string StagingDir { get; init; } = "";
    public string DataDir { get; init; } = "";
    public string BackupDir { get; init; } = "";
    public RestartMode Restart { get; init; } = RestartMode.Process;
    public string ServiceName { get; init; } = "Sportarr";
    public string AppExecutable { get; init; } = "";
    public IReadOnlyList<string> AppArguments { get; init; } = Array.Empty<string>();
    public string WorkingDir { get; init; } = "";
    public string HealthUrl { get; init; } = "";
    public string DatabaseBackup { get; init; } = "";
    public string DatabaseProvider { get; init; } = "sqlite";
    public string DatabasePath { get; init; } = "";
    public string TargetVersion { get; init; } = "";
    public string PreviousVersion { get; init; } = "";
    public TimeSpan ExitTimeout { get; init; } = TimeSpan.FromMinutes(2);

    // After ExitTimeout the helper waits this much longer. Then it stops a
    // parent that still runs and goes on with the update.
    public TimeSpan ExtraExitWait { get; init; } = TimeSpan.FromMinutes(1);

    // Under systemd, how long the helper waits for Sportarr to say that it
    // stops. Sportarr waits up to 32 minutes for a slow helper, so this is longer.
    public TimeSpan HandoverTimeout { get; init; } = TimeSpan.FromMinutes(35);
    // A start can include database migrations, which can take a while on a
    // large library. While the app reports "migrating" and still runs, the
    // helper waits up to MigrationTimeout.
    public TimeSpan HealthTimeout { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan MigrationTimeout { get; init; } = TimeSpan.FromMinutes(60);

    public string LogPath => Path.Combine(DataDir, "logs", "update.log");
    public string ResultPath => Path.Combine(DataDir, "updates", UpdateResultFile.FileName);
    public string SwappedSignalPath => Path.Combine(DataDir, "updates", "swapped");
    public string StoppingSignalPath => Path.Combine(DataDir, "updates", "stopping");
    public string HelperPidPath => Path.Combine(DataDir, "updates", "helper.pid");
    public string InstalledVersionPath => Path.Combine(DataDir, "updates", "installed-version");
    public string StartingMarkerPath => Path.Combine(DataDir, "updates", "starting");
    public string AppPidPath => Path.Combine(DataDir, "updates", "app.pid");

    // The app writes this when it applies migrations and never removes it.
    // The helper removes it before each start that it makes.
    public string MigratedMarkerPath => Path.Combine(DataDir, "updates", "migrated");

    // The helper writes this while it restores the database. The app waits
    // at startup while it exists, so it does not open a half-restored file.
    public string RestoringMarkerPath => Path.Combine(DataDir, "updates", "restoring");

    // Sportarr writes a newer backup path here when it hands over late.
    public string DatabaseBackupOverridePath => Path.Combine(DataDir, "updates", "db-backup-path");

    public static UpdaterOptions Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var appArgs = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Count)
                throw new ArgumentException($"Unexpected argument: {key}");

            var value = args[++i];
            if (string.Equals(key, "--app-arg", StringComparison.OrdinalIgnoreCase))
                appArgs.Add(value);
            else
                values[key] = value;
        }

        string Required(string key) => values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : throw new ArgumentException($"Missing argument: {key}");
        string Optional(string key, string fallback) => values.TryGetValue(key, out var v) ? v : fallback;

        var dataDir = Path.GetFullPath(Required("--data-dir"));
        var installDir = Path.GetFullPath(Required("--install-dir"));
        return new UpdaterOptions
        {
            ParentPid = int.Parse(Required("--pid"), System.Globalization.CultureInfo.InvariantCulture),
            ParentStartTicks = long.Parse(Optional("--pid-start", "0"), System.Globalization.CultureInfo.InvariantCulture),
            InstallDir = installDir,
            WorkingDir = Path.GetFullPath(Optional("--working-dir", installDir)),
            StagingDir = Path.GetFullPath(Required("--staging-dir")),
            DataDir = dataDir,
            BackupDir = Path.GetFullPath(Optional("--backup-dir", Path.Combine(dataDir, "updates", "backup"))),
            Restart = Enum.Parse<RestartMode>(Optional("--restart", nameof(RestartMode.Process)), ignoreCase: true),
            ServiceName = Optional("--service-name", "Sportarr"),
            AppExecutable = Path.GetFullPath(Required("--app-exe")),
            AppArguments = appArgs,
            HealthUrl = Required("--health-url"),
            DatabaseBackup = Optional("--db-backup", ""),
            DatabaseProvider = Optional("--db-provider", "sqlite").ToLowerInvariant(),
            DatabasePath = Optional("--db-path", Path.Combine(dataDir, "sportarr.db")),
            TargetVersion = Optional("--version", ""),
            PreviousVersion = Optional("--previous-version", ""),
            ExitTimeout = TimeSpan.FromSeconds(int.Parse(Optional("--exit-timeout", "120"), System.Globalization.CultureInfo.InvariantCulture)),
            HandoverTimeout = TimeSpan.FromSeconds(int.Parse(Optional("--handover-timeout", "2100"), System.Globalization.CultureInfo.InvariantCulture)),
            ExtraExitWait = TimeSpan.FromSeconds(int.Parse(Optional("--extra-exit-wait", "60"), System.Globalization.CultureInfo.InvariantCulture)),
            HealthTimeout = TimeSpan.FromSeconds(int.Parse(Optional("--health-timeout", "600"), System.Globalization.CultureInfo.InvariantCulture)),
            MigrationTimeout = TimeSpan.FromSeconds(int.Parse(Optional("--migration-timeout", "3600"), System.Globalization.CultureInfo.InvariantCulture))
        };
    }
}
