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
        // Include both country names AND demonyms (Mexican, Brazilian, etc.) for indexer compatibility
        { "Mexico City", new[] { "Mexico", "Mexican" } },
        { "Sao Paulo", new[] { "Brazil", "Brazilian", "Interlagos" } },
        { "Las Vegas", new[] { "Vegas" } },
        { "Abu Dhabi", new[] { "AbuDhabi", "Yas Marina", "Fight Island", "UFC Fight Island" } },
        { "Monte Carlo", new[] { "Monaco", "Monegasque" } },
        { "Spielberg", new[] { "Austria", "Austrian" } },
        { "Silverstone", new[] { "British", "Britain", "UK", "Great Britain" } },
        { "Monza", new[] { "Italy", "Italian" } },
        { "Spa", new[] { "Belgium", "Belgian", "Spa-Francorchamps" } },
        { "Suzuka", new[] { "Japan", "Japanese" } },
        { "Singapore", new[] { "Marina Bay", "Singaporean" } },
        { "Melbourne", new[] { "Australia", "Australian" } },
        { "Montreal", new[] { "Canada", "Canadian" } },
        { "Baku", new[] { "Azerbaijan", "Azerbaijani" } },
        { "Jeddah", new[] { "Saudi Arabia", "Saudi Arabian", "Saudi" } },
        { "Miami", new[] { "Miami Gardens" } },
        { "Imola", new[] { "Emilia Romagna", "San Marino", "Emilia-Romagna" } },
        { "Zandvoort", new[] { "Netherlands", "Dutch" } },
        { "Budapest", new[] { "Hungary", "Hungarian", "Hungaroring" } },
        { "Barcelona", new[] { "Spain", "Spanish", "Catalunya", "Catalan" } },
        { "Shanghai", new[] { "China", "Chinese" } },
        { "Bahrain", new[] { "Sakhir", "Bahraini" } },
        { "Qatar", new[] { "Lusail", "Qatari" } },

        // MotoGP locations
        { "Mugello", new[] { "Italy", "Italian" } },
        { "Le Mans", new[] { "France", "French" } },
        { "Sachsenring", new[] { "Germany", "German" } },
        { "Assen", new[] { "Netherlands", "Dutch", "TT Assen" } },
        { "Phillip Island", new[] { "Australia", "Australian" } },
        { "Sepang", new[] { "Malaysia", "Malaysian" } },
        { "Losail", new[] { "Qatar", "Qatari" } },
        { "Termas de Rio Hondo", new[] { "Argentina", "Argentine", "Argentinian" } },
        { "Circuit of the Americas", new[] { "COTA", "Austin", "Texas", "United States", "American", "USA", "US" } },
        // United States GP can be called many things - this is the primary entry for official F1 naming
        { "United States", new[] { "USA", "US", "American", "COTA", "Circuit of the Americas", "Austin" } },

        // UFC / MMA Fight Night locations
        { "Riyadh", new[] { "Saudi Arabia", "Saudi" } },

        // Common city variations
        { "New York City", new[] { "New York", "NYC", "NY" } },
        { "Los Angeles", new[] { "LA" } },
        { "San Francisco", new[] { "SF" } },
    };

    /// <summary>
    /// Country demonym mappings - maps demonyms (adjective forms) to country/location names.
    /// This allows "Mexican Grand Prix" in a release to match events named "Mexico Grand Prix".
    /// Used in addition to LocationAliases for bidirectional matching.
    /// Key: demonym (case-insensitive), Value: list of equivalent location names
    /// </summary>
    private static readonly Dictionary<string, string[]> DemonymToLocation = new(StringComparer.OrdinalIgnoreCase)
    {
        // Americas
        { "Mexican", new[] { "Mexico", "Mexico City" } },
        { "Brazilian", new[] { "Brazil", "Sao Paulo", "Interlagos" } },
        { "Canadian", new[] { "Canada", "Montreal" } },
        { "American", new[] { "United States", "USA", "US", "COTA", "Circuit of the Americas", "Austin", "Las Vegas", "Miami" } },
        { "Argentine", new[] { "Argentina", "Termas de Rio Hondo" } },
        { "Argentinian", new[] { "Argentina", "Termas de Rio Hondo" } },

        // Europe
        { "British", new[] { "Britain", "UK", "Great Britain", "Silverstone" } },
        { "Italian", new[] { "Italy", "Monza", "Imola", "Mugello" } },
        { "Spanish", new[] { "Spain", "Barcelona", "Catalunya" } },
        { "French", new[] { "France", "Le Mans" } },
        { "German", new[] { "Germany", "Sachsenring", "Hockenheim", "Nurburgring" } },
        { "Belgian", new[] { "Belgium", "Spa", "Spa-Francorchamps" } },
        { "Dutch", new[] { "Netherlands", "Zandvoort", "Assen" } },
        { "Hungarian", new[] { "Hungary", "Budapest", "Hungaroring" } },
        { "Austrian", new[] { "Austria", "Spielberg" } },
        { "Monegasque", new[] { "Monaco", "Monte Carlo" } },
        { "Azerbaijani", new[] { "Azerbaijan", "Baku" } },

        // Asia/Middle East
        { "Japanese", new[] { "Japan", "Suzuka" } },
        { "Chinese", new[] { "China", "Shanghai" } },
        { "Singaporean", new[] { "Singapore", "Marina Bay" } },
        { "Malaysian", new[] { "Malaysia", "Sepang" } },
        { "Bahraini", new[] { "Bahrain", "Sakhir" } },
        { "Qatari", new[] { "Qatar", "Lusail" } },
        { "Saudi", new[] { "Saudi Arabia", "Jeddah", "Riyadh" } },

        // Oceania
        { "Australian", new[] { "Australia", "Melbourne", "Phillip Island" } },
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
        { "versus", new[] { "vs", "v", "@" } },
        { "vs", new[] { "versus", "v", "@" } },
        { "@", new[] { "vs", "versus", "v" } },
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
    /// Generate alternate search queries by expanding location aliases, demonyms, and word substitutions.
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

        // Check for location aliases (location -> aliases)
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

        // Check for demonym mappings (demonym -> locations)
        // This handles "Mexican Grand Prix" -> "Mexico Grand Prix" matching
        foreach (var (demonym, locations) in DemonymToLocation)
        {
            if (ContainsWord(normalized, demonym))
            {
                // Replace demonym with each equivalent location name
                foreach (var location in locations)
                {
                    var alternate = ReplaceWord(normalized, demonym, location);
                    if (!variations.Contains(alternate, StringComparer.OrdinalIgnoreCase))
                    {
                        variations.Add(alternate);
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
    /// Handles bidirectional matching: "Mexican Grand Prix" release matches "Mexico Grand Prix" event and vice versa.
    /// Also handles partial matches where release has just location (e.g., "United States" matches "United States Grand Prix").
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

        // Check with event title variations (location aliases, demonyms)
        // This handles: event "Mexico Grand Prix" matches release with "Mexican"
        var eventVariations = GenerateSearchVariations(eventTitle);
        foreach (var variation in eventVariations)
        {
            var normalizedVariation = NormalizeForSearch(variation).ToLowerInvariant();
            if (normalizedRelease.Contains(normalizedVariation))
                return true;
        }

        // Check with release title variations (reverse direction)
        // This handles: release "Mexican Grand Prix" matches event "Mexico Grand Prix"
        var releaseVariations = GenerateSearchVariations(releaseTitle);
        foreach (var variation in releaseVariations)
        {
            var normalizedVariation = NormalizeForSearch(variation).ToLowerInvariant();
            if (normalizedVariation.Contains(normalizedEvent))
                return true;
        }

        // Check if key location terms from event are in release
        // This handles: release "Formula1.2025.United.States" matching "United States Grand Prix"
        // We check if the key location (ignoring "Grand Prix", "Race", etc.) appears in the release
        var keyTerms = ExtractKeyTerms(eventTitle);
        foreach (var term in keyTerms)
        {
            // Check if this key term (or its aliases) appears in the release
            if (ContainsWord(normalizedRelease, term))
                return true;

            // Also check location aliases for this term
            foreach (var (location, aliases) in LocationAliases)
            {
                if (location.Equals(term, StringComparison.OrdinalIgnoreCase) ||
                    term.Contains(location.ToLowerInvariant()))
                {
                    // Check if any alias appears in release
                    foreach (var alias in aliases)
                    {
                        if (ContainsWord(normalizedRelease, alias.ToLowerInvariant()))
                            return true;
                    }
                }
                // Check reverse - if term matches an alias
                if (aliases.Any(a => a.Equals(term, StringComparison.OrdinalIgnoreCase)))
                {
                    if (ContainsWord(normalizedRelease, location.ToLowerInvariant()))
                        return true;
                }
            }

            // Check demonym mappings
            foreach (var (demonym, locations) in DemonymToLocation)
            {
                if (demonym.Equals(term, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var loc in locations)
                    {
                        if (ContainsWord(normalizedRelease, loc.ToLowerInvariant()))
                            return true;
                    }
                }
                if (locations.Any(l => l.Equals(term, StringComparison.OrdinalIgnoreCase)))
                {
                    if (ContainsWord(normalizedRelease, demonym.ToLowerInvariant()))
                        return true;
                }
            }
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
