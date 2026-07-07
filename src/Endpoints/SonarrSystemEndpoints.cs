using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using System.Runtime.InteropServices;

namespace Sportarr.Api.Endpoints;

public static class SonarrSystemEndpoints
{
    public static IEndpointRouteBuilder MapSonarrSystemEndpoints(this IEndpointRouteBuilder app, string dataPath)
    {
        // GET /api/v3/system/status - System status (Sonarr v3 API for Prowlarr)
        app.MapGet("/api/v3/system/status", (HttpContext context, ILogger<Program> logger) =>
        {
            // Prowlarr polls this endpoint on a cadence for connectivity checks.
            // Request-logging middleware already records every inbound HTTP call at
            // Info, so a second per-call announcement here is pure duplication.
            logger.LogDebug("[PROWLARR] GET /api/v3/system/status - Prowlarr requesting system status (v3 API)");

            return Results.Ok(new
            {
                // The v3 shim must identify as Sonarr: consumers validate the
                // connection by checking appName (Maintainerr rejects anything
                // where appName.toLowerCase() != "sonarr" with "Unexpected
                // application name returned"). instanceName stays Sportarr -
                // that's the user-visible label, and Sonarr itself returns
                // arbitrary user-chosen instance names there.
                appName = "Sonarr",
                instanceName = "Sportarr",
                version = Sportarr.Api.Version.AppVersion,
                buildTime = DateTime.UtcNow,
                isDebug = false,
                isProduction = true,
                isAdmin = false,
                isUserInteractive = false,
                startupPath = Directory.GetCurrentDirectory(),
                appData = dataPath,
                osName = RuntimeInformation.OSDescription,
                osVersion = Environment.OSVersion.VersionString,
                isNetCore = true,
                isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
                isOsx = RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
                isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true",
                mode = "console",
                branch = "main",
                authentication = "forms",
                sqliteVersion = "3.0",
                urlBase = "",
                runtimeVersion = Environment.Version.ToString(),
                runtimeName = ".NET",
                startTime = DateTime.UtcNow
            });
        });

        // GET /api/v3/health - Health check endpoint for Decypharr validation
        app.MapGet("/api/v3/health", (HttpContext context, ILogger<Program> logger) =>
        {
            logger.LogDebug("[DECYPHARR] GET /api/v3/health - Health check requested");
            return Results.Ok(Array.Empty<object>());
        });

        // GET /api/v3/diskspace - Free space per root folder mount
        // (Maintainerr and dashboards read this alongside system/status).
        app.MapGet("/api/v3/diskspace", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/diskspace");

            var rootFolders = await db.RootFolders.ToListAsync();

            var entries = new List<object>();
            foreach (var folder in rootFolders)
            {
                try
                {
                    var drive = new DriveInfo(folder.Path);
                    entries.Add(new
                    {
                        path = folder.Path,
                        label = drive.VolumeLabel is { Length: > 0 } label ? label : folder.Path,
                        freeSpace = drive.AvailableFreeSpace,
                        totalSpace = drive.TotalSize
                    });
                }
                catch (Exception ex)
                {
                    // An unmounted/unreadable root shouldn't fail the whole
                    // response; report it with zeroed sizes instead.
                    logger.LogDebug(ex, "[V3-COMPAT] Could not read drive info for {Path}", folder.Path);
                    entries.Add(new { path = folder.Path, label = folder.Path, freeSpace = 0L, totalSpace = 0L });
                }
            }

            return Results.Ok(entries);
        });

        return app;
    }
}
