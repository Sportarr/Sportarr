namespace Sportarr.Api.Helpers;

public static class PartRelevanceHelper
{
    public static int GetPartRelevanceScore(string title, string? requestedPart)
    {
        if (string.IsNullOrEmpty(title)) return 0;

        var titleLower = title.ToLowerInvariant();
        int score = 0;

        if (!string.IsNullOrEmpty(requestedPart))
        {
            if (titleLower.Contains(requestedPart.ToLowerInvariant()))
            {
                score += 100;
            }
        }

        if (titleLower.Contains("main card") || titleLower.Contains("maincard"))
            score += 50;
        else if (titleLower.Contains("prelim"))
            score += 40;
        else if (titleLower.Contains("early prelim"))
            score += 35;
        else if (titleLower.Contains("weigh") || titleLower.Contains("weigh-in"))
            score += 10;
        else if (titleLower.Contains("press conference") || titleLower.Contains("presser"))
            score += 5;

        return score;
    }
}
