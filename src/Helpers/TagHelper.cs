namespace Sportarr.Api.Helpers;

/// <summary>
/// Tag matching logic following Sonarr's model:
/// - Config entity with no tags = applies globally (to all leagues)
/// - Config entity with tags = applies only to leagues sharing at least one tag
/// - League with no tags = only untagged (global) config entities apply
/// </summary>
public static class TagHelper
{
    /// <summary>
    /// Returns true if a config entity should apply to the given league based on tag intersection.
    /// </summary>
    public static bool TagsMatch(List<int> entityTags, List<int> leagueTags)
    {
        // No tags on config entity = applies globally
        if (entityTags == null || entityTags.Count == 0)
            return true;

        // No tags on league = only untagged config entities apply
        if (leagueTags == null || leagueTags.Count == 0)
            return false;

        // Set intersection: at least one common tag
        return entityTags.Any(t => leagueTags.Contains(t));
    }
}
