using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

public static class SystemBackupEndpoints
{
    public static IEndpointRouteBuilder MapSystemBackupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/system/backup", async (BackupService backupService) =>
        {
            var backups = await backupService.GetBackupsAsync();
            return Results.Ok(backups);
        });

        app.MapPost("/api/system/backup", async (BackupService backupService, string? note) =>
        {
            try
            {
                var backup = await backupService.CreateBackupAsync(note);
                return Results.Ok(backup);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        app.MapPost("/api/system/backup/restore/{backupName}", async (string backupName, BackupService backupService) =>
        {
            try
            {
                await backupService.RestoreBackupAsync(backupName);
                return Results.Ok(new { message = "Backup restored successfully. Please restart Sportarr for changes to take effect." });
            }
            catch (Exception ex)
            {
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
