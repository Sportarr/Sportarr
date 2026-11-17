using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>A database backup made just before an update.</summary>
public sealed record UpdateDatabaseBackup(string Path, string Provider);

/// <summary>
/// The checks and the backup that come before an update. Tests use a fake.
/// </summary>
public interface IUpdateSafety
{
    /// <summary>Returns what keeps Sportarr busy, or null when it is quiet.</summary>
    Task<string?> WhatIsBusyAsync(CancellationToken cancellationToken);

    /// <summary>Backs up the database with the normal backup, for SQLite and Postgres.</summary>
    Task<UpdateDatabaseBackup> BackUpDatabaseAsync(string note);

    /// <summary>Returns why the backup before an update cannot work, or null.</summary>
    string? BackupProblem();
}

public sealed class UpdateSafety : IUpdateSafety
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;

    public UpdateSafety(IServiceScopeFactory scopeFactory, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
    }

    public string? BackupProblem() =>
        BackupProblem(DatabaseSettings.FromConfiguration(_configuration).Provider, Environment.GetEnvironmentVariable("PATH"));

    /// <summary>
    /// A Postgres backup runs pg_dump. Without it the backup fails, so the
    /// update says so before it starts.
    /// </summary>
    internal static string? BackupProblem(DatabaseProviderKind provider, string? path)
    {
        if (provider != DatabaseProviderKind.Postgres)
            return null;
        var names = OperatingSystem.IsWindows() ? new[] { "pg_dump.exe" } : new[] { "pg_dump" };
        var found = (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => names.Any(n => File.Exists(Path.Combine(dir, n))));
        return found
            ? null
            : "Sportarr uses Postgres, and pg_dump is not installed. The built-in update needs it for the database backup before an update.";
    }

    public async Task<string?> WhatIsBusyAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        return await WhatIsBusyAsync(db, cancellationToken);
    }

    internal static async Task<string?> WhatIsBusyAsync(SportarrDbContext db, CancellationToken cancellationToken)
    {
        if (await db.DvrRecordings.AnyAsync(r => r.Status == DvrRecordingStatus.Recording || r.Status == DvrRecordingStatus.Importing, cancellationToken))
            return "A recording is in progress. Install the update when it has finished.";
        if (await db.DownloadQueue.AnyAsync(d => d.Status == DownloadStatus.Importing, cancellationToken))
            return "A download is being imported. Install the update when it has finished.";
        if (await db.PendingImports.AnyAsync(p => p.Status == PendingImportStatus.Importing, cancellationToken))
            return "An import is in progress. Install the update when it has finished.";
        // A batch that stops part way leaves files half moved. Startup
        // clears Running and Aborting task rows, so a stale row cannot block
        // an update.
        if (await db.Tasks.AnyAsync(t => t.CommandName == "LibraryImport" &&
                                         (t.Status == Sportarr.Api.Models.TaskStatus.Running || t.Status == Sportarr.Api.Models.TaskStatus.Aborting), cancellationToken))
            return "A library import is running. Install the update when it has finished.";
        if (LibraryRescanService.IsRunning)
            return "A library rescan is running. Install the update when it has finished.";
        return null;
    }

    /// <summary>
    /// Sets imports that a stopped process left in Importing back to
    /// Pending. Runs at startup, when no import can run yet. A failed import
    /// goes back to Pending too, so this is the normal state for a retry.
    /// Without it, one such row would block every update for good.
    /// </summary>
    public static async Task<int> ReopenInterruptedImportsAsync(SportarrDbContext db, CancellationToken cancellationToken = default)
    {
        var stuck = await db.PendingImports
            .Where(p => p.Status == PendingImportStatus.Importing && !p.DownloadId.StartsWith("dvr-"))
            .ToListAsync(cancellationToken);
        foreach (var import in stuck)
            import.Status = PendingImportStatus.Pending;
        await db.SaveChangesAsync(cancellationToken);
        return stuck.Count;
    }

    public async Task<UpdateDatabaseBackup> BackUpDatabaseAsync(string note)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var backup = await scope.ServiceProvider.GetRequiredService<BackupService>().CreateBackupAsync(note);
        return new UpdateDatabaseBackup(backup.Path, db.Database.IsNpgsql() ? "postgres" : "sqlite");
    }
}
