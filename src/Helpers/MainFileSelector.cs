namespace Sportarr.Api.Helpers;

/// <summary>
/// Picks the main video file out of a multi-file release. Largest file wins,
/// with one refinement: when another file is close in size (within 10%) and
/// the largest one's name marks it as ancillary content (pre/post show,
/// buildup, analysis, highlights, ...), the biggest non-ancillary candidate
/// is preferred. Motorsport releases in particular ship the session alongside
/// buildup and analysis files, and the post-session analysis can edge out the
/// actual session by a few megabytes (#205).
/// </summary>
public static class MainFileSelector
{
    private static readonly HashSet<string> AncillaryTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "pre", "post", "buildup", "analysis", "preview", "review", "recap",
        "highlights", "highlight", "presser", "weigh", "weighin"
    };

    /// <summary>
    /// Returns the best main-file candidate from <paramref name="videoFiles"/>.
    /// <paramref name="sizeOf"/> supplies file sizes (symlink-resolving in the
    /// import path).
    /// </summary>
    public static string SelectMainVideoFile(IReadOnlyList<string> videoFiles, Func<string, long> sizeOf)
    {
        if (videoFiles.Count == 1)
        {
            return videoFiles[0];
        }

        var sized = videoFiles
            .Select(f => (Path: f, Size: sizeOf(f)))
            .OrderByDescending(x => x.Size)
            .ToList();
        var maxSize = sized[0].Size;
        if (maxSize <= 0)
        {
            return sized[0].Path;
        }

        // Only files within 10% of the largest compete on name relevance -
        // a genuinely bigger file always wins regardless of what it's called.
        var comparable = sized.Where(x => x.Size * 10 >= maxSize * 9).ToList();
        if (comparable.Count == 1)
        {
            return comparable[0].Path;
        }

        var preferred = comparable.FirstOrDefault(x => !HasAncillaryName(x.Path));
        return (preferred.Path ?? comparable[0].Path);
    }

    /// <summary>
    /// True when the file name carries a marker of ancillary content. Matching
    /// is on whole tokens (split at every non-alphanumeric character) so
    /// "Premier.League" never trips the "pre" marker, while
    /// "Post-Qualifying.Analysis" and "Pre.Race.Buildup" do.
    /// </summary>
    internal static bool HasAncillaryName(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var tokens = System.Text.RegularExpressions.Regex
            .Split(name, "[^A-Za-z0-9]+")
            .Where(t => t.Length > 0)
            .ToArray();

        for (var i = 0; i < tokens.Length; i++)
        {
            if (AncillaryTokens.Contains(tokens[i]))
            {
                return true;
            }

            // "Build-Up" / "Build.Up" splits into two tokens
            if (i + 1 < tokens.Length &&
                tokens[i].Equals("build", StringComparison.OrdinalIgnoreCase) &&
                tokens[i + 1].Equals("up", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
