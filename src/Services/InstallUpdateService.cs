using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

public sealed record UpdateStatus(
    string CurrentVersion,
    string? LatestVersion,
    bool Available,
    string Mechanism,
    bool Automatic,
    bool CanInstall,
    string? Reason,
    string? ReleaseUrl,
    bool Installing,
    UpdateLastResult? LastResult = null);

/// <summary>A failed or rolled back update that still matters.</summary>
public sealed record UpdateLastResult(string Status, string Version, string Message, string Time);

public enum UpdateInstallOutcome
{
    Started,
    Refused,
    Failed
}

public sealed record UpdateInstallResult(UpdateInstallOutcome Outcome, string Message);

/// <summary>
/// Starts the update helper. Tests use a fake, so that no process starts.
/// </summary>
public interface IUpdaterLauncher
{
    IUpdaterHandle Start(string executable, IReadOnlyList<string> arguments);
}

/// <summary>Tells if the started helper still runs.</summary>
public interface IUpdaterHandle
{
    bool HasExited { get; }
}

public sealed class ProcessUpdaterLauncher : IUpdaterLauncher
{
    public IUpdaterHandle Start(string executable, IReadOnlyList<string> arguments)
    {
        // Output is not redirected. A pipe to Sportarr closes when Sportarr
        // stops, and the helper must keep running after that.
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);
        var process = Process.Start(psi) ?? throw new InvalidOperationException("The update helper did not start");
        return new ProcessHandle(process);
    }

    private sealed class ProcessHandle(Process process) : IUpdaterHandle
    {
        public bool HasExited
        {
            get
            {
                try { return process.HasExited; }
                catch (InvalidOperationException) { return true; }
            }
        }
    }
}

/// <summary>
/// The built-in update. It downloads the release package for this system,
/// checks it, unpacks it, starts the update helper and stops Sportarr. The
/// helper does the file swap, the restart and the rollback.
/// </summary>
public class InstallUpdateService
{
    public const string HelperFolder = "Sportarr.Update";
    public const string DownloadHttpClientName = "SportarrUpdateDownload";
    internal static TimeSpan SystemdSwapTimeout { get; set; } = TimeSpan.FromMinutes(2);
    internal static TimeSpan SystemdLateSwapTimeout { get; set; } = TimeSpan.FromMinutes(30);


    private readonly ConfigService _configService;
    private readonly UpdateCheckService _updateCheck;
    private readonly IUpdateEnvironment _environment;
    private readonly IUpdaterLauncher _launcher;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConfiguration _configuration;
    private readonly IUpdateSafety _safety;
    private readonly IServer? _server;
    private readonly ILogger<InstallUpdateService> _logger;
    private int _installing;

    // The helper of the last install. While it runs, no second install
    // starts and no clean-up touches the update folders.
    private IUpdaterHandle? _helper;

    public InstallUpdateService(
        ConfigService configService,
        UpdateCheckService updateCheck,
        IUpdateEnvironment environment,
        IUpdaterLauncher launcher,
        IHttpClientFactory httpClientFactory,
        IHostApplicationLifetime lifetime,
        IConfiguration configuration,
        IUpdateSafety safety,
        ILogger<InstallUpdateService> logger,
        IServer? server = null)
    {
        _safety = safety;
        _configService = configService;
        _updateCheck = updateCheck;
        _environment = environment;
        _launcher = launcher;
        _httpClientFactory = httpClientFactory;
        _lifetime = lifetime;
        _configuration = configuration;
        _logger = logger;
        _server = server;
    }

    /// <summary>True when Sportarr runs in a container.</summary>
    public bool RunsInContainer => _environment.IsContainer;

    /// <summary>The User-Agent of package downloads. It names this version.</summary>
    public static string DownloadUserAgent =>
        $"Sportarr/{Sportarr.Api.Version.GetFullVersion()} (https://github.com/Sportarr/Sportarr)";

    /// <summary>
    /// True while an install runs here, or while its helper still works.
    /// The helper is done when it has written its result or has ended. A
    /// new install removes the old result first, so any result is new.
    /// </summary>
    public bool IsInstalling =>
        Volatile.Read(ref _installing) == 1 ||
        (_helper is { HasExited: false } && !File.Exists(Path.Combine(UpdatesDir, UpdateResultMarker.FileName))) ||
        UpdateStartupMarkers.IsHelperRunning(DataDir);

    public string DataDir => Path.GetFullPath(
        _configuration["Sportarr:DataPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "data"));

    public string UpdatesDir => Path.Combine(DataDir, "updates");

    public async Task<UpdateStatus> GetStatusAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var config = await _configService.GetConfigAsync();
        var check = await _updateCheck.CheckAsync(forceRefresh, cancellationToken);
        var reason = GetBlockReason(config, check);
        return new UpdateStatus(
            _updateCheck.Running.FullVersion,
            check.Latest?.Version,
            check.Available,
            UpdateMechanisms.Normalize(config.UpdateMechanism).ToLowerInvariant(),
            config.UpdateAutomatically,
            reason == null,
            reason,
            check.Latest?.PageUrl,
            IsInstalling,
            LastFailedResult());
    }

    // The page shows a failed or cancelled update at once, without a wait
    // for a new version.
    private UpdateLastResult? LastFailedResult()
    {
        var marker = UpdateResultMarker.Read(UpdatesDir);
        var cancelled = marker?.Status == "Cancelled" &&
                        LatestReleaseClient.IsNewer(marker.Version, _updateCheck.Running.BaseVersion);
        return marker != null && (cancelled || marker.StillRelevant(_updateCheck.Running.BaseVersion))
            ? new UpdateLastResult(marker.Status, marker.Version, marker.Message, marker.Time)
            : null;
    }

    /// <summary>
    /// Returns why this computer cannot use the built-in update, or null.
    /// These reasons do not depend on GitHub. The health check shows them
    /// when the mechanism is Built-in.
    /// </summary>
    public string? GetEnvironmentProblem()
    {
        if (_environment.IsContainer)
            return "Sportarr runs in a container. Pull the new image and recreate the container instead.";
        if (!_updateCheck.Running.IsReleaseBuild)
            return "This is a development build. The built-in update only works for release builds.";

        var processPath = _environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath) ||
            !string.Equals(Path.GetDirectoryName(processPath), _environment.InstallDir, PathComparison))
            return "Sportarr was not started from its own program file, so it cannot replace it.";

        var restart = _environment.DetectRestart();
        if (restart.Problem != null)
            return restart.Problem;

        if (!File.Exists(HelperPath(_environment.InstallDir)))
            return "The update helper is missing from the install folder. Install the latest release by hand once.";
        if (!_environment.IsDirectoryWritable(_environment.InstallDir))
            return $"Sportarr cannot write to its install folder {_environment.InstallDir}.";
        return _safety.BackupProblem();
    }

    /// <summary>
    /// Returns why the built-in update cannot install now, or null.
    /// </summary>
    public string? GetBlockReason(Config config, UpdateCheckResult check, bool ignoreInstalling = false)
    {
        if (UpdateMechanisms.Normalize(config.UpdateMechanism) != UpdateMechanisms.BuiltIn)
            return "The update mechanism is not Built-in.";
        var problem = GetEnvironmentProblem();
        if (problem != null)
            return problem;
        if (check.Latest == null)
            return "GitHub cannot be reached right now. Sportarr tries again later.";
        if (!check.Available)
            return "Sportarr is up to date.";
        var asset = LatestReleaseClient.SelectAsset(check.Latest.Assets, _environment.Os, _environment.Arch);
        if (asset == null)
            return $"The release has no package for this system ({_environment.Os ?? "unknown"}-{_environment.Arch ?? "unknown"}).";
        if (string.IsNullOrEmpty(asset.Sha256) || asset.Size <= 0)
            return "The release does not list a checksum and size for its package, so Sportarr cannot check the download.";
        if (!LatestReleaseClient.DownloadUrlAllowed(asset.DownloadUrl))
            return "The package link of the release does not use HTTPS.";
        if (IsInstalling && !ignoreInstalling)
            return "An update is already being installed.";
        return null;
    }

    public async Task<UpdateInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (IsInstalling || Interlocked.CompareExchange(ref _installing, 1, 0) != 0)
            return new UpdateInstallResult(UpdateInstallOutcome.Refused, "An update is already being installed.");

        var handedOver = false;
        var swappedOk = false;
        try
        {
            var config = await _configService.GetConfigAsync();
            var check = await _updateCheck.CheckAsync(forceRefresh: true, cancellationToken);
            var reason = GetBlockReason(config, check, ignoreInstalling: true)
                ?? await _safety.WhatIsBusyAsync(cancellationToken);
            if (reason != null)
                return new UpdateInstallResult(UpdateInstallOutcome.Refused, reason);

            var release = check.Latest!;
            var asset = LatestReleaseClient.SelectAsset(release.Assets, _environment.Os, _environment.Arch)!;
            var spaceProblem = CheckSpaceForDownload(asset.Size);
            if (spaceProblem != null)
                return new UpdateInstallResult(UpdateInstallOutcome.Refused, spaceProblem);
            _logger.LogInformation("[Update] Installing Sportarr {Version} from {Asset}", release.Version, asset.Name);

            var downloadDir = Path.Combine(UpdatesDir, "download");
            var stagingDir = Path.Combine(UpdatesDir, "staging");
            var helperDir = Path.Combine(UpdatesDir, "updater");
            ResetDirectory(downloadDir);
            ResetDirectory(stagingDir);

            var archive = Path.Combine(downloadDir, asset.Name);
            await DownloadAsync(asset, archive, cancellationToken);
            Extract(archive, stagingDir);

            var exeName = Path.GetFileName(_environment.ProcessPath!);
            var problem = CheckPackage(stagingDir, exeName);
            if (problem != null)
                return new UpdateInstallResult(UpdateInstallOutcome.Failed, problem);

            // The download can go now. Then check the real needs.
            Directory.Delete(downloadDir, recursive: true);
            var installSpaceProblem = CheckSpaceForInstall(stagingDir);
            if (installSpaceProblem != null)
                return new UpdateInstallResult(UpdateInstallOutcome.Refused, installSpaceProblem);

            // The helper runs from a copy, so that it can replace its own
            // files in the install folder.
            ResetDirectory(helperDir);
            CopyDirectory(Path.Combine(stagingDir, HelperFolder), helperDir);

            // The normal backup covers SQLite and Postgres. The helper uses
            // it to put a migrated SQLite database back on a rollback.
            var backup = await _safety.BackUpDatabaseAsync($"Before the update to {release.Version}");
            _logger.LogInformation("[Update] Database backed up to {Path}", backup.Path);

            // A recording can start while the download runs. Ask again just
            // before Sportarr stops.
            var busyNow = await _safety.WhatIsBusyAsync(cancellationToken);
            if (busyNow != null)
                return new UpdateInstallResult(UpdateInstallOutcome.Refused, busyNow);

            // Look again. The service setup can have changed since the start.
            var restart = _environment.DetectRestart(refresh: true);
            if (restart.Problem != null)
                return new UpdateInstallResult(UpdateInstallOutcome.Refused, restart.Problem);
            // Files of an earlier install must not count for this one.
            var signal = Path.Combine(UpdatesDir, "swapped");
            foreach (var old in new[] { "db-backup-path", "swapped", "stopping", UpdateResultMarker.FileName })
                File.Delete(Path.Combine(UpdatesDir, old));

            var arguments = BuildHelperArguments(release.Version, stagingDir, restart, config, backup);
            _logger.LogInformation("[Update] Starting the update helper. Sportarr stops now and the helper starts it again");
            _helper = _launcher.Start(HelperPath(helperDir, inHelperFolder: false), arguments);

            if (restart.Mode == UpdateRestartMode.Systemd)
            {
                // The helper swaps the files first. A failure exit code then
                // makes systemd start the new version.
                var swapped = await WaitForSignalAsync(signal, SystemdSwapTimeout, cancellationToken);
                if (swapped == null)
                {
                    // The helper still works. The install stays marked as
                    // running until it reports, so a second one cannot start.
                    handedOver = true;
                    _ = FinishSystemdHandoverLaterAsync(signal);
                    return new UpdateInstallResult(UpdateInstallOutcome.Started,
                        "The update helper is still copying files. Sportarr restarts when it is done. See logs/update.log.");
                }
                if (swapped != "ok")
                {
                    WriteStopping("cancel");
                    return new UpdateInstallResult(UpdateInstallOutcome.Failed,
                        "The update helper could not put the new files in place. See logs/update.log.");
                }
                swappedOk = true;
                var busyAtHandover = await _safety.WhatIsBusyAsync(cancellationToken);
                if (busyAtHandover != null)
                {
                    // The helper then puts the old files back.
                    WriteStopping("cancel");
                    return new UpdateInstallResult(UpdateInstallOutcome.Refused, busyAtHandover);
                }

                // The data can have changed while the helper copied files. A
                // fresh backup makes a rollback lose nothing.
                var handoverBackup = await _safety.BackUpDatabaseAsync($"Before the update to {release.Version}, at the handover");
                UpdateStartupMarkers.WriteAtomic(Path.Combine(UpdatesDir, "db-backup-path"), handoverBackup.Path);

                Environment.ExitCode = 1;
                if (!WriteStopping("stop"))
                {
                    // Without "stop" the helper would wait in vain. Sportarr
                    // keeps running, and the helper puts the old files back.
                    Environment.ExitCode = 0;
                    return new UpdateInstallResult(UpdateInstallOutcome.Failed,
                        "Sportarr could not tell the update helper that it stops, so it keeps running. See logs/update.log.");
                }
            }

            handedOver = true;
            StopSoon();
            return new UpdateInstallResult(UpdateInstallOutcome.Started,
                $"Installing Sportarr {release.Version}. Sportarr restarts in a moment.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The helper waits for an answer after the swap. Tell it to put
            // the old files back now.
            if (swappedOk && !handedOver)
            {
                Environment.ExitCode = 0;
                WriteStopping("cancel");
            }
            _logger.LogWarning(ex, "[Update] The update could not be installed");
            return new UpdateInstallResult(UpdateInstallOutcome.Failed, $"The update could not be installed: {ex.Message}");
        }
        finally
        {
            if (!handedOver)
                Interlocked.Exchange(ref _installing, 0);
        }
    }

    /// <summary>
    /// Builds the helper command line. The helper checks /ping on the same
    /// port and URL base to see if the new version runs.
    /// </summary>
    internal List<string> BuildHelperArguments(string version, string stagingDir, UpdateRestartTarget restart, Config config, UpdateDatabaseBackup? backup = null)
    {
        var args = new List<string>
        {
            "--pid", _environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--pid-start", _environment.ProcessStartUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--install-dir", _environment.InstallDir,
            "--staging-dir", stagingDir,
            "--data-dir", DataDir,
            "--backup-dir", Path.Combine(UpdatesDir, "backup"),
            "--restart", restart.Mode.ToString(),
            "--app-exe", _environment.ProcessPath!,
            "--working-dir", _environment.WorkingDirectory,
            "--health-url", HealthUrl(config),
            "--version", version,
            "--previous-version", _updateCheck.Running.FullVersion
        };
        if (!string.IsNullOrEmpty(restart.ServiceName))
        {
            args.Add("--service-name");
            args.Add(restart.ServiceName);
        }
        if (backup != null)
        {
            args.AddRange(new[] { "--db-backup", backup.Path, "--db-provider", backup.Provider, "--db-path", Path.Combine(DataDir, "sportarr.db") });
        }
        foreach (var argument in RestartArguments())
        {
            args.Add("--app-arg");
            args.Add(argument);
        }
        return args;
    }

    /// <summary>
    /// The arguments for the restart. When the original start did not name
    /// the data folder, "-data" is added, so that the new process finds the
    /// same data even when its environment differs.
    /// </summary>
    internal List<string> RestartArguments()
    {
        var args = _environment.Arguments.ToList();
        var namesData = args.Any(a => a.Equals("-data", StringComparison.OrdinalIgnoreCase) ||
                                      a.Equals("--data", StringComparison.OrdinalIgnoreCase) ||
                                      a.StartsWith("-data=", StringComparison.OrdinalIgnoreCase) ||
                                      a.StartsWith("--data=", StringComparison.OrdinalIgnoreCase));
        if (!namesData)
        {
            args.Add("-data");
            args.Add(DataDir);
        }
        return args;
    }

    internal string? CheckSpaceForDownload(long packageSize)
    {
        Directory.CreateDirectory(UpdatesDir);
        // The download and its unpacked copy are both in the data folder.
        return CheckVolumes(new Dictionary<string, long> { [UpdatesDir] = packageSize * 2 });
    }

    /// <summary>
    /// Checks the free space that the install really needs. The install
    /// folder takes the new files. The data folder takes the backup of the
    /// files that they replace, a copy of the database for a rollback and
    /// the copy of the helper. Two folders on one volume add up.
    /// </summary>
    internal string? CheckSpaceForInstall(string stagingDir)
    {
        long staged = 0, replaced = 0;
        foreach (var file in Directory.EnumerateFiles(stagingDir, "*", SearchOption.AllDirectories))
        {
            var length = new FileInfo(file).Length;
            staged += length;
            var installed = new FileInfo(Path.Combine(_environment.InstallDir, Path.GetRelativePath(stagingDir, file)));
            if (installed.Exists)
                replaced += installed.Length;
        }

        var helper = Directory.EnumerateFiles(Path.Combine(stagingDir, HelperFolder), "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
        var database = new[] { "sportarr.db", "sportarr.db-wal" }
            .Select(n => new FileInfo(Path.Combine(DataDir, n)))
            .Where(f => f.Exists)
            .Sum(f => f.Length);

        return CheckVolumes(new Dictionary<string, long>
        {
            [_environment.InstallDir] = staged,
            [UpdatesDir] = replaced + database + helper
        });
    }

    private string? CheckVolumes(Dictionary<string, long> needs)
    {
        foreach (var volume in needs.GroupBy(n => _environment.VolumeOf(n.Key)))
        {
            var needed = volume.Sum(n => n.Value);
            var free = _environment.FreeSpace(volume.First().Key);
            if (free != null && free < needed)
            {
                var folders = string.Join(" and ", volume.Select(n => n.Key));
                return $"There is not enough free space for the update. It needs about {needed / 1048576 + 1} MB on the volume of {folders}, and {free / 1048576} MB are free.";
            }
        }
        return null;
    }

    /// <summary>
    /// Removes the download, the unpacked copy, the helper copy and the file
    /// backup after an update that worked. Runs on a later health pass, when
    /// the helper has ended.
    /// </summary>
    public void CleanUpAfterSuccessfulUpdate()
    {
        var marker = UpdateResultMarker.Read(UpdatesDir);
        if (marker == null || IsInstalling || UpdateStartupMarkers.IsHelperRunning(DataDir))
            return;

        // After a good update or a rollback the old files are not needed. A
        // failure that left the new files keeps the backup of the old ones.
        // The database backup is a normal backup and stays in Backups.
        string[] folders = marker.Status switch
        {
            "Succeeded" when !LatestReleaseClient.IsNewer(marker.Version, _updateCheck.Running.BaseVersion)
                => new[] { "download", "staging", "updater", "backup" },
            "RolledBack" or "Cancelled" => new[] { "download", "staging", "updater", "backup" },
            "Failed" => new[] { "download", "staging", "updater" },
            _ => Array.Empty<string>()
        };

        if (marker.Status == "Succeeded")
            UpdateStartupMarkers.ClearInstalledVersionIfCurrent(DataDir, _updateCheck.Running.FullVersion);

        foreach (var folder in folders)
        {
            var path = Path.Combine(UpdatesDir, folder);
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                    _logger.LogInformation("[Update] Removed {Path} after the update to {Version}", path, marker.Version);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "[Update] Could not remove {Path}. Sportarr tries again later", path);
            }
        }
    }

    internal string HealthUrl(Config config)
    {
        var address = ListeningAddress() ?? $"http://127.0.0.1:{config.Port}";
        var urlBase = (config.UrlBase ?? "").Trim().Trim('/');
        return address.TrimEnd('/') + (urlBase.Length > 0 ? "/" + urlBase : "") + "/ping";
    }

    // The address that Kestrel really listens on. A wildcard host becomes
    // the loopback address, because the helper runs on the same computer.
    private string? ListeningAddress()
    {
        var addresses = _server?.Features.Get<IServerAddressesFeature>()?.Addresses;
        var http = addresses?.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        if (http == null || !Uri.TryCreate(http.Replace("://+", "://0.0.0.0").Replace("://*", "://0.0.0.0"), UriKind.Absolute, out var uri))
            return null;
        var host = uri.Host is "0.0.0.0" or "[::]" or "::" or "localhost" ? "127.0.0.1" : uri.Host;
        if (uri.HostNameType == UriHostNameType.IPv6 && host != "127.0.0.1")
            host = $"[{uri.Host.Trim('[', ']')}]";
        return $"http://{host}:{uri.Port}";
    }

    internal static string? CheckPackage(string stagingDir, string exeName)
    {
        if (!File.Exists(Path.Combine(stagingDir, exeName)))
            return $"The downloaded package does not contain {exeName}.";
        if (!File.Exists(HelperPath(stagingDir)))
            return "The downloaded package does not contain the update helper.";
        return null;
    }

    internal static string HelperPath(string root, bool inHelperFolder = true)
    {
        var name = OperatingSystem.IsWindows() ? "Sportarr.Update.exe" : "Sportarr.Update";
        return inHelperFolder ? Path.Combine(root, HelperFolder, name) : Path.Combine(root, name);
    }

    private async Task DownloadAsync(ReleaseAsset asset, string target, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient(DownloadHttpClientName);
        using var response = await http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var file = File.Create(target))
        {
            await CopyAtMostAsync(source, file, asset.Size, cancellationToken);
        }

        VerifyDownload(target, asset);
        _logger.LogInformation("[Update] Downloaded {Asset} ({Bytes} bytes)", asset.Name, new FileInfo(target).Length);
    }

    /// <summary>
    /// Copies the stream, but stops when it is longer than the size that the
    /// release lists. A wrong or hostile server cannot fill the disk.
    /// </summary>
    internal static async Task CopyAtMostAsync(Stream source, Stream destination, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > limit)
                throw new InvalidDataException($"The download is larger than the {limit} bytes that the release lists.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    internal static void VerifyDownload(string path, ReleaseAsset asset)
    {
        var length = new FileInfo(path).Length;
        if (asset.Size > 0 && length != asset.Size)
            throw new InvalidDataException($"The download has {length} bytes, but the release lists {asset.Size}.");

        if (!string.IsNullOrEmpty(asset.Sha256))
        {
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (hash != asset.Sha256)
                throw new InvalidDataException("The download does not match the checksum that the release lists.");
        }
    }

    internal static void Extract(string archive, string destination)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: true);
            return;
        }

        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        ExtractTar(gzip, destination);
    }

    /// <summary>
    /// Unpacks only regular files and folders. Links, devices and pipes are
    /// skipped, because a release package has none. Each file keeps its
    /// mode, so the executables stay executable. An entry whose path leaves
    /// the destination is refused.
    /// </summary>
    internal static void ExtractTar(Stream tar, string destination)
    {
        var root = Path.GetFullPath(destination);
        var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        using var reader = new TarReader(tar);
        while (reader.GetNextEntry() is { } entry)
        {
            var target = Path.GetFullPath(Path.Combine(root, entry.Name));
            if (target != root && !target.StartsWith(rootWithSeparator, StringComparison.Ordinal))
                throw new InvalidDataException($"The package entry {entry.Name} points outside the update folder.");

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(target);
                    break;
                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                case TarEntryType.ContiguousFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                    break;
            }
        }
    }

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static async Task<string?> WaitForSignalAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            // An empty or unreadable file is not an answer yet.
            var value = UpdateStartupMarkers.ReadText(path);
            if (value != null)
                return value;
            await Task.Delay(500, cancellationToken);
        }
        return null;
    }

    // Waits longer for a slow helper. "ok" stops Sportarr as usual. Anything
    // else, or no answer, ends the install so that it can be tried again.
    private async Task FinishSystemdHandoverLaterAsync(string signal)
    {
        var lateSwappedOk = false;
        try
        {
            var swapped = await WaitForSignalAsync(signal, SystemdLateSwapTimeout, CancellationToken.None);
            lateSwappedOk = swapped == "ok";
            if (swapped == "ok")
            {
                // Time has passed since the first checks. Ask again, and
                // back up again, so that a rollback loses no new data. If
                // Sportarr is busy now, it keeps running, and the helper
                // puts the old files back when Sportarr does not stop.
                var busy = await _safety.WhatIsBusyAsync(CancellationToken.None);
                if (busy != null)
                {
                    _logger.LogWarning("[Update] The update helper is ready, but Sportarr keeps running and the update is cancelled: {Busy}", busy);
                    WriteStopping("cancel");
                    Interlocked.Exchange(ref _installing, 0);
                    return;
                }
                else
                {
                    var backup = await _safety.BackUpDatabaseAsync("Before the update, after a slow handover");
                    UpdateStartupMarkers.WriteAtomic(Path.Combine(UpdatesDir, "db-backup-path"), backup.Path);
                    Environment.ExitCode = 1;
                    if (WriteStopping("stop"))
                    {
                        StopSoon();
                        return;
                    }
                    Environment.ExitCode = 0;
                    _logger.LogWarning("[Update] Sportarr could not tell the update helper that it stops, so it keeps running");
                    Interlocked.Exchange(ref _installing, 0);
                    return;
                }
            }
            _logger.LogWarning("[Update] The update helper did not put the new files in place. See logs/update.log");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Update] Waiting for the update helper failed");
            if (lateSwappedOk)
            {
                Environment.ExitCode = 0;
                WriteStopping("cancel");
            }
        }
        Interlocked.Exchange(ref _installing, 0);
    }

    // Tells the helper under systemd that Sportarr stops now ("stop"), or
    // that it keeps running ("cancel"). The helper starts its exit wait only
    // after "stop".
    // Returns false when the file cannot be written.
    private bool WriteStopping(string value)
    {
        try
        {
            UpdateStartupMarkers.WriteAtomic(Path.Combine(UpdatesDir, "stopping"), value);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[Update] Could not tell the helper that Sportarr {Value}s", value);
            return false;
        }
    }

    // The API answer must reach the browser before Sportarr stops.
    private void StopSoon()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            _lifetime.StopApplication();
        });
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
