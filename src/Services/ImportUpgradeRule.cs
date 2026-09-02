using System;
using System.Collections.Generic;
using System.Linq;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Whether a file may take the place of the file an event already holds.
/// One rule for every way a file arrives (a completed download, a file
/// found in the library, a manual import), so they never disagree.
/// A lower quality never replaces. The same quality replaces unless it is
/// a revision downgrade while propers are preferred, or its custom format
/// score is lower. A higher quality always replaces.
/// </summary>
public static class ImportUpgradeRule
{
    /// <summary>
    /// Equal is true when quality, revision and custom format score all
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
        string? propersSetting)
    {
        var existingScore = ReleaseEvaluator.CalculateQualityScoreFromName(existingQuality);
        var newScore = ReleaseEvaluator.CalculateQualityScoreFromName(newQuality);

        if (newScore < existingScore)
        {
            return new Decision(false,
                $"Not an upgrade for the existing file. Existing quality: {Label(existingQuality)}. New quality: {Label(newQuality)}.");
        }

        if (newScore == existingScore)
        {
            var propersPreferred = !string.Equals(propersSetting, "doNotPrefer", StringComparison.OrdinalIgnoreCase);
            if (propersPreferred && ReleaseRevision.Parse(newTitle) < ReleaseRevision.Parse(existingTitle ?? existingQuality))
            {
                return new Decision(false, "Not a revision upgrade for the existing file.");
            }

            if (newFormatScore < existingFormatScore)
            {
                return new Decision(false,
                    $"Not a custom format upgrade for the existing file. New score {newFormatScore} does not improve on {existingFormatScore}.");
            }

            // A revision only tells copies apart while propers are preferred;
            // otherwise two copies that differ only by a PROPER mark would
            // take turns.
            if (newFormatScore == existingFormatScore
                && (!propersPreferred || ReleaseRevision.Parse(newTitle) == ReleaseRevision.Parse(existingTitle ?? existingQuality)))
            {
                return new Decision(true, null, Equal: true);
            }
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
