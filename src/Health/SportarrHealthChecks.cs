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
    private readonly ILogger<DiskSpaceHealthCheck> _logger;

    // Thresholds in GB
    private const long UnhealthyThresholdGb = 1;
    private const long DegradedThresholdGb = 5;

    public DiskSpaceHealthCheck(ILogger<DiskSpaceHealthCheck> logger)
    {
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var root = Path.GetPathRoot(currentDir);

            if (string.IsNullOrEmpty(root))
            {
                return Task.FromResult(HealthCheckResult.Degraded("Could not determine disk root"));
            }

            var drive = new DriveInfo(root);

            if (!drive.IsReady)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Disk is not ready"));
            }

            var freeSpaceGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);

            var data = new Dictionary<string, object>
            {
                { "drive", drive.Name },
                { "freeSpaceGb", Math.Round(freeSpaceGb, 2) },
                { "totalSpaceGb", Math.Round(drive.TotalSize / (1024.0 * 1024.0 * 1024.0), 2) }
            };

            if (freeSpaceGb < UnhealthyThresholdGb)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Critical: Only {freeSpaceGb:F1}GB free disk space", null, data));
            }

            if (freeSpaceGb < DegradedThresholdGb)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Low disk space: {freeSpaceGb:F1}GB free", null, data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Disk space OK: {freeSpaceGb:F1}GB free", data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disk space health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy("Failed to check disk space", ex));
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

            var conflicts = new List<string>();
            foreach (var downloadPath in localDownloadPaths)
            {
                var downloadDevice = GetDeviceToken(downloadPath);
                if (downloadDevice == null) continue; // couldn't determine — skip rather than false-alarm

                foreach (var root in rootFolders)
                {
                    var rootDevice = GetDeviceToken(root.Path);
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
    private static string? GetDeviceToken(string path)
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

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);

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
