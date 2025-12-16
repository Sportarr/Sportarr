using Sportarr.Api.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Evaluates releases against quality profiles and custom formats.
/// Uses *arr-compatible scoring logic for seamless integration with existing workflows.
///
/// Sportarr ranking priority (Quality Trumps All):
/// 1. Quality (profile position)
/// 2. Custom Format Score
/// 3. Seeders (for torrents)
/// 4. Size
/// </summary>
public class ReleaseEvaluator
{
    private readonly ILogger<ReleaseEvaluator> _logger;
    private readonly EventPartDetector _partDetector;

    // Default quality weights when no profile is specified
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
    // Based on standard quality types: Bluray, WEBDL, WEBRip, HDTV, DVD, SDTV, Raw-HD
    private static readonly Dictionary<string, int> SourceModifiers = new()
    {
        { "Bluray Remux", 4 },  // Bluray-1080p Remux, Bluray-2160p Remux
        { "Bluray", 3 },        // Bluray-480p, Bluray-576p, Bluray-720p, Bluray-1080p, Bluray-2160p
        { "Raw-HD", 2 },        // Raw-HD
        { "WEBDL", 1 },         // WEBDL-480p, WEBDL-720p, WEBDL-1080p, WEBDL-2160p
        { "WEBRip", 0 },        // WEBRip-480p, WEBRip-720p, WEBRip-1080p, WEBRip-2160p
        { "HDTV", -1 },         // HDTV-720p, HDTV-1080p, HDTV-2160p
        { "DVD", -2 },          // DVD
        { "SDTV", -3 },         // SDTV
    };

    public ReleaseEvaluator(ILogger<ReleaseEvaluator> logger, EventPartDetector partDetector)
    {
        _logger = logger;
        _partDetector = partDetector;
    }

    /// <summary>
    /// Evaluate a release against a quality profile
    /// </summary>
    /// <param name="release">The release to evaluate</param>
    /// <param name="profile">Quality profile to evaluate against</param>
    /// <param name="customFormats">Optional custom formats to apply</param>
    /// <param name="requestedPart">Optional specific part requested (e.g., "Main Card", "Prelims")</param>
    /// <param name="sport">Sport type for part detection</param>
    /// <param name="enableMultiPartEpisodes">Whether multi-part episodes are enabled. When false, rejects releases with detected parts.</param>
    /// <param name="eventTitle">Optional event title for event-type-specific part handling (e.g., Fight Night vs PPV)</param>
    public ReleaseEvaluation EvaluateRelease(
        ReleaseSearchResult release,
        QualityProfile? profile,
        List<CustomFormat>? customFormats = null,
        string? requestedPart = null,
        string? sport = null,
        bool enableMultiPartEpisodes = true,
        string? eventTitle = null)
    {
        var evaluation = new ReleaseEvaluation();

        // Parse quality from title
        var (resolution, source) = ParseQualityDetails(release.Title);
        evaluation.Quality = FormatQualityString(resolution, source);

        // Calculate base quality score from profile or defaults
        evaluation.QualityScore = CalculateQualityScore(resolution, source, profile);

        // PART VALIDATION: For multi-part episodes (Fighting sports), validate release matches requested part
        var isFightingSport = EventPartDetector.IsFightingSport(sport ?? "");

        if (isFightingSport)
        {
            var detectedPart = _partDetector.DetectPart(release.Title, sport ?? "Fighting", eventTitle);

            // Check if this is a Fight Night style event (base name = Main Card)
            var isFightNightStyle = EventPartDetector.IsFightNightStyleEvent(eventTitle, null);

            if (!enableMultiPartEpisodes)
            {
                // Multi-part DISABLED: Only accept full event files (no part detected)
                if (detectedPart != null)
                {
                    // This is a part file (Main Card, Prelims, PPV, etc.) - reject it
                    evaluation.Rejections.Add($"Multi-part disabled: rejecting part file '{detectedPart.SegmentName}' (only full event files accepted)");
                    evaluation.Approved = false;
                    _logger.LogInformation("[Release Evaluator] {Title} - REJECTED: Multi-part disabled but release has part '{Part}'",
                        release.Title, detectedPart.SegmentName);
                    return evaluation;
                }
                else
                {
                    // No part detected - this is likely a full event file, which is what we want
                    _logger.LogDebug("[Release Evaluator] {Title} - Full event file accepted (multi-part disabled)", release.Title);
                }
            }
            else if (!string.IsNullOrEmpty(requestedPart))
            {
                // Multi-part ENABLED and specific part requested
                if (detectedPart == null)
                {
                    // No part detected in release title
                    // FIGHT NIGHT HANDLING: For Fight Night events, base name = Main Card
                    // UFC Fight Night releases typically use just the event name for the main card
                    if (isFightNightStyle && requestedPart.Equals("Main Card", StringComparison.OrdinalIgnoreCase))
                    {
                        // Accept base-name releases as Main Card for Fight Night events
                        _logger.LogDebug("[Release Evaluator] {Title} - Fight Night: base name accepted as Main Card", release.Title);
                    }
                    else
                    {
                        // For PPV events or when requesting non-Main Card parts, reject base-name releases
                        evaluation.Rejections.Add($"Requested part '{requestedPart}' but release has no part detected (likely full event file)");
                        evaluation.Approved = false;
                        _logger.LogInformation("[Release Evaluator] {Title} - REJECTED: Requested '{RequestedPart}' but no part detected in release",
                            release.Title, requestedPart);
                        return evaluation;
                    }
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
            // else: Multi-part enabled but no specific part requested - accept any (parts or full event)
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

        // Calculate total score for display purposes
        // Note: Sportarr compares Quality and CF Score separately in priority order
        // (Quality trumps CF score - see IndexerSearchService sorting)
        // This combined score is for UI display/reference only
        evaluation.TotalScore = evaluation.QualityScore + evaluation.CustomFormatScore;

        // All releases are approved for manual search
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

        // Parse source (order matters - more specific patterns first)
        // Standard quality types: Bluray Remux, Bluray, Raw-HD, WEBDL, WEBRip, HDTV, DVD, SDTV
        if (Regex.IsMatch(title, @"\bRemux\b", RegexOptions.IgnoreCase) && Regex.IsMatch(title, @"\bBlu-?Ray\b", RegexOptions.IgnoreCase))
            source = "Bluray Remux";
        else if (Regex.IsMatch(title, @"\bBlu-?Ray\b", RegexOptions.IgnoreCase)) source = "Bluray";
        else if (Regex.IsMatch(title, @"\bRaw[-.]?HD\b", RegexOptions.IgnoreCase)) source = "Raw-HD";
        else if (Regex.IsMatch(title, @"\bWEB[-.]?DL\b", RegexOptions.IgnoreCase)) source = "WEBDL";
        else if (Regex.IsMatch(title, @"\bWEBRip\b", RegexOptions.IgnoreCase)) source = "WEBRip";
        else if (Regex.IsMatch(title, @"\bWEB\b", RegexOptions.IgnoreCase)) source = "WEBDL"; // Generic WEB treated as WEBDL
        else if (Regex.IsMatch(title, @"\bHDTV\b", RegexOptions.IgnoreCase)) source = "HDTV";
        else if (Regex.IsMatch(title, @"\b(DVD|DVDRip)\b", RegexOptions.IgnoreCase)) source = "DVD";
        else if (Regex.IsMatch(title, @"\bSDTV\b", RegexOptions.IgnoreCase)) source = "SDTV";

        return (resolution, source);
    }

    /// <summary>
    /// Format quality string for display in standard format (e.g., "WEBDL-1080p", "Bluray-720p")
    /// Uses *arr-compatible naming convention matching Sonarr's quality definitions
    /// </summary>
    private string FormatQualityString(string? resolution, string? source)
    {
        if (source != null && resolution != null)
        {
            // Special handling for Remux - format as "Bluray-1080p Remux"
            if (source == "Bluray Remux")
                return $"Bluray-{resolution} Remux";
            return $"{source}-{resolution}";
        }
        if (resolution != null)
            return resolution;
        if (source != null)
            return source;
        return "Unknown";
    }

    /// <summary>
    /// Sonarr standard quality definitions mapping.
    /// Maps our detected quality to Sonarr-compatible quality names.
    /// </summary>
    private static readonly Dictionary<string, string[]> QualityGroupMappings = new()
    {
        // WEB groups - contain both WEBDL and WEBRip at each resolution
        { "WEB 2160p", new[] { "WEBDL-2160p", "WEBRip-2160p", "WEB-DL-2160p" } },
        { "WEB 1080p", new[] { "WEBDL-1080p", "WEBRip-1080p", "WEB-DL-1080p" } },
        { "WEB 720p", new[] { "WEBDL-720p", "WEBRip-720p", "WEB-DL-720p" } },
        { "WEB 480p", new[] { "WEBDL-480p", "WEBRip-480p", "WEB-DL-480p" } },

        // HDTV group variations
        { "HDTV-2160p", new[] { "HDTV-2160p" } },
        { "HDTV-1080p", new[] { "HDTV-1080p" } },
        { "HDTV-720p", new[] { "HDTV-720p" } },

        // Bluray groups
        { "Bluray-2160p", new[] { "Bluray-2160p", "BluRay-2160p" } },
        { "Bluray-2160p Remux", new[] { "Bluray-2160p Remux", "BluRay-2160p Remux" } },
        { "Bluray-1080p", new[] { "Bluray-1080p", "BluRay-1080p" } },
        { "Bluray-1080p Remux", new[] { "Bluray-1080p Remux", "BluRay-1080p Remux" } },
        { "Bluray-720p", new[] { "Bluray-720p", "BluRay-720p" } },
        { "Bluray-576p", new[] { "Bluray-576p", "BluRay-576p" } },
        { "Bluray-480p", new[] { "Bluray-480p", "BluRay-480p" } },

        // SD qualities
        { "DVD", new[] { "DVD", "DVD-R" } },
        { "SDTV", new[] { "SDTV" } },
        { "Raw-HD", new[] { "Raw-HD", "RawHD" } },
    };

    /// <summary>
    /// Find which quality group a detected quality belongs to
    /// </summary>
    private string? FindMatchingQualityGroup(string detectedQuality)
    {
        var qualityLower = detectedQuality.ToLowerInvariant();

        foreach (var (groupName, members) in QualityGroupMappings)
        {
            if (members.Any(m => m.Equals(detectedQuality, StringComparison.OrdinalIgnoreCase)))
            {
                return groupName;
            }
        }

        // Direct match check (e.g., "HDTV-720p" matches itself)
        if (QualityGroupMappings.ContainsKey(detectedQuality))
        {
            return detectedQuality;
        }

        return null;
    }

    /// <summary>
    /// Calculate quality score based on profile's quality item ordering
    /// Higher position in the profile = higher score
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
    /// Check if resolution/source matches a quality profile item (including groups)
    /// Implements Sonarr-compatible quality group matching
    /// </summary>
    private bool MatchesQualityItem(string? resolution, string? source, QualityItem item)
    {
        var detectedQuality = FormatQualityString(resolution, source);
        var itemName = item.Name;
        var itemNameLower = itemName.ToLowerInvariant();

        // If this item is a group, check if detected quality matches any child item
        if (item.IsGroup && item.Items != null)
        {
            foreach (var childItem in item.Items)
            {
                if (MatchesQualityItem(resolution, source, childItem))
                {
                    return true;
                }
            }
        }

        // Direct quality name match (e.g., "WEBDL-1080p" matches "WEBDL-1080p")
        if (detectedQuality.Equals(itemName, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check if detected quality belongs to a quality group that matches this item
        // e.g., "WEBDL-1080p" belongs to "WEB 1080p" group
        var detectedGroup = FindMatchingQualityGroup(detectedQuality);
        if (detectedGroup != null && detectedGroup.Equals(itemName, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check if this profile item is a WEB group and detected quality is a WEB type
        if (itemNameLower.StartsWith("web ") && (source == "WEBDL" || source == "WEBRip" || source == "WEB-DL"))
        {
            // Extract resolution from item name (e.g., "WEB 1080p" -> "1080p")
            var itemResolution = itemName.Split(' ').LastOrDefault();
            if (itemResolution != null && resolution != null &&
                itemResolution.Equals(resolution, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Check if item name contains both source and resolution
        // Handle variations: "WEBDL-1080p", "WEBRip-720p", "HDTV-1080p"
        if (resolution != null && source != null)
        {
            // Normalize source names for comparison
            var normalizedSource = source.Replace("-", "").Replace("_", "").ToLowerInvariant();
            var normalizedItem = itemNameLower.Replace("-", "").Replace("_", "").Replace(" ", "");

            // Check if item contains both source and resolution
            if (normalizedItem.Contains(normalizedSource) && itemNameLower.Contains(resolution.ToLowerInvariant()))
                return true;

            // Special handling for WEB variants
            if ((source == "WEBDL" || source == "WEB-DL") && itemNameLower.Contains("webdl") && itemNameLower.Contains(resolution.ToLowerInvariant()))
                return true;
            if (source == "WEBRip" && itemNameLower.Contains("webrip") && itemNameLower.Contains(resolution.ToLowerInvariant()))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Check if quality is allowed in profile (handles quality groups)
    /// </summary>
    private bool IsQualityAllowed(string? resolution, string? source, QualityProfile profile)
    {
        if (!profile.Items.Any())
            return true;

        var detectedQuality = FormatQualityString(resolution, source);

        // Check if any allowed item matches
        foreach (var item in profile.Items)
        {
            if (!item.Allowed)
                continue;

            // If it's a group, check all children
            if (item.IsGroup && item.Items != null)
            {
                foreach (var childItem in item.Items)
                {
                    if (childItem.Allowed && MatchesQualityItem(resolution, source, childItem))
                    {
                        _logger.LogDebug("[Release Evaluator] Quality '{Quality}' allowed via group '{Group}' -> '{Child}'",
                            detectedQuality, item.Name, childItem.Name);
                        return true;
                    }
                }
            }

            // Direct match
            if (MatchesQualityItem(resolution, source, item))
            {
                _logger.LogDebug("[Release Evaluator] Quality '{Quality}' allowed via profile item '{Item}'",
                    detectedQuality, item.Name);
                return true;
            }
        }

        _logger.LogDebug("[Release Evaluator] Quality '{Quality}' (resolution: {Resolution}, source: {Source}) not found in allowed items",
            detectedQuality, resolution ?? "null", source ?? "null");
        return false;
    }

    /// <summary>
    /// Evaluate which custom formats match this release
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
    /// Check if a custom format matches a release.
    /// A format matches if:
    ///   1. NO required specifications fail (if any required spec fails, format doesn't match)
    ///   2. NOT ALL specifications fail (at least one must match)
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
        // Strip "Specification" suffix if present (imported JSON may use full names)
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
