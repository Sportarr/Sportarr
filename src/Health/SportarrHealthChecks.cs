using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sportarr.Api.Data;

namespace Sportarr.Api.Health;

/// <summary>
/// Health check that verifies database connectivity.
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(IServiceScopeFactory scopeFactory, ILogger<DatabaseHealthCheck> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

            // Test connection and basic query
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);

            if (canConnect)
            {
                return HealthCheckResult.Healthy("Database connection OK");
            }

            return HealthCheckResult.Unhealthy("Database connection failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            return HealthCheckResult.Unhealthy("Database connection failed", ex);
        }
    }
}

/// <summary>
/// Health check that verifies available disk space.
/// </summary>
public class DiskSpaceHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DiskSpaceHealthCheck> _logger;

    // Thresholds in GB
    private const long UnhealthyThresholdGb = 1;
    private const long DegradedThresholdGb = 5;

    public DiskSpaceHealthCheck(IServiceScopeFactory scopeFactory, ILogger<DiskSpaceHealthCheck> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Only the volume the application itself runs from was measured.
            // On any normal install that is not where anything lands, so the
            // check reported plenty of room while the disk taking recordings
            // and library imports was full and nothing warned anybody.
            var paths = new List<string> { Directory.GetCurrentDirectory() };

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
                paths.AddRange(await dbContext.RootFolders
                    .Select(rf => rf.Path)
                    .ToListAsync(cancellationToken).ConfigureAwait(false));

                var configService = scope.ServiceProvider.GetService<Sportarr.Api.Services.Interfaces.IConfigService>();
                if (configService != null)
                {
                    var appConfig = await configService.GetConfigAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(appConfig.DvrRecordingPath))
                    {
                        paths.Add(appConfig.DvrRecordingPath);
                    }
                }
            }
            catch (Exception ex)
            {
                // A database that cannot be read is the database check's
                // problem, not this one's. Fall back to the volume we know.
                _logger.LogDebug(ex, "Disk space check could not read the configured paths");
            }

            var worstFreeGb = double.MaxValue;
            string? worstDrive = null;
            double worstTotalGb = 0;
            var seenDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var checkedAny = false;

            // Every call below is a synchronous syscall against a mount that
            // may be dead. The sibling hardlink check learned this already. A
            // stat on a hung mount blocked the health request itself, and each
            // probe after it piled on another blocked thread. Give the sweep
            // one deadline and each mount a slice of it.
            using var probeDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeDeadline.CancelAfter(TimeSpan.FromSeconds(6));

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (probeDeadline.IsCancellationRequested) break;

                DriveReading? reading;
                try
                {
                    reading = await Task.Run(() => ReadDrive(path, _logger), probeDeadline.Token)
                        .WaitAsync(TimeSpan.FromSeconds(3), probeDeadline.Token)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // That thread stays in the kernel until the mount answers.
                    // Nothing here can free it. The health request no longer
                    // waits on it, which is the part that matters.
                    _logger.LogWarning("Disk space check timed out reading {Path}. The mount is not responding.", path);
                    continue;
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (reading == null) continue;
                if (!seenDrives.Add(reading.Name)) continue;

                checkedAny = true;
                if (reading.FreeGb < worstFreeGb)
                {
                    worstFreeGb = reading.FreeGb;
                    worstDrive = reading.Name;
                    worstTotalGb = reading.TotalGb;
                }
            }

            if (!checkedAny || worstDrive == null)
            {
                return HealthCheckResult.Degraded("Could not determine disk root");
            }

            var data = new Dictionary<string, object>
            {
                { "drive", worstDrive },
                { "freeSpaceGb", Math.Round(worstFreeGb, 2) },
                { "totalSpaceGb", Math.Round(worstTotalGb, 2) },
                { "drivesChecked", seenDrives.Count }
            };

            if (worstFreeGb < UnhealthyThresholdGb)
            {
                return HealthCheckResult.Unhealthy(
                    $"Critical: Only {worstFreeGb:F1}GB free on {worstDrive}", null, data);
            }

            if (worstFreeGb < DegradedThresholdGb)
            {
                return HealthCheckResult.Degraded(
                    $"Low disk space: {worstFreeGb:F1}GB free on {worstDrive}", null, data);
            }

            return HealthCheckResult.Healthy(
                $"Disk space OK: {worstFreeGb:F1}GB free on {worstDrive}", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disk space health check failed");
            return HealthCheckResult.Unhealthy("Failed to check disk space", ex);
        }
    }

    private sealed record DriveReading(string Name, double FreeGb, double TotalGb);

    private static DriveReading? ReadDrive(string path, ILogger logger)
    {
        if (!Directory.Exists(path)) return null;

        var fullPath = Path.GetFullPath(path);

        DriveInfo drive;
        try
        {
            // Ask about the filesystem this folder is really on. On Linux
            // every absolute path roots at "/", so measuring the path root
            // reported the operating system disk for every folder and
            // collapsed them all into one entry. A library on its own mount
            // could be full while this said there was plenty of room.
            drive = new DriveInfo(fullPath);
            if (!drive.IsReady) return null;
        }
        catch (ArgumentException)
        {
            // Windows wants a drive letter rather than a folder.
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root)) return null;

            try
            {
                drive = new DriveInfo(root);
                if (!drive.IsReady) return null;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Disk space check skipped {Path}", path);
                return null;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Disk space check skipped {Path}", path);
            return null;
        }

        try
        {
            const double gb = 1024.0 * 1024.0 * 1024.0;
            return new DriveReading(drive.Name, drive.AvailableFreeSpace / gb, drive.TotalSize / gb);
        }
        catch (Exception ex)
        {
            // The mount can disappear between answering IsReady and being
            // asked for its size.
            logger.LogDebug(ex, "Disk space check could not size {Path}", path);
            return null;
        }
    }
}

/// <summary>
/// Health check that verifies configuration is valid.
/// </summary>
public class ConfigurationHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConfigurationHealthCheck> _logger;

    public ConfigurationHealthCheck(IServiceScopeFactory scopeFactory, ILogger<ConfigurationHealthCheck> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<Sportarr.Api.Services.ConfigService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

            var config = await configService.GetConfigAsync().ConfigureAwait(false);

            var issues = new List<string>();

            // Check for essential configuration
            if (string.IsNullOrEmpty(config.ApiKey))
            {
                issues.Add("API key not configured");
            }

            // Check if any root folders are configured
            var rootFolders = await dbContext.RootFolders.ToListAsync(cancellationToken).ConfigureAwait(false);
            if (rootFolders.Count == 0)
            {
                issues.Add("No root folders configured");
            }
            else
            {
                // Check if any root folder paths don't exist
                var missingFolders = rootFolders.Where(rf => !Directory.Exists(rf.Path)).ToList();
                if (missingFolders.Count > 0)
                {
                    issues.Add($"{missingFolders.Count} root folder(s) not found on disk");
                }
            }

            if (issues.Count > 0)
            {
                return HealthCheckResult.Degraded($"Configuration issues: {string.Join(", ", issues)}");
            }

            return HealthCheckResult.Healthy("Configuration OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Configuration health check failed");
            return HealthCheckResult.Unhealthy("Failed to check configuration", ex);
        }
    }
}

/// <summary>
/// Health check that reports memory usage.
/// </summary>
public class MemoryHealthCheck : IHealthCheck
{
    private readonly ILogger<MemoryHealthCheck> _logger;

    // Thresholds in MB
    private const long DegradedThresholdMb = 1024; // 1GB
    private const long UnhealthyThresholdMb = 2048; // 2GB

    public MemoryHealthCheck(ILogger<MemoryHealthCheck> logger)
    {
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var workingSetMb = process.WorkingSet64 / (1024.0 * 1024.0);
            var privateMemoryMb = process.PrivateMemorySize64 / (1024.0 * 1024.0);

            var gcMemory = GC.GetTotalMemory(false);
            var gcMemoryMb = gcMemory / (1024.0 * 1024.0);

            var data = new Dictionary<string, object>
            {
                { "workingSetMb", Math.Round(workingSetMb, 2) },
                { "privateMemoryMb", Math.Round(privateMemoryMb, 2) },
                { "gcMemoryMb", Math.Round(gcMemoryMb, 2) },
                { "gen0Collections", GC.CollectionCount(0) },
                { "gen1Collections", GC.CollectionCount(1) },
                { "gen2Collections", GC.CollectionCount(2) }
            };

            if (workingSetMb > UnhealthyThresholdMb)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"High memory usage: {workingSetMb:F0}MB working set", null, data));
            }

            if (workingSetMb > DegradedThresholdMb)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Elevated memory usage: {workingSetMb:F0}MB working set", null, data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Memory OK: {workingSetMb:F0}MB working set", data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy("Failed to check memory", ex));
        }
    }
}

/// <summary>
/// Health check that warns when hardlinks are enabled but a download path and a
/// library root folder live on different mounts/volumes, so imports will silently
/// fall back to a full copy. This is the most common cause of "imports are slow"
/// in Docker, where separate bind mounts have different device ids even on one
/// host filesystem. Read-only: it only compares device ids (no files created).
/// </summary>
public class HardlinkHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HardlinkHealthCheck> _logger;

    public HardlinkHealthCheck(IServiceScopeFactory scopeFactory, ILogger<HardlinkHealthCheck> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

            var settings = await dbContext.MediaManagementSettings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (settings == null || !settings.UseHardlinks)
            {
                // Copy mode is intended — nothing to warn about.
                return HealthCheckResult.Healthy("Hardlinks not enabled");
            }

            var mappings = await dbContext.RemotePathMappings.ToListAsync(cancellationToken).ConfigureAwait(false);
            var localDownloadPaths = mappings
                .Select(m => m.LocalPath)
                .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                .Distinct()
                .ToList();

            if (localDownloadPaths.Count == 0)
            {
                // Without a remote path mapping we can't identify the local download
                // path, so we can't compare it against the library here.
                return HealthCheckResult.Healthy("No local download paths to verify");
            }

            var rootFolders = (await dbContext.RootFolders.ToListAsync(cancellationToken).ConfigureAwait(false))
                .Where(rf => Directory.Exists(rf.Path))
                .ToList();
            if (rootFolders.Count == 0)
            {
                return HealthCheckResult.Healthy("No accessible root folders to verify");
            }

            // Resolve each root folder once. The inner loop used to stat every
            // root again for every download path, so a handful of each meant
            // dozens of processes for one health probe.
            var rootDevices = new Dictionary<string, string?>();
            foreach (var root in rootFolders)
            {
                rootDevices[root.Path] = await GetDeviceTokenAsync(root.Path, cancellationToken).ConfigureAwait(false);
            }

            var conflicts = new List<string>();
            foreach (var downloadPath in localDownloadPaths)
            {
                var downloadDevice = await GetDeviceTokenAsync(downloadPath, cancellationToken).ConfigureAwait(false);
                if (downloadDevice == null) continue; // couldn't determine — skip rather than false-alarm

                foreach (var root in rootFolders)
                {
                    var rootDevice = rootDevices[root.Path];
                    if (rootDevice == null) continue;

                    if (!string.Equals(downloadDevice, rootDevice, StringComparison.OrdinalIgnoreCase))
                    {
                        conflicts.Add($"'{downloadPath}' and '{root.Path}' are on different mounts");
                    }
                }
            }

            if (conflicts.Count > 0)
            {
                return HealthCheckResult.Degraded(
                    "Hardlinks enabled but download and library paths are on different mounts, so imports " +
                    "will fall back to slow full copies. Put both under a single shared volume/mount to enable " +
                    "hardlinks. " + string.Join("; ", conflicts.Take(10)));
            }

            return HealthCheckResult.Healthy("Hardlink paths OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hardlink health check failed");
            // Don't fail readiness over a diagnostic check.
            return HealthCheckResult.Healthy("Hardlink check skipped (error)");
        }
    }

    /// <summary>
    /// Return a token identifying the filesystem/volume a path lives on, so two
    /// paths on the same mount compare equal. Unix: the device number from
    /// `stat -c %d`. Windows: the path root (drive). Null if it can't be determined.
    /// </summary>
    private static async Task<string?> GetDeviceTokenAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var full = Path.GetFullPath(path);

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return Path.GetPathRoot(full)?.ToLowerInvariant();
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "stat",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("%d");
            psi.ArgumentList.Add(full);

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return null;

            // The output was read to the end before anything imposed a limit,
            // so the three second wait below it protected nothing. A stat on a
            // hung mount blocked the health request itself, and every probe
            // after it piled another blocked thread and another orphan process
            // on top.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));

            string output;
            try
            {
                var readTask = process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                output = (await readTask.WaitAsync(timeout.Token).ConfigureAwait(false)).Trim();
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return null;
            }

            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Extension methods to register Sportarr health checks.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Add all Sportarr health checks to the service collection.
    /// </summary>
    public static IHealthChecksBuilder AddSportarrHealthChecks(this IHealthChecksBuilder builder)
    {
        return builder
            .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "db", "ready" })
            .AddCheck<DiskSpaceHealthCheck>("disk_space", tags: new[] { "resources" })
            .AddCheck<ConfigurationHealthCheck>("configuration", tags: new[] { "config", "ready" })
            .AddCheck<MemoryHealthCheck>("memory", tags: new[] { "resources" })
            .AddCheck<HardlinkHealthCheck>("hardlinks", tags: new[] { "resources" });
    }
}
