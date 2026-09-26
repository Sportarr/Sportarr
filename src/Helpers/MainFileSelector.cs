namespace Sportarr.Api.Helpers;

/// <summary>
/// Picks the main video file from a multi-file release.
/// A session file wins over a file named as extra content when its size is plausible.
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
    public static string? SelectMainVideoFile(IReadOnlyList<string> videoFiles, Func<string, long> sizeOf,
        string? releaseTitle = null)
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
        if (maxSize <= 0) return null;

        var minimumMainSize = maxSize / 4;
        var highlightsRelease = !string.IsNullOrWhiteSpace(releaseTitle)
            && System.Text.RegularExpressions.Regex.IsMatch(releaseTitle,
                @"(?<![A-Za-z0-9])highlights?(?![A-Za-z0-9])",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (highlightsRelease)
        {
            var highlights = sized.Where(x => x.Size >= minimumMainSize && HasHighlightName(x.Path)).ToList();
            return highlights.Count == 1 ? highlights[0].Path : null;
        }

        var candidates = sized.Where(x => x.Size >= minimumMainSize && !HasAncillaryName(x.Path)).ToList();
        return candidates.Count == 1 ? candidates[0].Path : null;
    }

    private static bool HasHighlightName(string path) =>
        System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(path),
            @"(?<![A-Za-z0-9])highlights?(?![A-Za-z0-9])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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
