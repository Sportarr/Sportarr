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
        // Recalculate quality scores from quality strings (don't trust stored
        // values from old inverted scoring). CalculateQualityScoreFromName
        // returns 0 for null, empty, "Unknown", or any other unparseable
        // string, so the gate below covers all three cases in one check.
        var existingQualityScoreOnly = ReleaseEvaluator.CalculateQualityScoreFromName(existingFile.Quality);
        var existingTotalScore = existingQualityScoreOnly + existingFile.CustomFormatScore;
        var newQualityScoreOnly = ReleaseEvaluator.CalculateQualityScoreFromName(releaseQuality);
        var newTotalScore = newQualityScoreOnly + releaseCustomFormatScore;

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

        // A proper/repack of the SAME quality is a legitimate upgrade: the
        // original was broken and re-released fixed. Gated on the Download
        // Propers and Repacks setting.
        var revisionUpgrade = config.DownloadPropersAndRepacks == "preferAndUpgrade" &&
            newTotalScore == existingTotalScore &&
            ReleaseRevision.Parse(releaseTitle) >
            ReleaseRevision.Parse(existingFile.OriginalTitle ?? existingFile.Quality);

        if (newTotalScore <= existingTotalScore && !revisionUpgrade)
        {
            return $"Existing file has same or better score ({existingTotalScore})";
        }

        // COSMETIC-DUPLICATE GUARD: broadcasters repost the identical release
        // under a branded name. When the only tokens separating the new title
        // from the existing file's original title are broadcaster words, it
        // is the same content; only a proper/repack revision justifies
        // replacing it.
        if (!revisionUpgrade &&
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
            var isQualityUpgrade = newQualityScoreOnly > existingQualityScoreOnly;
            var formatGain = releaseCustomFormatScore - existingFile.CustomFormatScore;
            if (!isQualityUpgrade && formatGain < profile.FormatScoreIncrement)
            {
                return $"Custom-format gain {formatGain} below minimum score increment {profile.FormatScoreIncrement}";
            }
        }

        return null;
    }
}
