using System.Diagnostics.CodeAnalysis;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Source precedence: an indexer release ranks above an IPTV recording,
/// whatever either one's quality, and an IPTV recording never replaces an
/// indexer file. A recording carries commercials and an as-aired encode,
/// so an indexer release of the same event is the better file even at a
/// lower resolution. Off unless the user turns it on.
/// </summary>
public static class SourcePrecedence
{
    public const string IptvRecordingRejection = "An IPTV recording never replaces a file from an indexer.";

    /// <summary>
    /// What source alone says about replacing one file with another.
    /// </summary>
    public enum Verdict
    {
        /// <summary>Source decides nothing; the normal upgrade rules apply.</summary>
        None,
        /// <summary>The incoming file replaces the existing one on source alone.</summary>
        IncomingWins,
        /// <summary>The existing file stays on source alone.</summary>
        ExistingWins,
    }

    public static Verdict Compare(Config config, bool incomingIsIptvRecording, bool existingIsIptvRecording)
    {
        if (!config.DvrReplaceRecordingsWithIndexerReleases || incomingIsIptvRecording == existingIsIptvRecording)
        {
            return Verdict.None;
        }

        return existingIsIptvRecording ? Verdict.IncomingWins : Verdict.ExistingWins;
    }

    /// <summary>
    /// Whether an indexer release would replace this file on source alone.
    /// </summary>
    public static bool IndexerReleaseReplaces(Config config, [NotNullWhen(true)] EventFile? existingFile) =>
        existingFile != null && Compare(config, incomingIsIptvRecording: false, existingFile.IsIptvRecording) == Verdict.IncomingWins;

    /// <summary>
    /// The file an event holds for a part, whole-event file first. With
    /// nullPartMatchesAnyPart a null part takes any held file; otherwise a
    /// null part means the whole-event file only. The caller runs the query.
    /// </summary>
    public static IQueryable<EventFile> HeldFor(this IQueryable<EventFile> files, int eventId, string? part, bool nullPartMatchesAnyPart) =>
        files
            .Where(f => f.EventId == eventId && f.Exists)
            .Where(f => f.PartName == part || (part == null && nullPartMatchesAnyPart))
            .OrderBy(f => f.PartName != null);

    /// <summary>
    /// Drop the quality profile's allowed-quality and minimum-score
    /// rejections from a release that would replace an IPTV recording. Every
    /// other rejection (size, seeders, release profiles, blocklist) stands.
    /// </summary>
    public static void LiftQualityProfileRejections(ReleaseSearchResult release)
    {
        if (release.Rejections.RemoveAll(ReleaseEvaluator.IsQualityProfileRejection) > 0)
        {
            release.Approved = release.Rejections.Count == 0;
        }
    }
}
