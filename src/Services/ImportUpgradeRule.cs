using System;
using System.Collections.Generic;
using System.Linq;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Whether a file may take the place of the file an event already holds.
/// Automatic imports use one rule for every way a file arrives.
/// An explicit choice can override a preference rejection.
/// A lower profile rank never replaces. The same rank replaces unless it is
/// a revision downgrade while propers are preferred, or its custom format
/// score is lower. A higher profile rank always replaces.
/// </summary>
public static class ImportUpgradeRule
{
    public const string LowerQualityRejection = "Not an upgrade for the existing file.";
    public const string RevisionRejection = "Not a revision upgrade for the existing file.";
    public const string CustomFormatRejection = "Not a custom format upgrade for the existing file.";

    /// <summary>
    /// Equal is true when profile rank, revision and custom format score all
    /// match: an accepted copy that improves nothing. An automatic import
    /// of such a copy that already sits in the league folder keeps the file
    /// the event holds, or two equal copies would swap places on every
    /// rescan.
    /// </summary>
    public sealed record Decision(bool IsUpgrade, string? Rejection, bool Equal = false);

    public static readonly Decision Accept = new(true, null);

    public static Decision Evaluate(
        string? existingQuality, int existingFormatScore, string? existingTitle,
        string? newQuality, int newFormatScore, string? newTitle,
        string? propersSetting,
        QualityProfile? profile = null)
    {
        var qualityComparison = QualityProfileRanker.Compare(profile, newQuality, existingQuality);

        if (qualityComparison < 0)
        {
            return new Decision(false,
                $"{LowerQualityRejection} Existing quality: {Label(existingQuality)}. New quality: {Label(newQuality)}.");
        }

        if (qualityComparison == 0)
        {
            var propersPreferred = !string.Equals(propersSetting, "doNotPrefer", StringComparison.OrdinalIgnoreCase);
            var revisionComparison = ReleaseRevision.Parse(newTitle)
                .CompareTo(ReleaseRevision.Parse(existingTitle ?? existingQuality));
            var preference = ReleasePreferenceComparer.Compare(
                profile,
                newQuality, newTitle, newFormatScore,
                existingQuality, existingTitle, existingFormatScore,
                propersSetting);

            if (preference < 0 && propersPreferred && revisionComparison < 0)
            {
                return new Decision(false, RevisionRejection);
            }

            if (preference > 0)
            {
                return Accept;
            }

            if (preference < 0)
            {
                return new Decision(false,
                    $"{CustomFormatRejection} New score {newFormatScore} does not improve on {existingFormatScore}.");
            }

            return new Decision(true, null, Equal: true);
        }

        return Accept;
    }

    /// <summary>
    /// The file an event already holds for this part, if any. A part number
    /// names that part's file. Without one the whole-event file counts; a
    /// part file stands in only when multi-part events are off, since then
    /// the event holds one file whatever it is called.
    /// </summary>
    public static EventFile? ExistingFileForPart(IEnumerable<EventFile> files, int? partNumber, string? incomingPath, bool multiPartEvents)
    {
        var held = files
            .Where(f => f.Exists && !string.Equals(f.FilePath, incomingPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (partNumber.HasValue)
        {
            return held.FirstOrDefault(f => f.PartNumber == partNumber);
        }
        var whole = held.FirstOrDefault(f => f.PartName == null && f.PartNumber == null);
        return whole ?? (multiPartEvents ? null : held.FirstOrDefault());
    }

    private static string Label(string? quality) => string.IsNullOrWhiteSpace(quality) ? "unknown" : quality;
}
