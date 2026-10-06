using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Decides whether a release may replace the file an event, or one part of
/// it, already has.
/// </summary>
/// <remarks>
/// RSS sync and the pending-release reaper both have to answer this, and for
/// a while they answered it differently: the reaper compared scores alone,
/// so it grabbed over a file whose quality nobody could read, ignored a
/// profile that forbids upgrades, took a trivial custom-format bump as an
/// upgrade, and dropped a proper at equal score that RSS sync had
/// deliberately let through. One decision, made in one place.
/// </remarks>
public static class ExistingFileUpgradeGate
{
    /// <summary>
    /// The reason the release must not replace the file, or null when it
    /// may.
    /// </summary>
    public static string? RefusalReason(
        EventFile existingFile,
        string? releaseTitle,
        string? releaseQuality,
        int releaseCustomFormatScore,
        QualityProfile? profile,
        Config config)
    {
        // Every release reaching this gate came from an indexer, so under
        // source precedence it replaces an IPTV recording before any profile
        // rule is asked.
        if (SourcePrecedence.IndexerReleaseReplaces(config, existingFile))
        {
            return null;
        }

        // Recalculate quality scores from quality strings (don't trust stored
        // values from old inverted scoring). CalculateQualityScoreFromName
        // returns 0 for null, empty, "Unknown", or any other unparseable
        // string, so the gate below covers all three cases in one check.
        var existingQualityScoreOnly = ReleaseEvaluator.CalculateQualityScoreFromName(existingFile.Quality);
        // REFUSE-UNKNOWN-UPGRADE GATE: Library imports whose filenames lacked a
        // quality keyword get persisted with Quality="Unknown" (or null/empty),
        // which scores 0. Every discovered release then looks like an upgrade
        // and the event gets re-downloaded, defeating the user's import.
        if (existingQualityScoreOnly == 0)
        {
            return $"Existing file quality is unrecognized ('{existingFile.Quality ?? "null"}'), refusing auto re-download";
        }

        // Upgrades disabled for this profile: never replace an existing file,
        // regardless of score.
        if (profile != null && !profile.UpgradesAllowed)
        {
            return "Upgrades are disabled for this quality profile";
        }

        // Profile rank comes first. A lower rank is never an upgrade.
        // Revision and custom format score decide between equal ranks.
        var qualityComparison = QualityProfileRanker.Compare(profile, releaseQuality, existingFile.Quality);
        if (qualityComparison < 0)
        {
            return $"Existing file is of higher quality ({existingFile.Quality})";
        }
        var sameQuality = qualityComparison == 0;

        // A proper or repack at the same rank can replace a broken release.
        var existingRevision = ReleaseRevision.Parse(existingFile.OriginalTitle ?? existingFile.Quality);
        var releaseRevision = ReleaseRevision.Parse(releaseTitle);
        var revisionUpgrade = sameQuality &&
            config.DownloadPropersAndRepacks == "preferAndUpgrade" &&
            releaseRevision > existingRevision;

        // Refuse an older revision at the same rank when propers are preferred.
        if (sameQuality && config.DownloadPropersAndRepacks != "doNotPrefer" && releaseRevision < existingRevision)
        {
            return $"Existing file is a newer revision ({existingFile.OriginalTitle ?? existingFile.Quality})";
        }

        var qualityCutoffMet = false;
        var formatCutoffMet = false;
        if (profile?.CutoffQuality != null)
        {
            var cutoffRank = QualityProfileRanker.GetCutoffRank(profile, profile.CutoffQuality.Value);
            qualityCutoffMet = cutoffRank > 0
                && QualityProfileRanker.GetRank(profile, existingFile.Quality) >= cutoffRank;
        }
        if (profile?.CutoffFormatScore != null)
        {
            formatCutoffMet = existingFile.CustomFormatScore >= profile.CutoffFormatScore.Value;
        }

        if (!revisionUpgrade && qualityCutoffMet
            && (formatCutoffMet || profile?.CutoffFormatScore == null))
        {
            return "Existing file meets the quality profile cutoff";
        }

        var qualityImprovementAllowed = qualityComparison > 0 && !qualityCutoffMet;

        if (!qualityImprovementAllowed
            && releaseCustomFormatScore <= existingFile.CustomFormatScore
            && !revisionUpgrade)
        {
            return $"Existing file has same or better custom format score ({existingFile.CustomFormatScore} vs {releaseCustomFormatScore})";
        }

        // COSMETIC-DUPLICATE GUARD: broadcasters repost the identical release
        // under a branded name. When the only tokens separating the new title
        // from the existing file's original title are broadcaster words, it
        // is the same content; only a proper/repack revision justifies
        // replacing it.
        if (sameQuality && !revisionUpgrade &&
            RssSyncService.TitlesDifferOnlyByBroadcasterBranding(existingFile.OriginalTitle, releaseTitle))
        {
            return "Same release as the existing file (title differs only by broadcaster branding)";
        }

        // A custom-format-only gain must clear the profile's minimum score
        // increment. A genuine quality-tier upgrade is always allowed, but
        // when the quality is unchanged a trivial bump must not trigger a
        // needless second download. A proper is neither: it is the same
        // release fixed, with no format gain at all, and this rule used to
        // refuse it right after the revision check had let it through, so
        // with the default increment of one no proper was ever grabbed.
        if (profile != null && !revisionUpgrade)
        {
            var formatGain = releaseCustomFormatScore - existingFile.CustomFormatScore;
            if (!qualityImprovementAllowed && formatGain < profile.FormatScoreIncrement)
            {
                return $"Custom-format gain {formatGain} below minimum score increment {profile.FormatScoreIncrement}";
            }
        }

        return null;
    }
}
