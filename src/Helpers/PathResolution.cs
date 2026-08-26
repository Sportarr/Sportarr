namespace Sportarr.Api.Helpers;

/// <summary>
/// Turning a path into the place it really points at.
///
/// Anywhere a path is checked against a folder it is allowed to be inside,
/// the check has to be made against what the path resolves to. A link sitting
/// anywhere along it can lead somewhere else entirely, and comparing the text
/// alone lets that through.
/// </summary>
public static class PathResolution
{
    /// <summary>
    /// How many links to follow at one component before giving up, so a loop
    /// cannot spin.
    /// </summary>
    private const int MaxHops = 16;

    /// <summary>
    /// Resolve a path one component at a time, following a link wherever one
    /// is found, including partway along.
    ///
    /// Best effort. A path that does not exist yet resolves as far as it can,
    /// and the rest is left as written.
    /// </summary>
    public static string ResolveThroughLinks(string path)
    {
        TryResolveThroughLinks(path, out var resolved);
        return resolved;
    }

    /// <summary>
    /// Resolve a path one component at a time, reporting whether it was
    /// resolved completely.
    ///
    /// False means a component was still a link after following it as far as
    /// this is willing to, so what comes back is only partly resolved and the
    /// rest of the path was appended as written. A caller deciding whether
    /// something may be deleted has to refuse that rather than trust it.
    /// </summary>
    public static bool TryResolveThroughLinks(string path, out string resolved)
    {
        resolved = path ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        resolved = fullPath;

        try
        {
            var root = Path.GetPathRoot(fullPath) ?? string.Empty;
            var segments = fullPath
                .Substring(root.Length)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(s => s.Length > 0);

            var current = root;

            foreach (var segment in segments)
            {
                current = Path.Combine(current, segment);

                // Following one link can land on another, so keep going until
                // the path stops being one.
                for (var hop = 0; hop < MaxHops; hop++)
                {
                    FileSystemInfo? target;
                    try
                    {
                        target = Directory.Exists(current)
                            ? Directory.ResolveLinkTarget(current, returnFinalTarget: false)
                            : File.Exists(current)
                                ? File.ResolveLinkTarget(current, returnFinalTarget: false)
                                : null;
                    }
                    catch
                    {
                        // Not something this host can resolve, so what is
                        // there already is the best answer available.
                        break;
                    }

                    if (target == null) break;

                    // One more hop than this is willing to follow. Whatever
                    // is here is still a link, so the rest of the path would
                    // be appended to something that is not where it lands.
                    if (hop == MaxHops - 1)
                    {
                        return false;
                    }

                    var next = target.FullName;
                    if (!Path.IsPathRooted(next))
                    {
                        // A link can point somewhere relative to the folder
                        // holding it.
                        next = Path.Combine(Path.GetDirectoryName(current) ?? root, next);
                    }

                    current = Path.GetFullPath(next);
                }
            }

            resolved = current;
            return true;
        }
        catch
        {
            resolved = fullPath;
            return false;
        }
    }

    /// <summary>
    /// Whether a path, once resolved, sits inside one of the given roots.
    /// The roots are resolved the same way, so a link on either side is
    /// followed before they are compared.
    /// </summary>
    public static bool IsInsideAny(string path, IEnumerable<string> roots)
    {
        // A path that could not be resolved all the way is refused. This
        // decides whether a recursive delete runs, so anything uncertain has
        // to be a no.
        if (!TryResolveThroughLinks(path, out var resolvedPath)) return false;

        var resolved = resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(resolved)) return false;

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;

            if (!TryResolveThroughLinks(root, out var rootResolved)) continue;

            var resolvedRoot = rootResolved
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(resolvedRoot)) continue;

            if (resolved.Equals(resolvedRoot, comparison)) return true;
            if (resolved.StartsWith(resolvedRoot + Path.DirectorySeparatorChar, comparison)) return true;
            if (resolved.StartsWith(resolvedRoot + Path.AltDirectorySeparatorChar, comparison)) return true;
        }

        return false;
    }
}
