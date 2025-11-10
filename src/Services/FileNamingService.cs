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
    /// </summary>
    public string BuildFolderName(string format, Event eventInfo)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "{Event Title}", eventInfo.Title },
            { "{Event Title The}", MoveArticleToEnd(eventInfo.Title) },
            { "{Event CleanTitle}", CleanTitle(eventInfo.Title) },
            { "{Event Id}", eventInfo.Id.ToString() }
        };

        tokens["{Year}"] = eventInfo.EventDate.Year.ToString();

        var folderName = ReplaceTokens(format, tokens);
        folderName = CleanFileName(folderName);

        return folderName;
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
            { "{Original Filename}", tokens.OriginalFilename }
        };

        if (tokens.AirDate.HasValue)
        {
            tokenMap["{Air Date}"] = tokens.AirDate.Value.ToString("yyyy-MM-dd");
            tokenMap["{Air Date Year}"] = tokens.AirDate.Value.Year.ToString();
            tokenMap["{Air Date Month}"] = tokens.AirDate.Value.Month.ToString("00");
            tokenMap["{Air Date Day}"] = tokens.AirDate.Value.Day.ToString("00");
        }

        return ReplaceTokens(format, tokenMap);
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
            "{Air Date}",
            "{Air Date Year}",
            "{Air Date Month}",
            "{Air Date Day}",
            "{Quality}",
            "{Quality Full}",
            "{Release Group}",
            "{Original Title}",
            "{Original Filename}"
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
            "{Year}"
        };
    }
}
