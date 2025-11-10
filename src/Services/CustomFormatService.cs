using System.Text.RegularExpressions;
using System.Text.Json;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for evaluating releases against custom format specifications
/// Implements Sonarr/Radarr-compatible custom format matching
/// </summary>
public class CustomFormatService
{
    private readonly MediaFileParser _parser;

    public CustomFormatService(MediaFileParser parser)
    {
        _parser = parser;
    }

    /// <summary>
    /// Evaluates a release against all custom formats and returns matches with scores
    /// </summary>
    public List<MatchedFormat> EvaluateRelease(string releaseTitle, List<CustomFormat> customFormats, Dictionary<int, int> formatScores)
    {
        var matched = new List<MatchedFormat>();

        foreach (var format in customFormats)
        {
            if (MatchesFormat(releaseTitle, format))
            {
                // Get score from profile's format items
                var score = formatScores.GetValueOrDefault(format.Id, 0);

                matched.Add(new MatchedFormat
                {
                    Name = format.Name,
                    Score = score
                });
            }
        }

        return matched;
    }

    /// <summary>
    /// Checks if a release matches all specifications in a custom format
    /// All specifications must match (AND logic)
    /// </summary>
    public bool MatchesFormat(string releaseTitle, CustomFormat format)
    {
        if (!format.Specifications.Any())
        {
            return false; // Empty format matches nothing
        }

        // Parse release title once
        var parsed = _parser.Parse(releaseTitle);

        // All specifications must match
        foreach (var spec in format.Specifications)
        {
            var matches = EvaluateSpecification(spec, releaseTitle, parsed);

            // Apply negation
            if (spec.Negate)
            {
                matches = !matches;
            }

            // If required and doesn't match, format fails
            if (spec.Required && !matches)
            {
                return false;
            }

            // If not required but doesn't match, format fails (all must match)
            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Evaluates a single specification against a release
    /// </summary>
    private bool EvaluateSpecification(FormatSpecification spec, string releaseTitle, ParsedFileInfo parsed)
    {
        return spec.Implementation switch
        {
            "ReleaseTitle" => EvaluateReleaseTitle(spec, releaseTitle),
            "Source" => EvaluateSource(spec, parsed),
            "Resolution" => EvaluateResolution(spec, parsed),
            "Size" => EvaluateSize(spec, 0), // Size needs to be passed in
            "ReleaseGroup" => EvaluateReleaseGroup(spec, parsed),
            "Language" => EvaluateLanguage(spec, parsed),
            "IndexerFlag" => false, // Not implemented yet
            "ReleaseType" => false, // Not implemented yet
            _ => false
        };
    }

    private bool EvaluateReleaseTitle(FormatSpecification spec, string releaseTitle)
    {
        if (!spec.Fields.ContainsKey("value"))
        {
            return false;
        }

        var pattern = spec.Fields["value"]?.ToString();
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(releaseTitle, pattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private bool EvaluateSource(FormatSpecification spec, ParsedFileInfo parsed)
    {
        if (!spec.Fields.ContainsKey("value"))
        {
            return false;
        }

        // Value can be either a source name (string) or ID (int)
        var value = spec.Fields["value"]?.ToString();
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(parsed.Source))
        {
            return false;
        }

        // Match source name case-insensitively
        return parsed.Source.Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    private bool EvaluateResolution(FormatSpecification spec, ParsedFileInfo parsed)
    {
        if (!spec.Fields.ContainsKey("value"))
        {
            return false;
        }

        var value = spec.Fields["value"]?.ToString();
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(parsed.Resolution))
        {
            return false;
        }

        // Match resolution case-insensitively
        return parsed.Resolution.Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    private bool EvaluateSize(FormatSpecification spec, long sizeInBytes)
    {
        var sizeInMB = sizeInBytes / (1024.0 * 1024.0);

        var hasMin = spec.Fields.ContainsKey("min");
        var hasMax = spec.Fields.ContainsKey("max");

        if (!hasMin && !hasMax)
        {
            return false;
        }

        if (hasMin)
        {
            var minValue = spec.Fields["min"];
            var min = minValue switch
            {
                JsonElement element => element.GetDouble(),
                double d => d,
                int i => (double)i,
                _ => 0.0
            };

            if (sizeInMB < min)
            {
                return false;
            }
        }

        if (hasMax)
        {
            var maxValue = spec.Fields["max"];
            var max = maxValue switch
            {
                JsonElement element => element.GetDouble(),
                double d => d,
                int i => (double)i,
                _ => double.MaxValue
            };

            if (sizeInMB > max)
            {
                return false;
            }
        }

        return true;
    }

    private bool EvaluateReleaseGroup(FormatSpecification spec, ParsedFileInfo parsed)
    {
        if (!spec.Fields.ContainsKey("value"))
        {
            return false;
        }

        var pattern = spec.Fields["value"]?.ToString();
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(parsed.ReleaseGroup))
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(parsed.ReleaseGroup, pattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private bool EvaluateLanguage(FormatSpecification spec, ParsedFileInfo parsed)
    {
        // For now, assume English unless specified in filename
        // This would need to be enhanced with proper language detection
        if (!spec.Fields.ContainsKey("value"))
        {
            return false;
        }

        var value = spec.Fields["value"]?.ToString();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        // Default to English
        return value.Equals("English", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.OrdinalIgnoreCase);
    }
}
