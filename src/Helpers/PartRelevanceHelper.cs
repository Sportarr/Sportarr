namespace Sportarr.Api.Helpers;

public static class PartRelevanceHelper
{
    public static int GetPartRelevanceScore(string title, string? requestedPart)
    {
        if (string.IsNullOrEmpty(title)) return 0;

        var titleLower = title.ToLowerInvariant();
        int score = 0;

        // Work out which segment the release itself is, most specific first.
        // "Early Prelims" contains "prelim", so testing the shorter phrase
        // first made the early-prelims branch unreachable and scored an early
        // card exactly like the regular one.
        // Release names separate words with dots and underscores, so the
        // phrases have to be looked for in a form where "Early.Prelims" reads
        // as "early prelims".
        var spaced = Spaced(titleLower);
        var titlePart = DescribePart(spaced);

        if (!string.IsNullOrEmpty(requestedPart))
        {
            var requestedLower = Spaced(requestedPart.ToLowerInvariant());
            var requestedNamed = DescribePart(requestedLower);

            if (titlePart != null && requestedNamed != null && titlePart != requestedNamed)
            {
                // The release names a different segment of the same card.
                // Crediting it because one name contains the other is how a
                // search for the prelims came back with the early prelims.
                return -100;
            }

            if (spaced.Contains(requestedLower))
            {
                score += 100;
            }
        }

        score += titlePart switch
        {
            "main card" => 50,
            "prelims" => 40,
            "early prelims" => 35,
            "weigh in" => 10,
            "press conference" => 5,
            _ => 0
        };

        return score;
    }

    /// <summary>
    /// Name the card segment a title refers to, or null when it names none.
    /// Ordered so the longer phrase wins over the shorter one it contains.
    /// </summary>
    /// <summary>
    /// Replace the separators release names use with spaces, so a phrase of
    /// two words is found however the release wrote it.
    /// </summary>
    private static string Spaced(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is '.' or '_' or '-' or '+') chars[i] = ' ';
        }
        return new string(chars);
    }

    private static string? DescribePart(string titleLower)
    {
        if (titleLower.Contains("early prelim")) return "early prelims";
        if (titleLower.Contains("main card") || titleLower.Contains("maincard")) return "main card";
        if (titleLower.Contains("prelim")) return "prelims";
        if (titleLower.Contains("weigh")) return "weigh in";
        if (titleLower.Contains("press conference") || titleLower.Contains("presser")) return "press conference";
        return null;
    }
}
