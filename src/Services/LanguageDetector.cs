using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Detects language from release titles.
/// Based on Sonarr's language detection patterns.
/// </summary>
public static class LanguageDetector
{
    // Language detection patterns - order matters (more specific first)
    private static readonly (string Language, Regex Pattern)[] LanguagePatterns = new[]
    {
        // Multi-language indicators
        ("Multi", new Regex(@"\b(MULTI|MULTi|MULTILANG|MULTiLANG)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        // Note: "DL" alone is too ambiguous - matches WEB-DL. Only match explicit dual audio patterns.
        ("Dual Audio", new Regex(@"\b(DUAL[\.\-\s]?AUDIO|DUAL[\.\-\s]?LANG|DualAudio)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),

        // Specific languages (alphabetical, with common scene naming patterns)
        ("Arabic", new Regex(@"\b(ARABIC|ARA|AR)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Chinese", new Regex(@"\b(CHINESE|CHI|CN|MANDARIN|CANTONESE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Czech", new Regex(@"\b(CZECH|CZ|CZE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Danish", new Regex(@"\b(DANISH|DAN|DA)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Dutch", new Regex(@"\b(DUTCH|NL|NLD|FLEMISH)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Finnish", new Regex(@"\b(FINNISH|FIN|FI)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("French", new Regex(@"\b(FRENCH|FRE|FR|TRUEFRENCH|VFF|VFQ|VF2|VOSTFR)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        // Note: Removed "DL" - too ambiguous, conflicts with WEB-DL
        ("German", new Regex(@"\b(GERMAN|GER|DE|DEUTSCH)\b(?![\.\-]?SUB)", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Greek", new Regex(@"\b(GREEK|GRE|GR)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Hebrew", new Regex(@"\b(HEBREW|HEB|HE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Hindi", new Regex(@"\b(HINDI|HIN|HI)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Hungarian", new Regex(@"\b(HUNGARIAN|HUN|HU)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Indonesian", new Regex(@"\b(INDONESIAN|IND|ID)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Italian", new Regex(@"\b(ITALIAN|ITA|IT)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Japanese", new Regex(@"\b(JAPANESE|JAP|JP|JPN)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Korean", new Regex(@"\b(KOREAN|KOR|KO|KR)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Norwegian", new Regex(@"\b(NORWEGIAN|NOR|NO)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Persian", new Regex(@"\b(PERSIAN|PER|FA|FARSI)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Polish", new Regex(@"\b(POLISH|POL|PL)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Portuguese", new Regex(@"\b(PORTUGUESE|POR|PT|PTBR|PT-BR)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Romanian", new Regex(@"\b(ROMANIAN|ROM|RO)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Russian", new Regex(@"\b(RUSSIAN|RUS|RU)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Spanish", new Regex(@"\b(SPANISH|SPA|ES|ESP|LATINO|CASTELLANO|LATAM)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Swedish", new Regex(@"\b(SWEDISH|SWE|SV)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Tamil", new Regex(@"\b(TAMIL|TAM|TA)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Telugu", new Regex(@"\b(TELUGU|TEL|TE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Thai", new Regex(@"\b(THAI|THA|TH)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Turkish", new Regex(@"\b(TURKISH|TUR|TR)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Ukrainian", new Regex(@"\b(UKRAINIAN|UKR|UK)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Vietnamese", new Regex(@"\b(VIETNAMESE|VIE|VI)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),

        // English should be detected but only if explicitly mentioned
        // Most English releases don't say "English" - they're assumed English by default
        ("English", new Regex(@"\b(ENGLISH|ENG|EN)\b(?![\.\-]?SUB)", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    };

    // Subtitle-only indicators (these should not be treated as audio language)
    private static readonly Regex SubtitleOnlyPattern = new Regex(
        @"\b(SUBBED|SUB|SUBS|SUBTITLED|VOSTFR|HARDSUB|SOFTSUB|[\w]+[\.\-]SUB)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Detect language from a release title.
    /// Returns null if no language detected (assume English for unmarked releases).
    /// </summary>
    public static string? DetectLanguage(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        // Check for subtitle-only indicators first
        // If the language appears only as a subtitle indicator, don't return it as the main language
        var hasSubIndicator = SubtitleOnlyPattern.IsMatch(title);

        foreach (var (language, pattern) in LanguagePatterns)
        {
            if (pattern.IsMatch(title))
            {
                // For non-English languages, check if it's subtitle-only
                if (language != "English" && language != "Multi" && language != "Dual Audio")
                {
                    // If subtitle indicator present, check if this language appears as subtitle
                    // e.g., "Movie.Name.1080p.GER.SUBS" should not be marked as German
                    var match = pattern.Match(title);
                    if (match.Success)
                    {
                        var afterMatch = title.Substring(match.Index + match.Length);
                        // If SUB/SUBS immediately follows, it's subtitle-only
                        if (Regex.IsMatch(afterMatch, @"^[\.\-\s]*(SUB|SUBS)\b", RegexOptions.IgnoreCase))
                        {
                            continue;
                        }
                    }
                }

                return language;
            }
        }

        // No explicit language found - default to English (Sonarr behavior)
        // Most releases without explicit language tags are English
        return "English";
    }

    /// <summary>
    /// Detect all languages mentioned in a release title.
    /// Useful for multi-language releases.
    /// </summary>
    public static List<string> DetectAllLanguages(string title)
    {
        var languages = new List<string>();

        if (string.IsNullOrWhiteSpace(title))
            return languages;

        foreach (var (language, pattern) in LanguagePatterns)
        {
            if (pattern.IsMatch(title))
            {
                // Skip duplicate language categories
                if (language == "Dual Audio" && languages.Contains("Multi"))
                    continue;

                languages.Add(language);
            }
        }

        return languages;
    }
}
