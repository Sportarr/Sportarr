using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Handles file and folder naming with token replacement
/// Based on Sonarr/Radarr naming conventions
/// </summary>
public class FileNamingService
{
    private readonly ILogger<FileNamingService> _logger;

    // Invalid filename characters (Windows + Unix)
    private static readonly char[] InvalidFileChars = Path.GetInvalidFileNameChars()
        .Concat(new[] { ':', '*', '?', '"', '<', '>', '|' })
        .Distinct()
        .ToArray();

    private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

    public FileNamingService(ILogger<FileNamingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Build filename from format template and tokens
    /// </summary>
    public string BuildFileName(string format, FileNamingTokens tokens, string extension)
    {
        var filename = ReplaceTokens(format, tokens);
        filename = CleanFileName(filename);

        // Ensure extension starts with dot
        if (!extension.StartsWith('.'))
            extension = "." + extension;

        return filename + extension;
    }

    /// <summary>
    /// Build folder name from format template and event
    /// Supports tokens: {League}, {Sport}, {Event Title}, {Event Title The}, {Event CleanTitle}, {Year}, {Event Id}, {Series}, {Season}
    /// Example: "{League}/{Event Title}" → "UFC/UFC 320"
    /// Example: "{Series}/Season {Season}" → "UFC/Season 2024" (Plex TV show style)
    /// </summary>
    public string BuildFolderName(string format, Event eventInfo)
    {
        var tokens = GetFolderTokens(eventInfo);
        var folderName = ReplaceTokens(format, tokens);

        // Clean each path segment separately (in case format contains slashes for hierarchy)
        var segments = folderName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var cleanedSegments = segments.Select(seg => CleanFileName(seg)).ToArray();
        folderName = string.Join(Path.DirectorySeparatorChar, cleanedSegments);

        return folderName;
    }

    /// <summary>
    /// Build complete folder path using granular folder settings
    /// Respects CreateLeagueFolders, CreateSeasonFolders, and CreateEventFolders settings
    /// </summary>
    /// <param name="settings">Media management settings with folder options</param>
    /// <param name="eventInfo">Event to build path for</param>
    /// <returns>Relative folder path from root folder</returns>
    public string BuildFolderPath(MediaManagementSettings settings, Event eventInfo)
    {
        var tokens = GetFolderTokens(eventInfo);
        var pathParts = new List<string>();

        // League folder (e.g., "UFC", "Premier League")
        if (settings.CreateLeagueFolders && !string.IsNullOrWhiteSpace(settings.LeagueFolderFormat))
        {
            var leagueFolder = ReplaceTokens(settings.LeagueFolderFormat, tokens);
            leagueFolder = CleanFileName(leagueFolder);
            if (!string.IsNullOrWhiteSpace(leagueFolder))
            {
                pathParts.Add(leagueFolder);
            }
        }

        // Season folder (e.g., "Season 2024") - only if league folders are enabled
        if (settings.CreateLeagueFolders && settings.CreateSeasonFolders && !string.IsNullOrWhiteSpace(settings.SeasonFolderFormat))
        {
            var seasonFolder = ReplaceTokens(settings.SeasonFolderFormat, tokens);
            seasonFolder = CleanFileName(seasonFolder);
            if (!string.IsNullOrWhiteSpace(seasonFolder))
            {
                pathParts.Add(seasonFolder);
            }
        }

        // Event folder (e.g., "UFC 310 (2024-12-14) E45") - only if season folders are enabled
        // HARDCODED format to ensure episode numbers are always included for proper file organization
        // This prevents conflicts with same-day events and ensures structural consistency
        if (settings.CreateLeagueFolders && settings.CreateSeasonFolders && settings.CreateEventFolders)
        {
            const string EventFolderFormat = "{Event Title} ({Year}-{Month}-{Day}) E{Episode}";
            var eventFolder = ReplaceTokens(EventFolderFormat, tokens);
            eventFolder = CleanFileName(eventFolder);
            if (!string.IsNullOrWhiteSpace(eventFolder))
            {
                pathParts.Add(eventFolder);
            }
        }

        return string.Join(Path.DirectorySeparatorChar, pathParts);
    }

    /// <summary>
    /// Get common folder tokens for an event
    /// </summary>
    private Dictionary<string, string> GetFolderTokens(Event eventInfo)
    {
        // Build team matchup string for team sports (e.g., "Arsenal vs Chelsea")
        var homeTeam = eventInfo.HomeTeam?.Name ?? eventInfo.HomeTeamName;
        var awayTeam = eventInfo.AwayTeam?.Name ?? eventInfo.AwayTeamName;
        var matchup = !string.IsNullOrEmpty(homeTeam) && !string.IsNullOrEmpty(awayTeam)
            ? $"{homeTeam} vs {awayTeam}"
            : null;

        // Use the event title, but for team sports with teams defined, prefer the matchup format
        var effectiveTitle = eventInfo.Title;
        if (!string.IsNullOrEmpty(matchup) &&
            (string.IsNullOrEmpty(effectiveTitle) ||
             effectiveTitle.Equals(eventInfo.League?.Name, StringComparison.OrdinalIgnoreCase) ||
             effectiveTitle.Contains("Season", StringComparison.OrdinalIgnoreCase)))
        {
            // Title is generic (just league name or contains "Season"), use matchup instead
            effectiveTitle = matchup;
        }

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "{Event Title}", effectiveTitle ?? "Unknown Event" },
            { "{Event Title The}", MoveArticleToEnd(effectiveTitle ?? "Unknown Event") },
            { "{Event CleanTitle}", CleanTitle(effectiveTitle ?? "Unknown Event") },
            { "{Event Id}", eventInfo.Id.ToString() },
            { "{League}", eventInfo.League?.Name ?? "Unknown League" },
            { "{Sport}", eventInfo.Sport ?? "Unknown Sport" },
            // Team tokens for team sports
            { "{Home Team}", homeTeam ?? "" },
            { "{Away Team}", awayTeam ?? "" },
            { "{Matchup}", matchup ?? effectiveTitle ?? "Unknown Event" },
            // Plex TV show structure support
            { "{Series}", eventInfo.League?.Name ?? eventInfo.Sport ?? "Unknown" },
            { "{Season}", eventInfo.SeasonNumber?.ToString("0000") ?? eventInfo.Season ?? eventInfo.EventDate.Year.ToString() },
            // Date tokens for folder naming
            { "{Year}", eventInfo.EventDate.Year.ToString() },
            { "{Month}", eventInfo.EventDate.Month.ToString("00") },
            { "{Day}", eventInfo.EventDate.Day.ToString("00") },
            // Episode number for unique identification (handles double headers)
            { "{Episode}", eventInfo.EpisodeNumber?.ToString("00") ?? "01" }
        };

        return tokens;
    }

    /// <summary>
    /// Replace tokens in a format string
    /// </summary>
    private string ReplaceTokens(string format, FileNamingTokens tokens)
    {
        var tokenMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "{Event Title}", tokens.EventTitle },
            { "{Event Title The}", MoveArticleToEnd(tokens.EventTitle) },
            { "{Event CleanTitle}", CleanTitle(tokens.EventTitle) },
            { "{Quality}", tokens.Quality },
            { "{Quality Full}", tokens.QualityFull },
            { "{Release Group}", tokens.ReleaseGroup },
            { "{Original Title}", tokens.OriginalTitle },
            { "{Original Filename}", tokens.OriginalFilename },
            // Plex TV show structure tokens (S## and E## format for Plex/Sonarr compatibility)
            { "{Series}", tokens.Series },
            { "{Season}", FormatSeasonNumber(tokens.Season) },  // S01, S02, S2025, etc
            { "{Episode}", FormatEpisodeNumber(tokens.Episode) },  // E01, E02, etc
            { "{Part}", tokens.Part }
        };

        if (tokens.AirDate.HasValue)
        {
            var dateStr = tokens.AirDate.Value.ToString("yyyy-MM-dd");
            tokenMap["{Air Date}"] = dateStr;
            tokenMap["{Event Date}"] = dateStr;  // Alias for sports context
            tokenMap["{Air Date Year}"] = tokens.AirDate.Value.Year.ToString();
            tokenMap["{Air Date Month}"] = tokens.AirDate.Value.Month.ToString("00");
            tokenMap["{Air Date Day}"] = tokens.AirDate.Value.Day.ToString("00");
            // Event Date variants
            tokenMap["{Event Date Year}"] = tokens.AirDate.Value.Year.ToString();
            tokenMap["{Event Date Month}"] = tokens.AirDate.Value.Month.ToString("00");
            tokenMap["{Event Date Day}"] = tokens.AirDate.Value.Day.ToString("00");
        }

        return ReplaceTokens(format, tokenMap);
    }

    /// <summary>
    /// Format season number with S## prefix (e.g., "2025" → "S2025", "01" → "S01")
    /// </summary>
    private string FormatSeasonNumber(string? season)
    {
        if (string.IsNullOrEmpty(season))
            return "S01";

        // Remove any existing S prefix
        season = season.TrimStart('S', 's');

        // If it's a 4-digit year, use it as-is (S2025)
        if (season.Length == 4 && int.TryParse(season, out _))
            return $"S{season}";

        // Otherwise, pad to 2 digits (S01, S02, etc)
        if (int.TryParse(season, out var seasonNum))
            return $"S{seasonNum:00}";

        return $"S{season}";
    }

    /// <summary>
    /// Format episode number with E## prefix (e.g., "1" → "E01", "10" → "E10")
    /// </summary>
    private string FormatEpisodeNumber(string? episode)
    {
        if (string.IsNullOrEmpty(episode))
            return "E01";

        // Remove any existing E prefix
        episode = episode.TrimStart('E', 'e');

        // Pad to 2 digits
        if (int.TryParse(episode, out var episodeNum))
            return $"E{episodeNum:00}";

        return $"E{episode}";
    }

    /// <summary>
    /// Replace tokens using a dictionary
    /// </summary>
    private string ReplaceTokens(string format, Dictionary<string, string> tokens)
    {
        var result = format;

        foreach (var token in tokens)
        {
            if (!string.IsNullOrEmpty(token.Value))
            {
                result = result.Replace(token.Key, token.Value, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Remove any remaining unreplaced tokens
        result = Regex.Replace(result, @"\{[^}]+\}", string.Empty);

        // Clean up extra spaces and dashes
        result = Regex.Replace(result, @"\s+", " ");
        result = Regex.Replace(result, @"\s*-\s*-\s*", " - ");
        result = result.Trim(' ', '-', '.');

        return result;
    }

    /// <summary>
    /// Remove invalid characters from filename
    /// </summary>
    public string CleanFileName(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return filename;

        // Replace invalid characters with space
        foreach (var c in InvalidFileChars)
        {
            filename = filename.Replace(c, ' ');
        }

        // Clean up multiple spaces
        filename = Regex.Replace(filename, @"\s+", " ");

        // Trim
        filename = filename.Trim(' ', '.');

        return filename;
    }

    /// <summary>
    /// Remove invalid characters from path
    /// </summary>
    public string CleanPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        foreach (var c in InvalidPathChars)
        {
            path = path.Replace(c, '_');
        }

        return path;
    }

    /// <summary>
    /// Create a clean title (alphanumeric only, lowercase)
    /// </summary>
    private string CleanTitle(string title)
    {
        // Remove non-alphanumeric characters
        var clean = Regex.Replace(title, @"[^a-z0-9\s]", string.Empty, RegexOptions.IgnoreCase);

        // Replace spaces with empty string and convert to lowercase
        clean = Regex.Replace(clean, @"\s+", string.Empty).ToLowerInvariant();

        return clean;
    }

    /// <summary>
    /// Move leading article (The, A, An) to the end
    /// </summary>
    private string MoveArticleToEnd(string title)
    {
        var match = Regex.Match(title, @"^(The|A|An)\s+(.+)$", RegexOptions.IgnoreCase);

        if (match.Success)
        {
            return $"{match.Groups[2].Value}, {match.Groups[1].Value}";
        }

        return title;
    }

    /// <summary>
    /// Get available file naming tokens for UI display
    /// </summary>
    public List<string> GetAvailableFileTokens()
    {
        return new List<string>
        {
            "{Event Title}",
            "{Event Title The}",
            "{Event CleanTitle}",
            "{Event Date}",
            "{Event Date Year}",
            "{Event Date Month}",
            "{Event Date Day}",
            "{Air Date}",       // Alias for Event Date (Sonarr compatibility)
            "{Air Date Year}",
            "{Air Date Month}",
            "{Air Date Day}",
            "{Quality}",
            "{Quality Full}",
            "{Release Group}",
            "{Original Title}",
            "{Original Filename}",
            // Plex TV show structure
            "{Series}",
            "{Season}",
            "{Episode}",
            "{Part}"
        };
    }

    /// <summary>
    /// Get available folder naming tokens for UI display
    /// </summary>
    public List<string> GetAvailableFolderTokens()
    {
        return new List<string>
        {
            "{Event Title}",
            "{Event Title The}",
            "{Event CleanTitle}",
            "{Event Id}",
            "{League}",
            "{Sport}",
            "{Year}",
            "{Month}",
            "{Day}",
            // Team sport tokens
            "{Home Team}",
            "{Away Team}",
            "{Matchup}",
            // Plex TV show structure
            "{Series}",
            "{Season}",
            "{Episode}"
        };
    }
}
