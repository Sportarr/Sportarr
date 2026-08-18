using System.Text.RegularExpressions;
using System.Text.Json;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for evaluating releases against custom format specifications.
/// Supports importing custom formats from other *arr applications.
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
    /// Value for the {Custom Formats} naming token: space-joined names of
    /// the formats that match the release title AND are flagged
    /// IncludeCustomFormatWhenRenaming.
    /// </summary>
    public string BuildRenameToken(string releaseTitle, List<CustomFormat> customFormats, long sizeInBytes = 0, string? indexerFlags = null)
    {
        if (string.IsNullOrWhiteSpace(releaseTitle) || customFormats.Count == 0)
            return string.Empty;

        return string.Join(" ", customFormats
            .Where(f => f.IncludeCustomFormatWhenRenaming && MatchesFormat(releaseTitle, f, sizeInBytes, indexerFlags))
            .Select(f => f.Name));
    }

    /// <summary>
    /// Checks if a release matches a custom format with the same semantics the
    /// release evaluator uses: every Required specification must match, and
    /// each implementation group needs at least one match. Specs of the same
    /// type are alternatives, different types are cumulative. Demanding that
    /// every spec match made a TRaSH web tier unmatchable here, because its
    /// WEBDL and WEBRIP source specs can never both be true.
    /// </summary>
    public bool MatchesFormat(string releaseTitle, CustomFormat format, long sizeInBytes = 0, string? indexerFlags = null)
    {
        if (!format.Specifications.Any())
        {
            return false; // Empty format matches nothing
        }

        // Parse release title once
        var parsed = _parser.Parse(releaseTitle);

        var specResults = format.Specifications.Select(spec =>
        {
            var matches = EvaluateSpecification(spec, releaseTitle, parsed, sizeInBytes, indexerFlags);
            if (spec.Negate)
            {
                matches = !matches;
            }
            return (Spec: spec, Matched: matches);
        }).ToList();

        if (specResults.Any(r => r.Spec.Required && !r.Matched))
        {
            return false;
        }

        foreach (var group in specResults.GroupBy(r => NormalizeImplementation(r.Spec.Implementation)))
        {
            if (!group.Any(r => r.Matched))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// TRaSH JSON uses the full Sonarr names ("ReleaseGroupSpecification");
    /// specs created in the UI use the short forms. Both must route to the
    /// same evaluator and land in the same alternatives group.
    /// </summary>
    private static string NormalizeImplementation(string implementation)
    {
        if (implementation.EndsWith("Specification", StringComparison.OrdinalIgnoreCase))
        {
            return implementation[..^"Specification".Length];
        }
        return implementation;
    }

    /// <summary>
    /// Evaluates a single specification against a release
    /// </summary>
    private bool EvaluateSpecification(FormatSpecification spec, string releaseTitle, ParsedFileInfo parsed, long sizeInBytes, string? indexerFlags)
    {
        return NormalizeImplementation(spec.Implementation) switch
        {
            "ReleaseTitle" => EvaluateReleaseTitle(spec, releaseTitle),
            "Source" => EvaluateSource(spec, parsed),
            "Resolution" => EvaluateResolution(spec, parsed),
            "Size" => EvaluateSize(spec, sizeInBytes),
            "ReleaseGroup" => EvaluateReleaseGroup(spec, parsed),
            "Language" => EvaluateLanguage(spec, parsed),
            "IndexerFlag" => EvaluateIndexerFlag(spec, indexerFlags),
            "ReleaseType" => EvaluateReleaseType(spec, releaseTitle, parsed),
            _ => false
        };
    }

    /// <summary>
    /// Mirrors ReleaseEvaluator.EvaluateIndexerFlagSpec exactly so grab-time and
    /// rename/import-time evaluation of the same release agree.
    /// </summary>
    private bool EvaluateIndexerFlag(FormatSpecification spec, string? indexerFlags)
    {
        if (!spec.Fields.ContainsKey("value"))
        {
            return false;
        }

        var value = spec.Fields["value"]?.ToString();
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(indexerFlags))
        {
            return false;
        }

        if (int.TryParse(value, out var flagId))
        {
            var flagName = flagId switch
            {
                1 => "freeleech",
                2 => "halfleech",
                4 => "doubleupload",
                8 => "internal",
                16 => "scene",
                32 => "freeleech75",
                64 => "freeleech25",
                _ => null
            };

            if (flagName == null)
                return false;

            return indexerFlags.Contains(flagName, StringComparison.OrdinalIgnoreCase);
        }

        return indexerFlags.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private bool EvaluateReleaseType(FormatSpecification spec, string releaseTitle, ParsedFileInfo parsed)
    {
        if (!spec.Fields.ContainsKey("value"))
        {
            return false;
        }

        var value = spec.Fields["value"]?.ToString();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (!Enum.TryParse<ReleaseType>(value, ignoreCase: true, out var expectedType))
        {
            return false;
        }

        var detected = ReleaseTypeDetector.Detect(releaseTitle, parsed.SportarrLeagueId, parsed.SportarrEventId);
        return detected == expectedType;
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
