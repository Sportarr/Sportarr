using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services.Import;

/// <summary>A season folder's years. EndYear is set for a span such as 2024-25.</summary>
public sealed record SeasonFolder(int StartYear, int? EndYear);

/// <summary>
/// Reads the league and the season from a library's folders.
/// It works on names and paths only and never reads the disk.
/// </summary>
public static partial class LibraryFolderResolver
{
    [GeneratedRegex(@"^(?:season[\s._-]*)?(?<start>\d{4})(?:\s*[-/]\s*(?<end>\d{4}|\d{2}))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonPattern();

    /// <summary>
    /// "Season 2024", "Season 2024-2025", "Season 2024-25", "2024", "2024-25".
    /// Null for "Season 0", "Specials" and anything else.
    /// </summary>
    public static SeasonFolder? ParseSeasonFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return null;
        var match = SeasonPattern().Match(folderName.Trim());
        if (!match.Success) return null;

        var start = int.Parse(match.Groups["start"].Value);
        if (start < 1900 || start > 2199) return null;
        if (!match.Groups["end"].Success) return new SeasonFolder(start, null);

        var endText = match.Groups["end"].Value;
        var end = int.Parse(endText);
        if (endText.Length == 2)
        {
            // "1999-00" ends in the next century.
            end += start / 100 * 100;
            if (end < start) end += 100;
        }
        return end > start ? new SeasonFolder(start, end) : null;
    }

    /// <summary>The league's own season string for this folder, or null when none fits.</summary>
    public static string? MapToLeagueSeason(SeasonFolder folder, IReadOnlyCollection<string> leagueSeasons)
    {
        var parsed = leagueSeasons
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => (Text: s, Folder: ParseSeasonFolder(s)))
            .Where(s => s.Folder != null)
            .ToList();

        if (folder.EndYear != null)
            return parsed.FirstOrDefault(s => s.Folder == folder).Text;

        return parsed.FirstOrDefault(s => s.Folder!.StartYear == folder.StartYear && s.Folder.EndYear == null).Text
            ?? parsed.FirstOrDefault(s => s.Folder!.StartYear == folder.StartYear).Text;
    }

    /// <summary>
    /// The one league that the folder name names exactly, or null. The name
    /// must equal the league's name or one of its alternate names after
    /// normalization. A folder that only shares words with a league does not
    /// name it: "Premier League" is not "Antigua and Barbuda Premier League".
    /// Two exact matches are ambiguous.
    /// </summary>
    public static League? ResolveLeague(string folderName, IReadOnlyCollection<League> leagues)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return null;
        var matches = leagues
            .Where(l => ReleaseMatchingService.SeriesLabelEqualsLeagueAlias(folderName, l))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// (leagueFolderName, seasonFolderName) for a file below the root. The
    /// first folder that parses as a season is the season folder. The folder
    /// above it is the league folder. Without a season folder, the first
    /// folder below the root is the league folder. A file directly under the
    /// root gives the root's own name and no season folder.
    /// </summary>
    public static (string LeagueFolder, string? SeasonFolder) SplitPath(string rootPath, string filePath)
    {
        var split = Split(rootPath, filePath);
        var league = split.LeagueIndex < 0 ? Path.GetFileName(split.Root) : split.Folders[split.LeagueIndex];
        return (league, split.SeasonIndex < 0 ? null : split.Folders[split.SeasonIndex]);
    }

    /// <summary>The full path of the league folder that <see cref="SplitPath"/> names.</summary>
    public static string LeagueFolderPath(string rootPath, string filePath)
    {
        var split = Split(rootPath, filePath);
        return split.LeagueIndex < 0
            ? split.Root
            : Path.Combine(new[] { split.Root }.Concat(split.Folders.Take(split.LeagueIndex + 1)).ToArray());
    }

    // LeagueIndex -1 means the root itself is the league folder.
    private static (string Root, string[] Folders, int LeagueIndex, int SeasonIndex) Split(string rootPath, string filePath)
    {
        var root = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(root, filePath)) ?? string.Empty;
        var folders = relativeDirectory
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < folders.Length; i++)
        {
            if (ParseSeasonFolder(folders[i]) != null) return (root, folders, i - 1, i);
        }
        return (root, folders, folders.Length == 0 ? -1 : 0, -1);
    }
}
