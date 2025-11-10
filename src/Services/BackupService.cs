using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace Sportarr.Api.Services;

/// <summary>
/// Handles database backup and restore operations
/// </summary>
public class BackupService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<BackupService> _logger;
    private readonly string _dataDirectory;
    private readonly string _databasePath;
    private readonly ConfigService _configService;

    public BackupService(SportarrDbContext db, ILogger<BackupService> logger, IConfiguration configuration, ConfigService configService)
    {
        _db = db;
        _logger = logger;
        _configService = configService;
        _dataDirectory = configuration["DataDirectory"] ?? "./data";
        _databasePath = Path.Combine(_dataDirectory, "sportarr.db");
    }

    /// <summary>
    /// Get backup folder path from settings
    /// </summary>
    private async Task<string> GetBackupFolderAsync()
    {
        var config = await _configService.GetConfigAsync();
        var backupFolder = config.BackupFolder;

        if (string.IsNullOrWhiteSpace(backupFolder))
        {
            backupFolder = Path.Combine(_dataDirectory, "Backups");
        }

        if (!Directory.Exists(backupFolder))
        {
            Directory.CreateDirectory(backupFolder);
        }

        return backupFolder;
    }

    /// <summary>
    /// List all available backups
    /// </summary>
    public async Task<List<BackupInfo>> GetBackupsAsync()
    {
        var backupFolder = await GetBackupFolderAsync();
        var backups = new List<BackupInfo>();

        if (!Directory.Exists(backupFolder))
        {
            return backups;
        }

        foreach (var file in Directory.GetFiles(backupFolder, "fightarr_backup_*.zip"))
        {
            var fileInfo = new FileInfo(file);
            backups.Add(new BackupInfo
            {
                Name = Path.GetFileName(file),
                Path = file,
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc
            });
        }

        return backups.OrderByDescending(b => b.CreatedAt).ToList();
    }

    /// <summary>
    /// Create a new backup of the database
    /// </summary>
    public async Task<BackupInfo> CreateBackupAsync(string? note = null)
    {
        var backupFolder = await GetBackupFolderAsync();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"fightarr_backup_{timestamp}.zip";
        var backupPath = Path.Combine(backupFolder, backupFileName);

        _logger.LogInformation("Creating backup: {BackupPath}", backupPath);

        try
        {
            // Ensure WAL mode checkpoint to get a consistent backup
            await _db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(FULL)");

            // Create backup zip file
            using (var zipArchive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
            {
                // Add main database file
                if (File.Exists(_databasePath))
                {
                    zipArchive.CreateEntryFromFile(_databasePath, "sportarr.db");
                }

                // Add WAL file if it exists
                var walPath = _databasePath + "-wal";
                if (File.Exists(walPath))
                {
                    zipArchive.CreateEntryFromFile(walPath, "sportarr.db-wal");
                }

                // Add SHM file if it exists
                var shmPath = _databasePath + "-shm";
                if (File.Exists(shmPath))
                {
                    zipArchive.CreateEntryFromFile(shmPath, "sportarr.db-shm");
                }

                // Add backup metadata
                var metadata = zipArchive.CreateEntry("backup_metadata.txt");
                using (var writer = new StreamWriter(metadata.Open()))
                {
                    writer.WriteLine($"Backup Created: {DateTime.UtcNow:O}");
                    writer.WriteLine($"Sportarr Version: {Version.AppVersion}");
                    if (!string.IsNullOrWhiteSpace(note))
                    {
                        writer.WriteLine($"Note: {note}");
                    }
                }
            }

            var fileInfo = new FileInfo(backupPath);
            _logger.LogInformation("Backup created successfully: {BackupPath} ({Size} bytes)", backupPath, fileInfo.Length);

            return new BackupInfo
            {
                Name = backupFileName,
                Path = backupPath,
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backup: {BackupPath}", backupPath);
            throw new InvalidOperationException($"Failed to create backup: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Restore database from a backup
    /// </summary>
    public async Task RestoreBackupAsync(string backupName)
    {
        var backupFolder = await GetBackupFolderAsync();
        var backupPath = Path.Combine(backupFolder, backupName);

        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException($"Backup file not found: {backupName}");
        }

        _logger.LogInformation("Restoring backup: {BackupPath}", backupPath);

        try
        {
            // Close all database connections
            await _db.Database.CloseConnectionAsync();

            // Create a restore directory
            var restoreDir = Path.Combine(_dataDirectory, "restore_temp");
            if (Directory.Exists(restoreDir))
            {
                Directory.Delete(restoreDir, true);
            }
            Directory.CreateDirectory(restoreDir);

            // Extract backup
            ZipFile.ExtractToDirectory(backupPath, restoreDir);

            // Backup current database before replacing (safety measure)
            var currentBackupPath = _databasePath + ".before_restore";
            if (File.Exists(_databasePath))
            {
                File.Copy(_databasePath, currentBackupPath, true);
            }

            // Replace database files
            var restoredDbPath = Path.Combine(restoreDir, "sportarr.db");
            if (File.Exists(restoredDbPath))
            {
                File.Copy(restoredDbPath, _databasePath, true);
            }

            var restoredWalPath = Path.Combine(restoreDir, "sportarr.db-wal");
            if (File.Exists(restoredWalPath))
            {
                File.Copy(restoredWalPath, _databasePath + "-wal", true);
            }

            var restoredShmPath = Path.Combine(restoreDir, "sportarr.db-shm");
            if (File.Exists(restoredShmPath))
            {
                File.Copy(restoredShmPath, _databasePath + "-shm", true);
            }

            // Clean up restore directory
            Directory.Delete(restoreDir, true);

            _logger.LogInformation("Backup restored successfully: {BackupPath}", backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore backup: {BackupPath}", backupPath);
            throw new InvalidOperationException($"Failed to restore backup: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Delete a backup file
    /// </summary>
    public async Task DeleteBackupAsync(string backupName)
    {
        var backupFolder = await GetBackupFolderAsync();
        var backupPath = Path.Combine(backupFolder, backupName);

        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException($"Backup file not found: {backupName}");
        }

        _logger.LogInformation("Deleting backup: {BackupPath}", backupPath);
        File.Delete(backupPath);
    }

    /// <summary>
    /// Clean up old backups based on retention policy
    /// </summary>
    public async Task CleanupOldBackupsAsync()
    {
        var config = await _configService.GetConfigAsync();
        var retentionDays = config.BackupRetention; // Default 28 days

        var backups = await GetBackupsAsync();
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

        foreach (var backup in backups.Where(b => b.CreatedAt < cutoffDate))
        {
            _logger.LogInformation("Cleaning up old backup: {BackupName} (created {CreatedAt})", backup.Name, backup.CreatedAt);
            await DeleteBackupAsync(backup.Name);
        }
    }
}

/// <summary>
/// Information about a backup file
/// </summary>
public class BackupInfo
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; }
    public string SizeFormatted => FormatBytes(Size);

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
