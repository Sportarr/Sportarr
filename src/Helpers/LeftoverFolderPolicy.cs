namespace Sportarr.Api.Helpers;

/// <summary>
/// Decides whether a download client's leftover job folder may be removed.
/// The decision lives here alone because a wrong answer deletes a user's media.
/// </summary>
public static class LeftoverFolderPolicy
{
    /// <summary>
    /// True only when <paramref name="folder"/> exists, is not a drive or share
    /// root, is not a configured root folder, and holds no file at any depth.
    /// <paramref name="fullPath"/> receives the normalized path to delete.
    /// </summary>
    public static bool MayRemove(string? folder, IEnumerable<string>? rootFolders, out string? fullPath)
    {
        fullPath = null;

        if (string.IsNullOrWhiteSpace(folder))
            return false;

        // Trimming the separators off a filesystem root leaves nothing, and
        // asking for the full path of nothing throws. This is a safety check,
        // so it has to answer no rather than take the caller's cleanup pass
        // down with it.
        var trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception)
        {
            return false;
        }

        if (!Directory.Exists(full))
            return false;

        // A drive or share root has no parent. Never touch one.
        if (Directory.GetParent(full) == null)
            return false;

        // A root folder can legitimately be empty, and deleting one would take
        // out a configured library path.
        if (rootFolders != null)
        {
            foreach (var root in rootFolders)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                var normalizedRoot = Path.GetFullPath(
                    root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.Equals(normalizedRoot, full, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        // Any file at any depth means this is not leftover scaffolding. A
        // directory this cannot read might hold one, and a walk that throws
        // part way through used to escape and abort the caller, so anything
        // going wrong here answers no.
        try
        {
            if (Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Any())
                return false;
        }
        catch (Exception)
        {
            return false;
        }

        fullPath = full;
        return true;
    }
}
