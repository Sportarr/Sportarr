using System.IO.Compression;

namespace Sportarr.Update;

/// <summary>
/// Puts the SQLite database back from the backup that Sportarr made just
/// before the update. A Postgres database cannot be restored here, because
/// the helper has neither the database tools nor the credentials.
/// </summary>
public static class DatabaseRestorer
{
    /// <summary>
    /// The backup to restore. A late handover writes a newer one, which
    /// then wins over the one on the command line.
    /// </summary>
    public static string BackupPath(UpdaterOptions options)
    {
        return AtomicFile.Read(options.DatabaseBackupOverridePath) ?? options.DatabaseBackup;
    }

    // Every SQLite file starts with these 16 bytes.
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    // A Sportarr database always has this table. Its name is in the schema
    // text that SQLite keeps inside the file.
    private static readonly byte[] KnownTable = "__EFMigrationsHistory"u8.ToArray();

    public static string TempPath(UpdaterOptions options) => options.DatabasePath + ".sportarr-restore";

    /// <summary>
    /// Takes the database out of the backup into a file next to the live
    /// one and checks it. Nothing live changes here. Returns null when the
    /// copy is ready, or the reason why it is not. The helper has no SQLite
    /// library, so the check reads the file header and looks for a table
    /// that every Sportarr database has.
    /// </summary>
    public static string? Prepare(UpdaterOptions options)
    {
        var temp = TempPath(options);
        try
        {
            var backup = BackupPath(options);
            if (options.DatabaseProvider != "sqlite")
                return "the database is not SQLite";
            if (string.IsNullOrEmpty(backup) || !File.Exists(backup))
                return "the database backup is missing";

            using (var archive = ZipFile.OpenRead(backup))
            {
                var entry = archive.GetEntry("sportarr.db");
                if (entry == null)
                    return "the database backup contains no sportarr.db";
                entry.ExtractToFile(temp, overwrite: true);
            }

            var problem = Check(temp);
            if (problem != null)
                File.Delete(temp);
            return problem;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (IOException) { }
            return $"the database backup cannot be read: {ex.Message.TrimEnd('.')}";
        }
    }

    internal static string? Check(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 100 || !bytes.AsSpan(0, SqliteHeader.Length).SequenceEqual(SqliteHeader))
            return "the database in the backup is not a SQLite file";
        if (bytes.AsSpan().IndexOf(KnownTable) < 0)
            return "the database in the backup is not a Sportarr database";
        return null;
    }

    /// <summary>
    /// Moves the prepared copy over the database and removes the journal
    /// files that belong to the newer database.
    /// </summary>
    public static void Commit(UpdaterOptions options)
    {
        // The journal files of the newer database go first. Otherwise a
        // start between the two steps could apply them to the old database.
        var temp = TempPath(options);
        foreach (var journal in new[] { options.DatabasePath + "-wal", options.DatabasePath + "-shm" })
        {
            if (File.Exists(journal))
                PackageSwapper.Retry(() => File.Delete(journal));
        }
        PackageSwapper.Retry(() => File.Move(temp, options.DatabasePath, overwrite: true));
    }

    public static void Discard(UpdaterOptions options)
    {
        try { File.Delete(TempPath(options)); } catch (IOException) { }
    }
}
