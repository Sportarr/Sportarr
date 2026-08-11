using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class LanguageDetectorTests
{
    [Theory]
    [InlineData("Cycling.UCI.World.Tour.2026.Tour.De.France.Men.Elite.Stage.18.1080p.WEB.h264-BILLIE")]
    [InlineData("Tour.De.France.2026.Stage.21.Highlights.1080p.HDTV.H264-DARKSPORT")]
    [InlineData("Tour.de.France.Femmes.2024.Stage.2.1080p.HDTV.H264-DARKSPORT")]
    public void DetectLanguage_TourDeFrance_IsNotGerman(string title)
    {
        // "De" in "Tour De France" is a French word, not a language tag. It
        // used to match the German pattern and scored these releases against
        // German custom formats.
        LanguageDetector.DetectLanguage(title).Should().NotBe("German");
    }

    [Theory]
    [InlineData("Tour.De.France.2026.Etappe.16.German.1080p.WEB.h264-SPORTY")]
    [InlineData("Some.Event.2026.GERMAN.1080p.WEB.h264-GRP")]
    [InlineData("Some.Event.2026.GER.1080p.WEB.h264-GRP")]
    [InlineData("Some.Event.2026.DEUTSCH.1080p.WEB.h264-GRP")]
    public void DetectLanguage_RealGermanTags_StillDetected(string title)
    {
        LanguageDetector.DetectLanguage(title).Should().Be("German");
    }

    [Fact]
    public void DetectLanguage_GermanSubtitleTag_IsNotAudioGerman()
    {
        LanguageDetector.DetectLanguage("Some.Event.2026.1080p.WEB.GER.SUBS.h264-GRP")
            .Should().NotBe("German");
    }

    [Fact]
    public void DetectLanguage_UnmarkedRelease_DefaultsToEnglish()
    {
        LanguageDetector.DetectLanguage("Some.Event.2026.1080p.WEB.h264-GRP")
            .Should().Be("English");
    }
}
