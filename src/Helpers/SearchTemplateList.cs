namespace Sportarr.Api.Helpers;

/// <summary>
/// A league's custom search query is stored as one text field that may hold
/// several templates, one per line. Release groups name the same event
/// differently, and one template cannot cover them all, so a league can carry
/// a few and the search asks the indexer with each.
///
/// One line stays exactly what it always was, so existing leagues need no
/// migration and no change in behaviour. Order is the user's preference: the
/// first line is the primary query and decides the search cache key.
/// </summary>
public static class SearchTemplateList
{
    /// <summary>
    /// Upper bound on templates per league. Every template costs one query
    /// per event against every indexer, so this stops a paste from turning
    /// one search into hundreds of requests.
    /// </summary>
    public const int MaxTemplates = 10;

    public static List<string> Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return new List<string>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var templates = new List<string>();

        foreach (var line in stored.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || !seen.Add(trimmed))
            {
                continue;
            }

            templates.Add(trimmed);
            if (templates.Count == MaxTemplates)
            {
                break;
            }
        }

        return templates;
    }

    /// <summary>
    /// Number of distinct templates the input holds, ignoring the cap. Used
    /// to refuse a save rather than silently drop the extras.
    /// </summary>
    public static int CountDistinct(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return 0;
        }

        return stored.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    /// <summary>
    /// Normalizes what the user typed back into storage form: trimmed, blank
    /// lines and duplicates dropped, capped. Returns null when nothing is
    /// left, so "no template" stays a single representation in the database.
    /// </summary>
    public static string? Normalize(string? input)
    {
        var templates = Parse(input);
        return templates.Count == 0 ? null : string.Join("\n", templates);
    }
}
