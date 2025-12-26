using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for normalizing search queries and matching releases.
/// Handles diacritics (São Paulo → Sao Paulo), location variations (Mexico City → Mexico),
/// and other common search matching issues.
/// </summary>
public static class SearchNormalizationService
{
    /// <summary>
    /// Location name aliases - maps full/official names to common search variations.
    /// Used to generate alternate search queries and for release matching.
    /// Key: normalized full name (lowercase), Value: list of aliases to try
    /// </summary>
    private static readonly Dictionary<string, string[]> LocationAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Formula 1 Grand Prix locations
        { "Mexico City", new[] { "Mexico" } },
        { "Sao Paulo", new[] { "Brazil", "Brazilian", "Interlagos" } },
        { "Las Vegas", new[] { "Vegas" } },
        { "Abu Dhabi", new[] { "AbuDhabi", "Yas Marina" } },
        { "Monte Carlo", new[] { "Monaco" } },
        { "Spielberg", new[] { "Austria", "Austrian" } },
        { "Silverstone", new[] { "British", "Britain", "UK" } },
        { "Monza", new[] { "Italy", "Italian" } },
        { "Spa", new[] { "Belgium", "Belgian", "Spa-Francorchamps" } },
        { "Suzuka", new[] { "Japan", "Japanese" } },
        { "Singapore", new[] { "Marina Bay" } },
        { "Melbourne", new[] { "Australia", "Australian" } },
        { "Montreal", new[] { "Canada", "Canadian" } },
        { "Baku", new[] { "Azerbaijan" } },
        { "Jeddah", new[] { "Saudi Arabia", "Saudi Arabian", "Saudi" } },
        { "Miami", new[] { "Miami Gardens" } },
        { "Imola", new[] { "Emilia Romagna", "San Marino" } },
        { "Zandvoort", new[] { "Netherlands", "Dutch" } },
        { "Budapest", new[] { "Hungary", "Hungarian", "Hungaroring" } },
        { "Barcelona", new[] { "Spain", "Spanish", "Catalunya" } },
        { "Shanghai", new[] { "China", "Chinese" } },
        { "Bahrain", new[] { "Sakhir" } },
        { "Qatar", new[] { "Lusail" } },

        // MotoGP locations
        { "Mugello", new[] { "Italy", "Italian" } },
        { "Le Mans", new[] { "France", "French" } },
        { "Sachsenring", new[] { "Germany", "German" } },
        { "Assen", new[] { "Netherlands", "Dutch", "TT Assen" } },
        { "Phillip Island", new[] { "Australia", "Australian" } },
        { "Sepang", new[] { "Malaysia", "Malaysian" } },
        { "Losail", new[] { "Qatar" } },
        { "Termas de Rio Hondo", new[] { "Argentina", "Argentine" } },
        { "Circuit of the Americas", new[] { "COTA", "Austin", "Texas" } },

        // UFC / MMA Fight Night locations
        { "Abu Dhabi", new[] { "Fight Island", "UFC Fight Island" } },
        { "Riyadh", new[] { "Saudi Arabia", "Saudi" } },

        // Common city variations
        { "New York City", new[] { "New York", "NYC", "NY" } },
        { "Los Angeles", new[] { "LA" } },
        { "San Francisco", new[] { "SF" } },
    };

    /// <summary>
    /// Word substitutions for common naming differences.
    /// Key: word in Sportarr database, Value: words to also search for
    /// </summary>
    private static readonly Dictionary<string, string[]> WordSubstitutions = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Grand Prix", new[] { "GP" } },
        { "GP", new[] { "Grand Prix" } },
        { "Championship", new[] { "Champ", "Championships" } },
        { "Tournament", new[] { "Tourney" } },
        { "International", new[] { "Intl" } },
        { "versus", new[] { "vs", "v" } },
        { "vs", new[] { "versus", "v" } },
    };

    /// <summary>
    /// Remove diacritics (accents) from text.
    /// Examples: São Paulo → Sao Paulo, Zürich → Zurich, München → Munchen
    /// </summary>
    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Normalize to FormD (decomposed form) which separates base characters from combining marks
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(normalizedString.Length);

        foreach (var c in normalizedString)
        {
            // Get the Unicode category of the character
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);

            // Skip combining marks (NonSpacingMark includes accents, diacritics, etc.)
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        // Normalize back to FormC (composed form)
        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Normalize a search query by removing diacritics and cleaning up common issues.
    /// </summary>
    public static string NormalizeForSearch(string query)
    {
        if (string.IsNullOrEmpty(query))
            return query;

        // Step 1: Remove diacritics
        var normalized = RemoveDiacritics(query);

        // Step 2: Normalize whitespace (multiple spaces to single space)
        normalized = Regex.Replace(normalized, @"\s+", " ");

        // Step 3: Trim
        normalized = normalized.Trim();

        return normalized;
    }

    /// <summary>
    /// Generate alternate search queries by expanding location aliases and word substitutions.
    /// Returns the original query plus any relevant alternates.
    /// </summary>
    public static List<string> GenerateSearchVariations(string query)
    {
        var variations = new List<string> { query };

        // First, normalize the query (remove diacritics)
        var normalized = NormalizeForSearch(query);
        if (normalized != query)
        {
            variations.Add(normalized);
        }

        // Check for location aliases
        foreach (var (location, aliases) in LocationAliases)
        {
            // Check if the query contains this location
            if (ContainsWord(normalized, location))
            {
                foreach (var alias in aliases)
                {
                    var alternate = ReplaceWord(normalized, location, alias);
                    if (!variations.Contains(alternate, StringComparer.OrdinalIgnoreCase))
                    {
                        variations.Add(alternate);
                    }
                }
            }

            // Also check reverse - if query has an alias, suggest the main location
            foreach (var alias in aliases)
            {
                if (ContainsWord(normalized, alias))
                {
                    var alternate = ReplaceWord(normalized, alias, location);
                    if (!variations.Contains(alternate, StringComparer.OrdinalIgnoreCase))
                    {
                        // Insert at position 1 (after original) - main location is preferred
                        variations.Insert(1, alternate);
                    }
                }
            }
        }

        return variations;
    }

    /// <summary>
    /// Check if a string contains a word (case-insensitive, whole word match).
    /// </summary>
    private static bool ContainsWord(string text, string word)
    {
        var pattern = $@"\b{Regex.Escape(word)}\b";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Replace a word in a string (case-insensitive, preserves surrounding text).
    /// </summary>
    private static string ReplaceWord(string text, string oldWord, string newWord)
    {
        var pattern = $@"\b{Regex.Escape(oldWord)}\b";
        return Regex.Replace(text, pattern, newWord, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Check if two strings match after normalization.
    /// Used for comparing release titles to event titles.
    /// </summary>
    public static bool NormalizedMatch(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;

        var normalizedA = NormalizeForSearch(a).ToLowerInvariant();
        var normalizedB = NormalizeForSearch(b).ToLowerInvariant();

        return normalizedA == normalizedB;
    }

    /// <summary>
    /// Check if normalized string A contains normalized string B.
    /// </summary>
    public static bool NormalizedContains(string text, string search)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(search))
            return false;

        var normalizedText = NormalizeForSearch(text).ToLowerInvariant();
        var normalizedSearch = NormalizeForSearch(search).ToLowerInvariant();

        return normalizedText.Contains(normalizedSearch);
    }

    /// <summary>
    /// Check if a release title matches an event title with normalization and alias expansion.
    /// Returns true if either the exact normalized match or any alias variation matches.
    /// </summary>
    public static bool IsReleaseMatch(string releaseTitle, string eventTitle)
    {
        if (string.IsNullOrEmpty(releaseTitle) || string.IsNullOrEmpty(eventTitle))
            return false;

        var normalizedRelease = NormalizeForSearch(releaseTitle).ToLowerInvariant();
        var normalizedEvent = NormalizeForSearch(eventTitle).ToLowerInvariant();

        // Direct match after normalization
        if (normalizedRelease.Contains(normalizedEvent))
            return true;

        // Check with event title variations (location aliases)
        var eventVariations = GenerateSearchVariations(eventTitle);
        foreach (var variation in eventVariations)
        {
            var normalizedVariation = NormalizeForSearch(variation).ToLowerInvariant();
            if (normalizedRelease.Contains(normalizedVariation))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Extract key terms from an event title for fuzzy matching.
    /// Returns normalized terms that should appear in a matching release.
    /// </summary>
    public static List<string> ExtractKeyTerms(string eventTitle)
    {
        if (string.IsNullOrEmpty(eventTitle))
            return new List<string>();

        var normalized = NormalizeForSearch(eventTitle);

        // Split into words and filter out common words
        var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "of", "at", "in", "on", "for", "to", "and", "or",
            "grand", "prix", "race", "event", "championship", "cup", "series"
        };

        var terms = normalized
            .Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1 && !commonWords.Contains(w))
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .ToList();

        return terms;
    }
}
