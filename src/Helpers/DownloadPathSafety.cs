namespace Sportarr.Api.Helpers;

/// <summary>Consistent local path identity and shared-root protection for download ignores.</summary>
public static class DownloadPathSafety
{
    public static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return null; }
    }

    public static IEnumerable<string> FileAndAncestors(string filePath)
    {
        for (var path = Normalize(filePath); path != null; path = Path.GetDirectoryName(path))
        {
            yield return path;
            yield return path + Path.DirectorySeparatorChar; // Also match older directory markers.
        }
    }

    public static bool IsSharedRoot(string path, IEnumerable<string> sharedRoots)
    {
        var fullPath = Normalize(path);
        if (fullPath == null || Comparer.Equals(fullPath, Path.GetPathRoot(fullPath))) return true;
        foreach (var root in sharedRoots)
        {
            var sharedRoot = Normalize(root);
            if (sharedRoot == null || FileAndAncestors(sharedRoot).Contains(fullPath, Comparer)) return true;
        }
        return false;
    }
}
