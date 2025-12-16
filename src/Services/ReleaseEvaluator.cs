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
/// 4. Size
/// </summary>
public class ReleaseEvaluator
{
    private readonly ILogger<ReleaseEvaluator> _logger;
    private readonly EventPartDetector _partDetector;

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
            var isAllowed = IsQualityAllowed(qualityModel.Quality, profile);

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
    private (List<MatchedFormat> MatchedFormats, int TotalScore) EvaluateCustomFormats(
        ReleaseSearchResult release,
        List<CustomFormat> allFormats,
        QualityProfile? profile)
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

        foreach (var format in allFormats)
        {
            if (DoesFormatMatch(release, format))
            {
                // Get score for this format from profile's FormatItems
                // Sonarr behavior: scores must be explicitly configured per profile
                var formatItem = profile?.FormatItems?.FirstOrDefault(fi => fi.FormatId == format.Id);
                var formatScore = formatItem?.Score ?? 0;

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
    ///   1. Evaluate ALL specifications
    ///   2. If ANY required specification fails → format doesn't match
    ///   3. If ALL specifications fail → format doesn't match
    ///   4. Otherwise → format matches (at least one spec passed, no required specs failed)
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
        var failedRequired = specResults.Where(r => r.Spec.Required && !r.Matched).ToList();
        if (failedRequired.Any())
        {
            _logger.LogDebug("[Custom Format] '{Format}' - REJECTED: Required spec(s) failed: {FailedSpecs}",
                format.Name, string.Join(", ", failedRequired.Select(r => r.Spec.Name)));
            return false;
        }

        // Rule 2: If ALL specifications failed, format doesn't match
        var anyPassed = specResults.Any(r => r.Matched);
        if (!anyPassed)
        {
            _logger.LogDebug("[Custom Format] '{Format}' - REJECTED: All {Count} specifications failed",
                format.Name, specResults.Count);
            return false;
        }

        // At least one spec passed and no required specs failed
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
    /// </summary>
    private bool EvaluateLanguageSpec(ReleaseSearchResult release, FormatSpecification spec)
    {
        var value = GetFieldValue(spec, "value");
        if (string.IsNullOrEmpty(value))
            return false;

        // Use LanguageDetector which defaults to English when no explicit language tag found
        // This matches Sonarr's behavior where unmarked releases are assumed English
        var detectedLanguage = LanguageDetector.DetectLanguage(release.Title);

        // Handle numeric IDs (Sonarr's language IDs) or language names
        if (int.TryParse(value, out var langId))
        {
            // Map Sonarr language IDs to language names
            var targetLanguage = langId switch
            {
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

            // Compare detected language with target
            return string.Equals(detectedLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase);
        }

        // Direct language name comparison
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
}
