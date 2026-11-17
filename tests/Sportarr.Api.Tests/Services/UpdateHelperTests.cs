using System.IO.Compression;
using FluentAssertions;
using Sportarr.Update;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The update helper works on temporary folders here. A fake host stands
/// in for processes, services, the clock and the health check.
/// </summary>
public class UpdateHelperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"update-helper-{Guid.NewGuid():N}");
    private readonly string _install;
    private readonly string _staging;
    private readonly string _data;
    private readonly string _workingDir;
    private readonly FakeHost _host = new();

    public UpdateHelperTests()
    {
        _install = Path.Combine(_root, "install");
        _staging = Path.Combine(_root, "data", "updates", "staging");
        _data = Path.Combine(_root, "data");
        _workingDir = Path.Combine(_root, "somewhere-else");
        Directory.CreateDirectory(_workingDir);

        Write(_install, "Sportarr", "old app");
        Write(_install, "Sportarr.dll", "old dll");
        Write(_install, "wwwroot/index.html", "old page");
        Write(_install, "data/config.xml", "user settings");
        Write(_install, "extra-file.txt", "not in the package");

        Write(_staging, "Sportarr", "new app");
        Write(_staging, "Sportarr.dll", "new dll");
        Write(_staging, "wwwroot/index.html", "new page");
        Write(_staging, "wwwroot/new-asset.js", "new asset");
        Write(_staging, "Sportarr.Update/Sportarr.Update", "new helper");

        PackageSwapper.RetryDelay = TimeSpan.FromMilliseconds(1);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    // File swap

    [Fact]
    public void Swap_replaces_package_files_and_keeps_the_rest()
    {
        var backup = Path.Combine(_data, "updates", "backup");

        PackageSwapper.Backup(_staging, _install, backup);
        PackageSwapper.Apply(_staging, _install);

        Read(_install, "Sportarr").Should().Be("new app");
        Read(_install, "wwwroot/new-asset.js").Should().Be("new asset");
        Read(_install, "data/config.xml").Should().Be("user settings");
        Read(_install, "extra-file.txt").Should().Be("not in the package");
        Read(backup, "Sportarr").Should().Be("old app");
        File.Exists(Path.Combine(backup, "data", "config.xml")).Should().BeFalse("only replaced files are backed up");
        File.ReadAllLines(Path.Combine(backup, PackageSwapper.NewFilesManifest))
            .Should().BeEquivalentTo(Path.Combine("Sportarr.Update", "Sportarr.Update"), Path.Combine("wwwroot", "new-asset.js"));
    }

    [Fact]
    public void Rollback_restores_the_old_files_and_removes_new_ones()
    {
        var backup = Path.Combine(_data, "updates", "backup");
        PackageSwapper.Backup(_staging, _install, backup);
        PackageSwapper.Apply(_staging, _install);

        PackageSwapper.Rollback(backup, _install);

        Read(_install, "Sportarr").Should().Be("old app");
        Read(_install, "wwwroot/index.html").Should().Be("old page");
        File.Exists(Path.Combine(_install, "wwwroot", "new-asset.js")).Should().BeFalse();
        File.Exists(Path.Combine(_install, "Sportarr.Update", "Sportarr.Update")).Should().BeFalse();
        Read(_install, "data/config.xml").Should().Be("user settings");
    }

    [Fact]
    public void Swap_keeps_the_executable_bit()
    {
        if (OperatingSystem.IsWindows())
            return;

        var staged = Path.Combine(_staging, "Sportarr");
        File.SetUnixFileMode(staged, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(Path.Combine(_install, "Sportarr"), UnixFileMode.UserRead | UnixFileMode.UserWrite);

        PackageSwapper.Backup(_staging, _install, Path.Combine(_data, "updates", "backup"));
        PackageSwapper.Apply(_staging, _install);

        File.GetUnixFileMode(Path.Combine(_install, "Sportarr")).Should().HaveFlag(UnixFileMode.UserExecute);
    }

    [Fact]
    public void Rollback_refuses_an_incomplete_backup()
    {
        var backup = Path.Combine(_root, "empty-backup");
        Directory.CreateDirectory(backup);

        var act = () => PackageSwapper.Rollback(backup, _install);

        act.Should().Throw<InvalidOperationException>();
        Read(_install, "Sportarr").Should().Be("old app");
    }

    [Fact]
    public void A_locked_file_is_tried_again()
    {
        var calls = 0;

        PackageSwapper.Retry(() =>
        {
            calls++;
            if (calls < 3)
                throw new IOException("The process cannot access the file because it is being used by another process.");
        });

        calls.Should().Be(3);
    }

    [Fact]
    public void A_file_that_stays_locked_fails_after_the_last_try()
    {
        var calls = 0;

        var act = () => PackageSwapper.Retry(() =>
        {
            calls++;
            throw new IOException("locked");
        });

        act.Should().Throw<IOException>();
        calls.Should().Be(PackageSwapper.Attempts);
    }

    // Process mode

    [Fact]
    public async Task Installs_restarts_in_the_original_folder_and_reports_success()
    {
        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.Succeeded);
        Read(_install, "Sportarr").Should().Be("new app");
        var start = _host.Started.Should().ContainSingle().Subject;
        start.Executable.Should().Be(Path.Combine(_install, "Sportarr"));
        start.Arguments.Should().Equal("-data", Path.Combine(_root, "data"));
        start.WorkingDirectory.Should().Be(_workingDir);
        ResultFile().Should().Contain("status=Succeeded").And.Contain("version=4.1.13.1130");
    }

    [Fact]
    public async Task Changes_nothing_when_Sportarr_does_not_stop()
    {
        _host.ParentExits = false;

        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.Failed);
        Read(_install, "Sportarr").Should().Be("old app");
        _host.Started.Should().BeEmpty();
        _host.KilledApps.Should().Contain(new AppProcessId(1234, 99));
        ResultFile().Should().Contain("status=Failed").And.Contain("could not stop it");
    }

    [Fact]
    public async Task Rolls_back_and_starts_the_old_version_when_the_new_one_does_not_answer()
    {
        _host.Ping = h => h.Started.Count >= 2;

        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.RolledBack);
        Read(_install, "Sportarr").Should().Be("old app");
        File.Exists(Path.Combine(_install, "wwwroot", "new-asset.js")).Should().BeFalse();
        _host.Started.Should().HaveCount(2);
        _host.StartedApps[0].Killed.Should().BeTrue("the new version is stopped and waited for before the rollback");
        ResultFile().Should().Contain("status=RolledBack").And.Contain("4.1.13.1130 did not start");
    }

    [Fact]
    public async Task Waits_for_a_slow_migration_and_does_not_stop_the_new_version()
    {
        // The new version migrates for 25 minutes, longer than the normal
        // wait of 10 minutes, then answers.
        // The marker goes back to "starting" for the rest of the start.
        var migrationEnds = _host.UtcNow.AddMinutes(25);
        var answersAt = _host.UtcNow.AddMinutes(26);
        _host.OnStart = _ => WriteMarker("migrating");
        _host.Ping = h =>
        {
            if (h.UtcNow >= migrationEnds)
                WriteMarker("starting");
            return h.UtcNow >= answersAt;
        };

        var status = await Runner(RestartMode.Process, healthTimeout: TimeSpan.FromMinutes(10)).RunAsync();

        status.Should().Be(UpdateResultStatus.Succeeded);
        _host.StartedApps.Should().ContainSingle().Which.Killed.Should().BeFalse();
        Read(_install, "Sportarr").Should().Be("new app");
    }

    [Fact]
    public async Task Gives_up_on_a_migration_that_never_ends_and_restores_the_database()
    {
        WriteDatabaseBackup("old database");
        Write(_data, "sportarr.db", "migrated database");
        Write(_data, "sportarr.db-wal", "migrated journal");
        _host.OnStart = h => { if (h.Started.Count == 1) WriteMarker("migrating"); };
        _host.Ping = h => h.Started.Count >= 2;

        var status = await Runner(RestartMode.Process, healthTimeout: TimeSpan.FromMinutes(10), migrationTimeout: TimeSpan.FromMinutes(60)).RunAsync();

        status.Should().Be(UpdateResultStatus.RolledBack);
        (_host.UtcNow - FakeHost.Start).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(60));
        Read(_data, "sportarr.db").Should().Contain("old database");
        File.Exists(Path.Combine(_data, "sportarr.db-wal")).Should().BeFalse();
        Read(_install, "Sportarr").Should().Be("old app");
        ResultFile().Should().Contain("database was restored");
    }

    [Fact]
    public async Task Restores_the_SQLite_database_when_a_crashed_new_version_had_migrated()
    {
        WriteDatabaseBackup("old database");
        Write(_data, "sportarr.db", "migrated database");
        _host.OnStart = h =>
        {
            if (h.Started.Count == 1)
            {
                WriteMarker("migrating");
                h.StartedApps[0].Exit();
            }
        };
        _host.Ping = h => h.Started.Count >= 2;

        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.RolledBack);
        Read(_data, "sportarr.db").Should().Contain("old database");
        Read(_install, "Sportarr").Should().Be("old app");
    }

    [Fact]
    public async Task Leaves_the_database_alone_when_the_new_version_did_not_migrate()
    {
        WriteDatabaseBackup("old database");
        Write(_data, "sportarr.db", "current database");
        _host.Ping = h => h.Started.Count >= 2;

        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.RolledBack);
        Read(_data, "sportarr.db").Should().Be("current database");
    }

    [Fact]
    public async Task Refuses_to_roll_back_across_a_Postgres_migration_but_still_starts_Sportarr()
    {
        _host.OnStart = h => { if (h.Started.Count == 1) WriteMarker("migrating"); };
        _host.Ping = _ => false;

        var status = await Runner(RestartMode.Process, provider: "postgres").RunAsync();

        status.Should().Be(UpdateResultStatus.Failed);
        Read(_install, "Sportarr").Should().Be("new app", "the old version cannot run on the migrated schema");
        _host.Started.Should().HaveCount(2, "something is always started again");
        ResultFile().Should().Contain("already changed the database");
    }

    [Fact]
    public async Task A_failed_rollback_is_reported_and_Sportarr_is_still_started()
    {
        _host.Ping = h => h.Started.Count >= 2;
        _host.OnStart = h =>
        {
            // The backup disappears, so the rollback cannot work.
            if (h.Started.Count == 1)
                Directory.Delete(Path.Combine(_data, "updates", "backup"), recursive: true);
        };

        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.Failed);
        _host.Started.Should().HaveCount(2);
        ResultFile().Should().Contain("the rollback failed");
    }

    [Fact]
    public async Task Starts_the_old_version_when_the_copy_fails()
    {
        // A folder where the package has a file makes the copy fail.
        Directory.CreateDirectory(Path.Combine(_install, "wwwroot", "new-asset.js"));

        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.Failed);
        Read(_install, "Sportarr").Should().Be("old app");
        _host.Started.Should().ContainSingle();
    }

    [Fact]
    public async Task Restores_the_database_after_a_quick_migration_and_a_crash()
    {
        // The migration was over before the helper looked. Only the
        // durable marker tells that the database changed.
        WriteDatabaseBackup("old database");
        Write(_data, "sportarr.db", "migrated database");
        _host.OnStart = h =>
        {
            if (h.Started.Count == 1)
            {
                Write(_data, "updates/migrated", "migrated");
                h.StartedApps[0].Exit();
            }
        };
        _host.Ping = h => h.Started.Count >= 2;

        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.RolledBack);
        Read(_data, "sportarr.db").Should().Contain("old database");
        File.Exists(Path.Combine(_data, "updates", "migrated")).Should().BeFalse("the helper clears it before it starts the old version");
    }

    [Fact]
    public async Task An_old_migrated_marker_does_not_restore_the_database()
    {
        WriteDatabaseBackup("old database");
        Write(_data, "sportarr.db", "current database");
        Write(_data, "updates/migrated", "from an earlier start");
        _host.Ping = h => h.Started.Count >= 2;

        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.RolledBack);
        Read(_data, "sportarr.db").Should().Be("current database");
    }

    [Fact]
    public async Task Does_not_restore_the_database_while_the_new_version_still_runs()
    {
        WriteDatabaseBackup("old database");
        Write(_data, "sportarr.db", "migrated database");
        _host.OnStart = h =>
        {
            if (h.Started.Count == 1)
            {
                Write(_data, "updates/migrated", "migrated");
                // A child process that the kill did not reach.
                Write(_data, "updates/app.pid", "777\n1\n");
                h.RunningPids.Add(777);
                h.StartedApps[0].Exit();
            }
        };
        _host.Ping = _ => false;
        _host.OnKill = h => h.RunningPids.Add(777); // The kill does not reach it.

        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.Failed);
        Read(_data, "sportarr.db").Should().Be("migrated database");
        _host.Started.Should().ContainSingle("a second copy must not start while the new one runs");
        ResultFile().Should().Contain("did not stop");
    }

    [Fact]
    public async Task Starts_nothing_while_the_new_version_still_runs_even_without_a_migration()
    {
        _host.OnStart = h =>
        {
            Write(_data, "updates/app.pid", "777\n1\n");
            h.RunningPids.Add(777);
        };
        _host.OnKill = h => h.RunningPids.Add(777);
        _host.Ping = _ => false;

        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.Failed);
        _host.Started.Should().ContainSingle();
        Read(_install, "Sportarr").Should().Be("new app");
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("not sqlite")]
    [InlineData("not sportarr")]
    public async Task Keeps_the_new_files_when_the_database_cannot_be_restored(string broken)
    {
        // The old version must not run on the migrated schema, so nothing
        // goes back when the old database cannot be prepared.
        if (broken == "zip")
            Write(_data, "Backups/before-update.zip", "not a zip");
        else
            WriteBackupEntry(broken == "not sqlite" ? "plain text" : "SQLite format 3\0" + new string(' ', 200));
        Write(_data, "sportarr.db", "migrated database");
        _host.OnStart = h =>
        {
            if (h.Started.Count == 1)
            {
                Write(_data, "updates/migrated", "migrated");
                h.StartedApps[0].Exit();
            }
        };
        _host.Ping = _ => false;

        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.Failed);
        Read(_install, "Sportarr").Should().Be("new app");
        Read(_data, "sportarr.db").Should().Be("migrated database");
        File.Exists(Path.Combine(_data, "updates", "restoring")).Should().BeFalse();
        File.Exists(Path.Combine(_data, "sportarr.db.sportarr-restore")).Should().BeFalse();
        ResultFile().Should().Contain("cannot be restored").And.Contain("was not rolled back");
        ResultFile().Should().NotContain("..");
    }

    [Fact]
    public async Task Restores_files_and_database_with_the_restoring_marker_in_place()
    {
        WriteDatabaseBackup("old database");
        Write(_data, "sportarr.db", "migrated database");
        Write(_data, "sportarr.db-shm", "journal");
        string? markerDuringRestore = null;
        var markerWhileFilesWentBack = false;
        _host.OnStart = h =>
        {
            if (h.Started.Count == 1)
            {
                Write(_data, "updates/migrated", "migrated");
                h.StartedApps[0].Exit();
            }
            else
            {
                markerDuringRestore = File.Exists(Path.Combine(_data, "updates", "restoring")) ? "present" : "gone";
            }
        };
        _host.Ping = h => h.Started.Count >= 2;
        var runner = Runner(RestartMode.Process);
        runner.OnFilesRolledBack = () => markerWhileFilesWentBack = File.Exists(Path.Combine(_data, "updates", "restoring"));

        var status = await runner.RunAsync();

        status.Should().Be(UpdateResultStatus.RolledBack);
        markerWhileFilesWentBack.Should().BeTrue("a start during the restore must wait");
        Read(_install, "Sportarr").Should().Be("old app");
        Read(_data, "sportarr.db").Should().Contain("old database");
        File.Exists(Path.Combine(_data, "sportarr.db-shm")).Should().BeFalse();
        markerDuringRestore.Should().Be("gone", "the marker goes away before the old version starts");
    }

    [Fact]
    public async Task Says_when_Sportarr_does_not_stop_and_does_not_answer()
    {
        _host.ParentExits = false;
        _host.Ping = _ => false;

        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.Failed);
        ResultFile().Should().Contain("does not answer").And.NotContain("still runs");
    }

    [Fact]
    public async Task Stops_a_parent_that_does_not_stop_and_goes_on()
    {
        _host.ParentExitsWhen = h => h.KilledApps.Any(a => a.Pid == 1234);

        var status = await Runner(RestartMode.Process).RunAsync();

        status.Should().Be(UpdateResultStatus.Succeeded);
        _host.KilledApps.Should().Contain(new AppProcessId(1234, 99), "the helper checks the start time it was given");
        _host.ParentWaits.Should().Be(3, "the normal wait, the extra wait and the wait after the kill");
        Read(_install, "Sportarr").Should().Be("new app");
    }

    // Windows service mode

    [Fact]
    public async Task Windows_service_mode_starts_the_service_again()
    {
        var status = await Runner(RestartMode.WindowsService).RunAsync();

        status.Should().Be(UpdateResultStatus.Succeeded);
        _host.ServiceStarts.Should().Equal("Sportarr");
        _host.Started.Should().BeEmpty();
    }

    [Fact]
    public async Task Windows_service_rollback_waits_for_the_service_to_stop()
    {
        _host.Ping = h => h.ServiceStarts.Count >= 2;

        var status = await Runner(RestartMode.WindowsService).RunAsync();

        status.Should().Be(UpdateResultStatus.RolledBack);
        _host.ServiceStops.Should().Equal("Sportarr");
        _host.ServiceStarts.Should().HaveCount(2);
    }

    // systemd mode

    [Fact]
    public async Task Systemd_mode_swaps_before_Sportarr_exits_and_signals_the_app()
    {
        var signal = Path.Combine(_data, "updates", "swapped");
        _host.OnWaitForExit = () =>
        {
            // Sportarr waits for this signal before it exits.
            File.ReadAllText(signal).Should().Be("ok");
            Read(_install, "Sportarr").Should().Be("new app");
        };

        SayStopping();
        var status = await Runner(RestartMode.Systemd).RunAsync();

        status.Should().Be(UpdateResultStatus.Succeeded);
        _host.Started.Should().BeEmpty("systemd starts the new version");
    }

    [Fact]
    public async Task Systemd_mode_rolls_back_and_stops_the_new_process_by_its_pid()
    {
        var newApp = new AppProcessId(5555, 123456789);
        _host.OnWaitForExit = () => File.WriteAllText(Path.Combine(_data, "updates", "app.pid"), $"{newApp.Pid}\n{newApp.StartTicksUtc}\n");
        _host.Ping = h => h.KilledApps.Count > 0;

        SayStopping();
        var status = await Runner(RestartMode.Systemd).RunAsync();

        status.Should().Be(UpdateResultStatus.RolledBack);
        Read(_install, "Sportarr").Should().Be("old app");
        _host.KilledApps.Should().Equal(newApp);
        _host.Events.Should().Equal("pause 5555", "kill 5555");
    }

    [Fact]
    public async Task Systemd_mode_does_not_restore_the_database_when_the_new_process_cannot_be_paused()
    {
        WriteDatabaseBackup("old database");
        Write(_data, "sportarr.db", "migrated database");
        var newApp = new AppProcessId(5555, 123456789);
        _host.OnWaitForExit = () =>
        {
            File.WriteAllText(Path.Combine(_data, "updates", "app.pid"), $"{newApp.Pid}\n{newApp.StartTicksUtc}\n");
            Write(_data, "updates/migrated", "migrated");
            _host.RunningPids.Add(newApp.Pid);
        };
        _host.PauseWorks = false;
        _host.Ping = h => h.KilledApps.Count > 0;

        SayStopping();
        var status = await Runner(RestartMode.Systemd).RunAsync();

        status.Should().Be(UpdateResultStatus.Failed);
        Read(_data, "sportarr.db").Should().Be("migrated database");
    }

    [Fact]
    public async Task Systemd_mode_resumes_a_paused_process_that_cannot_be_stopped()
    {
        var newApp = new AppProcessId(5555, 123456789);
        _host.OnWaitForExit = () => File.WriteAllText(Path.Combine(_data, "updates", "app.pid"), $"{newApp.Pid}\n{newApp.StartTicksUtc}\n");
        _host.KillWorks = false;
        _host.Ping = _ => false;

        SayStopping();
        await Runner(RestartMode.Systemd).RunAsync();

        _host.Events.Should().ContainInOrder("pause 5555", "kill 5555", "resume 5555");
    }

    [Fact]
    public async Task Systemd_mode_waits_for_the_app_to_say_that_it_stops()
    {
        var exitWaitStartedAfterSignal = false;
        _host.RealDelay = true;
        _host.OnWaitForExit = () => exitWaitStartedAfterSignal = File.Exists(Path.Combine(_data, "updates", "stopping"));
        var writer = Task.Run(async () =>
        {
            // Sportarr says it later than the exit wait would last.
            while (!File.Exists(Path.Combine(_data, "updates", "swapped")))
                await Task.Delay(10);
            SayStopping();
        });

        var status = await Runner(RestartMode.Systemd).RunAsync();

        await writer;
        status.Should().Be(UpdateResultStatus.Succeeded);
        exitWaitStartedAfterSignal.Should().BeTrue();
    }

    [Fact]
    public async Task Systemd_mode_puts_the_files_back_when_the_app_keeps_running()
    {
        SayStopping("cancel");

        var status = await Runner(RestartMode.Systemd).RunAsync();

        status.Should().Be(UpdateResultStatus.Cancelled, "a cancel is not a failure of the new version");
        Read(_install, "Sportarr").Should().Be("old app");
        _host.ParentWaits.Should().Be(0);
        ResultFile().Should().Contain("status=Cancelled").And.Contain("busy and kept running");
    }

    // systemd starts a new process when a given step of the restore runs.
    // It cannot be paused.
    private UpdateRunner SystemdRunnerWithLateProcess(string step)
    {
        WriteDatabaseBackup("old database");
        Write(_data, "sportarr.db", "migrated database");
        _host.OnWaitForExit = () =>
        {
            File.WriteAllText(Path.Combine(_data, "updates", "app.pid"), "5555\n1\n");
            Write(_data, "updates/migrated", "migrated");
            _host.RunningPids.Add(5555);
        };
        _host.PauseFunc = app => app.Pid == 5555;
        _host.Ping = _ => false;
        SayStopping();
        var runner = Runner(RestartMode.Systemd);
        Action start = () =>
        {
            File.WriteAllText(Path.Combine(_data, "updates", "app.pid"), "6666\n2\n");
            _host.RunningPids.Add(6666);
        };
        switch (step)
        {
            case "marker": runner.OnRestoringMarkerWritten = start; break;
            case "files": runner.OnFilesRolledBack = start; break;
            default: runner.OnDatabaseRestored = start; break;
        }
        return runner;
    }

    [Fact]
    public async Task Systemd_mode_pauses_and_stops_a_start_that_waits_on_the_marker()
    {
        // The new start writes its pid first and then waits on the marker.
        // It can be paused, so the restore goes on and the start is stopped
        // before it can open the restored database.
        WriteDatabaseBackup("old database");
        Write(_data, "sportarr.db", "migrated database");
        _host.OnWaitForExit = () =>
        {
            File.WriteAllText(Path.Combine(_data, "updates", "app.pid"), "5555\n1\n");
            Write(_data, "updates/migrated", "migrated");
            _host.RunningPids.Add(5555);
        };
        _host.Ping = h => h.KilledApps.Any(a => a.Pid == 7777);
        SayStopping();
        var runner = Runner(RestartMode.Systemd);
        runner.OnRestoringMarkerWritten = () =>
        {
            File.WriteAllText(Path.Combine(_data, "updates", "app.pid"), "7777\n3\n");
            _host.RunningPids.Add(7777);
        };

        var status = await runner.RunAsync();

        status.Should().Be(UpdateResultStatus.RolledBack);
        _host.Events.Should().ContainInOrder("pause 5555", "pause 7777");
        _host.KilledApps.Select(a => a.Pid).Should().Contain(new[] { 5555, 7777 });
        _host.RunningPids.Should().NotContain(7777, "it never runs on the restored database");
        Read(_data, "sportarr.db").Should().Contain("old database");
    }

    [Fact]
    public async Task Systemd_mode_reads_an_empty_stopping_signal_as_not_yet()
    {
        _host.RealDelay = true;
        Write(_data, "updates/stopping", "");
        var writer = Task.Run(async () =>
        {
            await Task.Delay(100);
            File.WriteAllText(Path.Combine(_data, "updates", "stopping"), "stop");
        });

        var status = await Runner(RestartMode.Systemd).RunAsync();

        await writer;
        status.Should().Be(UpdateResultStatus.Succeeded);
    }

    [Fact]
    public async Task Systemd_mode_restores_nothing_when_a_process_starts_after_the_marker()
    {
        var status = await SystemdRunnerWithLateProcess("marker").RunAsync();

        status.Should().Be(UpdateResultStatus.Failed);
        Read(_data, "sportarr.db").Should().Be("migrated database");
        Read(_install, "Sportarr").Should().Be("new app");
        _host.Events.Should().Contain("pause 6666");
        File.Exists(Path.Combine(_data, "updates", "restoring")).Should().BeFalse();
    }

    [Fact]
    public async Task Systemd_mode_keeps_the_new_version_when_a_process_starts_while_the_files_go_back()
    {
        var status = await SystemdRunnerWithLateProcess("files").RunAsync();

        status.Should().Be(UpdateResultStatus.Failed);
        Read(_install, "Sportarr").Should().Be("new app", "the new version can run on the changed database");
        Read(_data, "sportarr.db").Should().Be("migrated database");
        ResultFile().Should().Contain("Restart Sportarr now").And.Contain("It may not start").And.Contain("before-update.zip");
    }

    [Fact]
    public async Task Systemd_mode_keeps_the_new_version_when_a_process_starts_after_the_database_is_back()
    {
        var status = await SystemdRunnerWithLateProcess("commit").RunAsync();

        status.Should().Be(UpdateResultStatus.Failed);
        Read(_install, "Sportarr").Should().Be("new app");
        Read(_data, "sportarr.db").Should().Contain("old database");
        ResultFile().Should().Contain("Restart Sportarr now").And.Contain("failed to start before").And.Contain("before-update.zip");
    }

    [Fact]
    public async Task Systemd_mode_goes_on_when_Sportarr_exits_without_the_stopping_signal()
    {
        // Sportarr stops without a word, and systemd starts the new version,
        // which migrates and then hangs. It cannot be paused, so the database
        // must stay as it is under it.
        WriteDatabaseBackup("old database");
        Write(_data, "sportarr.db", "migrated database");
        _host.ParentRunning = h => h.UtcNow < FakeHost.Start.AddSeconds(5);
        _host.OnWaitForExit = () =>
        {
            File.WriteAllText(Path.Combine(_data, "updates", "app.pid"), "5555\n1\n");
            Write(_data, "updates/migrated", "migrated");
            _host.RunningPids.Add(5555);
        };
        _host.PauseWorks = false;
        _host.Ping = _ => false;

        var status = await Runner(RestartMode.Systemd).RunAsync();

        _host.ParentWaits.Should().Be(1, "the normal stop path runs");
        (_host.UtcNow - FakeHost.Start).Should().BeLessThan(TimeSpan.FromMinutes(20), "it does not wait the full handover time");
        status.Should().Be(UpdateResultStatus.Failed);
        Read(_data, "sportarr.db").Should().Be("migrated database");
        ResultFile().Should().Contain("did not stop");
    }

    [Fact]
    public async Task Systemd_mode_stops_waiting_when_systemd_gives_up()
    {
        _host.SystemdState = "failed";
        _host.Ping = _ => false;

        SayStopping();
        var status = await Runner(RestartMode.Systemd, healthTimeout: TimeSpan.FromMinutes(10)).RunAsync();

        (_host.UtcNow - FakeHost.Start).Should().BeLessThan(TimeSpan.FromMinutes(1), "a crash loop ends the wait at once");
        status.Should().Be(UpdateResultStatus.Failed);
        ResultFile().Should().Contain("did not answer after that");
    }

    [Fact]
    public async Task Systemd_mode_always_tells_the_app_when_it_fails()
    {
        Directory.Delete(_staging, recursive: true);

        SayStopping();
        var status = await Runner(RestartMode.Systemd).RunAsync();

        status.Should().Be(UpdateResultStatus.Failed);
        File.ReadAllText(Path.Combine(_data, "updates", "swapped")).Should().Be("failed");
        Read(_install, "Sportarr").Should().Be("old app");
    }

    // Command line

    [Fact]
    public async Task The_helper_writes_which_version_is_installed()
    {
        var installed = Path.Combine(_data, "updates", "installed-version");
        string? duringNewStart = null;
        string? duringOldStart = null;
        _host.OnStart = h =>
        {
            if (h.Started.Count == 1)
                duringNewStart = File.ReadAllText(installed);
            else
                duringOldStart = File.ReadAllText(installed);
        };
        _host.Ping = h => h.Started.Count >= 2;

        await Runner(RestartMode.Process).RunAsync();

        duringNewStart.Should().Be("4.1.13.1130");
        duringOldStart.Should().Be("4.1.12.1121", "the rollback puts the old version back");
        File.Exists(installed).Should().BeFalse("the helper removes the file when it ends");
    }

    [Fact]
    public async Task An_error_that_escapes_the_runner_still_starts_Sportarr_and_writes_Failed()
    {
        _host.WaitThrows = true;
        _host.ParentRunning = _ => false;
        var options = UpdaterOptions.Parse(new[]
        {
            "--pid", "1234", "--install-dir", _install, "--staging-dir", _staging, "--data-dir", _data,
            "--app-exe", Path.Combine(_install, "Sportarr"), "--health-url", "http://x/ping", "--version", "4.1.13.1130"
        });

        var status = await UpdaterProgram.RunAsync(options, _host, new UpdateLog(null));

        status.Should().Be(UpdateResultStatus.Failed);
        _host.Started.Should().ContainSingle("Sportarr must not be left down");
        ResultFile().Should().Contain("status=Failed").And.Contain("stopped with an error");
    }

    [Fact]
    public void The_log_does_not_throw_when_it_cannot_write()
    {
        if (OperatingSystem.IsWindows())
            return;
        var locked = Path.Combine(_root, "locked");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var log = new UpdateLog(Path.Combine(locked, "logs", "update.log"));
            var act = () => log.Write("a line");

            act.Should().NotThrow();
            log.Lines.Should().ContainSingle();
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void The_restore_removes_the_newer_journal_before_it_moves_the_old_database_in()
    {
        var options = UpdaterOptions.Parse(new[]
        {
            "--pid", "1", "--install-dir", _install, "--staging-dir", _staging, "--data-dir", _data,
            "--app-exe", Path.Combine(_install, "Sportarr"), "--health-url", "http://x/ping"
        });
        Write(_data, "sportarr.db.sportarr-restore", "old database");
        Write(_data, "sportarr.db-wal", "newer journal");
        // A folder where the database goes makes the move fail.
        Directory.CreateDirectory(Path.Combine(_data, "sportarr.db"));

        var act = () => DatabaseRestorer.Commit(options);

        act.Should().Throw<IOException>();
        File.Exists(Path.Combine(_data, "sportarr.db-wal")).Should().BeFalse();
    }

    [Fact]
    public async Task An_error_in_Windows_service_mode_starts_the_service_again()
    {
        _host.WaitThrows = true;
        _host.ParentRunning = _ => false;
        var options = UpdaterOptions.Parse(new[]
        {
            "--pid", "1234", "--install-dir", _install, "--staging-dir", _staging, "--data-dir", _data,
            "--app-exe", Path.Combine(_install, "Sportarr"), "--health-url", "http://x/ping",
            "--restart", "WindowsService", "--service-name", "SportarrBeta"
        });

        await UpdaterProgram.RunAsync(options, _host, new UpdateLog(null));

        _host.ServiceStarts.Should().Equal("SportarrBeta");
    }

    [Fact]
    public void The_result_file_ignores_a_stale_temporary_file()
    {
        var result = Path.Combine(_data, "updates", UpdateResultFile.FileName);
        Directory.CreateDirectory(result + ".tmp"); // Left by an older helper.

        UpdateResultFile.Write(result, UpdateResultStatus.Succeeded, "4.1.13.1130", "4.1.12.1121", "ok.", DateTime.UtcNow);

        File.ReadAllText(result).Should().Contain("status=Succeeded");
    }

    [Fact]
    public void A_failed_atomic_write_leaves_no_temporary_file()
    {
        var target = Path.Combine(_data, "updates", "stopping");
        Directory.CreateDirectory(target);

        var act = () => AtomicFile.Write(target, "stop");

        act.Should().Throw<IOException>();
        Directory.EnumerateFiles(Path.Combine(_data, "updates"), "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Parses_the_command_line()
    {
        var options = UpdaterOptions.Parse(new[]
        {
            "--pid", "77", "--install-dir", _install, "--staging-dir", _staging, "--data-dir", _data,
            "--restart", "systemd", "--service-name", "sportarr.service", "--app-exe", Path.Combine(_install, "Sportarr"),
            "--working-dir", _workingDir, "--db-backup", "/b.zip", "--db-provider", "Postgres",
            "--health-url", "http://127.0.0.1:1867/ping", "--version", "4.1.13.1130", "--app-arg", "-data", "--app-arg", "/d"
        });

        options.ParentPid.Should().Be(77);
        options.Restart.Should().Be(RestartMode.Systemd);
        options.ServiceName.Should().Be("sportarr.service");
        options.AppArguments.Should().Equal("-data", "/d");
        options.WorkingDir.Should().Be(_workingDir);
        options.DatabaseBackup.Should().Be("/b.zip");
        options.DatabaseProvider.Should().Be("postgres");
        options.DatabasePath.Should().Be(Path.Combine(_data, "sportarr.db"));
        options.HealthTimeout.Should().Be(TimeSpan.FromMinutes(10));
        options.BackupDir.Should().Be(Path.Combine(_data, "updates", "backup"));
        options.LogPath.Should().Be(Path.Combine(_data, "logs", "update.log"));
    }

    [Fact]
    public void Prefers_the_newer_backup_of_a_late_handover()
    {
        var options = UpdaterOptions.Parse(new[]
        {
            "--pid", "1", "--install-dir", _install, "--staging-dir", _staging, "--data-dir", _data,
            "--app-exe", Path.Combine(_install, "Sportarr"), "--health-url", "http://x/ping", "--db-backup", "/old.zip"
        });
        DatabaseRestorer.BackupPath(options).Should().Be("/old.zip");

        Write(_data, "updates/db-backup-path", "/newer.zip");

        DatabaseRestorer.BackupPath(options).Should().Be("/newer.zip");
    }

    [Fact]
    public void Rejects_a_command_line_without_the_required_values()
    {
        var act = () => UpdaterOptions.Parse(new[] { "--pid", "1" });

        act.Should().Throw<ArgumentException>().WithMessage("Missing argument: *");
    }

    // Sportarr writes this under systemd when it stops now.
    private void SayStopping(string value = "stop") => Write(_data, "updates/stopping", value);

    private UpdateRunner Runner(
        RestartMode mode,
        TimeSpan? healthTimeout = null,
        TimeSpan? migrationTimeout = null,
        string provider = "sqlite")
    {
        var options = new UpdaterOptions
        {
            ParentPid = 1234,
            ParentStartTicks = 99,
            InstallDir = _install,
            StagingDir = _staging,
            DataDir = _data,
            BackupDir = Path.Combine(_data, "updates", "backup"),
            Restart = mode,
            ServiceName = "Sportarr",
            AppExecutable = Path.Combine(_install, "Sportarr"),
            AppArguments = new[] { "-data", Path.Combine(_root, "data") },
            WorkingDir = _workingDir,
            HealthUrl = "http://127.0.0.1:1867/ping",
            TargetVersion = "4.1.13.1130",
            PreviousVersion = "4.1.12.1121",
            DatabaseBackup = Path.Combine(_data, "Backups", "before-update.zip"),
            DatabaseProvider = provider,
            DatabasePath = Path.Combine(_data, "sportarr.db"),
            ExitTimeout = TimeSpan.FromSeconds(1),
            HealthTimeout = healthTimeout ?? TimeSpan.FromSeconds(10),
            MigrationTimeout = migrationTimeout ?? TimeSpan.FromMinutes(60)
        };
        return new UpdateRunner(options, _host, new UpdateLog(null));
    }

    private void WriteDatabaseBackup(string label) => WriteBackupEntry(SqliteLike(label));

    private void WriteBackupEntry(string content)
    {
        var zip = Path.Combine(_data, "Backups", "before-update.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        File.Delete(zip);
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("sportarr.db").Open());
        writer.Write(content);
    }

    // This is enough of a SQLite file for the helper's check. It has the
    // header and the name of a table that every Sportarr database has.
    private static string SqliteLike(string label) =>
        "SQLite format 3\0" + new string(' ', 100) + "CREATE TABLE \"__EFMigrationsHistory\" " + label;

    private void WriteMarker(string value) => Write(_data, "updates/starting", value);

    private string ResultFile() => File.ReadAllText(Path.Combine(_data, "updates", UpdateResultFile.FileName));

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(string root, string relative) => File.ReadAllText(Path.Combine(root, relative));

    private sealed class FakeHost : IUpdaterHost
    {
        public static readonly DateTime Start = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

        public bool ParentExits { get; set; } = true;
        public Func<FakeHost, bool>? ParentExitsWhen { get; set; }
        public HashSet<int> RunningPids { get; } = new();
        public List<AppProcessId> PausedApps { get; } = new();
        public List<string> Events { get; } = new();
        public string? SystemdState { get; set; }
        public int ParentWaits { get; private set; }
        public Action? OnWaitForExit { get; set; }
        public Action<FakeHost>? OnStart { get; set; }
        public Action<FakeHost>? OnKill { get; set; }
        public Func<FakeHost, bool> Ping { get; set; } = _ => true;
        public List<(string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory)> Started { get; } = new();
        public List<FakeApp> StartedApps { get; } = new();
        public List<string> ServiceStarts { get; } = new();
        public List<string> ServiceStops { get; } = new();
        public List<AppProcessId> KilledApps { get; } = new();

        public DateTime UtcNow { get; private set; } = Start;

        public bool WaitThrows { get; set; }

        public Task<bool> WaitForExitAsync(AppProcessId app, TimeSpan timeout)
        {
            if (WaitThrows)
                throw new InvalidOperationException("The process list cannot be read.");
            ParentWaits++;
            OnWaitForExit?.Invoke();
            UtcNow += timeout;
            return Task.FromResult(ParentExitsWhen?.Invoke(this) ?? ParentExits);
        }

        public bool PauseWorks { get; set; } = true;
        public bool KillWorks { get; set; } = true;
        public List<AppProcessId> ResumedApps { get; } = new();

        public Func<AppProcessId, bool>? PauseFunc { get; set; }

        public bool PauseApp(AppProcessId app)
        {
            PausedApps.Add(app);
            Events.Add($"pause {app.Pid}");
            var paused = PauseFunc?.Invoke(app) ?? PauseWorks;
            if (paused)
                Paused.Add(app.Pid);
            return paused;
        }

        public bool ResumeApp(AppProcessId app)
        {
            ResumedApps.Add(app);
            Events.Add($"resume {app.Pid}");
            return true;
        }

        public Task<string?> SystemdStateAsync(string unit) => Task.FromResult(SystemdState);

        public IStartedApp StartApp(string executable, IReadOnlyList<string> arguments, string workingDirectory)
        {
            Started.Add((executable, arguments, workingDirectory));
            var app = new FakeApp();
            StartedApps.Add(app);
            OnStart?.Invoke(this);
            return app;
        }

        public Task<bool> PingAsync(string url) => Task.FromResult(Ping(this));

        // Set when a test needs real time to pass, for another task to act.
        public bool RealDelay { get; set; }

        public Task DelayAsync(TimeSpan delay)
        {
            UtcNow += delay;
            return RealDelay ? Task.Delay(5) : Task.CompletedTask;
        }

        public Func<FakeHost, bool> ParentRunning { get; set; } = _ => true;

        // A paused process still exists, so it counts as running.
        public bool IsRunning(AppProcessId app) =>
            app.Pid == 1234 ? ParentRunning(this) : RunningPids.Contains(app.Pid) || Paused.Contains(app.Pid);

        public HashSet<int> Paused { get; } = new();

        public Task<bool> StartServiceAsync(string serviceName)
        {
            ServiceStarts.Add(serviceName);
            OnStart?.Invoke(this);
            return Task.FromResult(true);
        }

        public Task<bool> StopServiceAsync(string serviceName, TimeSpan timeout)
        {
            ServiceStops.Add(serviceName);
            return Task.FromResult(true);
        }

        public Task<bool> KillAppAsync(AppProcessId app)
        {
            KilledApps.Add(app);
            Events.Add($"kill {app.Pid}");
            if (!KillWorks)
                return Task.FromResult(false);
            RunningPids.Remove(app.Pid);
            Paused.Remove(app.Pid);
            OnKill?.Invoke(this);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeApp : IStartedApp
    {
        private bool _exited;
        public bool HasExited => _exited || Killed;
        public bool Killed { get; private set; }
        public void Exit() => _exited = true;

        public Task KillAsync()
        {
            Killed = true;
            return Task.CompletedTask;
        }
    }
}
