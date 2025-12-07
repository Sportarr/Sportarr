using Sportarr.Api.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Evaluates releases against quality profiles and custom formats
/// Implements scoring logic matching Sonarr/Radarr exactly
///
/// Sonarr's priority order (Quality Trumps All as of 2024):
/// 1. Quality
/// 2. Custom Format Score
/// 3. Protocol
/// 4. Episode Count
/// 5. Episode Number
/// 6. Indexer Priority
/// 7. Seeds/Peers (torrents) or Age (Usenet)
/// 8. Size
/// </summary>
public class ReleaseEvaluator
{
    private readonly ILogger<ReleaseEvaluator> _logger;
    private readonly EventPartDetector _partDetector;

    // Default quality weights when no profile is specified
    // These map to Sonarr's quality ranking system
    private static readonly Dictionary<string, int> DefaultQualityWeights = new()
    {
        // Resolutions (higher = better)
        { "2160p", 31 },   // 4K/UHD
        { "1080p", 23 },   // Full HD
        { "720p", 17 },    // HD
        { "480p", 8 },     // SD
        { "360p", 5 },     // Low SD
    };

    // Source quality modifiers (added to resolution score)
    private static readonly Dictionary<string, int> SourceModifiers = new()
    {
        { "Remux", 3 },
        { "BluRay", 2 },
        { "WEB-DL", 1 },
        { "WEBDL", 1 },
        { "WEBRip", 0 },
        { "HDTV", -1 },
        { "DVDRip", -2 },
        { "SDTV", -3 },
        { "CAM", -10 },
        { "TS", -9 },
        { "TC", -8 }
    };

    public ReleaseEvaluator(ILogger<ReleaseEvaluator> logger, EventPartDetector partDetector)
    {
        _logger = logger;
        _partDetector = partDetector;
    }

    /// <summary>
    /// Evaluate a release against a quality profile
    /// </summary>
    public ReleaseEvaluation EvaluateRelease(
        ReleaseSearchResult release,
        QualityProfile? profile,
        List<CustomFormat>? customFormats = null,
        string? requestedPart = null,
        string? sport = null)
    {
        var evaluation = new ReleaseEvaluation();

        // Parse quality from title
        var (resolution, source) = ParseQualityDetails(release.Title);
        evaluation.Quality = FormatQualityString(resolution, source);

        // Calculate base quality score from profile or defaults
        evaluation.QualityScore = CalculateQualityScore(resolution, source, profile);

        // PART VALIDATION: For multi-part episodes (Fighting sports), validate release matches requested part
        if (!string.IsNullOrEmpty(requestedPart) && !string.IsNullOrEmpty(sport))
        {
            var detectedPart = _partDetector.DetectPart(release.Title, sport);

            if (detectedPart == null)
            {
                evaluation.Rejections.Add($"Requested part '{requestedPart}' but release doesn't specify a part (may be full event or unlabeled)");
                _logger.LogDebug("[Release Evaluator] {Title} - No part detected, requested: {RequestedPart}", release.Title, requestedPart);
            }
            else if (!detectedPart.SegmentName.Equals(requestedPart, StringComparison.OrdinalIgnoreCase))
            {
                evaluation.Rejections.Add($"Wrong part: requested '{requestedPart}' but release contains '{detectedPart.SegmentName}'");
                evaluation.Approved = false;
                _logger.LogInformation("[Release Evaluator] {Title} - REJECTED: Requested '{RequestedPart}' but detected '{DetectedPart}'",
                    release.Title, requestedPart, detectedPart.SegmentName);
                return evaluation;
            }
            else
            {
                _logger.LogDebug("[Release Evaluator] {Title} - Part match confirmed: {Part}", release.Title, detectedPart.SegmentName);
            }
        }

        // Check quality profile
        if (profile != null && !string.IsNullOrEmpty(evaluation.Quality) && evaluation.Quality != "Unknown")
        {
            var isAllowed = IsQualityAllowed(resolution, source, profile);

            if (!isAllowed)
            {
                var allowedItems = profile.Items.Where(q => q.Allowed).Select(q => q.Name).ToList();
                _logger.LogInformation("[Release Evaluator] REJECTION: Quality '{Quality}' not in allowed list: [{AllowedItems}]",
                    evaluation.Quality, string.Join(", ", allowedItems));
                evaluation.Rejections.Add($"Quality {evaluation.Quality} is not wanted in quality profile");
            }
        }

        // Check file size limits
        if (profile != null && release.Size > 0)
        {
            var sizeMB = release.Size / (1024.0 * 1024.0);

            if (profile.MaxSize.HasValue && sizeMB > profile.MaxSize.Value)
            {
                evaluation.Rejections.Add($"Size {sizeMB:F1}MB exceeds maximum {profile.MaxSize.Value}MB");
            }
            else if (profile.MinSize.HasValue && sizeMB < profile.MinSize.Value)
            {
                evaluation.Rejections.Add($"Size {sizeMB:F1}MB is below minimum {profile.MinSize.Value}MB");
            }
        }

        // Evaluate custom formats using Sonarr's exact matching logic
        if (customFormats != null && customFormats.Any())
        {
            var formatEval = EvaluateCustomFormats(release, customFormats, profile);
            evaluation.MatchedFormats = formatEval.MatchedFormats;
            evaluation.CustomFormatScore = formatEval.TotalScore;
        }

        // Check minimum custom format score (Sonarr: releases below this are rejected)
        if (profile != null && profile.MinFormatScore.HasValue &&
            evaluation.CustomFormatScore < profile.MinFormatScore.Value)
        {
            evaluation.Rejections.Add($"Custom format score {evaluation.CustomFormatScore} is below minimum {profile.MinFormatScore.Value}");
        }

        // Check seeders for torrents
        if (release.Seeders.HasValue && release.Seeders.Value == 0)
        {
            evaluation.Rejections.Add("No seeders available");
        }

        // Calculate total score
        // Sonarr uses Quality + Custom Format Score for ranking
        evaluation.TotalScore = evaluation.QualityScore + evaluation.CustomFormatScore;

        // All releases are approved for manual search (Sonarr behavior)
        // Rejections are shown as warnings, but users can still download
        evaluation.Approved = true;

        _logger.LogDebug(
            "[Release Evaluator] {Title} - Quality: {Quality} ({QScore}), CF Score: {CScore}, Total: {Total}",
            release.Title, evaluation.Quality, evaluation.QualityScore,
            evaluation.CustomFormatScore, evaluation.TotalScore);

        return evaluation;
    }

    /// <summary>
    /// Parse resolution and source from release title
    /// </summary>
    private (string? resolution, string? source) ParseQualityDetails(string title)
    {
        string? resolution = null;
        string? source = null;

        // Parse resolution
        if (Regex.IsMatch(title, @"\b2160p\b", RegexOptions.IgnoreCase)) resolution = "2160p";
        else if (Regex.IsMatch(title, @"\b(4K|UHD)\b", RegexOptions.IgnoreCase)) resolution = "2160p";
        else if (Regex.IsMatch(title, @"\b1080p\b", RegexOptions.IgnoreCase)) resolution = "1080p";
        else if (Regex.IsMatch(title, @"\b720p\b", RegexOptions.IgnoreCase)) resolution = "720p";
        else if (Regex.IsMatch(title, @"\b480p\b", RegexOptions.IgnoreCase)) resolution = "480p";
        else if (Regex.IsMatch(title, @"\b360p\b", RegexOptions.IgnoreCase)) resolution = "360p";

        // Parse source
        if (Regex.IsMatch(title, @"\bRemux\b", RegexOptions.IgnoreCase)) source = "Remux";
        else if (Regex.IsMatch(title, @"\bBlu-?Ray\b", RegexOptions.IgnoreCase)) source = "BluRay";
        else if (Regex.IsMatch(title, @"\bWEB[-.]?DL\b", RegexOptions.IgnoreCase)) source = "WEB-DL";
        else if (Regex.IsMatch(title, @"\bWEBRip\b", RegexOptions.IgnoreCase)) source = "WEBRip";
        else if (Regex.IsMatch(title, @"\bHDTV\b", RegexOptions.IgnoreCase)) source = "HDTV";
        else if (Regex.IsMatch(title, @"\bDVDRip\b", RegexOptions.IgnoreCase)) source = "DVDRip";
        else if (Regex.IsMatch(title, @"\b(CAM|CAMRIP)\b", RegexOptions.IgnoreCase)) source = "CAM";
        else if (Regex.IsMatch(title, @"\b(TS|TELESYNC)\b", RegexOptions.IgnoreCase)) source = "TS";

        return (resolution, source);
    }

    /// <summary>
    /// Format quality string for display (e.g., "WEB-DL 1080p")
    /// </summary>
    private string FormatQualityString(string? resolution, string? source)
    {
        if (source != null && resolution != null)
            return $"{source} {resolution}";
        if (resolution != null)
            return resolution;
        if (source != null)
            return source;
        return "Unknown";
    }

    /// <summary>
    /// Calculate quality score based on profile's quality item ordering
    /// Sonarr ranks by position in the quality profile (higher position = better)
    /// </summary>
    private int CalculateQualityScore(string? resolution, string? source, QualityProfile? profile)
    {
        if (profile != null && profile.Items.Any())
        {
            // Find matching quality item in profile
            var qualityString = FormatQualityString(resolution, source);

            // Search through profile items by position (index = rank)
            for (int i = 0; i < profile.Items.Count; i++)
            {
                var item = profile.Items[i];
                if (MatchesQualityItem(resolution, source, item))
                {
                    // Score based on position in list (higher index = higher score)
                    // Multiply by 100 to give headroom for custom format scores
                    return (i + 1) * 100;
                }
            }
        }

        // Fallback to default weights
        var score = 0;
        if (resolution != null && DefaultQualityWeights.TryGetValue(resolution, out var resWeight))
        {
            score = resWeight * 100;
        }
        if (source != null && SourceModifiers.TryGetValue(source, out var srcMod))
        {
            score += srcMod * 10;
        }
        return score;
    }

    /// <summary>
    /// Check if resolution/source matches a quality profile item
    /// </summary>
    private bool MatchesQualityItem(string? resolution, string? source, QualityItem item)
    {
        var itemName = item.Name.ToLowerInvariant();

        // Check for resolution match
        if (resolution != null)
        {
            if (itemName.Contains(resolution.ToLowerInvariant()))
                return true;
        }

        // Check for source match
        if (source != null)
        {
            var sourceLower = source.ToLowerInvariant();
            if (itemName.Contains(sourceLower))
                return true;
            // Handle variations like "web-dl" vs "webdl" vs "web"
            if (source == "WEB-DL" && (itemName.Contains("webdl") || itemName.Contains("web dl") || itemName.Contains("web 1080") || itemName.Contains("web 720")))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Check if quality is allowed in profile
    /// </summary>
    private bool IsQualityAllowed(string? resolution, string? source, QualityProfile profile)
    {
        if (!profile.Items.Any())
            return true;

        // Check if any allowed item matches
        return profile.Items.Any(q => q.Allowed && MatchesQualityItem(resolution, source, q));
    }

    /// <summary>
    /// Evaluate which custom formats match this release
    /// Uses Sonarr's exact matching algorithm
    /// </summary>
    private (List<MatchedFormat> MatchedFormats, int TotalScore) EvaluateCustomFormats(
        ReleaseSearchResult release,
        List<CustomFormat> allFormats,
        QualityProfile? profile)
    {
        var matchedFormats = new List<MatchedFormat>();
        var totalScore = 0;

        foreach (var format in allFormats)
        {
            if (DoesFormatMatch(release, format))
            {
                // Get score for this format from profile's FormatItems
                var formatScore = profile?.FormatItems
                    .FirstOrDefault(fi => fi.FormatId == format.Id)?.Score ?? 0;

                matchedFormats.Add(new MatchedFormat
                {
                    Name = format.Name,
                    Score = formatScore
                });

                totalScore += formatScore;

                _logger.LogDebug("[Release Evaluator] Format '{Format}' matched with score {Score}",
                    format.Name, formatScore);
            }
        }

        return (matchedFormats, totalScore);
    }

    /// <summary>
    /// Check if a custom format matches a release
    /// Implements Sonarr's exact matching logic from SpecificationMatchesGroup.DidMatch:
    ///
    /// DidMatch => !(Matches.Any(m => m.Key.Required && m.Value == false) ||
    ///               Matches.All(m => m.Value == false));
    ///
    /// Translation:
    /// - A format matches if:
    ///   1. NO required specifications fail (if any required spec fails, format doesn't match)
    ///   2. NOT ALL specifications fail (at least one must match)
    ///
    /// Specifications are grouped by implementation type, and each group is evaluated separately.
    /// </summary>
    private bool DoesFormatMatch(ReleaseSearchResult release, CustomFormat format)
    {
        if (!format.Specifications.Any())
        {
            return false; // Empty format matches nothing
        }

        // Group specifications by implementation type (Sonarr's behavior)
        var specGroups = format.Specifications.GroupBy(s => NormalizeImplementation(s.Implementation));

        foreach (var group in specGroups)
        {
            var matches = group.ToDictionary(
                spec => spec,
                spec => EvaluateSpecification(release, spec)
            );

            // Check if this group matches using Sonarr's DidMatch logic
            var hasFailedRequired = matches.Any(m => m.Key.Required && !m.Value);
            var allFailed = matches.All(m => !m.Value);

            if (hasFailedRequired || allFailed)
            {
                return false; // This group failed
            }
        }

        return true; // All groups passed
    }

    /// <summary>
    /// Normalize implementation names to handle both full and short forms
    /// e.g., "ReleaseTitleSpecification" -> "ReleaseTitle"
    /// </summary>
    private string NormalizeImplementation(string implementation)
    {
        // Strip "Specification" suffix if present (Sonarr JSON uses full names)
        if (implementation.EndsWith("Specification", StringComparison.OrdinalIgnoreCase))
        {
            return implementation[..^"Specification".Length];
        }
        return implementation;
    }

    /// <summary>
    /// Evaluate a single specification against a release
    /// Returns the match result BEFORE applying negate (negate is applied inside)
    /// </summary>
    private bool EvaluateSpecification(ReleaseSearchResult release, FormatSpecification spec)
    {
        var normalizedImpl = NormalizeImplementation(spec.Implementation);
        bool matches = normalizedImpl switch
        {
            "ReleaseTitle" => EvaluateReleaseTitleSpec(release, spec),
            "Source" => EvaluateSourceSpec(release, spec),
            "Resolution" => EvaluateResolutionSpec(release, spec),
            "ReleaseGroup" => EvaluateReleaseGroupSpec(release, spec),
            "Language" => EvaluateLanguageSpec(release, spec),
            "Size" => EvaluateSizeSpec(release, spec),
            "QualityModifier" => EvaluateQualityModifierSpec(release, spec),
            "IndexerFlag" => EvaluateIndexerFlagSpec(release, spec),
            _ => false
        };

        // Apply negate (Sonarr does this at the specification level)
        return spec.Negate ? !matches : matches;
    }

    /// <summary>
    /// Evaluate ReleaseTitle specification (regex match against release title)
    /// </summary>
    private bool EvaluateReleaseTitleSpec(ReleaseSearchResult release, FormatSpecification spec)
    {
        var pattern = GetFieldValue(spec, "value");
        if (string.IsNullOrEmpty(pattern))
            return false;

        try
        {
            return Regex.IsMatch(release.Title, pattern, RegexOptions.IgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Custom Format] Invalid regex in spec '{Name}': {Pattern}", spec.Name, pattern);
            return false;
        }
    }

    /// <summary>
    /// Evaluate Source specification (matches media source)
    /// </summary>
    private bool EvaluateSourceSpec(ReleaseSearchResult release, FormatSpecification spec)
    {
        var value = GetFieldValue(spec, "value");
        if (string.IsNullOrEmpty(value))
            return false;

        var (_, detectedSource) = ParseQualityDetails(release.Title);
        if (string.IsNullOrEmpty(detectedSource))
            return false;

        // Handle numeric IDs (Sonarr format) or string names
        if (int.TryParse(value, out var sourceId))
        {
            // Map source IDs to names (based on Sonarr's Source enum)
            var sourceName = sourceId switch
            {
                1 => "CAM",
                2 => "TS",
                3 => "WORKPRINT",
                4 => "DVD",
                5 => "SDTV",
                6 => "HDTV",
                7 => "WEBRip",
                8 => "WEB-DL",
                9 => "BluRay",
                10 => "Remux",
                _ => null
            };
            return sourceName != null && sourceName.Equals(detectedSource, StringComparison.OrdinalIgnoreCase);
        }

        return value.Equals(detectedSource, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Evaluate Resolution specification
    /// </summary>
    private bool EvaluateResolutionSpec(ReleaseSearchResult release, FormatSpecification spec)
    {
        var value = GetFieldValue(spec, "value");
        if (string.IsNullOrEmpty(value))
            return false;

        var (detectedResolution, _) = ParseQualityDetails(release.Title);
        if (string.IsNullOrEmpty(detectedResolution))
            return false;

        // Handle numeric IDs (Sonarr format) or string names
        if (int.TryParse(value, out var resolutionId))
        {
            // Map resolution IDs to names (based on Sonarr's Resolution enum)
            var resolutionName = resolutionId switch
            {
                1 => "360p",
                2 => "480p",
                3 => "540p",
                4 => "576p",
                5 => "720p",
                6 => "1080p",
                7 => "2160p",
                _ => null
            };
            return resolutionName != null && resolutionName.Equals(detectedResolution, StringComparison.OrdinalIgnoreCase);
        }

        return value.Equals(detectedResolution, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Evaluate ReleaseGroup specification (regex match against detected release group)
    /// </summary>
    private bool EvaluateReleaseGroupSpec(ReleaseSearchResult release, FormatSpecification spec)
    {
        var pattern = GetFieldValue(spec, "value");
        if (string.IsNullOrEmpty(pattern))
            return false;

        // Extract release group from title (typically at the end after a dash or in brackets)
        var groupMatch = Regex.Match(release.Title, @"-([A-Za-z0-9]+)(?:\.[a-z]{2,4})?$", RegexOptions.IgnoreCase);
        if (!groupMatch.Success)
        {
            // Try bracket format
            groupMatch = Regex.Match(release.Title, @"\[([A-Za-z0-9]+)\](?:\.[a-z]{2,4})?$", RegexOptions.IgnoreCase);
        }

        if (!groupMatch.Success)
            return false;

        var releaseGroup = groupMatch.Groups[1].Value;

        try
        {
            return Regex.IsMatch(releaseGroup, pattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Evaluate Language specification
    /// </summary>
    private bool EvaluateLanguageSpec(ReleaseSearchResult release, FormatSpecification spec)
    {
        var value = GetFieldValue(spec, "value");
        if (string.IsNullOrEmpty(value))
            return false;

        // Check for language indicators in release title
        var title = release.Title.ToLowerInvariant();

        // Handle numeric IDs (Sonarr's language IDs) or language names
        if (int.TryParse(value, out var langId))
        {
            // Map common language IDs
            var langPatterns = langId switch
            {
                1 => new[] { @"\benglish\b", @"\beng\b" }, // English
                2 => new[] { @"\bfrench\b", @"\bfre\b", @"\bfra\b", @"\bvff\b", @"\bvostfr\b" }, // French
                3 => new[] { @"\bspanish\b", @"\bspa\b", @"\besp\b", @"\bcastellano\b" }, // Spanish
                4 => new[] { @"\bgerman\b", @"\bger\b", @"\bdeu\b" }, // German
                5 => new[] { @"\bitalian\b", @"\bita\b" }, // Italian
                8 => new[] { @"\bjapanese\b", @"\bjpn\b", @"\bjap\b" }, // Japanese
                11 => new[] { @"\bportuguese\b", @"\bpor\b", @"\bptbr\b" }, // Portuguese
                _ => null
            };

            if (langPatterns == null)
                return false;

            return langPatterns.Any(p => Regex.IsMatch(title, p, RegexOptions.IgnoreCase));
        }

        // Direct language name match
        return title.Contains(value.ToLowerInvariant());
    }

    /// <summary>
    /// Evaluate Size specification
    /// </summary>
    private bool EvaluateSizeSpec(ReleaseSearchResult release, FormatSpecification spec)
    {
        if (release.Size <= 0)
            return false;

        var sizeGB = release.Size / (1024.0 * 1024.0 * 1024.0);

        var minValue = GetFieldNumeric(spec, "min");
        var maxValue = GetFieldNumeric(spec, "max");

        if (minValue.HasValue && sizeGB < minValue.Value)
            return false;
        if (maxValue.HasValue && sizeGB > maxValue.Value)
            return false;

        return minValue.HasValue || maxValue.HasValue;
    }

    /// <summary>
    /// Evaluate QualityModifier specification (Remux, Proper, Repack, etc.)
    /// </summary>
    private bool EvaluateQualityModifierSpec(ReleaseSearchResult release, FormatSpecification spec)
    {
        var value = GetFieldValue(spec, "value");
        if (string.IsNullOrEmpty(value))
            return false;

        var title = release.Title;

        // Handle numeric IDs or string names
        if (int.TryParse(value, out var modifierId))
        {
            var pattern = modifierId switch
            {
                1 => @"\bRemux\b",
                2 => @"\bProper\b",
                3 => @"\bRepack\b",
                4 => @"\bReal\b",
                5 => @"\bRegional\b",
                _ => null
            };

            if (pattern == null)
                return false;

            return Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase);
        }

        // Direct string match
        return Regex.IsMatch(title, $@"\b{Regex.Escape(value)}\b", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Evaluate IndexerFlag specification
    /// </summary>
    private bool EvaluateIndexerFlagSpec(ReleaseSearchResult release, FormatSpecification spec)
    {
        // IndexerFlag requires metadata from the indexer that may not be available
        // For now, check common flags that might be in the title
        var value = GetFieldValue(spec, "value");
        if (string.IsNullOrEmpty(value))
            return false;

        // Check if release has indexer flags
        if (release.IndexerFlags == null)
            return false;

        // Handle numeric IDs
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

            return release.IndexerFlags.Contains(flagName, StringComparison.OrdinalIgnoreCase);
        }

        return release.IndexerFlags.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get string field value from specification
    /// </summary>
    private string? GetFieldValue(FormatSpecification spec, string fieldName)
    {
        if (!spec.Fields.TryGetValue(fieldName, out var value))
            return null;

        return value switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32().ToString(),
            _ => value?.ToString()
        };
    }

    /// <summary>
    /// Get numeric field value from specification
    /// </summary>
    private double? GetFieldNumeric(FormatSpecification spec, string fieldName)
    {
        if (!spec.Fields.TryGetValue(fieldName, out var value))
            return null;

        return value switch
        {
            double d => d,
            int i => i,
            float f => f,
            decimal dec => (double)dec,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDouble(),
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }
}
