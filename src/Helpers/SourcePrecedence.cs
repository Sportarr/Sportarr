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
    /// Positive when the incoming file outranks the existing one on source
    /// alone, negative when it loses on source alone, zero when source
    /// decides nothing and the normal upgrade rules apply.
    /// </summary>
    public static int Compare(Config config, bool incomingIsIptvRecording, bool existingIsIptvRecording)
    {
        if (!config.DvrReplaceRecordingsWithIndexerReleases || incomingIsIptvRecording == existingIsIptvRecording)
        {
            return 0;
        }

        return existingIsIptvRecording ? 1 : -1;
    }

    /// <summary>
    /// Whether an indexer release would replace this file on source alone.
    /// </summary>
    public static bool IndexerReleaseReplaces(Config config, [NotNullWhen(true)] EventFile? existingFile) =>
        existingFile != null && Compare(config, incomingIsIptvRecording: false, existingFile.IsIptvRecording) > 0;

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
