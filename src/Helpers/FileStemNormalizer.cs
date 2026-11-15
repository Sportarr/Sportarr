using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Reduces a file name to the key that match memory stores. The hub does
/// the same steps in backend/app/utils/file_stem.py. Both sides test one
/// shared fixture, tests/Sportarr.Api.Tests/Services/Fixtures/StemNormalization/cases.json.
/// Change the two sides and the fixture together.
/// </summary>
public static class FileStemNormalizer
{
    /// <summary>The longest key, in Unicode code points.</summary>
    public const int MaxLength = 200;

    // A real file name is shorter than this. The cap keeps the regex work
    // small when a caller sends a very long value.
    private const int MaxInputLength = 1000;

    // Only these extensions are dropped. A generic rule would cut ".1"
    // from a stem such as "Round.1".
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.Ordinal)
    {
        "mkv", "mp4", "avi", "m4v", "mov", "wmv", "ts", "m2ts", "mts",
        "webm", "flv", "mpg", "mpeg", "iso", "vob", "strm",
    };

    // The fences are ASCII on purpose. Both regex engines then read them
    // the same way.
    private const string Start = @"(?<![a-z0-9])";
    private const string End = @"(?![a-z0-9])";

    // A channel layout can follow an audio codec, as in "DDP5.1" or "DD+5.1".
    private const string Channels = @"(?:\+?[ .]?[0-9][ .][0-9])?";

    private const string Audio = "(?:" + MediaFileParser.AudioCodecTokens + ")" + Channels;
    private const string Video = "(?:" + MediaFileParser.VideoCodecTokens + ")";
    private const string Source = "(?:" + MediaFileParser.SourceTokens + ")";
    private const string Quality = "(?:" + MediaFileParser.QualityTokens + ")";
    private const string GenericQuality = "(?:" + MediaFileParser.GenericQualityTokens + ")";
    private const string ProperRepack = "(?:" + MediaFileParser.ProperRepackTokens + ")";
    private const string Edition = "(?:" + MediaFileParser.EditionTokens + ")";
    private const string Language = "(?:" + MediaFileParser.LanguageTokens + ")";

    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // The technical tail starts at the first resolution or source token.
    // Tokens are removed only in the tail. Title words such as "Spanish
    // Open", "AVC" or "AAC" then stay, and different events keep
    // different keys.
    // "Cam" and "TS" are also title words ("Onboard Cam", "Cam Smith"), so
    // they do not start the tail. The tail still removes them.
    private const string TitleWordSources = "(?:cam|ts)" + End;

    private static readonly Regex TailStartPattern = new(
        Start + "(?:" + Quality + "|(?!" + TitleWordSources + ")" + Source + ")" + End,
        Options);

    /// <summary>
    /// Session, card and show words from EventPartDetector. A word after a
    /// dashed tag names the event, so the group rule keeps it. The word can
    /// carry a number, as in "Race2". Two spoken words are one word here,
    /// as in "WarmUp". The hub holds the same list in
    /// backend/app/utils/file_stem.py, and the shared fixture holds it too.
    /// Change the three together.
    /// </summary>
    public static readonly IReadOnlyList<string> EventWords = new[]
    {
        "afternoon", "analysis", "buyin", "countdown", "course", "day",
        "documentary", "duel", "duels", "earlycard", "earlyprelim",
        "earlypreliminary", "earlyprelims", "ep", "essais", "fastfriday",
        "fp", "gp", "grandprix", "grille", "highlights", "hyperpole",
        "kickoff", "leadcard", "magazine", "maincard", "mainevent",
        "mainshow", "mc", "morning", "notebook", "postevent", "postfight",
        "postfightshow", "postshow", "ppv", "practice", "prelim",
        "prelimcard", "preliminary", "prelims", "preshow", "preview", "q",
        "quali", "qualif", "qualification", "qualifications", "qualifier",
        "qualifs", "qualifying", "qualy", "race", "review", "session",
        "shakedown", "shakedownday", "shakedowntest", "shootout", "sprint",
        "sq", "superpole", "test", "testday", "testing", "tests", "top",
        "topten", "undercard", "warmup", "zerohour",
    };

    private static readonly string EventWord =
        "(?:" + string.Join("|", EventWords) + ")[0-9]*" + End;

    // A release group is removed only when it follows a quality, source or
    // codec token. "Arsenal-Chelsea" at the end of a name is not a group.
    // "Cam" and "TS" are title words, so they are not such a token here.
    // An event word is not a group, as in "1080p-Sprint".
    private static readonly Regex GroupPattern = new(
        Start + "(?<tech>" + Audio + "|" + Video + "|(?!" + TitleWordSources + ")" + Source + "|" + Quality + "|" + GenericQuality + ")"
        + "-(?!" + EventWord + @")[a-z0-9]+(?:\[[a-z0-9]+\])?(?<rest>[^a-z0-9]*)$",
        Options);

    // Audio and video go first. Otherwise "HD" takes the middle of "DTS-HD".
    private static readonly Regex[] TokenPatterns =
    {
        new(Start + Audio + End, Options),
        new(Start + Video + End, Options),
        new(Start + Source + End, Options),
        new(Start + Quality + End, Options),
        new(Start + GenericQuality + End, Options),
        new(Start + ProperRepack + End, Options),
        new(Start + Edition + End, Options),
        new(Start + Language + End, Options),
    };

    /// <summary>
    /// The match memory key for a file name, a stem or a path. The result
    /// is lower case, holds only letters, digits and single spaces, and
    /// keeps years and season and episode tokens.
    /// </summary>
    public static string Normalize(string? fileNameOrStem)
    {
        if (string.IsNullOrEmpty(fileNameOrStem))
            return string.Empty;

        var name = fileNameOrStem;
        var lastSeparator = name.LastIndexOfAny(new[] { '/', '\\' });
        if (lastSeparator >= 0)
            name = name[(lastSeparator + 1)..];

        name = DropMediaExtension(name);
        name = TakeCodePoints(name, MaxInputLength);
        name = CleanCodePoints(name).Normalize(NormalizationForm.FormC);
        name = SpacesToAscii(name);

        name = name.ToLowerInvariant();
        name = SportarrIdToken.Strip(name);
        name = GroupPattern.Replace(name, RemoveGroup);
        name = StripTailTokens(name);

        name = CollapseToLettersAndDigits(name);
        return TakeCodePoints(name, MaxLength).TrimEnd(' ');
    }

    // The group must be the last token. The pattern reads only ASCII, so
    // this check finds a word in another script after the group.
    private static string RemoveGroup(Match match)
    {
        foreach (var rune in match.Groups["rest"].Value.EnumerateRunes())
        {
            if (IsLetterOrDigit(rune))
                return match.Value;
        }
        return match.Groups["tech"].Value + " ";
    }

    // The fence before the first tail token is a character that is not an
    // ASCII letter or digit. The fences in the tail patterns therefore read
    // the cut tail the same way as the full name.
    private static string StripTailTokens(string name)
    {
        var tailStart = TailStartPattern.Match(name);
        if (!tailStart.Success)
            return name;

        var tail = name[tailStart.Index..];
        foreach (var pattern in TokenPatterns)
            tail = pattern.Replace(tail, " ");
        return name[..tailStart.Index] + " " + tail;
    }

    private static string DropMediaExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot < 0)
            return name;

        var extension = name[(dot + 1)..];
        if (!extension.All(char.IsAscii))
            return name;

        return MediaExtensions.Contains(extension.ToLowerInvariant()) ? name[..dot] : name;
    }

    // A lone surrogate becomes U+FFFD. Normalization then cannot fail.
    private static string CleanCodePoints(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
            builder.Append(rune.ToString());
        return builder.ToString();
    }

    // The token lists use \s. The two engines disagree on some \s
    // characters, so every space becomes an ASCII space first.
    private static string SpacesToAscii(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            var isSpace = category is UnicodeCategory.SpaceSeparator
                    or UnicodeCategory.LineSeparator
                    or UnicodeCategory.ParagraphSeparator
                || rune.Value is >= 0x09 and <= 0x0D or 0x85;
            builder.Append(isSpace ? " " : rune.ToString());
        }
        return builder.ToString();
    }

    // Each run of characters that are not letters or digits becomes one
    // space. The check reads the Unicode category, because \w differs
    // between the two regex engines.
    private static string CollapseToLettersAndDigits(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsLetterOrDigit(rune))
            {
                if (pendingSpace && builder.Length > 0)
                    builder.Append(' ');
                pendingSpace = false;
                builder.Append(rune.ToString());
            }
            else
            {
                pendingSpace = true;
            }
        }
        return builder.ToString();
    }

    private static bool IsLetterOrDigit(Rune rune) => Rune.GetUnicodeCategory(rune) switch
    {
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber => true,
        _ => false,
    };

    // The cap counts code points, as Python does. A UTF-16 count would cut
    // a letter outside the Basic Multilingual Plane in half.
    private static string TakeCodePoints(string text, int limit)
    {
        var count = 0;
        var length = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count == limit)
                return text[..length];
            count++;
            length += rune.Utf16SequenceLength;
        }
        return text;
    }
}
