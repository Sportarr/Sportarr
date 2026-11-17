using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class BuiltInUpdateTests : IDisposable
{
    private const string NewVersion = "4.1.13.1130";
    private const string PackageUrl = "https://example.test/Sportarr-linux-x64-4.1.13.1130.tar.gz";

    private static readonly RunningVersion Release = new("4.1.12", "4.1.12.1121", "4.1.12.1121+abc");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"builtin-update-{Guid.NewGuid():N}");
    private readonly string _dataPath;
    private readonly FakeEnvironment _environment;
    private readonly FakeLauncher _launcher = new();
    private readonly FakeSafety _safety = new();
    private readonly Mock<IHostApplicationLifetime> _lifetime = new();
    private readonly FakeTime _time = new(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
    private byte[] _package;
    private string _releaseJson;
    private bool _githubDown;
    private string _dbName = $"builtin-update-{Guid.NewGuid():N}";

    public BuiltInUpdateTests()
    {
        _dataPath = Path.Combine(_root, "data");
        var installDir = Path.Combine(_root, "install");
        Directory.CreateDirectory(Path.Combine(installDir, "Sportarr.Update"));
        File.WriteAllText(Path.Combine(installDir, "Sportarr"), "old app");
        File.WriteAllText(Path.Combine(installDir, "Sportarr.Update", "Sportarr.Update"), "old helper");
        Directory.CreateDirectory(_dataPath);
        WriteConfig("BuiltIn", automatic: true);

        _environment = new FakeEnvironment(installDir)
        {
            ProcessStartUtc = _time.GetUtcNow().UtcDateTime.AddHours(-1)
        };
        _package = BuildPackage(includeHelper: true);
        _releaseJson = ReleaseJson(_package);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    // Version and build checks

    [Theory]
    [InlineData("4.1.12.1121", "4.1.12.1121+abc", true)]
    [InlineData("4.1.12.0-abc1234", "4.1.12.0", false)]
    [InlineData("4.1.12.0-local", "4.1.12.0", false)]
    [InlineData("4.1.12.1130", "4.1.12.1130-dev+abc", false)]
    [InlineData("4.1.12.0", "4.1.12.0", false)]
    public void Only_release_builds_get_the_built_in_update(string full, string informational, bool expected)
    {
        new RunningVersion("4.1.12", full, informational).IsReleaseBuild.Should().Be(expected);
    }

    [Theory]
    [InlineData("4.1.13.1130", "4.1.12", true)]
    [InlineData("4.1.12.1130", "4.1.12", false)]
    [InlineData("4.1.11", "4.1.12", false)]
    [InlineData("5", "4.1.12", true)]
    [InlineData("not a version", "4.1.12", false)]
    public void Compares_the_first_three_version_parts(string latest, string running, bool expected)
    {
        LatestReleaseClient.IsNewer(latest, running).Should().Be(expected);
    }

    [Fact]
    public async Task Keeps_a_good_answer_and_asks_again_after_a_failure()
    {
        var calls = 0;
        var check = new UpdateCheckService(Factory(() =>
        {
            calls++;
            return calls == 1 ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Json(_releaseJson);
        }), _time, NullLogger<UpdateCheckService>.Instance, Release);

        (await check.GetLatestAsync()).Should().BeNull();
        (await check.GetLatestAsync())!.Version.Should().Be(NewVersion);
        (await check.GetLatestAsync())!.Version.Should().Be(NewVersion);
        calls.Should().Be(2);

        _time.Advance(UpdateCheckService.CacheDuration + TimeSpan.FromSeconds(1));
        await check.GetLatestAsync();
        calls.Should().Be(3);
    }

    [Fact]
    public void Reads_the_size_and_checksum_of_each_asset()
    {
        var asset = LatestReleaseClient.Parse(_releaseJson)!.Assets.Single(a => a.Name.Contains("linux-x64"));

        asset.Size.Should().Be(_package.Length);
        asset.Sha256.Should().Be(Sha256(_package));
    }

    // Refusals

    [Fact]
    public async Task Can_install_when_everything_is_in_place()
    {
        var status = await CreateInstaller().GetStatusAsync();

        status.Reason.Should().BeNull();
        status.CanInstall.Should().BeTrue();
        status.Available.Should().BeTrue();
        status.LatestVersion.Should().Be(NewVersion);
        status.CurrentVersion.Should().Be("4.1.12.1121");
        status.Mechanism.Should().Be("builtin");
    }

    [Fact]
    public async Task Refuses_in_a_container()
    {
        _environment.IsContainer = true;

        var status = await CreateInstaller().GetStatusAsync();

        status.CanInstall.Should().BeFalse();
        status.Reason.Should().Contain("container");
    }

    [Fact]
    public async Task Refuses_when_the_install_folder_is_not_writable()
    {
        _environment.Writable = false;

        (await CreateInstaller().GetStatusAsync()).Reason.Should().Contain("cannot write to its install folder");
    }

    [Fact]
    public async Task Refuses_when_the_release_has_no_package_for_this_system()
    {
        _environment.Arch = "arm64";

        (await CreateInstaller().GetStatusAsync()).Reason.Should().Contain("no package for this system (linux-arm64)");
    }

    [Fact]
    public async Task Refuses_a_development_build()
    {
        var status = await CreateInstaller(new RunningVersion("4.1.12", "4.1.12.0-local", "")).GetStatusAsync();

        status.Reason.Should().Contain("development build");
    }

    [Fact]
    public async Task Refuses_when_the_service_setup_cannot_restart_Sportarr()
    {
        _environment.Restart = new UpdateRestartTarget(UpdateRestartMode.Systemd, "sportarr.service", "Add KillMode=process to the sportarr.service service");

        (await CreateInstaller().GetStatusAsync()).Reason.Should().Contain("KillMode=process");
    }

    [Fact]
    public async Task Refuses_when_the_helper_is_missing()
    {
        File.Delete(Path.Combine(_environment.InstallDir, "Sportarr.Update", "Sportarr.Update"));

        (await CreateInstaller().GetStatusAsync()).Reason.Should().Contain("update helper is missing");
    }

    [Fact]
    public async Task Refuses_when_the_mechanism_is_not_built_in()
    {
        WriteConfig("Script", automatic: true);

        (await CreateInstaller().InstallAsync()).Outcome.Should().Be(UpdateInstallOutcome.Refused);
        _launcher.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Says_up_to_date_and_unreachable_plainly()
    {
        var upToDate = CreateInstaller(new RunningVersion("4.1.13", "4.1.13.1130", "4.1.13.1130"));
        (await upToDate.GetStatusAsync()).Reason.Should().Be("Sportarr is up to date.");

        _githubDown = true;
        var unreachable = await CreateInstaller().GetStatusAsync();
        unreachable.Reason.Should().Contain("GitHub cannot be reached");
        unreachable.Available.Should().BeFalse();
    }

    // Install

    [Fact]
    public async Task Downloads_checks_and_unpacks_the_package_then_starts_the_helper_and_stops()
    {
        _environment.Arguments = new[] { "--tray" };
        var installer = CreateInstaller();

        var result = await installer.InstallAsync();

        result.Outcome.Should().Be(UpdateInstallOutcome.Started, result.Message);
        var updates = Path.Combine(_dataPath, "updates");
        File.ReadAllText(Path.Combine(updates, "staging", "Sportarr")).Should().Be("new app");
        File.ReadAllText(Path.Combine(updates, "updater", "Sportarr.Update")).Should().Be("new helper");

        var call = _launcher.Calls.Should().ContainSingle().Subject;
        call.Executable.Should().Be(Path.Combine(updates, "updater", "Sportarr.Update"));
        ArgumentValue(call.Arguments, "--pid").Should().Be("4242");
        ArgumentValue(call.Arguments, "--pid-start").Should().Be(_environment.ProcessStartUtc.Ticks.ToString());
        ArgumentValue(call.Arguments, "--install-dir").Should().Be(_environment.InstallDir);
        ArgumentValue(call.Arguments, "--staging-dir").Should().Be(Path.Combine(updates, "staging"));
        ArgumentValue(call.Arguments, "--data-dir").Should().Be(Path.GetFullPath(_dataPath));
        ArgumentValue(call.Arguments, "--restart").Should().Be("Process");
        ArgumentValue(call.Arguments, "--app-exe").Should().Be(_environment.ProcessPath);
        ArgumentValue(call.Arguments, "--health-url").Should().Be("http://127.0.0.1:1867/ping");
        ArgumentValue(call.Arguments, "--version").Should().Be(NewVersion);
        ArgumentValue(call.Arguments, "--previous-version").Should().Be("4.1.12.1121");
        ArgumentValue(call.Arguments, "--working-dir").Should().Be("/home/user/start-folder");
        AllValues(call.Arguments, "--app-arg").Should().Equal("--tray", "-data", Path.GetFullPath(_dataPath));
        ArgumentValue(call.Arguments, "--db-backup").Should().Be("/backups/before-update.zip");
        ArgumentValue(call.Arguments, "--db-provider").Should().Be("sqlite");
        ArgumentValue(call.Arguments, "--db-path").Should().Be(Path.Combine(Path.GetFullPath(_dataPath), "sportarr.db"));
        _safety.Backups.Should().Be(1, "the database is backed up before the handover");

        await WaitUntilAsync(() => _lifetime.Invocations.Any(i => i.Method.Name == nameof(IHostApplicationLifetime.StopApplication)));
        installer.IsInstalling.Should().BeTrue("Sportarr is about to stop and must not start a second install");
    }

    [Fact]
    public async Task The_install_endpoint_answers_409_with_the_busy_reason()
    {
        _safety.Busy = "A recording is in progress. Install the update when it has finished.";
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(CreateInstaller());
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        Sportarr.Api.Endpoints.UpdateEndpoints.MapUpdateEndpoints(app);
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var response = await client.PostAsync("api/update/install", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("A recording is in progress");
    }

    [Fact]
    public void A_package_must_come_over_HTTPS_outside_the_test_setup()
    {
        Environment.GetEnvironmentVariable(LatestReleaseClient.ApiBaseOverrideVariable).Should().BeNull();

        LatestReleaseClient.DownloadUrlAllowed("https://github.com/Sportarr/Sportarr/releases/download/a.tar.gz").Should().BeTrue();
        LatestReleaseClient.DownloadUrlAllowed("http://example.test/a.tar.gz").Should().BeFalse();
    }

    [Fact]
    public void Keeps_a_data_argument_that_the_start_already_has()
    {
        _environment.Arguments = new[] { "-data", "/var/lib/sportarr" };

        CreateInstaller().RestartArguments().Should().Equal("-data", "/var/lib/sportarr");
    }

    [Fact]
    public async Task Refuses_a_release_without_a_checksum()
    {
        _releaseJson = ReleaseJson(_package, sha256: "");

        (await CreateInstaller().GetStatusAsync()).Reason.Should().Contain("does not list a checksum");
        (await CreateInstaller().InstallAsync()).Outcome.Should().Be(UpdateInstallOutcome.Refused);
        _launcher.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_when_the_disk_is_too_full_for_the_download()
    {
        _environment.Free = _package.Length;

        var result = await CreateInstaller().InstallAsync();

        result.Outcome.Should().Be(UpdateInstallOutcome.Refused);
        result.Message.Should().Contain("not enough free space");
    }

    [Fact]
    public async Task Refuses_when_the_install_volume_is_too_full_for_the_new_files()
    {
        // The install folder is on its own small volume. The data volume has room.
        _environment.Volume = p => p.StartsWith(_environment.InstallDir) ? "/opt" : "/";
        _environment.FreeByVolume["/opt"] = 3;
        _environment.Free = 1_000_000_000;

        var result = await CreateInstaller().InstallAsync();

        result.Outcome.Should().Be(UpdateInstallOutcome.Refused);
        result.Message.Should().Contain("not enough free space").And.Contain(_environment.InstallDir);
        _launcher.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Adds_the_needs_of_folders_on_one_volume()
    {
        var staging = Path.Combine(_root, "staging-check");
        Directory.CreateDirectory(Path.Combine(staging, "Sportarr.Update"));
        File.WriteAllBytes(Path.Combine(staging, "Sportarr"), new byte[600]);
        File.WriteAllBytes(Path.Combine(staging, "Sportarr.Update", "Sportarr.Update"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_dataPath, "sportarr.db"), new byte[300]);
        // The install folder gets 700 new bytes. The data folder needs 17 bytes
        // for the replaced files, 300 for the database and 100 for the helper.
        // Both are on one volume, so the update needs 1117 bytes in all.
        var installer = CreateInstaller();

        _environment.Free = 1200;
        installer.CheckSpaceForInstall(staging).Should().BeNull();
        _environment.Free = 1000;
        installer.CheckSpaceForInstall(staging).Should().Contain("not enough free space");
    }

    [Fact]
    public async Task Asks_again_if_Sportarr_is_busy_after_the_backup()
    {
        _safety.BusyAfterBackup = "A recording is in progress. Install the update when it has finished.";

        var result = await CreateInstaller().InstallAsync();

        result.Outcome.Should().Be(UpdateInstallOutcome.Refused);
        result.Message.Should().Contain("recording");
        _launcher.Calls.Should().BeEmpty();
        _lifetime.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Shows_a_missing_pg_dump_before_any_install()
    {
        _safety.Problem = "Sportarr uses Postgres, and pg_dump is not installed.";

        var status = await CreateInstaller().GetStatusAsync();

        status.CanInstall.Should().BeFalse();
        status.Reason.Should().Contain("pg_dump");
    }

    [Fact]
    public void Finds_pg_dump_on_the_path_only_for_Postgres()
    {
        var bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(bin);

        UpdateSafety.BackupProblem(Sportarr.Api.Data.DatabaseProviderKind.Sqlite, "").Should().BeNull();
        UpdateSafety.BackupProblem(Sportarr.Api.Data.DatabaseProviderKind.Postgres, bin).Should().Contain("pg_dump");
        File.WriteAllText(Path.Combine(bin, OperatingSystem.IsWindows() ? "pg_dump.exe" : "pg_dump"), "");
        UpdateSafety.BackupProblem(Sportarr.Api.Data.DatabaseProviderKind.Postgres, bin).Should().BeNull();
    }

    [Fact]
    public async Task The_status_carries_a_rolled_back_update()
    {
        var updates = Path.Combine(_dataPath, "updates");
        Sportarr.Update.UpdateResultFile.Write(Path.Combine(updates, UpdateResultMarker.FileName),
            Sportarr.Update.UpdateResultStatus.RolledBack, NewVersion, "4.1.12.1121", "did not start", DateTime.UtcNow);

        var status = await CreateInstaller().GetStatusAsync();

        status.LastResult!.Status.Should().Be("RolledBack");
        status.LastResult.Message.Should().Be("did not start");
        status.LastResult.Time.Should().NotBeNullOrEmpty("the page tells a repeat failure apart by its time");
    }

    [Fact]
    public async Task Stops_reading_a_download_that_is_longer_than_listed()
    {
        using var source = new MemoryStream(new byte[1000]);
        using var target = new MemoryStream();

        var act = () => InstallUpdateService.CopyAtMostAsync(source, target, 400, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
        target.Length.Should().BeLessThanOrEqualTo(400);
    }

    [Fact]
    public void Unpacks_only_regular_files_and_folders()
    {
        var destination = Path.Combine(_root, "unpacked");
        using var tar = new MemoryStream();
        using (var writer = new TarWriter(tar, leaveOpen: true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "Sportarr.Update/"));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "Sportarr") { DataStream = new MemoryStream("app"u8.ToArray()) });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Fifo, "a-pipe"));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "a-link") { LinkName = "/etc/passwd" });
        }
        tar.Position = 0;

        InstallUpdateService.ExtractTar(tar, destination);

        File.ReadAllText(Path.Combine(destination, "Sportarr")).Should().Be("app");
        Directory.Exists(Path.Combine(destination, "Sportarr.Update")).Should().BeTrue();
        File.Exists(Path.Combine(destination, "a-pipe")).Should().BeFalse();
        File.Exists(Path.Combine(destination, "a-link")).Should().BeFalse();
        Directory.EnumerateFileSystemEntries(destination).Should().HaveCount(2);
    }

    [Fact]
    public void Refuses_a_package_entry_that_leaves_the_folder()
    {
        using var tar = new MemoryStream();
        using (var writer = new TarWriter(tar, leaveOpen: true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "../escape") { DataStream = new MemoryStream("x"u8.ToArray()) });
        }
        tar.Position = 0;

        var act = () => InstallUpdateService.ExtractTar(tar, Path.Combine(_root, "unpacked"));

        act.Should().Throw<InvalidDataException>();
        File.Exists(Path.Combine(_root, "escape")).Should().BeFalse();
    }

    [Fact]
    public async Task Under_systemd_the_install_stays_marked_until_the_helper_reports()
    {
        _environment.Restart = new UpdateRestartTarget(UpdateRestartMode.Systemd, "sportarr.service", null);
        var swapTimeout = InstallUpdateService.SystemdSwapTimeout;
        var lateTimeout = InstallUpdateService.SystemdLateSwapTimeout;
        InstallUpdateService.SystemdSwapTimeout = TimeSpan.FromMilliseconds(200);
        InstallUpdateService.SystemdLateSwapTimeout = TimeSpan.FromSeconds(30);
        try
        {
            var installer = CreateInstaller();

            var result = await installer.InstallAsync();

            result.Outcome.Should().Be(UpdateInstallOutcome.Started);
            result.Message.Should().Contain("still copying");
            installer.IsInstalling.Should().BeTrue();
            (await installer.InstallAsync()).Outcome.Should().Be(UpdateInstallOutcome.Refused);

            File.WriteAllText(Path.Combine(_dataPath, "updates", "swapped"), "failed");
            await Task.Delay(500);
            installer.IsInstalling.Should().BeTrue("the helper still runs and has not reported");

            Sportarr.Update.UpdateResultFile.Write(Path.Combine(_dataPath, "updates", UpdateResultMarker.FileName),
                Sportarr.Update.UpdateResultStatus.Failed, NewVersion, "4.1.12.1121", "could not copy.", DateTime.UtcNow);
            await WaitUntilAsync(() => !installer.IsInstalling);
            _lifetime.Invocations.Should().BeEmpty("a failed swap keeps Sportarr running");
        }
        finally
        {
            InstallUpdateService.SystemdSwapTimeout = swapTimeout;
            InstallUpdateService.SystemdLateSwapTimeout = lateTimeout;
        }
    }

    [Fact]
    public void Removes_the_update_leftovers_after_a_good_update()
    {
        var updates = Path.Combine(_dataPath, "updates");
        foreach (var folder in new[] { "download", "staging", "updater", "backup" })
            Directory.CreateDirectory(Path.Combine(updates, folder));
        Sportarr.Update.UpdateResultFile.Write(Path.Combine(updates, UpdateResultMarker.FileName),
            Sportarr.Update.UpdateResultStatus.Succeeded, "4.1.12.1121", "4.1.11.1100", "running", DateTime.UtcNow);

        CreateInstaller().CleanUpAfterSuccessfulUpdate();

        Directory.EnumerateDirectories(updates).Should().BeEmpty();
    }

    [Fact]
    public void Cleans_up_after_a_rollback_too()
    {
        var updates = Path.Combine(_dataPath, "updates");
        foreach (var folder in new[] { "download", "staging", "updater", "backup" })
            Directory.CreateDirectory(Path.Combine(updates, folder));
        Sportarr.Update.UpdateResultFile.Write(Path.Combine(updates, UpdateResultMarker.FileName),
            Sportarr.Update.UpdateResultStatus.RolledBack, NewVersion, "4.1.12.1121", "did not start", DateTime.UtcNow);

        CreateInstaller().CleanUpAfterSuccessfulUpdate();

        Directory.EnumerateDirectories(updates).Should().BeEmpty();
    }

    [Fact]
    public void Keeps_the_old_files_after_a_failure_that_left_the_new_ones()
    {
        var updates = Path.Combine(_dataPath, "updates");
        foreach (var folder in new[] { "download", "staging", "updater", "backup" })
            Directory.CreateDirectory(Path.Combine(updates, folder));
        Sportarr.Update.UpdateResultFile.Write(Path.Combine(updates, UpdateResultMarker.FileName),
            Sportarr.Update.UpdateResultStatus.Failed, NewVersion, "4.1.12.1121", "not rolled back", DateTime.UtcNow);

        CreateInstaller().CleanUpAfterSuccessfulUpdate();

        Directory.EnumerateDirectories(updates).Select(Path.GetFileName).Should().Equal("backup");
    }

    [Fact]
    public async Task A_refresh_asks_GitHub_at_most_once_a_minute()
    {
        var calls = 0;
        var check = new UpdateCheckService(Factory(() => { calls++; return Json(_releaseJson); }),
            _time, NullLogger<UpdateCheckService>.Instance, Release);

        await check.GetLatestAsync(forceRefresh: true);
        await check.GetLatestAsync(forceRefresh: true);
        await check.GetLatestAsync(forceRefresh: true);
        calls.Should().Be(1);

        _time.Advance(TimeSpan.FromSeconds(61));
        await check.GetLatestAsync(forceRefresh: true);
        calls.Should().Be(2);
    }

    [Theory]
    [InlineData("\"Sportarr.exe\",\"1234\",\"Sportarr\"\r\n", "Sportarr")]
    [InlineData("\"Sportarr.exe\",\"1234\",\"SportarrBeta\"", "SportarrBeta")]
    [InlineData("\"Sportarr.exe\",\"1234\",\"N/A\"", null)]
    [InlineData("INFO: No tasks are running which match the specified criteria.", null)]
    public void Reads_the_Windows_service_name(string tasklist, string? expected)
    {
        SystemUpdateEnvironment.ServiceNameFromTasklist(tasklist).Should().Be(expected);
    }

    [Fact]
    public async Task The_health_check_url_includes_the_url_base()
    {
        WriteConfig("BuiltIn", automatic: true, urlBase: "/sportarr/");
        var installer = CreateInstaller();

        installer.HealthUrl(await new ConfigService(Configuration(), NullLogger<ConfigService>.Instance).GetConfigAsync())
            .Should().Be("http://127.0.0.1:1867/sportarr/ping");
    }

    [Fact]
    public async Task A_download_that_does_not_match_the_checksum_is_not_installed()
    {
        _releaseJson = ReleaseJson(_package, sha256: new string('0', 64));

        var result = await CreateInstaller().InstallAsync();

        result.Outcome.Should().Be(UpdateInstallOutcome.Failed);
        result.Message.Should().Contain("checksum");
        _launcher.Calls.Should().BeEmpty();
        _lifetime.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_download_with_the_wrong_size_is_not_installed()
    {
        _releaseJson = ReleaseJson(_package, size: _package.Length + 1);

        var result = await CreateInstaller().InstallAsync();

        result.Outcome.Should().Be(UpdateInstallOutcome.Failed);
        result.Message.Should().Contain("bytes");
        _launcher.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_package_without_the_helper_is_not_installed()
    {
        _package = BuildPackage(includeHelper: false);
        _releaseJson = ReleaseJson(_package);
        var installer = CreateInstaller();

        var result = await installer.InstallAsync();

        result.Outcome.Should().Be(UpdateInstallOutcome.Failed);
        result.Message.Should().Contain("update helper");
        _launcher.Calls.Should().BeEmpty();
        installer.IsInstalling.Should().BeFalse();
    }

    // Automatic

    [Fact]
    public async Task Automatic_update_installs_at_a_quiet_moment()
    {
        (await CreateAutomatic().RunIfDueAsync()).Should().Be(AutomaticUpdateOutcome.Started);

        _launcher.Calls.Should().ContainSingle();
        File.ReadAllText(Path.Combine(_dataPath, "config.xml")).Should().Contain($"<AutoUpdateLastVersion>{NewVersion}</AutoUpdateLastVersion>");
    }

    [Fact]
    public async Task Automatic_update_skips_a_version_that_was_rolled_back_but_a_click_still_installs_it()
    {
        Sportarr.Update.UpdateResultFile.Write(Path.Combine(_dataPath, "updates", UpdateResultMarker.FileName),
            Sportarr.Update.UpdateResultStatus.RolledBack, NewVersion, "4.1.12.1121", "did not start.", DateTime.UtcNow);

        (await CreateAutomatic().RunIfDueAsync()).Should().Be(AutomaticUpdateOutcome.PreviouslyFailed);
        _launcher.Calls.Should().BeEmpty();

        (await CreateInstaller().InstallAsync()).Outcome.Should().Be(UpdateInstallOutcome.Started);
    }

    [Fact]
    public async Task Looks_at_the_service_setup_again_when_it_installs()
    {
        // The setup was fine at start, and has changed since.
        _environment.RestartAfterRefresh = new UpdateRestartTarget(UpdateRestartMode.Systemd, "sportarr.service", "Add KillMode=process to the sportarr.service service.");

        var result = await CreateInstaller().InstallAsync();

        _environment.Refreshes.Should().BeGreaterThan(0);
        result.Outcome.Should().Be(UpdateInstallOutcome.Refused);
        result.Message.Should().Contain("KillMode=process");
        _launcher.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_late_systemd_handover_backs_up_again_and_tells_the_helper()
    {
        _environment.Restart = new UpdateRestartTarget(UpdateRestartMode.Systemd, "sportarr.service", null);
        var swapTimeout = InstallUpdateService.SystemdSwapTimeout;
        InstallUpdateService.SystemdSwapTimeout = TimeSpan.FromMilliseconds(200);
        try
        {
            var installer = CreateInstaller();
            (await installer.InstallAsync()).Message.Should().Contain("still copying");
            _safety.Backups.Should().Be(1);

            File.WriteAllText(Path.Combine(_dataPath, "updates", "swapped"), "ok");
            await WaitUntilAsync(() => _lifetime.Invocations.Any(i => i.Method.Name == nameof(IHostApplicationLifetime.StopApplication)));

            _safety.Backups.Should().Be(2, "a late handover takes a fresh backup");
            File.ReadAllText(Path.Combine(_dataPath, "updates", "db-backup-path")).Should().Be("/backups/before-update.zip");
        }
        finally
        {
            InstallUpdateService.SystemdSwapTimeout = swapTimeout;
            Environment.ExitCode = 0;
        }
    }

    [Fact]
    public async Task A_late_systemd_handover_keeps_Sportarr_running_when_it_is_busy()
    {
        _environment.Restart = new UpdateRestartTarget(UpdateRestartMode.Systemd, "sportarr.service", null);
        var swapTimeout = InstallUpdateService.SystemdSwapTimeout;
        InstallUpdateService.SystemdSwapTimeout = TimeSpan.FromMilliseconds(200);
        try
        {
            var installer = CreateInstaller();
            await installer.InstallAsync();
            _safety.Busy = "A recording is in progress.";

            File.WriteAllText(Path.Combine(_dataPath, "updates", "swapped"), "ok");
            await WaitUntilAsync(() => File.Exists(Path.Combine(_dataPath, "updates", "stopping")));
            _launcher.HelperExited = true; // The helper puts the old files back and ends.
            await WaitUntilAsync(() => !installer.IsInstalling);

            File.ReadAllText(Path.Combine(_dataPath, "updates", "stopping")).Should().Be("cancel");
            _lifetime.Invocations.Should().BeEmpty();
        }
        finally
        {
            InstallUpdateService.SystemdSwapTimeout = swapTimeout;
        }
    }

    [Fact]
    public async Task Reopens_imports_that_a_stopped_process_left_importing()
    {
        await using var db = CreateDb();
        db.PendingImports.Add(new PendingImport { Title = "Stuck", DownloadId = "disk-1", FilePath = "/x.mkv", Status = PendingImportStatus.Importing });
        db.PendingImports.Add(new PendingImport { Title = "Choice", DownloadId = "dvr-5", FilePath = "/y.ts", Status = PendingImportStatus.Importing });
        await db.SaveChangesAsync();

        (await UpdateSafety.ReopenInterruptedImportsAsync(db)).Should().Be(1);

        (await UpdateSafety.WhatIsBusyAsync(db, CancellationToken.None)).Should().Contain("import", "the DVR choice has its own rule and stays");
        db.PendingImports.Single(p => p.DownloadId == "disk-1").Status.Should().Be(PendingImportStatus.Pending);
    }

    [Fact]
    public void Startup_removes_a_restoring_marker_whose_helper_is_gone_at_once()
    {
        var marker = Path.Combine(_dataPath, "updates", UpdateStartupMarkers.RestoringFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        // A process id that does not run, as after a crashed helper.
        File.WriteAllText(marker, "2147483000\n638000000000000000\n");
        var watch = Stopwatch.StartNew();

        UpdateStartupMarkers.WaitWhileRestoring(_dataPath, TimeSpan.FromSeconds(20)).Should().Be(RestoreWait.StaleRemoved);

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        File.Exists(marker).Should().BeFalse();
    }

    [Fact]
    public async Task Startup_waits_while_a_live_helper_restores_and_then_goes_on()
    {
        var marker = Path.Combine(_dataPath, "updates", UpdateStartupMarkers.RestoringFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        using var self = Process.GetCurrentProcess();
        var live = $"{self.Id}\n{self.StartTime.ToUniversalTime().Ticks}\n";

        File.WriteAllText(marker, live);
        var watch = Stopwatch.StartNew();
        var remover = Task.Run(async () => { await Task.Delay(300); File.Delete(marker); });
        UpdateStartupMarkers.WaitWhileRestoring(_dataPath, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(20)).Should().Be(RestoreWait.WaitedForHelper);
        await remover;
        watch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(250), "the start waited for the restore");

        File.WriteAllText(marker, live);
        UpdateStartupMarkers.WaitWhileRestoring(_dataPath, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(20)).Should().Be(RestoreWait.TimedOut);
        File.Exists(marker).Should().BeFalse("the marker goes away at the limit");
    }

    [Fact]
    public async Task An_empty_marker_from_an_older_version_does_not_end_the_wait_early()
    {
        // Markers are written in one step now. An empty file can still be
        // left by an older version or a full disk. It counts as still being
        // written, so a live helper that writes its marker after it is not
        // taken for a stale one.
        var marker = Path.Combine(_dataPath, "updates", UpdateStartupMarkers.RestoringFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        using var self = Process.GetCurrentProcess();
        File.WriteAllText(marker, "");
        var helper = Task.Run(async () =>
        {
            await Task.Delay(200);
            UpdateStartupMarkers.WriteAtomic(marker, $"{self.Id}\n{self.StartTime.ToUniversalTime().Ticks}\n");
            await Task.Delay(200);
            File.Delete(marker);
        });

        UpdateStartupMarkers.WaitWhileRestoring(_dataPath, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(20))
            .Should().Be(RestoreWait.WaitedForHelper);
        await helper;
    }

    [Fact]
    public async Task A_start_writes_its_pid_before_it_waits_for_a_restore()
    {
        var updates = Path.Combine(_dataPath, "updates");
        var marker = Path.Combine(updates, UpdateStartupMarkers.RestoringFileName);
        using var self = Process.GetCurrentProcess();
        UpdateStartupMarkers.WriteAtomic(marker, $"{self.Id}\n{self.StartTime.ToUniversalTime().Ticks}\n");
        var pidSeenDuringWait = false;
        var helper = Task.Run(async () =>
        {
            await Task.Delay(200);
            pidSeenDuringWait = File.Exists(Path.Combine(updates, UpdateStartupMarkers.PidFileName));
            File.Delete(marker);
        });

        var exit = UpdateStartupMarkers.PrepareStart(_dataPath, TimeSpan.FromSeconds(10), underSystemd: true, runningVersion: "4.1.12.1121", TimeSpan.FromMilliseconds(20));
        await helper;

        pidSeenDuringWait.Should().BeTrue("the helper must be able to pause this process during the restore");
        exit.Should().BeTrue("under systemd the waiting process exits, so that systemd starts the files in place now");
        File.Exists(Path.Combine(updates, UpdateStartupMarkers.StartingFileName)).Should().BeFalse("an exiting start does not go on");
    }

    [Fact]
    public void A_start_without_a_restore_goes_on()
    {
        UpdateStartupMarkers.PrepareStart(_dataPath, TimeSpan.FromSeconds(10), underSystemd: true, runningVersion: "4.1.12.1121").Should().BeFalse();
        File.ReadAllText(Path.Combine(_dataPath, "updates", UpdateStartupMarkers.StartingFileName)).Should().Be("starting");
    }

    [Fact]
    public async Task An_empty_swapped_signal_is_not_an_answer_yet()
    {
        _environment.Restart = new UpdateRestartTarget(UpdateRestartMode.Systemd, "sportarr.service", null);
        var installer = CreateInstaller();
        var updates = Path.Combine(_dataPath, "updates");
        var install = Task.Run(() => installer.InstallAsync());
        await WaitUntilAsync(() => _launcher.Calls.Count == 1);
        File.WriteAllText(Path.Combine(updates, "swapped"), "");
        await Task.Delay(700);
        File.WriteAllText(Path.Combine(updates, "swapped"), "ok");

        (await install).Outcome.Should().Be(UpdateInstallOutcome.Started);
        Environment.ExitCode = 0;
    }

    [Fact]
    public async Task An_error_after_the_swap_tells_the_helper_to_cancel()
    {
        _environment.Restart = new UpdateRestartTarget(UpdateRestartMode.Systemd, "sportarr.service", null);
        _safety.FailOnBackup = 2; // The handover backup fails.
        var installer = CreateInstaller();
        var updates = Path.Combine(_dataPath, "updates");
        var install = Task.Run(() => installer.InstallAsync());
        await WaitUntilAsync(() => _launcher.Calls.Count == 1);
        File.WriteAllText(Path.Combine(updates, "swapped"), "ok");

        (await install).Outcome.Should().Be(UpdateInstallOutcome.Failed);
        File.ReadAllText(Path.Combine(updates, "stopping")).Should().Be("cancel");
        Environment.ExitCode.Should().Be(0);
        _lifetime.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_swap_tells_the_helper_to_cancel()
    {
        _environment.Restart = new UpdateRestartTarget(UpdateRestartMode.Systemd, "sportarr.service", null);
        var installer = CreateInstaller();
        var updates = Path.Combine(_dataPath, "updates");
        var install = Task.Run(() => installer.InstallAsync());
        await WaitUntilAsync(() => _launcher.Calls.Count == 1);
        File.WriteAllText(Path.Combine(updates, "swapped"), "failed");

        (await install).Outcome.Should().Be(UpdateInstallOutcome.Failed);
        File.ReadAllText(Path.Combine(updates, "stopping")).Should().Be("cancel");
    }

    [Fact]
    public async Task A_second_install_waits_for_the_helper_of_the_first()
    {
        _environment.Restart = new UpdateRestartTarget(UpdateRestartMode.Systemd, "sportarr.service", null);
        var installer = CreateInstaller();
        var updates = Path.Combine(_dataPath, "updates");
        var first = Task.Run(() => installer.InstallAsync());
        await WaitUntilAsync(() => _launcher.Calls.Count == 1);
        File.WriteAllText(Path.Combine(updates, "swapped"), "failed");
        (await first).Outcome.Should().Be(UpdateInstallOutcome.Failed);

        // The helper still runs and has written no result.
        installer.IsInstalling.Should().BeTrue();
        (await installer.InstallAsync()).Outcome.Should().Be(UpdateInstallOutcome.Refused);

        Sportarr.Update.UpdateResultFile.Write(Path.Combine(updates, UpdateResultMarker.FileName),
            Sportarr.Update.UpdateResultStatus.Failed, NewVersion, "4.1.12.1121", "could not copy.", DateTime.UtcNow);
        installer.IsInstalling.Should().BeFalse();
    }

    [Fact]
    public async Task A_new_install_removes_the_result_of_the_last_one()
    {
        var result = Path.Combine(_dataPath, "updates", UpdateResultMarker.FileName);
        Sportarr.Update.UpdateResultFile.Write(result, Sportarr.Update.UpdateResultStatus.Succeeded, "4.1.12.1121", "4.1.11.1100", "ok.", DateTime.UtcNow);

        (await CreateInstaller().InstallAsync()).Outcome.Should().Be(UpdateInstallOutcome.Started);

        File.Exists(result).Should().BeFalse();
    }

    [Fact]
    public void Cleans_up_nothing_while_a_helper_runs()
    {
        var updates = Path.Combine(_dataPath, "updates");
        Directory.CreateDirectory(Path.Combine(updates, "staging"));
        Sportarr.Update.UpdateResultFile.Write(Path.Combine(updates, UpdateResultMarker.FileName),
            Sportarr.Update.UpdateResultStatus.RolledBack, NewVersion, "4.1.12.1121", "did not start.", DateTime.UtcNow);
        using var self = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(updates, UpdateStartupMarkers.HelperPidFileName),
            $"{self.Id}\n{self.StartTime.ToUniversalTime().Ticks}\n");

        CreateInstaller().CleanUpAfterSuccessfulUpdate();

        Directory.Exists(Path.Combine(updates, "staging")).Should().BeTrue();
    }

    [Fact]
    public async Task Under_systemd_the_app_says_stop_before_it_stops()
    {
        _environment.Restart = new UpdateRestartTarget(UpdateRestartMode.Systemd, "sportarr.service", null);
        var installer = CreateInstaller();
        var updates = Path.Combine(_dataPath, "updates");
        var install = Task.Run(() => installer.InstallAsync());
        await WaitUntilAsync(() => _launcher.Calls.Count == 1);
        File.WriteAllText(Path.Combine(updates, "swapped"), "ok");

        (await install).Outcome.Should().Be(UpdateInstallOutcome.Started);
        File.ReadAllText(Path.Combine(updates, "stopping")).Should().Be("stop");
        Environment.ExitCode = 0;
    }

    [Fact]
    public async Task Under_systemd_a_failed_stop_signal_keeps_Sportarr_running()
    {
        _environment.Restart = new UpdateRestartTarget(UpdateRestartMode.Systemd, "sportarr.service", null);
        var installer = CreateInstaller();
        var updates = Path.Combine(_dataPath, "updates");
        var install = Task.Run(() => installer.InstallAsync());
        await WaitUntilAsync(() => _launcher.Calls.Count == 1);
        // A folder where the signal file goes makes the write fail.
        Directory.CreateDirectory(Path.Combine(updates, "stopping"));
        File.WriteAllText(Path.Combine(updates, "swapped"), "ok");

        var result = await install;

        result.Outcome.Should().Be(UpdateInstallOutcome.Failed);
        Environment.ExitCode.Should().Be(0);
        _lifetime.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Under_systemd_the_handover_takes_a_fresh_backup()
    {
        _environment.Restart = new UpdateRestartTarget(UpdateRestartMode.Systemd, "sportarr.service", null);
        var installer = CreateInstaller();
        var updates = Path.Combine(_dataPath, "updates");
        var install = Task.Run(() => installer.InstallAsync());
        await WaitUntilAsync(() => _launcher.Calls.Count == 1);
        _safety.Backups.Should().Be(1);
        File.WriteAllText(Path.Combine(updates, "swapped"), "ok");

        (await install).Outcome.Should().Be(UpdateInstallOutcome.Started);
        _safety.Backups.Should().Be(2);
        File.ReadAllText(Path.Combine(updates, "db-backup-path")).Should().Be("/backups/before-update.zip");
        Environment.ExitCode = 0;
    }

    [Fact]
    public async Task A_running_helper_from_an_earlier_start_blocks_an_install()
    {
        var updates = Path.Combine(_dataPath, "updates");
        Directory.CreateDirectory(updates);
        using var self = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(updates, UpdateStartupMarkers.HelperPidFileName),
            $"{self.Id}\n{self.StartTime.ToUniversalTime().Ticks}\n");
        var installer = CreateInstaller();

        installer.IsInstalling.Should().BeTrue();
        (await installer.GetStatusAsync()).Reason.Should().Contain("already being installed");
        (await installer.InstallAsync()).Outcome.Should().Be(UpdateInstallOutcome.Refused);
    }

    [Fact]
    public async Task Under_systemd_a_busy_app_tells_the_helper_to_cancel()
    {
        _environment.Restart = new UpdateRestartTarget(UpdateRestartMode.Systemd, "sportarr.service", null);
        var installer = CreateInstaller();
        var updates = Path.Combine(_dataPath, "updates");
        var install = Task.Run(() => installer.InstallAsync());
        await WaitUntilAsync(() => _launcher.Calls.Count == 1);
        _safety.Busy = "A recording is in progress.";
        File.WriteAllText(Path.Combine(updates, "swapped"), "ok");

        (await install).Outcome.Should().Be(UpdateInstallOutcome.Refused);
        File.ReadAllText(Path.Combine(updates, "stopping")).Should().Be("cancel");
        _lifetime.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("0::/user.slice/user-1000.slice/user@1000.service/app.slice/sportarr.service", true)]
    [InlineData("0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-gnome-terminal.scope", false)]
    [InlineData("0::/system.slice/sportarr.service", false)]
    public void Detects_a_systemd_user_service(string cgroup, bool expected)
    {
        SystemUpdateEnvironment.IsUserManagerCgroup(cgroup).Should().Be(expected);
    }

    [Fact]
    public async Task Automatic_update_waits_after_a_start()
    {
        _environment.ProcessStartUtc = _time.GetUtcNow().UtcDateTime.AddMinutes(-5);

        (await CreateAutomatic().RunIfDueAsync()).Should().Be(AutomaticUpdateOutcome.TooSoonAfterStart);
        _launcher.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Automatic_update_waits_while_Sportarr_is_busy()
    {
        _safety.Busy = "A recording is in progress. Install the update when it has finished.";

        (await CreateAutomatic().RunIfDueAsync()).Should().Be(AutomaticUpdateOutcome.Busy);
        _launcher.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_running_recording_counts_as_busy()
    {
        await using var db = CreateDb();
        db.DvrRecordings.Add(new DvrRecording { Title = "Live", Status = DvrRecordingStatus.Recording });
        await db.SaveChangesAsync();

        (await UpdateSafety.WhatIsBusyAsync(db, CancellationToken.None)).Should().Contain("recording is in progress");
    }

    [Fact]
    public async Task An_import_counts_as_busy_and_a_quiet_moment_does_not()
    {
        await using var db = CreateDb();
        (await UpdateSafety.WhatIsBusyAsync(db, CancellationToken.None)).Should().BeNull();

        db.DownloadQueue.Add(new DownloadQueueItem { Title = "Import", DownloadId = "d1", Status = DownloadStatus.Importing });
        await db.SaveChangesAsync();

        (await UpdateSafety.WhatIsBusyAsync(db, CancellationToken.None)).Should().Contain("download is being imported");
    }

    [Fact]
    public async Task A_running_library_import_counts_as_busy()
    {
        await using var db = CreateDb();
        db.Tasks.Add(new AppTask { Name = "Library Import", CommandName = "LibraryImport", Status = Sportarr.Api.Models.TaskStatus.Running });
        db.Tasks.Add(new AppTask { Name = "RSS Sync", CommandName = "RssSync", Status = Sportarr.Api.Models.TaskStatus.Running });
        await db.SaveChangesAsync();

        (await UpdateSafety.WhatIsBusyAsync(db, CancellationToken.None)).Should().Contain("A library import is running");

        db.Tasks.Single(t => t.CommandName == "LibraryImport").Status = Sportarr.Api.Models.TaskStatus.Completed;
        await db.SaveChangesAsync();
        (await UpdateSafety.WhatIsBusyAsync(db, CancellationToken.None)).Should().BeNull("other tasks do not move library files");
    }

    [Fact]
    public async Task A_running_library_rescan_counts_as_busy()
    {
        await using var db = CreateDb();

        using (LibraryRescanService.MarkRunningForTest())
        {
            (await UpdateSafety.WhatIsBusyAsync(db, CancellationToken.None)).Should().Contain("A library rescan is running");
        }

        (await UpdateSafety.WhatIsBusyAsync(db, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task A_manual_install_is_refused_while_Sportarr_is_busy()
    {
        _safety.Busy = "A recording is in progress. Install the update when it has finished.";

        var result = await CreateInstaller().InstallAsync();

        result.Outcome.Should().Be(UpdateInstallOutcome.Refused);
        result.Message.Should().Be(_safety.Busy);
        _launcher.Calls.Should().BeEmpty();
        _safety.Backups.Should().Be(0);
    }

    [Fact]
    public async Task Automatic_update_tries_a_failed_version_again_only_after_one_day()
    {
        WriteConfig("BuiltIn", automatic: true, lastVersion: NewVersion,
            lastAttempt: _time.GetUtcNow().UtcDateTime.AddHours(-3));

        (await CreateAutomatic().RunIfDueAsync()).Should().Be(AutomaticUpdateOutcome.WaitingToRetry);

        _time.Advance(TimeSpan.FromHours(22));
        (await CreateAutomatic().RunIfDueAsync()).Should().Be(AutomaticUpdateOutcome.Started);
    }

    [Theory]
    [InlineData("BuiltIn", false, AutomaticUpdateOutcome.AutomaticOff)]
    [InlineData("Script", true, AutomaticUpdateOutcome.NotBuiltIn)]
    [InlineData("Docker", true, AutomaticUpdateOutcome.NotBuiltIn)]
    public async Task Automatic_update_needs_automatic_on_and_built_in(string mechanism, bool automatic, AutomaticUpdateOutcome expected)
    {
        WriteConfig(mechanism, automatic);

        (await CreateAutomatic().RunIfDueAsync()).Should().Be(expected);
        _launcher.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Automatic_update_does_nothing_in_a_container()
    {
        _environment.IsContainer = true;

        (await CreateAutomatic().RunIfDueAsync()).Should().Be(AutomaticUpdateOutcome.CannotInstall);
    }

    // Environment detection and the rollback marker

    [Theory]
    [InlineData("0::/system.slice/docker-abc.scope", true)]
    [InlineData("12:pids:/kubepods/burstable/pod1", true)]
    [InlineData("0::/init.scope", false)]
    [InlineData("0::/lxc.payload.sportarr", false)]
    [InlineData(null, false)]
    public void Detects_application_containers_but_not_lxc(string? cgroup, bool expected)
    {
        SystemUpdateEnvironment.CgroupLooksLikeContainer(cgroup).Should().Be(expected);
    }

    [Theory]
    [InlineData("0::/system.slice/sportarr.service\n", "sportarr.service")]
    [InlineData("1:name=systemd:/system.slice/sportarr.service\n", "sportarr.service")]
    [InlineData("0::/user.slice/user-1000.slice/session-2.scope", null)]
    [InlineData("0::/user.slice/user-1000.slice/user@1000.service/app.slice/sportarr.service", null)]
    [InlineData("0::/user.slice/user-1000.slice/user@1000.service/init.scope", null)]
    [InlineData("0::/system.slice/sportarr.service/payload", null)]
    public void Finds_only_a_system_service_from_the_last_cgroup_segment(string cgroup, string? expected)
    {
        SystemUpdateEnvironment.SystemdUnitFromCgroup(cgroup).Should().Be(expected);
    }

    [Fact]
    public void Systemd_needs_kill_mode_process_and_a_restart_rule()
    {
        SystemUpdateEnvironment.SystemdProblem("s.service", new Dictionary<string, string> { ["KillMode"] = "control-group", ["Restart"] = "on-failure" })
            .Should().Contain("KillMode=process");
        SystemUpdateEnvironment.SystemdProblem("s.service", new Dictionary<string, string> { ["KillMode"] = "process", ["Restart"] = "no" })
            .Should().Contain("Restart=on-failure");
        SystemUpdateEnvironment.SystemdProblem("s.service", new Dictionary<string, string> { ["KillMode"] = "process", ["Restart"] = "always", ["RestartUSec"] = "5s" })
            .Should().BeNull();
        SystemUpdateEnvironment.SystemdProblem("s.service", new Dictionary<string, string> { ["KillMode"] = "process", ["Restart"] = "on-abnormal" })
            .Should().Contain("Restart=on-failure", "on-abnormal does not restart after exit code 1");
        SystemUpdateEnvironment.SystemdProblem("s.service", new Dictionary<string, string> { ["KillMode"] = "process", ["Restart"] = "on-failure", ["SuccessExitStatus"] = "1 SIGTERM" })
            .Should().Contain("SuccessExitStatus");
        SystemUpdateEnvironment.SystemdProblem("s.service", new Dictionary<string, string> { ["KillMode"] = "process", ["Restart"] = "on-failure", ["RestartPreventExitStatus"] = "1" })
            .Should().Contain("RestartPreventExitStatus");
        SystemUpdateEnvironment.SystemdProblem("s.service", new Dictionary<string, string> { ["KillMode"] = "process", ["Restart"] = "on-failure", ["SuccessExitStatus"] = "143 10", ["RestartUSec"] = "5s" })
            .Should().BeNull();
        SystemUpdateEnvironment.SystemdProblem("s.service", null).Should().Contain("systemctl");
    }

    [Theory]
    [InlineData("100ms", "5", "10s", "RestartSec=5")]
    [InlineData("1s", "5", "10s", "RestartSec=5")]
    [InlineData("5s", "5", "10s", null)]
    [InlineData("2s", "5", "10s", "start limit")]
    [InlineData("2s", "5", "0", null)]
    [InlineData("2s", "0", "10s", null)]
    [InlineData("5s", "5", "infinity", "start limit")]
    [InlineData("1min 30s", "5", "5min", null)]
    [InlineData("1min 30s", "5", "10min", "start limit")]
    public void Systemd_restart_pause_and_start_limit_must_survive_a_rollback(string restart, string burst, string interval, string? problem)
    {
        var result = SystemUpdateEnvironment.SystemdProblem("s.service", new Dictionary<string, string>
        {
            ["KillMode"] = "process", ["Restart"] = "on-failure",
            ["RestartUSec"] = restart, ["StartLimitBurst"] = burst, ["StartLimitIntervalUSec"] = interval
        });

        if (problem == null)
            result.Should().BeNull();
        else
            result.Should().Contain(problem);
    }

    [Theory]
    [InlineData("abc", "0::/system.slice/sportarr.service", true)]
    [InlineData(null, "0::/system.slice/sportarr.service", false)]
    [InlineData("abc", "0::/user.slice/user-1000.slice/session-2.scope", false)]
    [InlineData("abc", "0::/user.slice/user-1000.slice/user@1000.service/app.slice/sportarr.service", false)]
    public void The_start_and_the_update_agree_on_systemd(string? invocationId, string cgroup, bool expected)
    {
        SystemUpdateEnvironment.IsSystemdSystemService(invocationId, cgroup).Should().Be(expected);
    }

    [Fact]
    public void A_systemd_start_of_a_version_that_is_not_installed_exits_while_the_helper_runs()
    {
        // A crash loop loaded 4.1.13 just before the helper put 4.1.12 back.
        UpdateStartupMarkers.WriteAtomic(Path.Combine(_dataPath, "updates", UpdateStartupMarkers.InstalledVersionFileName), "4.1.12.1121");
        using var self = Process.GetCurrentProcess();
        UpdateStartupMarkers.WriteAtomic(Path.Combine(_dataPath, "updates", UpdateStartupMarkers.HelperPidFileName),
            $"{self.Id}\n{self.StartTime.ToUniversalTime().Ticks}\n");

        UpdateStartupMarkers.PrepareStart(_dataPath, TimeSpan.FromSeconds(5), underSystemd: true, runningVersion: "4.1.13.1130")
            .Should().BeTrue();
        UpdateStartupMarkers.PrepareStart(_dataPath, TimeSpan.FromSeconds(5), underSystemd: false, runningVersion: "4.1.13.1130")
            .Should().BeFalse("in process mode the helper controls the starts");
        UpdateStartupMarkers.PrepareStart(_dataPath, TimeSpan.FromSeconds(5), underSystemd: true, runningVersion: "4.1.12.1121")
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("4.1.12.1121", "4.1.14.1140")] // A hand upgrade after a rollback.
    [InlineData("4.1.13.1130", "4.1.12.1121")] // A hand downgrade after the new version was kept.
    [InlineData("4.1.13.1130", "4.1.13.1130")] // The version that is installed.
    public void An_old_installed_version_file_never_locks_a_start_out(string installed, string running)
    {
        // No helper runs. The file is left over.
        var path = Path.Combine(_dataPath, "updates", UpdateStartupMarkers.InstalledVersionFileName);
        UpdateStartupMarkers.WriteAtomic(path, installed);

        UpdateStartupMarkers.PrepareStart(_dataPath, TimeSpan.FromSeconds(5), underSystemd: true, runningVersion: running)
            .Should().BeFalse();
        File.Exists(path).Should().BeFalse("the file goes away once nothing needs it");
    }

    [Theory]
    [InlineData("Docker", true, "Pull the latest image")]
    [InlineData("Docker", false, "Update by downloading the latest release, or set the update mechanism in Settings > General.")]
    [InlineData("BuiltIn", false, "Install it in Settings > General > Updates.")]
    [InlineData("External", true, "installer or package manager")]
    public void The_update_notice_talks_about_images_only_in_a_container(string mechanism, bool inContainer, string expected)
    {
        var hint = HealthCheckService.UpdateHint(mechanism, inContainer);

        hint.Should().Contain(expected);
        if (!inContainer)
            hint.Should().NotContain("image");
    }

    [Fact]
    public void Package_downloads_name_the_running_version()
    {
        InstallUpdateService.DownloadUserAgent.Should().StartWith($"Sportarr/{Sportarr.Api.Version.GetFullVersion()} ");
        InstallUpdateService.DownloadUserAgent.Should().NotContain("Sportarr/1.0 ");

        var services = new ServiceCollection();
        services.AddHttpClient(InstallUpdateService.DownloadHttpClientName)
            .ConfigureHttpClient(c => c.DefaultRequestHeaders.UserAgent.ParseAdd(InstallUpdateService.DownloadUserAgent));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(InstallUpdateService.DownloadHttpClientName)
            .DefaultRequestHeaders.UserAgent.ToString().Should().StartWith("Sportarr/");
    }

    [Fact]
    public async Task The_status_shows_a_cancelled_install()
    {
        Sportarr.Update.UpdateResultFile.Write(Path.Combine(_dataPath, "updates", UpdateResultMarker.FileName),
            Sportarr.Update.UpdateResultStatus.Cancelled, NewVersion, "4.1.12.1121", "Sportarr was busy.", DateTime.UtcNow);

        (await CreateInstaller().GetStatusAsync()).LastResult!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public void A_good_update_clears_the_installed_version()
    {
        var updates = Path.Combine(_dataPath, "updates");
        UpdateStartupMarkers.WriteAtomic(Path.Combine(updates, UpdateStartupMarkers.InstalledVersionFileName), "4.1.12.1121");
        Sportarr.Update.UpdateResultFile.Write(Path.Combine(updates, UpdateResultMarker.FileName),
            Sportarr.Update.UpdateResultStatus.Succeeded, "4.1.12.1121", "4.1.11.1100", "ok.", DateTime.UtcNow);

        CreateInstaller().CleanUpAfterSuccessfulUpdate();

        File.Exists(Path.Combine(updates, UpdateStartupMarkers.InstalledVersionFileName)).Should().BeFalse();
    }

    [Fact]
    public void Under_systemd_a_live_helper_keeps_its_marker_at_the_limit()
    {
        var marker = Path.Combine(_dataPath, "updates", UpdateStartupMarkers.RestoringFileName);
        using var self = Process.GetCurrentProcess();
        UpdateStartupMarkers.WriteAtomic(marker, $"{self.Id}\n{self.StartTime.ToUniversalTime().Ticks}\n");

        UpdateStartupMarkers.PrepareStart(_dataPath, TimeSpan.FromMilliseconds(200), underSystemd: true, runningVersion: "4.1.12.1121", TimeSpan.FromMilliseconds(20))
            .Should().BeTrue();
        File.Exists(marker).Should().BeTrue("the helper still restores");

        UpdateStartupMarkers.PrepareStart(_dataPath, TimeSpan.FromMilliseconds(200), underSystemd: false, runningVersion: "4.1.12.1121", TimeSpan.FromMilliseconds(20))
            .Should().BeFalse("outside systemd the start goes on");
        File.Exists(marker).Should().BeFalse();
        File.ReadAllText(Path.Combine(_dataPath, "updates", UpdateStartupMarkers.StartingFileName)).Should().Be("starting");
    }

    [Fact]
    public void A_failed_atomic_write_leaves_no_temporary_file()
    {
        var target = Path.Combine(_dataPath, "updates", "stopping");
        Directory.CreateDirectory(target); // A folder in the way makes the move fail.
        var delay = UpdateStartupMarkers.RetryDelay;
        UpdateStartupMarkers.RetryDelay = TimeSpan.FromMilliseconds(1);
        try
        {
            var act = () => UpdateStartupMarkers.WriteAtomic(target, "stop");

            act.Should().Throw<IOException>();
            Directory.EnumerateFiles(Path.Combine(_dataPath, "updates"), "*.tmp").Should().BeEmpty();
        }
        finally
        {
            UpdateStartupMarkers.RetryDelay = delay;
        }
    }

    [Fact]
    public void A_cancelled_update_does_not_stop_automatic_updates()
    {
        Sportarr.Update.UpdateResultFile.Write(Path.Combine(_dataPath, "updates", UpdateResultMarker.FileName),
            Sportarr.Update.UpdateResultStatus.Cancelled, NewVersion, "4.1.12.1121", "Sportarr was busy.", DateTime.UtcNow);

        UpdateResultMarker.Read(Path.Combine(_dataPath, "updates"))!.StillRelevant("4.1.12").Should().BeFalse();
    }

    [Fact]
    public async Task Automatic_update_tries_a_cancelled_version_again()
    {
        Sportarr.Update.UpdateResultFile.Write(Path.Combine(_dataPath, "updates", UpdateResultMarker.FileName),
            Sportarr.Update.UpdateResultStatus.Cancelled, NewVersion, "4.1.12.1121", "Sportarr was busy.", DateTime.UtcNow);

        (await CreateAutomatic().RunIfDueAsync()).Should().Be(AutomaticUpdateOutcome.Started);
    }

    [Fact]
    public void A_rolled_back_update_stays_relevant_until_that_version_runs()
    {
        var updates = Path.Combine(_dataPath, "updates");
        Sportarr.Update.UpdateResultFile.Write(Path.Combine(updates, UpdateResultMarker.FileName),
            Sportarr.Update.UpdateResultStatus.RolledBack, NewVersion, "4.1.12.1121", "did not start", DateTime.UtcNow);

        var marker = UpdateResultMarker.Read(updates)!;

        marker.Status.Should().Be("RolledBack");
        marker.Version.Should().Be(NewVersion);
        marker.Message.Should().Be("did not start");
        marker.StillRelevant("4.1.12").Should().BeTrue();
        marker.StillRelevant("4.1.13").Should().BeFalse();
    }

    [Fact]
    public void A_successful_update_leaves_no_warning()
    {
        var updates = Path.Combine(_dataPath, "updates");
        Sportarr.Update.UpdateResultFile.Write(Path.Combine(updates, UpdateResultMarker.FileName),
            Sportarr.Update.UpdateResultStatus.Succeeded, NewVersion, "4.1.12.1121", "running", DateTime.UtcNow);

        UpdateResultMarker.Read(updates)!.StillRelevant("4.1.12").Should().BeFalse();
    }

    // Helpers

    private InstallUpdateService CreateInstaller(RunningVersion? running = null)
    {
        var configuration = Configuration();
        var configService = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
        var factory = Factory(() => _githubDown
            ? throw new HttpRequestException("No such host is known")
            : Json(_releaseJson));
        var check = new UpdateCheckService(factory, _time, NullLogger<UpdateCheckService>.Instance, running ?? Release);
        return new InstallUpdateService(configService, check, _environment, _launcher, factory, _lifetime.Object,
            configuration, _safety, NullLogger<InstallUpdateService>.Instance);
    }

    private AutomaticUpdateService CreateAutomatic()
    {
        var installer = CreateInstaller();
        var configuration = Configuration();
        var configService = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
        var check = new UpdateCheckService(Factory(() => Json(_releaseJson)), _time, NullLogger<UpdateCheckService>.Instance, Release);
        return new AutomaticUpdateService(configService, check, installer, _environment,
            _safety, _time, NullLogger<AutomaticUpdateService>.Instance);
    }

    private SportarrDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SportarrDbContext>().UseInMemoryDatabase(_dbName).Options);

    private IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = _dataPath })
        .Build();

    private IHttpClientFactory Factory(Func<HttpResponseMessage> github)
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsoluteUri == PackageUrl
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_package) }
            : github());
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
        return factory.Object;
    }

    private void WriteConfig(string mechanism, bool automatic, string urlBase = "", string lastVersion = "", DateTime? lastAttempt = null)
    {
        File.WriteAllText(Path.Combine(_dataPath, "config.xml"),
            "<Config><SettingsUpgradeLevel>999</SettingsUpgradeLevel>" +
            $"<UpdateAutomatically>{automatic.ToString().ToLowerInvariant()}</UpdateAutomatically>" +
            $"<UpdateMechanism>{mechanism}</UpdateMechanism>" +
            $"<UrlBase>{urlBase}</UrlBase>" +
            $"<AutoUpdateLastVersion>{lastVersion}</AutoUpdateLastVersion>" +
            $"<AutoUpdateLastAttemptUtc>{(lastAttempt ?? DateTime.MinValue):yyyy-MM-ddTHH:mm:ss}Z</AutoUpdateLastAttemptUtc>" +
            "</Config>");
    }

    private byte[] BuildPackage(bool includeHelper)
    {
        var source = Path.Combine(_root, $"package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(source, "Sportarr.Update"));
        File.WriteAllText(Path.Combine(source, "Sportarr"), "new app");
        if (includeHelper)
            File.WriteAllText(Path.Combine(source, "Sportarr.Update", "Sportarr.Update"), "new helper");

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            TarFile.CreateFromDirectory(source, gzip, includeBaseDirectory: false);
        }
        Directory.Delete(source, recursive: true);
        return output.ToArray();
    }

    private static string ReleaseJson(byte[] package, long? size = null, string? sha256 = null)
    {
        var hash = sha256 ?? Sha256(package);
        var digest = hash.Length > 0 ? "sha256:" + hash : "";
        return $$"""
        {
          "tag_name": "v{{NewVersion}}",
          "html_url": "https://github.com/Sportarr/Sportarr/releases/tag/v{{NewVersion}}",
          "assets": [
            { "name": "Sportarr-linux-x64-{{NewVersion}}.tar.gz", "browser_download_url": "{{PackageUrl}}",
              "size": {{size ?? package.Length}}, "digest": "{{digest}}" },
            { "name": "Sportarr-win-x64-{{NewVersion}}.zip", "browser_download_url": "https://example.test/win.zip", "size": 1 }
          ]
        }
        """;
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };

    private static string ArgumentValue(IReadOnlyList<string> args, string key) => args[args.ToList().IndexOf(key) + 1];

    private static List<string> AllValues(IReadOnlyList<string> args, string key) =>
        args.Select((a, i) => (a, i)).Where(p => p.a == key).Select(p => args[p.i + 1]).ToList();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition did not become true");
            await Task.Delay(50);
        }
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }

    private sealed class FakeSafety : IUpdateSafety
    {
        public string? Busy { get; set; }
        public int Backups { get; private set; }
        public string? BusyAfterBackup { get; set; }
        public string? Problem { get; set; }

        public Task<string?> WhatIsBusyAsync(CancellationToken cancellationToken) => Task.FromResult(Busy);

        public int FailOnBackup { get; set; }

        public Task<UpdateDatabaseBackup> BackUpDatabaseAsync(string note)
        {
            Backups++;
            if (Backups == FailOnBackup)
                throw new IOException("The disk is full.");
            Busy ??= BusyAfterBackup;
            return Task.FromResult(new UpdateDatabaseBackup("/backups/before-update.zip", "sqlite"));
        }

        public string? BackupProblem() => Problem;
    }

    private sealed class FakeLauncher : IUpdaterLauncher
    {
        public List<(string Executable, IReadOnlyList<string> Arguments)> Calls { get; } = new();

        public bool HelperExited { get; set; }

        public IUpdaterHandle Start(string executable, IReadOnlyList<string> arguments)
        {
            Calls.Add((executable, arguments));
            return new Handle(this);
        }

        private sealed class Handle(FakeLauncher owner) : IUpdaterHandle
        {
            public bool HasExited => owner.HelperExited;
        }
    }

    private sealed class FakeEnvironment : IUpdateEnvironment
    {
        public FakeEnvironment(string installDir)
        {
            InstallDir = installDir;
            ProcessPath = Path.Combine(installDir, "Sportarr");
        }

        public string InstallDir { get; }
        public string WorkingDirectory => "/home/user/start-folder";
        public long? Free { get; set; }
        public long? FreeSpace(string path) => FreeByVolume.TryGetValue(VolumeOf(path), out var f) ? f : Free;
        public Dictionary<string, long> FreeByVolume { get; } = new();
        public Func<string, string> Volume { get; set; } = _ => "/";
        public string VolumeOf(string path) => Volume(path);
        public string? ProcessPath { get; set; }
        public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();
        public int ProcessId => 4242;
        public DateTime ProcessStartUtc { get; set; }
        public string? Os { get; set; } = "linux";
        public string? Arch { get; set; } = "x64";
        public bool IsContainer { get; set; }
        public bool Writable { get; set; } = true;
        public UpdateRestartTarget Restart { get; set; } = new(UpdateRestartMode.Process, "", null);

        public bool IsDirectoryWritable(string path) => Writable;
        public int Refreshes { get; private set; }
        public UpdateRestartTarget? RestartAfterRefresh { get; set; }

        public UpdateRestartTarget DetectRestart(bool refresh = false)
        {
            if (refresh)
            {
                Refreshes++;
                if (RestartAfterRefresh != null)
                    Restart = RestartAfterRefresh;
            }
            return Restart;
        }
    }

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTime(DateTimeOffset now) => _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
