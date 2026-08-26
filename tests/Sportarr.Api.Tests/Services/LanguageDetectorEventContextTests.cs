using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A nationality naming the competition is not an audio tag. The Spanish
/// Grand Prix, the French Open and the Italian GP were all read as
/// non-English releases, so English-targeting formats missed them and
/// language penalties fired.
/// </summary>
public class LanguageDetectorEventContextTests
{
    [Theory]
    [InlineData("Formula1.2026.Spanish.Grand.Prix.Race.1080p.WEB")]
    [InlineData("Formula.1.2026.Italian.GP.Qualifying.2160p")]
    [InlineData("Tennis.2026.French.Open.Final.720p")]
    [InlineData("Golf.2026.Scottish.Open.Round.3")]
    [InlineData("Football.2026.German.Cup.Final")]
    public void A_nationality_naming_the_event_is_not_a_language(string title)
    {
        LanguageDetector.DetectLanguage(title).Should().Be("English");
    }

    [Theory]
    [InlineData("Formula1.2026.Round.05.Race.1080p.SPANISH.WEB", "Spanish")]
    [InlineData("Tennis.2026.Final.720p.FRENCH.HDTV", "French")]
    [InlineData("MotoGP.2026.Mugello.ITALIAN.1080p", "Italian")]
    public void A_bare_language_tag_is_still_read_as_one(string title, string expected)
    {
        LanguageDetector.DetectLanguage(title).Should().Be(expected);
    }
}

/// <summary>
/// Every occurrence is judged on its own. Looking at the first alone lost a
/// bare tag at the end of a title that opened with a competition name.
/// </summary>
public class LanguageDetectorLaterTagTests
{
    [Theory]
    [InlineData("Football.2026.German.Cup.Final.720p.GERMAN.HDTV", "German")]
    [InlineData("Tennis.2026.French.Open.Final.FRENCH.1080p", "French")]
    public void A_bare_tag_after_an_event_name_still_counts(string title, string expected)
    {
        LanguageDetector.DetectLanguage(title).Should().Be(expected);
    }
}
