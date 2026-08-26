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
    /// <summary>
    /// Ceiling on a single custom-format pattern evaluation. Matches the
    /// release profile matcher.
    /// </summary>
    private static readonly TimeSpan CustomFormatRegexTimeout = TimeSpan.FromSeconds(1);

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
            "Language" => EvaluateLanguage(spec, releaseTitle),
            "IndexerFlag" => EvaluateIndexerFlag(spec, indexerFlags),
            "QualityModifier" => EvaluateQualityModifier(spec, releaseTitle),
            "ReleaseType" => EvaluateReleaseType(spec, releaseTitle, parsed),
            _ => false
        };
    }

    /// <summary>
    /// Match a QualityModifier specification (Remux, Proper, Repack and so on).
    ///
    /// This case was missing here while the grab-time evaluator had it, and an
    /// unknown implementation answers no. Since a format only matches when
    /// every specification kind in it matches something, any imported format
    /// carrying a quality modifier could never match at rename or import time,
    /// however well the release fit.
    ///
    /// Mirrors ReleaseEvaluator.EvaluateQualityModifierSpec.
    /// </summary>
    private bool EvaluateQualityModifier(FormatSpecification spec, string releaseTitle)
    {
        if (!spec.Fields.TryGetValue("value", out var raw)) return false;

        var value = raw?.ToString();
        if (string.IsNullOrEmpty(value)) return false;

        if (int.TryParse(value, out var modifierId))
        {
            // The ids follow the enum these formats are exported with:
            // 0 none, 1 regional, 2 screener, 3 raw HD, 4 disc, 5 remux.
            // The old table paired the numbers with a different list
            // entirely, so an imported remux condition matched the word
            // "Regional" and scored the wrong releases at both ends.
            var pattern = modifierId switch
            {
                1 => @"\bRegional\b",
                2 => @"\b(Screener|SCR|DVDSCR|BDSCR)\b",
                3 => @"\bRaw[-_. ]?HD\b",
                4 => @"\b(BR[-_. ]?DISK|COMPLETE[-_. ]BLURAY|BD(25|50|66|100))\b",
                5 => @"\bRemux\b",
                _ => null
            };

            if (pattern == null) return false;

            return System.Text.RegularExpressions.Regex.IsMatch(
                releaseTitle, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return System.Text.RegularExpressions.Regex.IsMatch(
            releaseTitle,
            $@"\b{System.Text.RegularExpressions.Regex.Escape(value)}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
            // Bounded, like the release profile matcher already is. These
            // patterns are written by users or pulled from a guide, they are
            // evaluated for every release of every search, and one that
            // backtracks badly held a request thread for as long as it liked.
            // Enough of them at once would take the pool with it.
            return new Regex(pattern, RegexOptions.IgnoreCase, CustomFormatRegexTimeout).IsMatch(releaseTitle);
        }
        catch (RegexMatchTimeoutException)
        {
            // No logger on this service; the pattern simply does not match.
            return false;
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
        if (sizeInBytes <= 0)
        {
            return false;
        }

        // Custom format size bounds are gigabytes, which is what the grab-time
        // evaluator has always used. Comparing them against megabytes here
        // meant the same format scored one way at grab time and another at
        // rename time, off by a factor of a thousand.
        var sizeInGB = sizeInBytes / (1024.0 * 1024.0 * 1024.0);

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

            if (sizeInGB < min)
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

            if (sizeInGB > max)
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
            return new Regex(pattern, RegexOptions.IgnoreCase, CustomFormatRegexTimeout).IsMatch(parsed.ReleaseGroup);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Match a Language specification against the release's actual language.
    ///
    /// This used to answer "yes" for English no matter what the release was
    /// and "no" for every other language. A French or Spanish release picked
    /// up English scores it had not earned, and a format written to target
    /// that language could never match anything.
    /// </summary>
    private bool EvaluateLanguage(FormatSpecification spec, string releaseTitle)
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

        // Unmarked releases come back as English, which is the right default
        // for sports.
        var detected = LanguageDetector.DetectLanguage(releaseTitle) ?? "English";
        var isMultiLanguage = detected == "Multi" || detected == "Dual Audio";

        string? target;
        if (int.TryParse(value, out var languageId))
        {
            // -1 means any language, so anything satisfies it.
            if (languageId == -1) return true;
            target = LanguageDetector.NameForCustomFormatId(languageId);
        }
        else
        {
            target = value;
        }

        if (string.IsNullOrEmpty(target)) return false;

        // A multi-language release carries English alongside the rest, so it
        // satisfies an English requirement.
        if (isMultiLanguage && target.Equals("English", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return detected.Equals(target, StringComparison.OrdinalIgnoreCase);
    }
}
