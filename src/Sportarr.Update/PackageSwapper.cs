namespace Sportarr.Update;

/// <summary>
/// Copies a staged package over the install folder and undoes it.
/// Only files that are in the package change. Other files in the install
/// folder stay, for example a portable data folder.
/// </summary>
public static class PackageSwapper
{
    // Lists the package files that were not in the install folder before.
    // A rollback removes them.
    public const string NewFilesManifest = ".sportarr-new-files";

    /// <summary>
    /// Copies each install file that the package replaces into the backup
    /// folder. Removes an earlier backup first.
    /// </summary>
    public static void Backup(string stagingDir, string installDir, string backupDir)
    {
        if (Directory.Exists(backupDir))
            Directory.Delete(backupDir, recursive: true);
        Directory.CreateDirectory(backupDir);

        var newFiles = new List<string>();
        foreach (var relative in PackageFiles(stagingDir))
        {
            var installed = Path.Combine(installDir, relative);
            if (File.Exists(installed))
            {
                var target = Path.Combine(backupDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(installed, target, overwrite: true);
            }
            else
            {
                newFiles.Add(relative);
            }
        }

        File.WriteAllLines(Path.Combine(backupDir, NewFilesManifest), newFiles);
    }

    /// <summary>
    /// Copies every package file into the install folder. File.Copy keeps
    /// the file mode on Linux and macOS, so the executable bits stay.
    /// </summary>
    public static void Apply(string stagingDir, string installDir)
    {
        foreach (var relative in PackageFiles(stagingDir))
        {
            var target = Path.Combine(installDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            ReplaceFile(Path.Combine(stagingDir, relative), target);
        }
    }

    /// <summary>
    /// Puts the backed up files back and removes the files that the package
    /// added.
    /// </summary>
    public static void Rollback(string backupDir, string installDir)
    {
        var manifest = Path.Combine(backupDir, NewFilesManifest);
        if (!File.Exists(manifest))
            throw new InvalidOperationException($"The backup in {backupDir} is not complete");

        foreach (var relative in PackageFiles(backupDir))
        {
            if (relative == NewFilesManifest)
                continue;
            var target = Path.Combine(installDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            ReplaceFile(Path.Combine(backupDir, relative), target);
        }

        foreach (var relative in File.ReadAllLines(manifest).Where(l => l.Length > 0))
        {
            var added = Path.Combine(installDir, relative);
            if (File.Exists(added))
                Retry(() => File.Delete(added));
        }
    }

    internal static IEnumerable<string> PackageFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    // Short waits between tries. On Windows a file can stay locked for a
    // moment after its process has stopped, or while a virus scanner reads it.
    internal static TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    internal const int Attempts = 6;

    // A copy to a temporary name and a rename replace the file in one
    // step. On Linux and macOS this also works while the old file is open.
    internal static void ReplaceFile(string source, string target) =>
        Retry(() =>
        {
            var temp = target + ".sportarr-new";
            File.Copy(source, temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
        });

    internal static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < Attempts)
            {
                Thread.Sleep(RetryDelay);
            }
        }
    }
}
