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
    /// Under source precedence an event holding an IPTV recording never
    /// meets its cutoff, whatever the recording's quality string says.
    /// </summary>
    public static bool AwaitsIndexerRelease(Config config, bool hasIptvRecording) =>
        hasIptvRecording && config.DvrReplaceRecordingsWithIndexerReleases;

    /// <summary>
    /// A library file nothing told the import is a recording: an HDTV file
    /// in MPEG-TS, the container the DVR records to. Indexer releases come
    /// as mkv or mp4, so they never match. Keep in step with the
    /// MarkIptvRecordings migration.
    /// </summary>
    public static bool LooksLikeIptvRecording(string? filePath, string? quality) =>
        string.Equals(Path.GetExtension(filePath), ".ts", StringComparison.OrdinalIgnoreCase)
        && quality?.Contains("HDTV", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Whether an indexer release would replace this file on source alone.
    /// </summary>
    public static bool IndexerReleaseReplaces(Config config, [NotNullWhen(true)] EventFile? existingFile) =>
        existingFile != null && Compare(config, incomingIsIptvRecording: false, existingFile.IsIptvRecording) == Verdict.IncomingWins;

    /// <summary>
    /// The files an event holds, whole-event file first, then oldest first,
    /// so every caller that takes the first one agrees. The caller runs the
    /// query.
    /// </summary>
    public static IQueryable<EventFile> HeldFiles(this IQueryable<EventFile> files, int eventId) =>
        files
            .Where(f => f.EventId == eventId && f.Exists)
            .OrderBy(f => f.PartName != null)
            .ThenBy(f => f.Id);

    /// <summary>
    /// Drop the quality profile's allowed-quality and minimum-score
    /// rejections from a release that would replace an IPTV recording. Every
    /// other rejection (size, seeders, release profiles, blocklist) stands.
    /// </summary>
    public static void LiftQualityProfileRejections(ReleaseSearchResult release)
    {
        release.ReplacesIptvRecording = true;
        if (release.Rejections.RemoveAll(ReleaseEvaluator.IsQualityProfileRejection) > 0)
        {
            release.Approved = release.Rejections.Count == 0;
        }
    }
}
