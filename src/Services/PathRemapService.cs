using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;

namespace Sportarr.Api.Services;

/// <summary>
/// Detects + applies path-prefix rewrites against EventFile.FilePath.
///
/// Why this exists: when a backup is restored on a different machine (or
/// after the source machine reorganized its storage), the EventFile rows
/// land with absolute paths that no longer exist. Manually re-importing
/// every file is the Sonarr-equivalent of misery. The remap service walks
/// the missing-file set, computes the longest common prefix of those
/// paths, and looks for the same filenames under the locally-configured
/// root folders to determine the new prefix. If a confident match exists,
/// the admin can apply a one-click bulk UPDATE that rewrites every
/// affected row.
///
/// Intentionally read-only by default: detection returns a preview the
/// admin confirms before any writes happen. The apply path is a single
/// SQL statement so the rewrite is atomic.
/// </summary>
public class PathRemapService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<PathRemapService> _logger;

    // Minimum number of missing files that must share the candidate prefix
    // before we offer a remap. Below this we assume the missing-file set
    // is noise (a few files an admin actually deleted) rather than a
    // systematic path-prefix drift.
    private const int MinSamplesForRemap = 5;

    public PathRemapService(SportarrDbContext db, ILogger<PathRemapService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Inspect every EventFile whose FilePath does not exist on disk and
    /// look for evidence of a single common prefix drift. When we find one,
    /// scan the configured root folders for the same basenames to suggest
    /// the new prefix. The return value is a preview only -- no writes
    /// happen until ApplyRemapAsync is called explicitly.
    /// </summary>
    public async Task<PathRemapPreview> DetectAsync(CancellationToken ct = default)
    {
        var preview = new PathRemapPreview();

        // Pull missing file paths only. The reachability check happens in
        // DiskScanService; here we just look at rows it has already flagged.
        var missingPaths = await _db.EventFiles
            .AsNoTracking()
            .Where(ef => !ef.Exists && ef.FilePath != null)
            .Select(ef => ef.FilePath!)
            .ToListAsync(ct);

        preview.MissingFileCount = missingPaths.Count;

        if (missingPaths.Count < MinSamplesForRemap)
        {
            preview.Notes = $"Only {missingPaths.Count} missing file(s) -- not enough signal to detect a prefix drift. The remap heuristic requires at least {MinSamplesForRemap}.";
            return preview;
        }

        // Compute the longest common directory prefix of the missing paths.
        // We split on directory separators rather than character-by-character
        // so the result lines up with a meaningful path boundary.
        var oldPrefix = LongestCommonDirectoryPrefix(missingPaths);
        if (string.IsNullOrEmpty(oldPrefix))
        {
            preview.Notes = "Missing paths share no common directory prefix -- nothing to remap.";
            return preview;
        }
        preview.OldPrefix = oldPrefix;

        // Locate every configured root folder. The new prefix has to be
        // somewhere under one of them; otherwise the admin needs to add a
        // root folder before any remap is meaningful.
        var rootFolders = (await _db.RootFolders.ToListAsync(ct))
            .Where(rf => !string.IsNullOrEmpty(rf.Path) && Directory.Exists(rf.Path))
            .Select(rf => rf.Path)
            .ToList();
        if (rootFolders.Count == 0)
        {
            preview.Notes = "No reachable root folders configured -- add one before remapping.";
            return preview;
        }

        // For each missing file, peel off oldPrefix and look for the
        // remainder under any reachable root folder. The first prefix that
        // resolves a meaningful fraction of the sample is the candidate.
        var sampleSize = Math.Min(missingPaths.Count, 50);
        var sample = missingPaths.Take(sampleSize).ToList();
        var candidatePrefixes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var missing in sample)
        {
            if (!missing.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var relative = missing.Substring(oldPrefix.Length).TrimStart('/', '\\');

            foreach (var root in rootFolders)
            {
                var candidate = Path.Combine(root, relative);
                if (File.Exists(candidate))
                {
                    // The new prefix is whatever, when concatenated with
                    // `relative`, points at an existing file. Track each
                    // candidate's hit count so the winner is the prefix
                    // that resolves the most files.
                    var newPrefix = candidate.Substring(0, candidate.Length - relative.Length);
                    if (!candidatePrefixes.ContainsKey(newPrefix))
                        candidatePrefixes[newPrefix] = 0;
                    candidatePrefixes[newPrefix]++;
                    break;
                }
            }
        }

        if (candidatePrefixes.Count == 0)
        {
            preview.Notes = $"Could not locate any of the missing files under the configured root folders. Old prefix detected as `{oldPrefix}`. Add a root folder containing the moved files and rescan.";
            return preview;
        }

        // Winner = highest hit count among candidate prefixes. Tie-break on
        // alphabetical so the suggestion is deterministic across runs.
        var winner = candidatePrefixes
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .First();
        preview.NewPrefix = winner.Key;
        preview.SampleMatches = winner.Value;
        preview.SampleSize = sampleSize;

        // Project the total number of affected rows if the admin commits.
        // Use SUBSTR-equality rather than LIKE so an underscore inside the
        // prefix (very common in path components) doesn't act as a wildcard
        // and inflate the count.
        var oldPrefixLen = oldPrefix.Length;
        preview.AffectedRowCount = await _db.EventFiles
            .CountAsync(ef => !ef.Exists
                              && ef.FilePath != null
                              && ef.FilePath.Length >= oldPrefixLen
                              && ef.FilePath.Substring(0, oldPrefixLen) == oldPrefix,
                ct);

        preview.Notes = $"Detected likely path drift. {preview.SampleMatches}/{preview.SampleSize} sampled missing files exist at the new prefix. Apply to remap {preview.AffectedRowCount} rows.";
        return preview;
    }

    /// <summary>
    /// Apply a remap. The UPDATE is atomic: every EventFile row whose
    /// FilePath starts with `oldPrefix` gets the prefix swapped for
    /// `newPrefix` in a single SQL statement. After the rewrite we trigger
    /// the disk scanner so existence flags catch up to the new paths.
    /// </summary>
    public async Task<int> ApplyRemapAsync(string oldPrefix, string newPrefix, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(oldPrefix) || string.IsNullOrEmpty(newPrefix))
            throw new ArgumentException("Both oldPrefix and newPrefix must be non-empty.");
        if (oldPrefix.Equals(newPrefix, StringComparison.Ordinal))
            return 0;

        _logger.LogInformation(
            "[PathRemap] Rewriting FilePath prefix `{Old}` -> `{New}` on EventFile rows",
            oldPrefix, newPrefix);

        // Use parameterized raw SQL for the prefix-replace because EF Core's
        // ExecuteUpdate doesn't yet support REPLACE in projections. The
        // WHERE clause uses SUBSTR equality instead of LIKE because LIKE
        // treats `_` as a single-char wildcard -- literal underscores in
        // path components (very common: `Boston_Red_Sox`, etc.) would let
        // unrelated paths match and the SUBSTR rewrite would garble them.
        // SUBSTR-equality matches the prefix as a literal string.
        // Only rows already flagged missing. The prefix is derived from
        // missing paths alone, but the rewrite used to match every row under
        // it, so a drift affecting one mount rewrote the records of files on
        // every other mount below the same prefix. Those files were sitting
        // exactly where their record said, and the rewrite pointed the library
        // away from them.
        //
        // Identifiers are quoted because Postgres folds unquoted names to
        // lower case and the tables are created with their PascalCase names,
        // so the unquoted form could never resolve there at all.
        var affected = await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE ""EventFiles""
               SET ""FilePath"" = {newPrefix} || SUBSTR(""FilePath"", {oldPrefix.Length + 1})
               WHERE NOT ""Exists""
                 AND SUBSTR(""FilePath"", 1, {oldPrefix.Length}) = {oldPrefix}",
            ct);

        _logger.LogInformation("[PathRemap] Rewrote {Count} EventFile rows", affected);
        return affected;
    }

    /// <summary>
    /// Longest common directory prefix of a non-empty path list. We split
    /// on both `/` and `\` so backups produced on either OS produce a
    /// meaningful result. Trailing partial path elements are trimmed so
    /// the prefix always ends at a directory boundary.
    /// </summary>
    private static string LongestCommonDirectoryPrefix(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return string.Empty;
        if (paths.Count == 1)
        {
            var only = paths[0];
            var lastSlash = only.LastIndexOfAny(new[] { '/', '\\' });
            return lastSlash > 0 ? only.Substring(0, lastSlash + 1) : string.Empty;
        }

        var first = paths[0];
        var minLen = paths.Min(p => p.Length);
        var prefixLen = 0;
        for (int i = 0; i < minLen; i++)
        {
            var c = first[i];
            bool allMatch = true;
            for (int j = 1; j < paths.Count; j++)
            {
                if (paths[j][i] != c) { allMatch = false; break; }
            }
            if (!allMatch) break;
            prefixLen = i + 1;
        }
        if (prefixLen == 0) return string.Empty;
        var prefix = first.Substring(0, prefixLen);
        // Trim back to the last directory boundary so we don't end mid-name.
        var lastBoundary = prefix.LastIndexOfAny(new[] { '/', '\\' });
        return lastBoundary > 0 ? prefix.Substring(0, lastBoundary + 1) : string.Empty;
    }
}

/// <summary>
/// Result of PathRemapService.DetectAsync. Treat any non-null OldPrefix +
/// NewPrefix pair as a recommendation; the apply step is what mutates the
/// database.
/// </summary>
public class PathRemapPreview
{
    public int MissingFileCount { get; set; }
    public string? OldPrefix { get; set; }
    public string? NewPrefix { get; set; }
    public int SampleSize { get; set; }
    public int SampleMatches { get; set; }
    public int AffectedRowCount { get; set; }
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// True when the preview produced a concrete actionable suggestion.
    /// The frontend renders the "Apply Remap" button only when this is true.
    /// </summary>
    public bool HasSuggestion =>
        !string.IsNullOrEmpty(OldPrefix) &&
        !string.IsNullOrEmpty(NewPrefix) &&
        AffectedRowCount > 0;
}
