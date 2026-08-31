using System.Linq;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Decides whether a download client's leftover job folder may be removed.
/// The decision lives here alone because a wrong answer deletes a user's media.
/// </summary>
public static class LeftoverFolderPolicy
{
    // No real session fits below this. Thirty minutes of video at even a
    // modest bitrate is several times bigger, so anything smaller is a
    // sample, a promo clip, or scene junk.
    private const long SampleSizeBytes = 50_000_000;

    /// <summary>
    /// Resolve the folder a job owns from the path a client reported.
    ///
    /// SABnzbd reports a completed single-file job as the path of the file
    /// itself, and after a move-mode import that file is gone. Every check
    /// downstream starts with "is this an existing directory", so the empty
    /// job folder above it was never even considered and outlived every
    /// cleanup pass. The step up to the parent is taken only when the parent
    /// is named after the same release as the path's own leaf, because a job
    /// folder and its payload share the release name while a shared category
    /// or scan folder never does. SABnzbd's duplicate suffix (folder ".1")
    /// is covered by the prefix comparison running in both directions.
    /// </summary>
    public static string? ResolveOwnedFolder(string? path, string? identity)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (Directory.Exists(path))
        {
            // An existing directory must also prove it is the job's own. A
            // degenerate client answer (a bare category or save directory)
            // arrives as an existing directory too, and without this check it
            // would read as job-owned with every other download inside it.
            var leaf = Path.GetFileName(
                Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return !string.IsNullOrWhiteSpace(identity)
                && !string.IsNullOrWhiteSpace(leaf)
                && MatchesReleaseName(leaf, identity)
                ? path
                : null;
        }

        string? parent;
        try
        {
            parent = Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch (Exception)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            return null;

        var parentName = Path.GetFileName(parent);
        var name = string.IsNullOrWhiteSpace(identity)
            ? Path.GetFileNameWithoutExtension(path)
            : identity;

        if (string.IsNullOrWhiteSpace(parentName) || string.IsNullOrWhiteSpace(name))
            return null;

        return MatchesReleaseName(parentName, name) ? parent : null;
    }

    /// <summary>
    /// A folder owns a release when it carries exactly the release's name,
    /// or that name plus SABnzbd's numeric duplicate suffix (".1", ".2").
    /// An open-ended prefix match is not enough in either direction: a file
    /// like UFC.300.mkv inside a shared UFC folder starts with the folder's
    /// name, and treating that folder as release-owned would offer a whole
    /// staging directory to the sweeper.
    /// </summary>
    private static bool MatchesReleaseName(string folderName, string releaseName)
    {
        if (folderName.Equals(releaseName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (folderName.Length > releaseName.Length + 1
            && folderName.StartsWith(releaseName, StringComparison.OrdinalIgnoreCase)
            && folderName[releaseName.Length] == '.')
        {
            var suffix = folderName[(releaseName.Length + 1)..];
            return suffix.All(char.IsAsciiDigit);
        }

        return false;
    }

    /// <summary>
    /// The bar for a download client's own job folder, which is deleted
    /// together with whatever the import left in it (nfo, samples, archives).
    /// The shared guards apply to both lists. Root folders additionally
    /// refuse anything beneath them, because a job folder never legitimately
    /// lives inside the library, while a client's directory must keep
    /// allowing the job folders that live inside it.
    /// <paramref name="fullPath"/> receives the normalized path to delete.
    /// </summary>
    public static bool IsSafeTarget(
        string? folder,
        IEnumerable<string>? rootFolders,
        IEnumerable<string>? clientFolders,
        IEnumerable<string>? categoryNames,
        out string? fullPath)
    {
        var roots = (rootFolders ?? Enumerable.Empty<string>()).ToList();
        var combined = roots.Concat(clientFolders ?? Enumerable.Empty<string>());

        if (!IsRemovableDirectory(folder, combined, out fullPath) || fullPath == null)
            return false;

        // A job folder is never the category folder itself. A release whose
        // name collides with a category would otherwise claim the shared
        // category directory and every download inside it.
        var leaf = Path.GetFileName(fullPath);
        if (categoryNames != null && categoryNames.Any(c =>
                !string.IsNullOrWhiteSpace(c) && leaf.Equals(c, StringComparison.OrdinalIgnoreCase)))
        {
            fullPath = null;
            return false;
        }

        // The import takes one main file per download. A remaining video that
        // is not a sample and not ancillary-named (pre/post show, analysis,
        // highlights) is payload the import did not take, such as a second
        // session in a bundled release, and deleting it would destroy real
        // content. Samples, ancillary files, and tiny junk clips still go
        // with the folder. A .strm holds a whole session in a few bytes and
        // a symlink's length says nothing about its target, so both always
        // count as payload.
        try
        {
            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!Sportarr.Api.Services.SupportedExtensions.Video.Contains(ext))
                    continue;

                // The unconditional types come before any name exemption.
                var info = new FileInfo(file);
                if (ext == ".strm" || info.LinkTarget != null)
                {
                    fullPath = null;
                    return false;
                }

                if (MainFileSelector.HasAncillaryName(file) || IsSampleNamed(file))
                    continue;

                if (info.Length >= SampleSizeBytes)
                {
                    fullPath = null;
                    return false;
                }
            }
        }
        catch (Exception)
        {
            fullPath = null;
            return false;
        }

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var normalizedRoot = Path.GetFullPath(
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                fullPath = null;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when the file name carries a sample or proof marker as a whole
    /// token. "sample.mkv" and "Race-PROOF.mkv" match; "Sampler.Special.mkv"
    /// does not, because matching is on whole tokens only.
    /// </summary>
    private static bool IsSampleNamed(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var token in name.Split(_tokenSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("sample", StringComparison.OrdinalIgnoreCase)
                || token.Equals("proof", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static readonly char[] _tokenSeparators =
        Enumerable.Range(0, 128).Select(i => (char)i).Where(c => !char.IsLetterOrDigit(c)).ToArray();

    /// <summary>
    /// The guards shared by every removal: the folder exists, is not a drive
    /// or share root, is not a protected path, and does not contain one.
    /// </summary>
    private static bool IsRemovableDirectory(string? folder, IEnumerable<string>? protectedPaths, out string? fullPath)
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

        // A drive or share root has no parent. Never touch one. A
        // first-level directory such as /downloads is a mount point or a
        // shared root, never a job folder: jobs live at least two levels
        // deep, so anything shallower is refused outright.
        var parentDir = Directory.GetParent(full);
        if (parentDir == null || parentDir.Parent == null)
            return false;

        // A root folder can legitimately be empty, and deleting one would take
        // out a configured library path.
        if (protectedPaths != null)
        {
            foreach (var root in protectedPaths)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                var normalizedRoot = Path.GetFullPath(
                    root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.Equals(normalizedRoot, full, StringComparison.OrdinalIgnoreCase))
                    return false;

                // A protected path nested underneath the candidate would go
                // down with the recursive delete. It holds no files, or the
                // emptiness check below would refuse anyway, but removing a
                // configured directory breaks whatever was configured to use
                // it.
                if (normalizedRoot.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || normalizedRoot.StartsWith(full + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        fullPath = full;
        return true;
    }

    /// <summary>
    /// Like <see cref="IsSafeTarget"/>, but also requires the folder to hold
    /// no file at any depth. This is the bar for a folder the user chose,
    /// such as a library import source, which is never deleted with content
    /// still inside it.
    /// </summary>
    public static bool MayRemove(string? folder, IEnumerable<string>? protectedPaths, out string? fullPath)
    {
        if (!IsRemovableDirectory(folder, protectedPaths, out fullPath) || fullPath == null)
            return false;

        // A directory this cannot read might hold a file, and a walk that
        // throws part way through used to escape and abort the caller, so
        // anything going wrong here answers no.
        try
        {
            if (Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).Any())
            {
                fullPath = null;
                return false;
            }
        }
        catch (Exception)
        {
            fullPath = null;
            return false;
        }

        return true;
    }
}
