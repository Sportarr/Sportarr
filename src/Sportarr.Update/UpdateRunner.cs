namespace Sportarr.Update;

/// <summary>
/// Installs a staged package. The steps are these.
/// 1. Wait for Sportarr to stop. If it does not stop, change nothing.
/// 2. Back up the files that the package replaces.
/// 3. Copy the package over the install folder.
/// 4. Start Sportarr again and wait for it to answer.
/// 5. If it does not answer, put the backup back and start the old version.
/// Under systemd the order changes. See RunSystemdAsync.
/// </summary>
public sealed class UpdateRunner
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ServiceStopTimeout = TimeSpan.FromSeconds(60);

    private readonly UpdaterOptions _options;
    private readonly IUpdaterHost _host;
    private readonly UpdateLog _log;
    private IStartedApp? _started;
    private bool _migrationSeen;

    public UpdateRunner(UpdaterOptions options, IUpdaterHost host, UpdateLog log)
    {
        _options = options;
        _host = host;
        _log = log;
    }

    public async Task<UpdateResultStatus> RunAsync()
    {
        _log.Write($"Update to {_options.TargetVersion} from {_options.PreviousVersion} starts. Restart mode {_options.Restart}");
        WriteHelperPid();
        return _options.Restart == RestartMode.Systemd
            ? await RunSystemdAsync()
            : await RunStoppedFirstAsync();
    }

    // Process and Windows service mode. The old app must stop before its
    // files change, because Windows locks a running executable. After the
    // old app has stopped, every path ends with an attempt to start an app.
    private async Task<UpdateResultStatus> RunStoppedFirstAsync()
    {
        _log.Write($"Waiting for Sportarr (pid {_options.ParentPid}) to stop");
        if (!await WaitForParentToStopAsync())
        {
            var answers = await _host.PingAsync(_options.HealthUrl);
            return Finish(UpdateResultStatus.Failed, answers
                ? "Sportarr did not stop, and the helper could not stop it. Nothing was changed. The old version still runs."
                : "Sportarr did not stop, and the helper could not stop it. Nothing was changed, but Sportarr does not answer. Restart it by hand.");
        }

        try
        {
            if (!BackupAndApply(out var applyError))
            {
                await TryStartAsync("the old version");
                return Finish(UpdateResultStatus.Failed, $"The update was not installed, because {applyError}. The old version runs.");
            }

            if (await TryStartAsync("the new version"))
                return Finish(UpdateResultStatus.Succeeded, $"Sportarr {_options.TargetVersion} is running.");

            _log.Write("The new version did not answer. Rolling back");
            await StopAppAsync();
            if (!AppHasStopped())
            {
                // A second start now would run two copies on one database.
                _log.Write("The new version did not stop. The helper starts nothing");
                return Finish(UpdateResultStatus.Failed,
                    $"Sportarr {_options.TargetVersion} did not answer and did not stop. Nothing was rolled back. Stop Sportarr by hand, then start it again.");
            }
            var (status, message) = RollBack(() => true);
            var upAgain = await TryStartAsync(status == UpdateResultStatus.RolledBack ? "the old version" : "the installed version");
            if (!upAgain)
                message += " Sportarr did not answer after that either. Start it by hand.";
            return Finish(status, message);
        }
        catch (Exception ex)
        {
            _log.Write($"The update stopped with an error: {ex}");
            await TryStartAsync("the installed version");
            return Finish(UpdateResultStatus.Failed, $"The update stopped with an error: {ex.Message.TrimEnd('.')}.");
        }
    }

    // Under systemd the service manager starts Sportarr again after it exits
    // with a failure code. The files must be in place before that, so the
    // swap happens while the old app still runs. Linux and macOS allow this,
    // because the running process keeps the old files open. The app waits
    // for the swapped signal. "failed" makes it keep running.
    private async Task<UpdateResultStatus> RunSystemdAsync()
    {
        var signalled = false;
        try
        {
            if (!BackupAndApply(out var applyError))
            {
                Signal("failed");
                signalled = true;
                return Finish(UpdateResultStatus.Failed, $"The update was not installed, because {applyError}. The old version runs.");
            }

            ClearStartingMarker();
            ClearMigratedMarker();
            Signal("ok");
            signalled = true;

            // Sportarr can take a while to read the signal and to check that
            // it is not busy. The exit wait starts only when it says that it
            // stops now, or when it has already gone.
            _log.Write("Files are in place. Waiting for Sportarr to say that it stops");
            var stopping = await WaitForStoppingSignalAsync();
            if (stopping != "stop")
            {
                // Not a failure of this version. Automatic updates try it
                // again later.
                var (keptStatus, keptMessage) = await SystemdRollBackAsync();
                return Finish(keptStatus == UpdateResultStatus.RolledBack ? UpdateResultStatus.Cancelled : UpdateResultStatus.Failed, stopping == "cancel"
                    ? $"Sportarr was busy and kept running, so the update was not installed. {keptMessage}"
                    : $"Sportarr did not say that it stops, so the update was not installed. {keptMessage}");
            }

            _log.Write($"Waiting for Sportarr (pid {_options.ParentPid}) to stop");
            if (!await _host.WaitForExitAsync(_options.Parent, _options.ExitTimeout))
            {
                var (_, rollbackMessage) = await SystemdRollBackAsync();
                return Finish(UpdateResultStatus.Failed,
                    $"Sportarr did not stop within {_options.ExitTimeout.TotalSeconds:0} seconds. {rollbackMessage}");
            }

            _log.Write("Waiting for systemd to start the new version");
            if (await WaitForAppAsync(null))
                return Finish(UpdateResultStatus.Succeeded, $"Sportarr {_options.TargetVersion} is running.");

            _log.Write("The new version did not answer. Rolling back");
            var (status, message) = await SystemdRollBackAsync();
            if (status == UpdateResultStatus.RolledBack && !await WaitForAppAsync(null))
            {
                _log.Write("Sportarr did not answer after the rollback");
                return Finish(UpdateResultStatus.Failed,
                    $"{message} Sportarr did not answer after that. If systemd stopped trying, start the service by hand.");
            }
            return Finish(status, message);
        }
        catch (Exception ex)
        {
            _log.Write($"The update stopped with an error: {ex}");
            if (!signalled)
                Signal("failed");
            return Finish(UpdateResultStatus.Failed, $"The update stopped with an error: {ex.Message.TrimEnd('.')}.");
        }
    }

    /// <summary>
    /// Every rollback after the swap goes through here. systemd can have
    /// started the new version at any time after the swap. That process is
    /// paused first, so that it writes nothing to the database. Then the
    /// files and the database go back, and the process is stopped. systemd
    /// then starts the old files. Stopping it before the rollback could let
    /// systemd start the new files again.
    /// </summary>
    private async Task<(UpdateResultStatus Status, string Message)> SystemdRollBackAsync()
    {
        var newApp = AppProcessId.Read(_options.AppPidPath);
        if (newApp != null && newApp.Pid == _options.ParentPid)
            newApp = null;
        var paused = newApp != null && _host.PauseApp(newApp);
        var pausedApps = new List<AppProcessId>();
        if (paused)
            pausedApps.Add(newApp!);

        // systemd can start another process while the rollback runs. That
        // one is paused too. A live process that cannot be paused can still
        // write, so it makes the restore unsafe.
        bool StillSafe()
        {
            var latest = AppProcessId.Read(_options.AppPidPath);
            if (latest == null || latest.Pid == _options.ParentPid || pausedApps.Any(p => p.Pid == latest.Pid))
                return true;
            if (!_host.IsRunning(latest))
                return true;
            if (!_host.PauseApp(latest))
                return false;
            pausedApps.Add(latest);
            return true;
        }

        var result = RollBack(() => newApp == null || paused || !_host.IsRunning(newApp), StillSafe);
        ClearStartingMarker();
        ClearMigratedMarker();

        var toStop = pausedApps.ToList();
        if (newApp != null && !toStop.Any(p => p.Pid == newApp.Pid))
            toStop.Add(newApp);
        var latestApp = AppProcessId.Read(_options.AppPidPath);
        if (latestApp != null && latestApp.Pid != _options.ParentPid && !toStop.Any(p => p.Pid == latestApp.Pid))
            toStop.Add(latestApp);
        foreach (var app in toStop)
        {
            if (!await _host.KillAppAsync(app))
            {
                _log.Write($"Could not stop pid {app.Pid}");
                if (pausedApps.Contains(app))
                    _host.ResumeApp(app);
            }
        }
        return result;
    }

    /// <summary>
    /// Waits until the app answers. While the app reports that it migrates
    /// the database and still runs, it gets more time. Returns false when
    /// the app stops, or when the time is over.
    /// </summary>
    internal async Task<bool> WaitForAppAsync(IStartedApp? app)
    {
        var start = _host.UtcNow;
        var toldMigrating = false;
        var migratedThisStart = false;
        while (true)
        {
            if (await _host.PingAsync(_options.HealthUrl))
                return true;

            var migrating = StartingMarker() == "migrating";
            _migrationSeen |= migrating;
            migratedThisStart |= migrating;
            if (migrating && !toldMigrating)
            {
                _log.Write("Sportarr migrates the database. The helper waits for it");
                toldMigrating = true;
            }

            if (app?.HasExited == true)
            {
                _log.Write("The app stopped before it answered");
                return false;
            }

            // systemd gives up after a crash loop. Waiting longer is useless then.
            if (app == null && _options.Restart == RestartMode.Systemd && !string.IsNullOrEmpty(_options.ServiceName) &&
                await _host.SystemdStateAsync(_options.ServiceName) == "failed")
            {
                _log.Write($"systemd reports {_options.ServiceName} as failed");
                return false;
            }

            var elapsed = _host.UtcNow - start;
            if (elapsed >= _options.HealthTimeout)
            {
                // The rest of a start after a migration also gets the longer
                // time, as long as the same app still runs.
                if (!(migratedThisStart && elapsed < _options.MigrationTimeout && AppRuns(app)))
                {
                    _log.Write($"No answer from {_options.HealthUrl} after {elapsed.TotalSeconds:0} seconds");
                    return false;
                }
            }

            await _host.DelayAsync(PollInterval);
        }
    }

    // Reads the stopping signal that Sportarr writes. "stop" means it stops
    // now. "cancel" means it keeps running. Null means it did not answer.
    private async Task<string?> WaitForStoppingSignalAsync()
    {
        var deadline = _host.UtcNow + _options.HandoverTimeout;
        while (_host.UtcNow < deadline)
        {
            // An empty or unreadable file is not an answer yet.
            var signal = AtomicFile.Read(_options.StoppingSignalPath);
            if (signal != null)
                return signal;

            // Sportarr has gone without a word, so systemd can start the new
            // files at any moment. That is the same as "stop".
            if (!_host.IsRunning(_options.Parent))
            {
                _log.Write("Sportarr has stopped without the signal. The update goes on");
                return "stop";
            }
            await _host.DelayAsync(TimeSpan.FromSeconds(1));
        }
        return null;
    }

    private bool AppRuns(IStartedApp? app)
    {
        if (app != null)
            return !app.HasExited;
        var pid = AppProcessId.Read(_options.AppPidPath);
        return pid != null && _host.IsRunning(pid);
    }

    /// <summary>
    /// Puts the old files back. When the new version has migrated the
    /// database, the database goes back too. Never throws.
    /// The order matters. First the copy of the old database is taken out of
    /// the backup and checked. If that fails, nothing goes back, because the
    /// old version must not run on a newer schema. Then the restoring marker
    /// keeps a new start away from the database. Then the old files and the
    /// old database go back, and the marker goes away.
    /// "stillSafe" runs after the marker is written. It returns false when
    /// a process that cannot be paused has started since.
    /// </summary>
    private (UpdateResultStatus Status, string Message) RollBack(Func<bool> appStopped, Func<bool>? stillSafe = null)
    {
        var failed = $"Sportarr {_options.TargetVersion} did not start";
        var notStopped = $"{failed}, and it changed the database. It did not stop, so the database and the files were not rolled back. Stop Sportarr and restore {DatabaseBackupName} from System > Backup.";
        try
        {
            // The durable marker also catches a migration that was over
            // before the helper looked.
            var migrated = _migrationSeen || File.Exists(_options.MigratedMarkerPath) || StartingMarker() == "migrating";
            if (!migrated)
            {
                _log.Write("Putting the old files back");
                PutOldFilesBack();
                return (UpdateResultStatus.RolledBack, $"{failed}, so the update was rolled back.");
            }

            if (!appStopped())
            {
                _log.Write("The new version still runs, so the database cannot be restored safely");
                return (UpdateResultStatus.Failed, notStopped);
            }

            var problem = DatabaseRestorer.Prepare(_options);
            if (problem != null)
            {
                _log.Write($"The database cannot be restored, because {problem}. The files stay on the new version");
                return (UpdateResultStatus.Failed,
                    $"{failed}, and it had already changed the database. The old database cannot be restored, because {problem}. The old version cannot run on the changed database, so the update was not rolled back. Restore a backup from System > Backup, or start the new version again.");
            }

            WriteRestoringMarker();
            try
            {
                OnRestoringMarkerWritten?.Invoke();
                if (stillSafe != null && !stillSafe())
                {
                    DatabaseRestorer.Discard(_options);
                    _log.Write("A new Sportarr process started and cannot be paused. Nothing goes back");
                    return (UpdateResultStatus.Failed, notStopped);
                }

                _log.Write("Putting the old files back");
                PutOldFilesBack();
                OnFilesRolledBack?.Invoke();

                // A process that started in the meantime makes the restore
                // unsafe. The new files then go back in place, because the new
                // version can run on either database. The old version cannot.
                if (stillSafe != null && !stillSafe())
                    return KeepNewVersion(failed, committed: false);

                _log.Write($"Restoring the database from {DatabaseBackupName}");
                DatabaseRestorer.Commit(_options);
                OnDatabaseRestored?.Invoke();

                if (stillSafe != null && !stillSafe())
                    return KeepNewVersion(failed, committed: true);
            }
            finally
            {
                ClearRestoringMarker();
            }
            return (UpdateResultStatus.RolledBack,
                $"{failed}, so the update was rolled back. The database was restored from the backup made before the update.");
        }
        catch (Exception ex)
        {
            _log.Write($"The rollback failed: {ex}");
            DatabaseRestorer.Discard(_options);
            return (UpdateResultStatus.Failed,
                $"{failed}, and the rollback failed: {ex.Message.TrimEnd('.')}. The backup of the old files is in {_options.BackupDir}.");
        }
    }

    // Test hooks for the steps of a restore.
    internal Action? OnRestoringMarkerWritten { get; set; }
    internal Action? OnFilesRolledBack { get; set; }
    internal Action? OnDatabaseRestored { get; set; }

    private (UpdateResultStatus Status, string Message) KeepNewVersion(string failed, bool committed)
    {
        DatabaseRestorer.Discard(_options);
        _log.Write("A new Sportarr process started during the restore and cannot be paused. The new files go back in place");
        ApplyNewFiles();
        return (UpdateResultStatus.Failed, committed
            ? $"{failed}. A new Sportarr process started during the restore, so version {_options.TargetVersion} stays installed. The database is the one from before the update. Version {_options.TargetVersion} failed to start before, so it can fail again. Restart Sportarr now. If it does not start, install version {_options.PreviousVersion} again from the releases page and restore {DatabaseBackupName} from System > Backup."
            : $"{failed}. A new Sportarr process started during the restore, so version {_options.TargetVersion} stays installed on the changed database. Restart Sportarr now. It may not start, because version {_options.TargetVersion} failed to start before. If it does not start, install version {_options.PreviousVersion} again from the releases page and restore {DatabaseBackupName} from System > Backup.");
    }

    // The marker names this helper process, so that a start can tell a live
    // restore from a marker that a crashed helper left behind.
    private void WriteRestoringMarker()
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        AtomicFile.Write(_options.RestoringMarkerPath,
            $"{self.Id}\n{self.StartTime.ToUniversalTime().Ticks}\n");
    }

    // Sportarr skips its clean-up of the update folders while this
    // process runs.
    private void WriteHelperPid()
    {
        try
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            Directory.CreateDirectory(Path.GetDirectoryName(_options.HelperPidPath)!);
            AtomicFile.Write(_options.HelperPidPath, $"{self.Id}\n{self.StartTime.ToUniversalTime().Ticks}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write($"Could not write {_options.HelperPidPath}: {ex.Message}");
        }
    }

    private void ClearRestoringMarker()
    {
        try { File.Delete(_options.RestoringMarkerPath); }
        catch (IOException ex) { _log.Write($"Could not remove {_options.RestoringMarkerPath}: {ex.Message}"); }
    }

    private string DatabaseBackupName =>
        DatabaseRestorer.BackupPath(_options) is { Length: > 0 } path ? path : "the latest backup";

    /// <summary>
    /// Waits for the parent to stop. After the normal timeout it waits a bit
    /// longer. Then it stops a parent that still runs. Returns false only
    /// when the parent still runs at the end.
    /// </summary>
    private async Task<bool> WaitForParentToStopAsync()
    {
        if (await _host.WaitForExitAsync(_options.Parent, _options.ExitTimeout))
            return true;

        _log.Write($"Sportarr did not stop within {_options.ExitTimeout.TotalSeconds:0} seconds. Waiting {_options.ExtraExitWait.TotalSeconds:0} seconds more");
        if (await _host.WaitForExitAsync(_options.Parent, _options.ExtraExitWait))
            return true;

        _log.Write($"Stopping Sportarr (pid {_options.ParentPid})");
        if (!await _host.KillAppAsync(_options.Parent))
            _log.Write("The helper could not stop Sportarr");
        return await _host.WaitForExitAsync(_options.Parent, TimeSpan.FromSeconds(10));
    }

    private bool AppHasStopped()
    {
        if (_started != null && !_started.HasExited)
            return false;
        var pid = AppProcessId.Read(_options.AppPidPath);
        return pid == null || pid.Pid == _options.ParentPid || !_host.IsRunning(pid);
    }

    private bool BackupAndApply(out string error)
    {
        error = "";
        try
        {
            _log.Write($"Backing up {_options.InstallDir} to {_options.BackupDir}");
            PackageSwapper.Backup(_options.StagingDir, _options.InstallDir, _options.BackupDir);
        }
        catch (Exception ex)
        {
            // The install folder has not changed yet.
            error = $"the backup failed: {ex.Message}";
            _log.Write($"Backup failed: {ex.Message}");
            return false;
        }

        try
        {
            _log.Write($"Copying {_options.StagingDir} to {_options.InstallDir}");
            ApplyNewFiles();
            return true;
        }
        catch (Exception ex)
        {
            error = $"the copy failed: {ex.Message}";
            _log.Write($"Copy failed: {ex.Message}. Restoring the backup");
            try
            {
                PutOldFilesBack();
            }
            catch (Exception rollbackEx)
            {
                _log.Write($"Restoring the backup failed too: {rollbackEx}");
                error += $". Restoring the old files failed too: {rollbackEx.Message}";
            }
            return false;
        }
    }

    // Starts the app and waits for it. A failure to start is logged and
    // counts as no answer.
    private async Task<bool> TryStartAsync(string which)
    {
        try
        {
            ClearStartingMarker();
            ClearMigratedMarker();
            if (_options.Restart == RestartMode.WindowsService)
            {
                _started = null;
                if (!await _host.StartServiceAsync(_options.ServiceName))
                    _log.Write($"The {_options.ServiceName} service did not start");
            }
            else
            {
                _started = _host.StartApp(_options.AppExecutable, _options.AppArguments, _options.WorkingDir);
            }

            return await WaitForAppAsync(_started);
        }
        catch (Exception ex)
        {
            _log.Write($"Could not start {which}: {ex.Message}");
            return false;
        }
    }

    private async Task StopAppAsync()
    {
        try
        {
            if (_options.Restart == RestartMode.WindowsService)
                await _host.StopServiceAsync(_options.ServiceName, ServiceStopTimeout);
            if (_started != null)
                await _started.KillAsync();
            var pid = AppProcessId.Read(_options.AppPidPath);
            if (pid != null && pid.Pid != _options.ParentPid)
                await _host.KillAppAsync(pid);
        }
        catch (Exception ex)
        {
            _log.Write($"Stopping the new version failed: {ex.Message}");
        }
    }

    private string? StartingMarker()
    {
        return AtomicFile.Read(_options.StartingMarkerPath);
    }

    private void ClearStartingMarker()
    {
        try { File.Delete(_options.StartingMarkerPath); }
        catch (IOException ex) { _log.Write($"Could not remove {_options.StartingMarkerPath}: {ex.Message}"); }
    }

    private void ApplyNewFiles()
    {
        PackageSwapper.Apply(_options.StagingDir, _options.InstallDir);
        WriteInstalledVersion(_options.TargetVersion);
    }

    private void PutOldFilesBack()
    {
        PackageSwapper.Rollback(_options.BackupDir, _options.InstallDir);
        WriteInstalledVersion(_options.PreviousVersion);
    }

    // Tells a start which version is in the install folder. Under systemd a
    // process that loaded other files exits, so that systemd starts these.
    private void WriteInstalledVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
            return;
        try { AtomicFile.Write(_options.InstalledVersionPath, version); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write($"Could not write {_options.InstalledVersionPath}: {ex.Message}");
        }
    }

    private void ClearMigratedMarker()
    {
        try { File.Delete(_options.MigratedMarkerPath); }
        catch (IOException ex) { _log.Write($"Could not remove {_options.MigratedMarkerPath}: {ex.Message}"); }
    }

    private void Signal(string value)
    {
        try
        {
            AtomicFile.Write(_options.SwappedSignalPath, value);
        }
        catch (Exception ex)
        {
            _log.Write($"Could not write the signal file: {ex.Message}");
        }
    }

    private UpdateResultStatus Finish(UpdateResultStatus status, string message)
    {
        // The installed-version guard only matters while this helper runs.
        try { File.Delete(_options.InstalledVersionPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write($"Could not remove {_options.InstalledVersionPath}: {ex.Message}");
        }

        _log.Write($"Result {status}. {message}");
        try
        {
            UpdateResultFile.Write(_options.ResultPath, status, _options.TargetVersion, _options.PreviousVersion, message, _host.UtcNow);
        }
        catch (Exception ex)
        {
            _log.Write($"Could not write the result file: {ex.Message}");
        }
        return status;
    }
}
