using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

public static class SystemBackupEndpoints
{
    public static IEndpointRouteBuilder MapSystemBackupEndpoints(this IEndpointRouteBuilder app)
    {
        // Surface the actual error to the UI instead of a generic
        // 500. Without this the frontend shows "Failed to fetch
        // backups" with no clue whether the cause is a missing
        // folder, a permission issue on /config/Backups, or a
        // bogus BackupFolder set in config.xml.
        app.MapGet("/api/system/backup", async (BackupService backupService, ILogger<BackupService> logger) =>
        {
            try
            {
                var backups = await backupService.GetBackupsAsync();
                return Results.Ok(backups);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(ex, "[Backup] Permission denied reading backup folder");
                return Results.Problem(
                    title: "Backup folder not accessible",
                    detail: $"Sportarr can't read or write the backup folder. Check that the path is writable by the container user (PUID/PGID). Underlying error: {ex.Message}",
                    statusCode: 500);
            }
            catch (DirectoryNotFoundException ex)
            {
                logger.LogError(ex, "[Backup] Backup folder path is invalid");
                return Results.Problem(
                    title: "Backup folder path invalid",
                    detail: $"The configured backup folder doesn't exist and the parent directory is missing. Set a valid absolute path under Settings -> General -> Backup, or leave it empty to use the default. Underlying error: {ex.Message}",
                    statusCode: 500);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Backup] Unexpected error listing backups");
                return Results.Problem(
                    title: "Failed to list backups",
                    detail: ex.Message,
                    statusCode: 500);
            }
        });

        app.MapPost("/api/system/backup", async (BackupService backupService, ILogger<BackupService> logger, string? note) =>
        {
            try
            {
                var backup = await backupService.CreateBackupAsync(note);
                return Results.Ok(backup);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(ex, "[Backup] Permission denied creating backup");
                return Results.Problem(
                    title: "Backup folder not writable",
                    detail: $"Sportarr can't write to the backup folder. Check that the path is writable by the container user (PUID/PGID). Underlying error: {ex.Message}",
                    statusCode: 500);
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "[Backup] IO error creating backup");
                return Results.Problem(
                    title: "Backup write failed",
                    detail: $"Disk I/O error while writing the backup zip. Most commonly: the backup folder is full, on a read-only mount, or the database file is locked by another process. Underlying error: {ex.Message}",
                    statusCode: 500);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Backup] Unexpected error creating backup");
                return Results.Problem(
                    title: "Failed to create backup",
                    detail: ex.Message,
                    statusCode: 500);
            }
        });

        // POST /api/system/backup/restore/{backupName}
        //
        // Body (optional): { "scope": ["db", "config"] }
        //   * Omit / empty -> restore everything (legacy behavior).
        //   * scope = ["config"] -> rewrite config.xml only, leave db alone.
        //
        // On success the response carries the RestoreReport id so the UI
        // can navigate to /system/restore-reports/{id} and watch
        // reconciliation progress. The actual file-existence check
        // happens after this returns; the report transitions from
        // "pending" -> "completed" / "failed" out of band.
        app.MapPost("/api/system/backup/restore/{backupName}", async (
            string backupName,
            HttpRequest request,
            BackupService backupService,
            RestoreReconciliationService reconcile,
            IServiceProvider rootProvider,
            ILogger<BackupService> logger) =>
        {
            try
            {
                IReadOnlySet<string>? scope = null;
                if (request.ContentLength > 0
                    && (request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    using var doc = await System.Text.Json.JsonDocument.ParseAsync(request.Body);
                    if (doc.RootElement.TryGetProperty("scope", out var scopeEl)
                        && scopeEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var item in scopeEl.EnumerateArray())
                        {
                            if (item.ValueKind == System.Text.Json.JsonValueKind.String
                                && item.GetString() is { } s
                                && !string.IsNullOrWhiteSpace(s))
                            {
                                set.Add(s.Trim());
                            }
                        }
                        if (set.Count > 0) scope = set;
                    }
                }

                var manifest = await backupService.RestoreBackupAsync(backupName, scope);

                // BeginAsync creates the pending report row synchronously so
                // we can hand the id back to the client immediately. The
                // actual scan + polling happens in a fire-and-forget task
                // with its own service scope; without that, the request-
                // scoped DbContext would be disposed the moment we return
                // and the background work would crash. Errors inside the
                // background scope land in the report row's Notes field.
                var reportId = await reconcile.BeginAsync(backupName, manifest);
                _ = Task.Run(async () =>
                {
                    using var bgScope = rootProvider.CreateScope();
                    var bgReconcile = bgScope.ServiceProvider
                        .GetRequiredService<RestoreReconciliationService>();
                    try
                    {
                        await bgReconcile.RunAsync(reportId);
                    }
                    catch (Exception ex)
                    {
                        var bgLogger = bgScope.ServiceProvider
                            .GetRequiredService<ILogger<BackupService>>();
                        bgLogger.LogError(ex,
                            "[Backup] Background reconciliation failed for report {Id}",
                            reportId);
                    }
                });

                return Results.Ok(new
                {
                    message = "Backup restored. Reconciliation running -- watch the report for completion.",
                    restoreReportId = reportId,
                });
            }
            catch (Sportarr.Api.Exceptions.BackupRestoreException ex)
            {
                logger.LogWarning(ex, "[Backup] Refused cross-provider restore for {Name}", backupName);
                return Results.Problem(
                    title: "Backup provider mismatch",
                    detail: ex.Message,
                    statusCode: 400);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Backup] Restore failed for {Name}", backupName);
                return Results.Problem(ex.Message);
            }
        });

        // GET /api/system/backup/preview/{backupName}
        // Return the backup's manifest.json (if present) so the UI can
        // show "you're about to restore N files, last touched on host X
        // running Sportarr Y, configured root folder was Z" before the
        // admin commits. Backups produced before manifest.json was added
        // return null for manifest.
        app.MapGet("/api/system/backup/preview/{backupName}", async (
            string backupName,
            BackupService backupService) =>
        {
            try
            {
                var manifest = await backupService.ReadManifestAsync(backupName);
                return Results.Ok(new { manifest });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { message = "Backup file not found" });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // POST /api/system/backup/upload
        // Multipart upload of a backup zip from another machine. Saves
        // into the configured backup folder so the existing list /
        // restore flow picks it up. Up to 2 GB (covers very large
        // libraries without forcing the user to split).
        app.MapPost("/api/system/backup/upload", async (HttpRequest request, BackupService backupService) =>
        {
            try
            {
                if (!request.HasFormContentType)
                {
                    return Results.BadRequest(new { message = "Expected multipart/form-data" });
                }
                var form = await request.ReadFormAsync();
                var file = form.Files["backup"] ?? form.Files.FirstOrDefault();
                if (file == null || file.Length == 0)
                {
                    return Results.BadRequest(new { message = "No backup file in upload" });
                }

                await using var stream = file.OpenReadStream();
                var info = await backupService.SaveUploadedBackupAsync(stream, file.FileName);
                return Results.Ok(info);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        }).DisableAntiforgery();

        // GET /api/system/restore-reports
        app.MapGet("/api/system/restore-reports", async (SportarrDbContext db) =>
        {
            var reports = await db.RestoreReports
                .AsNoTracking()
                .OrderByDescending(r => r.StartedAt)
                .Take(50)
                .ToListAsync();
            return Results.Ok(reports);
        });

        // GET /api/system/restore-reports/{id}
        app.MapGet("/api/system/restore-reports/{id:int}", async (int id, SportarrDbContext db) =>
        {
            var report = await db.RestoreReports
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id);
            if (report == null)
                return Results.NotFound(new { message = "Restore report not found" });
            return Results.Ok(report);
        });

        // GET /api/library/remap/preview
        // Detect a likely path-prefix drift across missing files.
        // Returns a PathRemapPreview the UI renders as "we suggest
        // rewriting X to Y for N rows" with a confirm button.
        app.MapGet("/api/library/remap/preview", async (PathRemapService remap) =>
        {
            try
            {
                var preview = await remap.DetectAsync();
                return Results.Ok(preview);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // POST /api/library/remap/apply
        // Apply a remap. Body: { "from": "...", "to": "..." }. Returns
        // the number of EventFile rows that were rewritten.
        app.MapPost("/api/library/remap/apply", async (HttpRequest request, PathRemapService remap, DiskScanService disk) =>
        {
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(request.Body);
                if (!doc.RootElement.TryGetProperty("from", out var fromEl)
                    || !doc.RootElement.TryGetProperty("to", out var toEl)
                    || fromEl.ValueKind != System.Text.Json.JsonValueKind.String
                    || toEl.ValueKind != System.Text.Json.JsonValueKind.String)
                {
                    return Results.BadRequest(new { message = "Body must be { from, to }" });
                }
                var affected = await remap.ApplyRemapAsync(fromEl.GetString()!, toEl.GetString()!);
                // Kick a disk scan so existence flags catch up to the
                // newly-rewritten paths immediately.
                disk.TriggerScanNow();
                return Results.Ok(new { affected });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // POST /api/library/rescan
        // Walk every configured root folder and auto-import any media
        // files that match a known event with high confidence. Files
        // that match at medium confidence land in PendingImports for
        // admin review (the existing Library Import page).
        app.MapPost("/api/library/rescan", async (LibraryRescanService rescan, ILogger<BackupService> logger) =>
        {
            try
            {
                var result = await rescan.RescanAllAsync();
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[LibraryRescan] failed");
                return Results.Problem(ex.Message);
            }
        });

        app.MapGet("/api/system/backup/download/{backupName}", async (string backupName, BackupService backupService) =>
        {
            try
            {
                var backups = await backupService.GetBackupsAsync();
                var backup = backups.FirstOrDefault(b => b.Name == backupName);
                if (backup == null || !File.Exists(backup.Path))
                    return Results.NotFound(new { message = "Backup file not found" });

                return Results.File(backup.Path, "application/zip", backupName);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        app.MapDelete("/api/system/backup/{backupName}", async (string backupName, BackupService backupService) =>
        {
            try
            {
                await backupService.DeleteBackupAsync(backupName);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        app.MapPost("/api/system/backup/cleanup", async (BackupService backupService) =>
        {
            try
            {
                await backupService.CleanupOldBackupsAsync();
                return Results.Ok(new { message = "Old backups cleaned up successfully" });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        return app;
    }
}
