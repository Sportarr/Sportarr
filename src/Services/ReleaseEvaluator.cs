using Sportarr.Api.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Evaluates releases against quality profiles and custom formats.
/// Uses robust quality parsing with comprehensive pattern detection.
///
/// Sportarr ranking priority (Quality Trumps All):
/// 1. Quality (profile position)
/// 2. Custom Format Score
/// 3. Seeders (for torrents)
/// 4. Size (proximity to preferred size)
///
/// Size validation uses Sonarr-style per-quality definitions:
/// - MinSize/MaxSize/PreferredSize are in MB per minute of runtime
/// - For sports events, default runtime is ~180 minutes (3 hours)
/// - Releases outside min/max are rejected
/// - Size score is calculated based on proximity to preferred size
/// </summary>
public class ReleaseEvaluator
{
    private readonly ILogger<ReleaseEvaluator> _logger;
    private readonly EventPartDetector _partDetector;

    /// <summary>
    /// Default runtime for sports events in minutes (3 hours)
    /// Used when event duration is unknown
    /// </summary>
    public const int DefaultSportsRuntimeMinutes = 180;

    /// <summary>
    /// Chunk size in MB for rounding size comparisons (Sonarr uses 200MB)
    /// This prevents minor size differences from affecting selection
    /// </summary>
    private const double SizeComparisonChunkMB = 200.0;

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
    /// <param name="qualityDefinitions">Quality definitions for size limits (Sonarr-style)</param>
    /// <param name="requestedPart">Optional specific part requested (e.g., "Main Card", "Prelims")</param>
    /// <param name="sport">Sport type for part detection</param>
    /// <param name="enableMultiPartEpisodes">Whether multi-part episodes are enabled. When false, rejects releases with detected parts.</param>
    /// <param name="eventTitle">Optional event title for event-type-specific part handling (e.g., Fight Night vs PPV)</param>
    /// <param name="runtimeMinutes">Event runtime in minutes (defaults to 180 for sports events)</param>
    /// <param name="isPack">Whether this is a weekly pack search (relaxes size/format validation)</param>
    public ReleaseEvaluation EvaluateRelease(
        ReleaseSearchResult release,
        QualityProfile? profile,
        List<CustomFormat>? customFormats = null,
        List<QualityDefinition>? qualityDefinitions = null,
        string? requestedPart = null,
        string? sport = null,
        bool enableMultiPartEpisodes = true,
        string? eventTitle = null,
        int? runtimeMinutes = null,
        bool isPack = false)
    {
        var evaluation = new ReleaseEvaluation();

        // Parse quality from title using robust quality parser
        var qualityModel = QualityParser.ParseQuality(release.Title);
        evaluation.Quality = qualityModel.QualityName;

        // Calculate base quality score from profile or defaults
        evaluation.QualityScore = CalculateQualityScore(qualityModel.Quality, profile);

        // PART VALIDATION: For multi-part episodes (Fighting sports), validate release matches requested part
        var isFightingSport = EventPartDetector.IsFightingSport(sport ?? "");

        if (isFightingSport)
        {
            var detectedPart = _partDetector.DetectPart(release.Title, sport ?? "Fighting", eventTitle);

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
                    // For fighting sports, unmarked releases are typically the MAIN CARD/MAIN EVENT
                    // (Prelims and Early Prelims are almost always explicitly labeled)
                    // So: Accept unmarked releases for Main Card searches, reject for other parts
                    if (requestedPart.Equals("Main Card", StringComparison.OrdinalIgnoreCase))
                    {
                        // Accept unmarked releases as Main Card candidates
                        _logger.LogDebug("[Release Evaluator] {Title} - Unmarked release accepted as Main Card candidate", release.Title);
                    }
                    else
                    {
                        // Searching for Prelims/Early Prelims but release has no part indicator
                        // This is likely the main card, not the prelims we want
                        evaluation.Rejections.Add($"Requested part '{requestedPart}' but release has no part detected (likely Main Card)");
                        evaluation.Approved = false;
                        _logger.LogInformation("[Release Evaluator] {Title} - REJECTED: Requested '{RequestedPart}' but no part detected (likely Main Card)",
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
            var isAllowed = IsQualityAllowed(qualityModel.Quality, profile);

            if (!isAllowed)
            {
                var allowedItems = profile.Items.Where(q => q.Allowed).Select(q => q.Name).ToList();
                _logger.LogInformation("[Release Evaluator] REJECTION: Quality '{Quality}' not in allowed list: [{AllowedItems}]",
                    evaluation.Quality, string.Join(", ", allowedItems));
                evaluation.Rejections.Add($"Quality {evaluation.Quality} is not wanted in quality profile");
            }
        }

        // Check file size limits using Sonarr-style per-quality definitions
        // Size limits are defined in MB per minute of runtime
        // SKIP size validation for weekly packs - they contain multiple events so will always be large
        if (isPack)
        {
            _logger.LogDebug("[Release Evaluator] {Title} - Skipping size validation (weekly pack)", release.Title);
        }
        else if (release.Size > 0 && qualityDefinitions != null && qualityDefinitions.Any())
        {
            var sizeRejection = ValidateSizeAgainstQualityDefinition(
                release,
                qualityModel.Quality,
                qualityDefinitions,
                runtimeMinutes ?? DefaultSportsRuntimeMinutes);

            if (sizeRejection != null)
            {
                evaluation.Rejections.Add(sizeRejection);
            }

            // Calculate size score for tiebreaking (proximity to preferred size)
            evaluation.SizeScore = CalculateSizeScore(
                release.Size,
                qualityModel.Quality,
                qualityDefinitions,
                runtimeMinutes ?? DefaultSportsRuntimeMinutes);
        }
        else if (profile != null && release.Size > 0)
        {
            // Fallback to profile-level size limits if no quality definitions
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
            var formatEval = EvaluateCustomFormats(release, customFormats, profile, isPack);
            evaluation.MatchedFormats = formatEval.MatchedFormats;
            evaluation.CustomFormatScore = formatEval.TotalScore;
        }

        // Check minimum custom format score (Sonarr: releases below this are rejected)
        // Skip this check for weekly packs - they often have different naming conventions
        if (!isPack && profile != null && profile.MinFormatScore.HasValue &&
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
    /// Calculate quality score based on profile's quality item ordering
    /// Higher position in the profile = higher score
    /// </summary>
    private int CalculateQualityScore(QualityParser.QualityDefinition quality, QualityProfile? profile)
    {
        if (profile != null && profile.Items.Any())
        {
            // Search through profile items by position (index = rank)
            for (int i = 0; i < profile.Items.Count; i++)
            {
                var item = profile.Items[i];
                if (MatchesQualityItem(quality, item))
                {
                    // Score based on position in list (higher index = higher score)
                    // Multiply by 100 to give headroom for custom format scores
                    return (i + 1) * 100;
                }
            }
        }

        // Fallback to quality-based scoring
        // Use resolution as primary score indicator
        var score = quality.Resolution switch
        {
            QualityParser.Resolution.R2160p => 600,
            QualityParser.Resolution.R1080p => 500,
            QualityParser.Resolution.R720p => 400,
            QualityParser.Resolution.R576p => 300,
            QualityParser.Resolution.R540p => 250,
            QualityParser.Resolution.R480p => 200,
            QualityParser.Resolution.R360p => 100,
            _ => 0
        };

        // Add source modifier
        score += quality.Source switch
        {
            QualityParser.QualitySource.BlurayRaw => 50,  // Remux
            QualityParser.QualitySource.Bluray => 40,
            QualityParser.QualitySource.Web => 30,
            QualityParser.QualitySource.WebRip => 25,
            QualityParser.QualitySource.Television => 20,
            QualityParser.QualitySource.DVD => 10,
            _ => 0
        };

        return score;
    }

    /// <summary>
    /// Calculate quality score for a quality name string (public API for DVR integration)
    /// </summary>
    public int CalculateQualityScore(string qualityName, QualityProfile? profile)
    {
        var qualityModel = QualityParser.ParseQuality(qualityName);
        return CalculateQualityScore(qualityModel.Quality, profile);
    }

    /// <summary>
    /// Calculate custom format score for a synthetic title (public API for DVR integration).
    /// Only evaluates ReleaseTitleSpecification specs since synthetic titles don't have
    /// language, indexer, or other metadata that other spec types need.
    /// </summary>
    public int CalculateCustomFormatScore(string syntheticTitle, QualityProfile? profile, List<CustomFormat>? customFormats)
    {
        if (profile == null || profile.FormatItems == null || !profile.FormatItems.Any())
            return 0;

        if (customFormats == null || !customFormats.Any())
            return 0;

        var totalScore = 0;

        // For each custom format, check if it matches the synthetic title
        foreach (var format in customFormats)
        {
            if (format.Specifications == null || !format.Specifications.Any())
                continue;

            // Track if we've seen at least one ReleaseTitleSpecification
            // Formats with ONLY non-title specs (like LanguageSpecification) should NOT match
            var hasAnyTitleSpec = false;
            var allTitleSpecsMatch = true;

            foreach (var spec in format.Specifications)
            {
                // Only evaluate ReleaseTitleSpecification - other spec types cannot be matched
                // from a synthetic DVR title (no language info, no indexer flags, etc.)
                if (spec.Implementation == "ReleaseTitleSpecification")
                {
                    hasAnyTitleSpec = true;
                    var matches = MatchesTitleSpecification(spec, syntheticTitle);

                    // Handle Negate flag
                    if (spec.Negate)
                        matches = !matches;

                    // Handle Required flag
                    if (spec.Required && !matches)
                    {
                        allTitleSpecsMatch = false;
                        break;
                    }
                    if (!matches)
                    {
                        allTitleSpecsMatch = false;
                    }
                }
                else if (spec.Required)
                {
                    // If a non-title spec is Required, we can't satisfy it from a synthetic title
                    // (e.g., LanguageSpecification: German Required=true)
                    allTitleSpecsMatch = false;
                    break;
                }
                // Non-required, non-title specs are ignored (they're optional and we can't evaluate them)
            }

            // Only count the format if it has at least one title spec and all of them matched
            if (hasAnyTitleSpec && allTitleSpecsMatch)
            {
                var formatItem = profile.FormatItems?.FirstOrDefault(f => f.FormatId == format.Id);
                if (formatItem != null)
                {
                    totalScore += formatItem.Score;
                }
            }
        }

        return totalScore;
    }

    /// <summary>
    /// Match a ReleaseTitleSpecification against a title using regex
    /// </summary>
    private static bool MatchesTitleSpecification(FormatSpecification spec, string title)
    {
        if (string.IsNullOrEmpty(title))
            return false;

        // Get the regex pattern from Fields dictionary
        if (!spec.Fields.TryGetValue("value", out var valueObj))
            return false;

        var pattern = valueObj?.ToString();
        if (string.IsNullOrEmpty(pattern))
            return false;

        try
        {
            return Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            // If regex fails, fall back to contains check
            return title.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Check if quality matches a quality profile item (including groups)
    /// Uses QualityParser for robust matching
    /// </summary>
    private bool MatchesQualityItem(QualityParser.QualityDefinition quality, QualityItem item)
    {
        // If this item is a group, check if quality matches any child item
        if (item.IsGroup && item.Items != null)
        {
            foreach (var childItem in item.Items)
            {
                if (MatchesQualityItem(quality, childItem))
                {
                    return true;
                }
            }
        }

        // Use QualityParser's matching logic
        return QualityParser.MatchesProfileItem(quality, item.Name);
    }

    /// <summary>
    /// Check if quality is allowed in profile (handles quality groups)
    /// </summary>
    private bool IsQualityAllowed(QualityParser.QualityDefinition quality, QualityProfile profile)
    {
        if (!profile.Items.Any())
            return true;

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
                    // For groups, the group's Allowed state controls all children
                    if (MatchesQualityItem(quality, childItem))
                    {
                        _logger.LogDebug("[Release Evaluator] Quality '{Quality}' allowed via group '{Group}' -> '{Child}'",
                            quality.Name, item.Name, childItem.Name);
                        return true;
                    }
                }

                // Also check if quality matches the group itself (for WEB groups, etc.)
                if (QualityParser.MatchesProfileItem(quality, item.Name))
                {
                    _logger.LogDebug("[Release Evaluator] Quality '{Quality}' allowed via group '{Group}'",
                        quality.Name, item.Name);
                    return true;
                }
            }

            // Direct match
            if (MatchesQualityItem(quality, item))
            {
                _logger.LogDebug("[Release Evaluator] Quality '{Quality}' allowed via profile item '{Item}'",
                    quality.Name, item.Name);
                return true;
            }
        }

        _logger.LogDebug("[Release Evaluator] Quality '{Quality}' (source: {Source}, resolution: {Resolution}) not found in allowed items",
            quality.Name, quality.Source, quality.Resolution);
        return false;
    }

    /// <summary>
    /// Evaluate which custom formats match this release
    /// </summary>
    /// <param name="isPack">For weekly packs, skip penalty formats like No-RlsGroup</param>
    private (List<MatchedFormat> MatchedFormats, int TotalScore) EvaluateCustomFormats(
        ReleaseSearchResult release,
        List<CustomFormat> allFormats,
        QualityProfile? profile,
        bool isPack = false)
    {
        var matchedFormats = new List<MatchedFormat>();
        var totalScore = 0;

        // Log profile FormatItems status for debugging
        if (profile != null)
        {
            _logger.LogDebug("[Release Evaluator] Profile '{ProfileName}' has {FormatItemCount} FormatItems configured",
                profile.Name, profile.FormatItems?.Count ?? 0);

            if (profile.FormatItems != null && profile.FormatItems.Any())
            {
                foreach (var fi in profile.FormatItems)
                {
                    _logger.LogDebug("[Release Evaluator] FormatItem: FormatId={FormatId}, Score={Score}",
                        fi.FormatId, fi.Score);
                }
            }
        }
        else
        {
            _logger.LogDebug("[Release Evaluator] No profile provided for custom format evaluation");
        }

        // For weekly packs, identify penalty formats to skip (they use different naming conventions)
        var packSkipPatterns = new[] { "no-rlsgroup", "no-releasegroup", "no-group", "unknown-group" };

        foreach (var format in allFormats)
        {
            if (DoesFormatMatch(release, format))
            {
                // Get score for this format from profile's FormatItems
                // Sonarr behavior: scores must be explicitly configured per profile
                var formatItem = profile?.FormatItems?.FirstOrDefault(fi => fi.FormatId == format.Id);
                var formatScore = formatItem?.Score ?? 0;

                // For weekly packs, skip penalty formats that don't apply (e.g., No-RlsGroup)
                // Pack releases often have different naming conventions
                if (isPack && formatScore < 0)
                {
                    var formatNameLower = format.Name.ToLowerInvariant().Replace(" ", "").Replace("-", "");
                    if (packSkipPatterns.Any(p => formatNameLower.Contains(p.Replace("-", ""))))
                    {
                        _logger.LogDebug("[Release Evaluator] Skipping penalty format '{Format}' for weekly pack: {Title}",
                            format.Name, release.Title);
                        continue;
                    }
                }

                // Log when format matches but has no score configured (helps users diagnose)
                if (formatItem == null)
                {
                    _logger.LogDebug("[Release Evaluator] Format '{Format}' (Id={FormatId}) matched but has no score in profile. " +
                        "To assign a score, sync TRaSH scores or manually configure FormatItems in the quality profile.",
                        format.Name, format.Id);
                }
                else
                {
                    // Log significant scores (positive bonuses or negative penalties)
                    if (formatScore >= 100 || formatScore <= -100)
                    {
                        _logger.LogInformation("[Release Evaluator] Format '{Format}' matched with significant score {Score} for '{ReleaseTitle}'",
                            format.Name, formatScore, release.Title);
                    }
                    else
                    {
                        _logger.LogDebug("[Release Evaluator] Format '{Format}' (Id={FormatId}) matched with score {Score}",
                            format.Name, format.Id, formatScore);
                    }
                }

                matchedFormats.Add(new MatchedFormat
                {
                    Name = format.Name,
                    Score = formatScore
                });

                totalScore += formatScore;
            }
        }

        // Log total score summary for releases with significant negative scores
        if (totalScore <= -1000 && matchedFormats.Any())
        {
            var negativeFormats = matchedFormats.Where(m => m.Score < 0).OrderBy(m => m.Score).ToList();
            _logger.LogWarning("[Release Evaluator] LARGE NEGATIVE SCORE ({Score}) for '{Title}'. " +
                "Negative formats: {NegativeFormats}",
                totalScore, release.Title,
                string.Join(", ", negativeFormats.Select(m => $"{m.Name}({m.Score})")));
        }

        return (matchedFormats, totalScore);
    }

    /// <summary>
    /// Check if a custom format matches a release.
    /// Sonarr's actual matching logic (from CustomFormatCalculationService):
    ///   1. Evaluate ALL specifications (apply negate flag)
    ///   2. If ANY required specification fails → format doesn't match
    ///   3. If only required specs exist and all pass → format matches
    ///   4. If non-required specs exist, at least ONE must pass → format matches
    ///   5. Otherwise → format doesn't match
    ///
    /// Key distinction: Required specs are "gate" conditions. Non-required specs are the actual matchers.
    /// For a format like SDR with:
    ///   - 2160p (required=true) = gate condition
    ///   - HDR Formats (negate=true, required=false) = exclude HDR content
    ///   - SDR (required=false) = match explicit SDR
    /// The format should only match 2160p content that EITHER doesn't have HDR markers OR has explicit SDR.
    /// </summary>
    private bool DoesFormatMatch(ReleaseSearchResult release, CustomFormat format)
    {
        if (!format.Specifications.Any())
        {
            return false; // Empty format matches nothing
        }

        // Evaluate all specifications
        var specResults = format.Specifications.Select(spec => new
        {
            Spec = spec,
            Matched = EvaluateSpecification(release, spec)
        }).ToList();

        // Rule 1: If ANY required specification fails, format doesn't match
        var requiredSpecs = specResults.Where(r => r.Spec.Required).ToList();
        var failedRequired = requiredSpecs.Where(r => !r.Matched).ToList();
        if (failedRequired.Any())
        {
            _logger.LogDebug("[Custom Format] '{Format}' - REJECTED: Required spec(s) failed: {FailedSpecs}",
                format.Name, string.Join(", ", failedRequired.Select(r => r.Spec.Name)));
            return false;
        }

        // Get non-required specs
        var nonRequiredSpecs = specResults.Where(r => !r.Spec.Required).ToList();

        // Rule 2: If there are no non-required specs, the format matches based on required specs alone
        if (!nonRequiredSpecs.Any())
        {
            var passedRequiredSpecs = requiredSpecs.Where(r => r.Matched).Select(r => r.Spec.Name).ToList();
            _logger.LogDebug("[Custom Format] '{Format}' - MATCHED via required specs only: {PassedSpecs}",
                format.Name, string.Join(", ", passedRequiredSpecs));
            return requiredSpecs.Any(); // If there were required specs and they all passed
        }

        // Rule 3: If non-required specs exist, at least ONE must pass
        var passedNonRequired = nonRequiredSpecs.Where(r => r.Matched).ToList();
        if (!passedNonRequired.Any())
        {
            _logger.LogDebug("[Custom Format] '{Format}' - REJECTED: All {Count} non-required specifications failed (required specs passed but non-required gate blocked)",
                format.Name, nonRequiredSpecs.Count);
            return false;
        }

        // At least one non-required spec passed and all required specs passed
        var passedSpecs = specResults.Where(r => r.Matched).Select(r => r.Spec.Name).ToList();
        _logger.LogDebug("[Custom Format] '{Format}' - MATCHED via specs: {PassedSpecs}",
            format.Name, string.Join(", ", passedSpecs));
        return true;
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
    /// Sonarr Source enum values (from SourceSpecification):
    ///   0 = Unknown, 1 = Television, 2 = TelevisionRaw, 3 = WEBDL, 4 = WEBRip,
    ///   5 = DVD, 6 = Bluray, 7 = BlurayRaw (Remux)
    /// </summary>
    private bool EvaluateSourceSpec(ReleaseSearchResult release, FormatSpecification spec)
    {
        var value = GetFieldValue(spec, "value");
        if (string.IsNullOrEmpty(value))
            return false;

        var qualityModel = QualityParser.ParseQuality(release.Title);
        var detectedSource = qualityModel.Quality.Source;

        if (detectedSource == QualityParser.QualitySource.Unknown)
            return false;

        // Handle numeric IDs (Sonarr/TRaSH format) or string names
        if (int.TryParse(value, out var sourceId))
        {
            // Map Sonarr source IDs to QualitySource enum
            // These IDs match TRaSH Guides custom format specifications
            var expectedSource = sourceId switch
            {
                1 => QualityParser.QualitySource.Television,  // Television (HDTV/SDTV)
                2 => QualityParser.QualitySource.Television,  // TelevisionRaw
                3 => QualityParser.QualitySource.Web,         // WEBDL
                4 => QualityParser.QualitySource.WebRip,      // WEBRip
                5 => QualityParser.QualitySource.DVD,         // DVD
                6 => QualityParser.QualitySource.Bluray,      // Bluray
                7 => QualityParser.QualitySource.BlurayRaw,   // BlurayRaw (Remux)
                _ => (QualityParser.QualitySource?)null
            };
            return expectedSource.HasValue && detectedSource == expectedSource.Value;
        }

        // String name matching
        var normalizedValue = value.ToLowerInvariant().Replace("-", "").Replace(" ", "");
        return normalizedValue switch
        {
            "dvd" => detectedSource == QualityParser.QualitySource.DVD,
            "sdtv" or "hdtv" or "television" or "tv" => detectedSource == QualityParser.QualitySource.Television,
            "webrip" => detectedSource == QualityParser.QualitySource.WebRip,
            "webdl" or "web" => detectedSource == QualityParser.QualitySource.Web,
            "bluray" => detectedSource == QualityParser.QualitySource.Bluray,
            "remux" or "blurayraw" => detectedSource == QualityParser.QualitySource.BlurayRaw,
            _ => false
        };
    }

    /// <summary>
    /// Evaluate Resolution specification
    /// TRaSH/Sonarr uses actual resolution values (360, 480, 540, 576, 720, 1080, 2160)
    /// not sequential IDs
    /// </summary>
    private bool EvaluateResolutionSpec(ReleaseSearchResult release, FormatSpecification spec)
    {
        var value = GetFieldValue(spec, "value");
        if (string.IsNullOrEmpty(value))
            return false;

        var qualityModel = QualityParser.ParseQuality(release.Title);
        var detectedResolution = qualityModel.Quality.Resolution;

        if (detectedResolution == QualityParser.Resolution.Unknown)
            return false;

        // Handle numeric values (TRaSH format uses actual resolution values like 2160, 1080, etc.)
        if (int.TryParse(value, out var resolutionValue))
        {
            // Map resolution values to Resolution enum
            // TRaSH Guides use actual resolution values (e.g., 2160 for 4K, 1080 for Full HD)
            var expectedResolution = resolutionValue switch
            {
                360 => QualityParser.Resolution.R360p,
                480 => QualityParser.Resolution.R480p,
                540 => QualityParser.Resolution.R540p,
                576 => QualityParser.Resolution.R576p,
                720 => QualityParser.Resolution.R720p,
                1080 => QualityParser.Resolution.R1080p,
                2160 => QualityParser.Resolution.R2160p,
                _ => (QualityParser.Resolution?)null
            };
            return expectedResolution.HasValue && detectedResolution == expectedResolution.Value;
        }

        // String name matching (e.g., "1080p", "4k", "UHD")
        var normalizedValue = value.ToLowerInvariant().Replace("p", "");
        return normalizedValue switch
        {
            "360" => detectedResolution == QualityParser.Resolution.R360p,
            "480" => detectedResolution == QualityParser.Resolution.R480p,
            "540" => detectedResolution == QualityParser.Resolution.R540p,
            "576" => detectedResolution == QualityParser.Resolution.R576p,
            "720" => detectedResolution == QualityParser.Resolution.R720p,
            "1080" => detectedResolution == QualityParser.Resolution.R1080p,
            "2160" or "4k" or "uhd" => detectedResolution == QualityParser.Resolution.R2160p,
            _ => false
        };
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
    /// Multi-language releases (MULTI) are treated as including English since they typically do.
    /// This matches Sonarr/Radarr behavior where Multi is not penalized for language custom formats.
    /// </summary>
    private bool EvaluateLanguageSpec(ReleaseSearchResult release, FormatSpecification spec)
    {
        var value = GetFieldValue(spec, "value");
        if (string.IsNullOrEmpty(value))
            return false;

        // Use LanguageDetector which defaults to English when no explicit language tag found
        // This matches Sonarr's behavior where unmarked releases are assumed English
        var detectedLanguage = LanguageDetector.DetectLanguage(release.Title);

        // Multi-language releases are treated as satisfying English/Original language requirements
        // MULTI releases typically include English and other languages
        // Dual Audio releases also typically include English
        var isMultiLanguage = detectedLanguage == "Multi" || detectedLanguage == "Dual Audio";

        // Handle numeric IDs (Sonarr's language IDs) or language names
        if (int.TryParse(value, out var langId))
        {
            // Map Sonarr language IDs to language names
            // Special IDs: -2 = Original (series original language), -1 = Any, 0 = Unknown
            // For sports content, "Original" is always English
            var targetLanguage = langId switch
            {
                -2 => "English", // Original language - for sports, this is always English
                -1 => detectedLanguage, // Any language - always matches
                0 => "English", // Unknown - treat as English (safe default)
                1 => "English",
                2 => "French",
                3 => "Spanish",
                4 => "German",
                5 => "Italian",
                8 => "Japanese",
                10 => "Russian",
                11 => "Portuguese",
                12 => "Dutch",
                13 => "Swedish",
                14 => "Norwegian",
                15 => "Danish",
                16 => "Finnish",
                17 => "Turkish",
                18 => "Greek",
                19 => "Korean",
                20 => "Hungarian",
                21 => "Hebrew",
                22 => "Lithuanian",
                23 => "Czech",
                24 => "Hindi",
                25 => "Romanian",
                26 => "Thai",
                27 => "Bulgarian",
                28 => "Polish",
                29 => "Chinese",
                30 => "Vietnamese",
                31 => "Arabic",
                32 => "Ukrainian",
                33 => "Persian",
                34 => "Bengali",
                35 => "Slovak",
                36 => "Latvian",
                37 => "Indonesian",
                38 => "Catalan",
                39 => "Bosnian",
                _ => null
            };

            if (targetLanguage == null)
                return false;

            // Multi-language releases satisfy English/Original language requirements
            // They typically include English plus other languages
            if (isMultiLanguage && (targetLanguage == "English" || langId == -2 || langId == -1))
            {
                _logger.LogDebug("[Language Spec] Multi-language release satisfies '{Target}' requirement", targetLanguage);
                return true;
            }

            // Compare detected language with target
            return string.Equals(detectedLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase);
        }

        // Direct language name comparison
        // Multi-language releases satisfy English requirements
        if (isMultiLanguage && value.Equals("English", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("[Language Spec] Multi-language release satisfies 'English' requirement");
            return true;
        }

        return string.Equals(detectedLanguage, value, StringComparison.OrdinalIgnoreCase);
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

    #region Size Validation (Sonarr-style)

    /// <summary>
    /// Validate release size against quality definition limits.
    /// Returns rejection message if size is outside limits, null if valid.
    ///
    /// Size limits in QualityDefinition are in MB per minute of runtime.
    /// Example: MinSize=15 MB/min for a 180-minute event = 2700 MB minimum
    /// </summary>
    private string? ValidateSizeAgainstQualityDefinition(
        ReleaseSearchResult release,
        QualityParser.QualityDefinition quality,
        List<QualityDefinition> qualityDefinitions,
        int runtimeMinutes)
    {
        var qualityDef = FindMatchingQualityDefinition(quality, qualityDefinitions);
        if (qualityDef == null)
        {
            // No quality definition found for this quality - skip size validation
            _logger.LogDebug("[Size Validation] No quality definition found for '{Quality}', skipping size check", quality.Name);
            return null;
        }

        var sizeMB = release.Size / (1024.0 * 1024.0);
        var sizeGB = sizeMB / 1024.0;

        // Calculate size limits based on runtime
        // QualityDefinition stores MB per minute
        var minSizeMB = (double)qualityDef.MinSize * runtimeMinutes;
        var maxSizeMB = qualityDef.MaxSize.HasValue ? (double)qualityDef.MaxSize.Value * runtimeMinutes : (double?)null;

        // Check minimum size
        if (sizeMB < minSizeMB)
        {
            var minSizeGB = minSizeMB / 1024.0;
            _logger.LogInformation("[Size Validation] REJECTED: {Title} - Size {SizeGB:F2}GB below minimum {MinGB:F2}GB for {Quality} (runtime: {Runtime}min)",
                release.Title, sizeGB, minSizeGB, qualityDef.Title, runtimeMinutes);
            return $"Size {sizeGB:F2}GB is below minimum {minSizeGB:F2}GB for {qualityDef.Title} ({qualityDef.MinSize}MB/min × {runtimeMinutes}min runtime)";
        }

        // Check maximum size (if defined)
        if (maxSizeMB.HasValue && sizeMB > maxSizeMB.Value)
        {
            var maxSizeGB = maxSizeMB.Value / 1024.0;
            _logger.LogInformation("[Size Validation] REJECTED: {Title} - Size {SizeGB:F2}GB exceeds maximum {MaxGB:F2}GB for {Quality} (runtime: {Runtime}min)",
                release.Title, sizeGB, maxSizeGB, qualityDef.Title, runtimeMinutes);
            return $"Size {sizeGB:F2}GB exceeds maximum {maxSizeGB:F2}GB for {qualityDef.Title} ({qualityDef.MaxSize}MB/min × {runtimeMinutes}min runtime)";
        }

        _logger.LogDebug("[Size Validation] {Title} - Size {SizeGB:F2}GB is within limits for {Quality} (min: {MinGB:F2}GB, max: {MaxGB}GB)",
            release.Title, sizeGB, qualityDef.Title, minSizeMB / 1024.0,
            maxSizeMB.HasValue ? $"{maxSizeMB.Value / 1024.0:F2}" : "unlimited");

        return null;
    }

    /// <summary>
    /// Calculate size score for tiebreaking (Sonarr-style).
    ///
    /// When PreferredSize is set:
    ///   - Score = negative absolute distance from preferred size (closer = higher score)
    ///   - Rounded to 200MB chunks to prevent minor differences from affecting selection
    ///
    /// When PreferredSize is not set (unlimited):
    ///   - Score = file size in 200MB chunks (larger = higher score)
    ///   - This matches Sonarr's default "prefer larger" behavior
    /// </summary>
    private long CalculateSizeScore(
        long sizeBytes,
        QualityParser.QualityDefinition quality,
        List<QualityDefinition> qualityDefinitions,
        int runtimeMinutes)
    {
        var qualityDef = FindMatchingQualityDefinition(quality, qualityDefinitions);
        var sizeMB = sizeBytes / (1024.0 * 1024.0);

        // Round to 200MB chunks (Sonarr behavior)
        var roundedSizeMB = Math.Round(sizeMB / SizeComparisonChunkMB) * SizeComparisonChunkMB;

        if (qualityDef != null && qualityDef.PreferredSize > 0)
        {
            // Calculate preferred size in MB based on runtime
            var preferredSizeMB = (double)qualityDef.PreferredSize * runtimeMinutes;

            // Score is negative distance from preferred (closer = higher score, i.e., less negative)
            var distanceFromPreferred = Math.Abs(roundedSizeMB - preferredSizeMB);
            var roundedDistance = Math.Round(distanceFromPreferred / SizeComparisonChunkMB) * SizeComparisonChunkMB;

            // Return negative distance so higher (less negative) = better
            var score = (long)(-roundedDistance);

            _logger.LogDebug("[Size Score] {Quality} - Size: {SizeMB:F0}MB, Preferred: {PreferredMB:F0}MB, Distance: {Distance:F0}MB, Score: {Score}",
                quality.Name, sizeMB, preferredSizeMB, roundedDistance, score);

            return score;
        }
        else
        {
            // No preferred size - prefer larger files (Sonarr default)
            var score = (long)roundedSizeMB;

            _logger.LogDebug("[Size Score] {Quality} - Size: {SizeMB:F0}MB (no preferred), Score: {Score} (prefer larger)",
                quality.Name, sizeMB, score);

            return score;
        }
    }

    /// <summary>
    /// Find the matching QualityDefinition for a parsed quality.
    /// Maps quality name/source/resolution to the seeded quality definitions.
    /// </summary>
    private QualityDefinition? FindMatchingQualityDefinition(
        QualityParser.QualityDefinition quality,
        List<QualityDefinition> qualityDefinitions)
    {
        // Build quality name from source and resolution
        var qualityName = quality.Name;

        // Try exact match first
        var exactMatch = qualityDefinitions.FirstOrDefault(qd =>
            qd.Title.Equals(qualityName, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null)
            return exactMatch;

        // Try matching by building the quality string like Sonarr does
        // Format: "{Source}-{Resolution}" e.g., "WEBDL-1080p", "Bluray-720p"
        var sourceStr = quality.Source switch
        {
            QualityParser.QualitySource.Television => "HDTV",
            QualityParser.QualitySource.Web => "WEBDL",
            QualityParser.QualitySource.WebRip => "WEBRip",
            QualityParser.QualitySource.Bluray => "Bluray",
            QualityParser.QualitySource.BlurayRaw => "Bluray",
            QualityParser.QualitySource.DVD => "DVD",
            _ => null
        };

        var resolutionStr = quality.Resolution switch
        {
            QualityParser.Resolution.R2160p => "2160p",
            QualityParser.Resolution.R1080p => "1080p",
            QualityParser.Resolution.R720p => "720p",
            QualityParser.Resolution.R576p => "576p",
            QualityParser.Resolution.R540p => "540p",
            QualityParser.Resolution.R480p => "480p",
            QualityParser.Resolution.R360p => "360p",
            _ => null
        };

        if (sourceStr != null && resolutionStr != null)
        {
            var composedName = $"{sourceStr}-{resolutionStr}";
            var composedMatch = qualityDefinitions.FirstOrDefault(qd =>
                qd.Title.Equals(composedName, StringComparison.OrdinalIgnoreCase));
            if (composedMatch != null)
                return composedMatch;

            // Try with Remux suffix for BlurayRaw
            if (quality.Source == QualityParser.QualitySource.BlurayRaw)
            {
                var remuxName = $"Bluray-{resolutionStr} Remux";
                var remuxMatch = qualityDefinitions.FirstOrDefault(qd =>
                    qd.Title.Equals(remuxName, StringComparison.OrdinalIgnoreCase));
                if (remuxMatch != null)
                    return remuxMatch;
            }
        }

        // Fallback: try to match by resolution only
        if (resolutionStr != null)
        {
            var resMatch = qualityDefinitions.FirstOrDefault(qd =>
                qd.Title.Contains(resolutionStr, StringComparison.OrdinalIgnoreCase));
            if (resMatch != null)
                return resMatch;
        }

        // Last resort: return Unknown quality definition
        return qualityDefinitions.FirstOrDefault(qd =>
            qd.Title.Equals("Unknown", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
