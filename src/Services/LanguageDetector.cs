using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Detects language from release titles via pattern matching on common
/// scene tags (e.g. "FR", "FRENCH", "MULTI", "DUAL").
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

        // Specific languages (alphabetical, with common scene naming patterns).
        //
        // Bare two-letter codes are gone, for the same reason "DE" and "DL"
        // already were. Every one of them collides with something that turns up
        // in a sports title: "NO" and "IT" and "HE" and "HI" are ordinary
        // English words, "PL" is the Premier League, "AR" and "ID" and "CN" are
        // team and identifier abbreviations. A release tagged with none of them
        // was being assigned a language it does not have, which then rejected a
        // perfectly good release or picked the wrong one. The three-letter and
        // spelled-out forms are what release groups actually use.
        ("Arabic", new Regex(@"\b(ARABIC|ARA)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Chinese", new Regex(@"\b(CHINESE|CHI|MANDARIN|CANTONESE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Czech", new Regex(@"\b(CZECH|CZE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Danish", new Regex(@"\b(DANISH|DAN)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Dutch", new Regex(@"\b(DUTCH|NLD|FLEMISH)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Finnish", new Regex(@"\b(FINNISH|FIN)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("French", new Regex(@"\b(FRENCH|FRE|TRUEFRENCH|VFF|VFQ|VF2|VOSTFR)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        // Note: Removed "DL" - too ambiguous, conflicts with WEB-DL.
        // Note: Removed bare "DE" - collides with the French word "de" in
        // titles like "Tour De France". Real German scene tags are GERMAN/GER/DEUTSCH.
        ("German", new Regex(@"\b(GERMAN|GER|DEUTSCH)\b(?![\.\-]?SUB)", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Greek", new Regex(@"\b(GREEK|GRE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Hebrew", new Regex(@"\b(HEBREW|HEB)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Hindi", new Regex(@"\b(HINDI|HIN)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Hungarian", new Regex(@"\b(HUNGARIAN|HUN)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Indonesian", new Regex(@"\b(INDONESIAN|IND)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Italian", new Regex(@"\b(ITALIAN|ITA)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Japanese", new Regex(@"\b(JAPANESE|JAP|JPN)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Korean", new Regex(@"\b(KOREAN|KOR)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Norwegian", new Regex(@"\b(NORWEGIAN|NOR)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Persian", new Regex(@"\b(PERSIAN|PER|FARSI)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Polish", new Regex(@"\b(POLISH|POL)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Portuguese", new Regex(@"\b(PORTUGUESE|POR|PTBR|PT-BR)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Romanian", new Regex(@"\b(ROMANIAN|ROM)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Russian", new Regex(@"\b(RUSSIAN|RUS)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Spanish", new Regex(@"\b(SPANISH|SPA|ESP|LATINO|CASTELLANO|LATAM)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
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

    // Words that turn a nationality into the name of a competition rather
    // than a language tag: "Spanish Grand Prix", "French Open", "Italian GP".
    private static readonly Regex EventContextAfterLanguage = new Regex(
        @"^[\.\-\s_]*(GRAND[\.\-\s_]*PRIX|GP|OPEN|CUP|MASTERS|DERBY|LEAGUE|CHAMPIONSHIP|CLASSIC|SUPER[\.\-\s_]*CUP|TOUR)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Subtitle-only indicators (these should not be treated as audio language)
    private static readonly Regex SubtitleOnlyPattern = new Regex(
        @"\b(SUBBED|SUB|SUBS|SUBTITLED|VOSTFR|HARDSUB|SOFTSUB|[\w]+[\.\-]SUB)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Detect language from a release title.
    /// Returns null if no language detected (assume English for unmarked releases).
    /// </summary>
    /// <summary>
    /// Language names by the numeric id custom formats use, the same list
    /// Sonarr and the TRaSH guides publish.
    ///
    /// This lives here so grab-time evaluation and rename-time evaluation read
    /// one table. They used to disagree: the rename side answered "English"
    /// for every release and nothing else ever matched.
    /// </summary>
    public static string? NameForCustomFormatId(int id) => id switch
    {
        -2 => "English", // Original language. For sports that is English.
        0 => "English",  // Unknown, treated as English.
        1 => "English",
        2 => "French",
        3 => "Spanish",
        4 => "German",
        5 => "Italian",
        8 => "Japanese",
        10 => "Russian",
        11 => "Portuguese",
        12 => "Dutch",
        13 => "Swedish",
        14 => "Norwegian",
        15 => "Danish",
        16 => "Finnish",
        17 => "Turkish",
        18 => "Greek",
        19 => "Korean",
        20 => "Hungarian",
        21 => "Hebrew",
        22 => "Lithuanian",
        23 => "Czech",
        24 => "Hindi",
        25 => "Romanian",
        26 => "Thai",
        27 => "Bulgarian",
        28 => "Polish",
        29 => "Chinese",
        30 => "Vietnamese",
        31 => "Arabic",
        32 => "Ukrainian",
        33 => "Persian",
        34 => "Bengali",
        35 => "Slovak",
        36 => "Latvian",
        37 => "Indonesian",
        38 => "Catalan",
        39 => "Bosnian",
        _ => null
    };

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
                    // Every occurrence is judged on its own. Looking at the
                    // first alone lost a bare GERMAN tag at the end of a
                    // title that opened with "German Cup", and nationality
                    // plus competition word is common in sports titles.
                    var bareTag = false;
                    foreach (Match match in pattern.Matches(title))
                    {
                        var afterMatch = title.Substring(match.Index + match.Length);
                        // If SUB/SUBS immediately follows, it's subtitle-only
                        if (Regex.IsMatch(afterMatch, @"^[\.\-\s]*(SUB|SUBS)\b", RegexOptions.IgnoreCase))
                        {
                            continue;
                        }

                        // A nationality naming the event is not an audio
                        // tag. The Spanish Grand Prix, the French Open and
                        // the Italian GP were all scored as non-English
                        // releases, so English-targeting formats missed
                        // them and language penalties fired.
                        if (EventContextAfterLanguage.IsMatch(afterMatch))
                        {
                            continue;
                        }

                        bareTag = true;
                        break;
                    }

                    if (!bareTag)
                    {
                        continue;
                    }
                }

                return language;
            }
        }

        // No explicit language found - default to English.
        // Most releases without explicit language tags are English.
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
