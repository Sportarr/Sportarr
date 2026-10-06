using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

public static class QualityProfileRanker
{
    public static int GetRank(QualityProfile? profile, string? qualityName)
    {
        if (profile?.Items == null || profile.Items.Count == 0)
        {
            return ReleaseEvaluator.CalculateQualityScoreFromName(qualityName);
        }

        var index = ListedIndex(profile, qualityName);
        // A quality the profile lists but doesn't allow ranks below every
        // allowed one, so any allowed release upgrades it.
        return index >= 0 && profile.Items[index].Allowed ? profile.Items.Count - index : 0;
    }

    /// <summary>
    /// The profile lists this quality but doesn't allow it. It ranks 0 like
    /// an unparseable quality, but unlike one it is always below the cutoff.
    /// </summary>
    public static bool IsListedButDisallowed(QualityProfile? profile, string? qualityName)
    {
        var index = ListedIndex(profile, qualityName);
        return index >= 0 && !profile!.Items[index].Allowed;
    }

    private static int ListedIndex(QualityProfile? profile, string? qualityName)
    {
        if (profile?.Items == null)
        {
            return -1;
        }

        var quality = QualityParser.ParseQuality(qualityName ?? string.Empty).Quality;
        for (var index = 0; index < profile.Items.Count; index++)
        {
            if (Matches(profile.Items[index], quality))
            {
                return index;
            }
        }

        return -1;
    }

    public static int Compare(QualityProfile? profile, string? leftQuality, string? rightQuality)
    {
        return GetRank(profile, leftQuality).CompareTo(GetRank(profile, rightQuality));
    }

    public static int GetCutoffRank(QualityProfile profile, int qualityId)
    {
        for (var index = 0; index < profile.Items.Count; index++)
        {
            if (profile.Items[index].Quality == qualityId)
            {
                return profile.Items.Count - index;
            }
        }

        for (var index = 0; index < profile.Items.Count; index++)
        {
            if (profile.Items[index].Items?.Any(child => ContainsQualityId(child, qualityId)) == true)
            {
                return profile.Items.Count - index;
            }
        }

        return 0;
    }

    public static bool IsBelowCutoff(QualityProfile profile, string? qualityName)
    {
        if (!profile.UpgradesAllowed || !profile.CutoffQuality.HasValue)
        {
            return false;
        }

        var currentRank = GetRank(profile, qualityName);
        var cutoffRank = GetCutoffRank(profile, profile.CutoffQuality.Value);
        return cutoffRank > 0 &&
            (IsListedButDisallowed(profile, qualityName) || (currentRank > 0 && currentRank < cutoffRank));
    }

    internal static bool UsesAscendingImportedOrder(QualityProfile profile)
    {
        if (!profile.IsSynced || profile.IsCustomized ||
            string.IsNullOrEmpty(profile.TrashId) || profile.Items.Count < 2)
        {
            return false;
        }

        // Only an untouched import has enough evidence to reverse safely.
        for (var index = 0; index < profile.Items.Count; index++)
        {
            if (profile.Items[index].Quality != index)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Matches(QualityItem item, QualityParser.QualityDefinition quality)
    {
        if (item.IsGroup)
        {
            return item.Items!.Any(child => Matches(child, quality));
        }

        return QualityParser.MatchesProfileItem(quality, item.Name);
    }

    private static bool ContainsQualityId(QualityItem item, int qualityId)
    {
        return item.Quality == qualityId
            || item.Items?.Any(child => ContainsQualityId(child, qualityId)) == true;
    }
}
