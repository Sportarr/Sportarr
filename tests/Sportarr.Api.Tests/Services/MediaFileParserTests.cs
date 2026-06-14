using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

public class MediaFileParserTests
{
    private readonly MediaFileParser _parser;
    private readonly Mock<ILogger<MediaFileParser>> _mockLogger;

    public MediaFileParserTests()
    {
        _mockLogger = new Mock<ILogger<MediaFileParser>>();
        _parser = new MediaFileParser(_mockLogger.Object);
    }

    [Theory]
    [InlineData("UFC.300.2024.04.13.1080p.WEB-DL.x264-GROUP", "UFC 300 2024 04 13")]
    [InlineData("UFC 300 Main Card 1080p HDTV x264-ABC", "UFC 300 Main Card")]
    [InlineData("Fury vs Usyk 2024 720p BluRay x265-XYZ", "Fury vs Usyk")]
    [InlineData("Bellator.300.Prelims.480p.WEBRip.AAC-GROUP", "Bellator 300 Prelims")]
    public void Parse_ShouldExtractEventTitle(string filename, string expectedTitle)
    {
        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.EventTitle.Should().Be(expectedTitle);
    }

    [Theory]
    [InlineData("UFC.300.1080p.WEB-DL.x264-GROUP", "1080P")]
    [InlineData("Fight.Night.720p.HDTV.x264", "720P")]
    [InlineData("Event.2160p.BluRay.HEVC", "2160P")]
    [InlineData("Fight.480p.WEBRip.x264", "480P")]
    [InlineData("Card.4K.UHD.BluRay", "4K")]
    public void Parse_ShouldExtractResolution(string filename, string expectedResolution)
    {
        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.Resolution.Should().Be(expectedResolution);
    }

    [Theory]
    [InlineData("UFC.300.1080p.BluRay.x264", "BLURAY")]
    [InlineData("Fight.720p.WEB-DL.x264", "WEBDL")]
    [InlineData("Event.1080p.WEBRip.x265", "WEBRip")]
    [InlineData("Card.1080p.HDTV.x264", "HDTV")]
    [InlineData("Fight.DVDRip.XviD", "DVDRIP")]
    public void Parse_ShouldExtractSource(string filename, string expectedSource)
    {
        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.Source.Should().Be(expectedSource);
    }

    [Theory]
    [InlineData("UFC.300.1080p.WEB-DL.x264-GROUP", "x264")]
    [InlineData("Fight.720p.BluRay.x265.AAC", "x265")]
    [InlineData("Event.1080p.WEB.H264", "x264")]
    [InlineData("Fight.2160p.HEVC.HDR", "x265")]
    [InlineData("Event.720p.h.265.10bit", "x265")]
    public void Parse_ShouldExtractVideoCodec(string filename, string expectedCodec)
    {
        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.VideoCodec.Should().Be(expectedCodec);
    }

    [Theory]
    [InlineData("UFC.300.1080p.WEB-DL.AAC2.0.x264", "AAC")]
    [InlineData("Fight.720p.BluRay.DTS-HD.MA.x265", "DTS-HD")]
    [InlineData("Event.1080p.WEB.DD5.1.x264", "DD")]
    [InlineData("Fight.2160p.BluRay.TrueHD.Atmos", "TRUEHD")]
    [InlineData("Event.720p.WEB-DL.E-AC-3", "E-AC-3")]
    public void Parse_ShouldExtractAudioCodec(string filename, string expectedAudio)
    {
        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.AudioCodec.Should().Be(expectedAudio);
    }

    [Theory]
    [InlineData("UFC.300.1080p.WEB-DL.x264-SPORTARR", "SPORTARR")]
    [InlineData("Fight.720p.BluRay.x265-SPARKS", "SPARKS")]
    [InlineData("Event.2160p.WEB.H264-NTb[rarbg]", "NTb")]
    public void Parse_ShouldExtractReleaseGroup(string filename, string expectedGroup)
    {
        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.ReleaseGroup.Should().Be(expectedGroup);
    }

    [Theory]
    [InlineData("UFC.300.2024.04.13.1080p.WEB-DL", 2024, 4, 13)]
    [InlineData("Fight.Night.2024-03-15.720p.HDTV", 2024, 3, 15)]
    [InlineData("Event.2024.01.01.1080p.BluRay", 2024, 1, 1)]
    public void Parse_ShouldExtractFullDate(string filename, int year, int month, int day)
    {
        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.AirDate.Should().NotBeNull();
        result.AirDate!.Value.Year.Should().Be(year);
        result.AirDate!.Value.Month.Should().Be(month);
        result.AirDate!.Value.Day.Should().Be(day);
    }

    [Theory]
    [InlineData("UFC 300 2024 1080p BluRay", 2024)]
    [InlineData("Fury vs Usyk 2023 720p WEB-DL", 2023)]
    public void Parse_ShouldExtractYearOnly(string filename, int expectedYear)
    {
        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.AirDate.Should().NotBeNull();
        result.AirDate!.Value.Year.Should().Be(expectedYear);
    }

    [Theory]
    [InlineData("UFC.300.PROPER.1080p.WEB-DL.x264")]
    [InlineData("Fight.REPACK.720p.BluRay.x265")]
    [InlineData("Event.REAL.1080p.HDTV.x264")]
    public void Parse_ShouldDetectProperOrRepack(string filename)
    {
        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.IsProperOrRepack.Should().BeTrue();
    }

    [Theory]
    [InlineData("UFC.300.EXTENDED.1080p.BluRay", "EXTENDED")]
    [InlineData("Fight.UNRATED.720p.WEB-DL", "UNRATED")]
    [InlineData("Event.DIRECTORS.CUT.1080p.BluRay", "DIRECTORS")]
    [InlineData("Fight.IMAX.2160p.WEB", "IMAX")]
    public void Parse_ShouldExtractEdition(string filename, string expectedEdition)
    {
        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.Edition.Should().Be(expectedEdition);
    }

    [Theory]
    [InlineData("UFC.300.MULTI.1080p.BluRay", "MULTI")]
    [InlineData("Fight.GERMAN.720p.WEB-DL", "GERMAN")]
    [InlineData("Event.DUAL.1080p.BluRay", "DUAL")]
    public void Parse_ShouldExtractLanguage(string filename, string expectedLanguage)
    {
        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.Language.Should().Be(expectedLanguage);
    }

    [Fact]
    public void Parse_ShouldHandleComplexFilename()
    {
        // Arrange
        var filename = "UFC.300.Main.Card.2024.04.13.EXTENDED.1080p.BluRay.x265.DTS-HD.MA.5.1-SPORTARR";

        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.EventTitle.Should().Be("UFC 300 Main Card");
        result.Resolution.Should().Be("1080P");
        result.Source.Should().Be("BLURAY");
        result.VideoCodec.Should().Be("x265");
        result.AudioCodec.Should().Be("DTS-HD");
        result.ReleaseGroup.Should().Be("SPORTARR");
        result.Edition.Should().Be("EXTENDED");
        result.AirDate.Should().NotBeNull();
        result.AirDate!.Value.Year.Should().Be(2024);
    }

    [Fact]
    public void BuildQualityString_ShouldCombineQualityInfo()
    {
        // Arrange
        var parsed = new ParsedFileInfo
        {
            EventTitle = "Test Event",
            Resolution = "1080P",
            Source = "BLURAY",
            VideoCodec = "x265",
            AudioCodec = "DTS",
            IsProperOrRepack = true
        };

        // Act
        var qualityString = _parser.BuildQualityString(parsed);

        // Assert
        qualityString.Should().Be("1080P BLURAY x265 DTS PROPER");
    }

    [Fact]
    public void BuildQualityString_ShouldReturnUnknown_WhenNoQualityInfo()
    {
        // Arrange
        var parsed = new ParsedFileInfo
        {
            EventTitle = "Test Event"
        };

        // Act
        var qualityString = _parser.BuildQualityString(parsed);

        // Assert
        qualityString.Should().Be("Unknown");
    }

    [Theory]
    [InlineData("file.with.dots.1080p.mkv")]
    [InlineData("file_with_underscores_720p.mp4")]
    [InlineData("file with spaces 1080p.avi")]
    public void Parse_ShouldHandleDifferentSeparators(string filename)
    {
        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.Should().NotBeNull();
        result.EventTitle.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Parse_ShouldNotThrowOnEmptyFilename()
    {
        // Act
        var result = _parser.Parse("");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void Parse_ShouldNotThrowOnInvalidFilename()
    {
        // Act
        var result = _parser.Parse("@#$%^&*()");

        // Assert
        result.Should().NotBeNull();
    }

    // ============================================================
    // Layer A: QualityParser fallback regression tests
    // Filenames where the primary MediaFileParser regex returns null
    // and the QualityParser fallback fills in the gap so files don't
    // end up as Unknown quality on import.
    // ============================================================

    [Theory]
    [InlineData("EPL.2026.05.02.Match.1920x1080.x264", "1080P")]
    [InlineData("Event.3840x2160.HEVC", "2160P")]
    [InlineData("Game.1280x720.x264", "720P")]
    public void Parse_ShouldExtractResolutionFromBareDimensions(string filename, string expectedResolution)
    {
        // Filenames with only the dimension form (no "1080p" / "720p" tokens)
        // would have returned null from the legacy regex; the QualityParser
        // fallback now catches them.
        var result = _parser.Parse(filename);

        result.Resolution.Should().Be(expectedResolution);
    }

    [Fact]
    public void Parse_ShouldExtractWebSource_WhenOnlyAmazonBrandedWebTokenPresent()
    {
        // No "WEB-DL" literal, no resolution token. Original regex's "WEB"
        // alternative would catch it, but only when "WEB" is unambiguous; for
        // brand-tagged variants like "Amazon.WEB" the QualityParser fallback
        // is more reliable.
        var result = _parser.Parse("EPL.match.Amazon.WEB.1080p.x264-GROUP");

        result.Source.Should().BeOneOf("WEBDL", "WEB");
    }

    // ============================================================
    // Layer B: Extension fallback regression tests
    // Filename has no quality keywords but the container extension is
    // itself a strong signal — .ts -> RAWHD (1080p HDTV bucket),
    // .iso/.bdmv -> Bluray-1080p, .avi/.wmv -> SDTV.
    // ============================================================

    [Theory]
    [InlineData("recording.ts", "1080P", "RAWHD")]
    [InlineData("game.m2ts", "1080P", "RAWHD")]
    public void Parse_ShouldFallBackToTsExtensionAsRawhd(string filename, string expectedRes, string expectedSource)
    {
        // QualityParser.ParseQualityFromExtension returns Quality.RAWHD for
        // .ts/.m2ts. RAWHD maps to (TelevisionRaw, R1080p), and the verbose
        // mapper emits TelevisionRaw as the "RAWHD" source string, so the
        // resulting Quality string is "1080P RAWHD".
        var result = _parser.Parse(filename);

        result.Resolution.Should().Be(expectedRes);
        result.Source.Should().Be(expectedSource);
    }

    [Theory]
    [InlineData("disc.iso", "1080P", "BLURAY")]
    [InlineData("BACKUP.bdmv", "1080P", "BLURAY")]
    public void Parse_ShouldFallBackToDiscExtensionAsBluray(string filename, string expectedRes, string expectedSource)
    {
        // .iso / .bdmv are nearly always BluRay rips at 1080p — safer default
        // than guessing 2160p, matches Sonarr's extension hint behavior.
        var result = _parser.Parse(filename);

        result.Resolution.Should().Be(expectedRes);
        result.Source.Should().Be(expectedSource);
    }

    [Theory]
    [InlineData("oldcap.avi")]
    [InlineData("legacy.wmv")]
    [InlineData("ancient.flv")]
    public void Parse_ShouldFallBackToLegacySdContainers(string filename)
    {
        // SDTV (R480p, Television) maps to "480P" + "HDTV" in verbose form.
        var result = _parser.Parse(filename);

        result.Resolution.Should().Be("480P");
        result.Source.Should().Be("HDTV");
    }

    [Fact]
    public void Parse_ShouldStillReturnUnknown_WhenNoSignalAtAll()
    {
        // .mkv has no extension hint defined, and a name with no tokens has
        // nothing for any tier to latch onto. The third tier (ffprobe) is the
        // last-resort augmenter and only runs via ParseWithInspectionAsync
        // with a real file path — covered separately.
        var result = _parser.Parse("randomname.mkv");

        result.Resolution.Should().BeNull();
        result.Source.Should().BeNull();
    }

    // ============================================================
    // Layer C: ParseWithInspectionAsync pipeline behavior
    // The inspector is optional. Without it, the method must degrade
    // gracefully and just return the regex-only Parse() result.
    // ============================================================

    [Fact]
    public async Task ParseWithInspectionAsync_ShouldReturnRegexResultWhenFilePathNull()
    {
        // No file path => no ffprobe call. Result must equal Parse(filename).
        var result = await _parser.ParseWithInspectionAsync(
            "EPL.2026.05.02.Match.1080p.WEB-DL.x264-GROUP",
            filePath: null);

        result.Resolution.Should().Be("1080P");
        result.Source.Should().Be("WEBDL");
    }

    [Fact]
    public async Task ParseWithInspectionAsync_ShouldReturnRegexResultWhenFilePathDoesNotExist()
    {
        // Non-existent path => the inspector is checked, returns null, and the
        // method gracefully degrades to Parse() output. No exception thrown.
        var result = await _parser.ParseWithInspectionAsync(
            "EPL.2026.05.02.Match.1080p.WEB-DL.x264-GROUP",
            filePath: "/non/existent/path.mkv");

        result.Resolution.Should().Be("1080P");
        result.Source.Should().Be("WEBDL");
    }

    [Fact]
    public async Task ParseWithInspectionAsync_ShouldNotThrow_WhenFilenameYieldsNothing()
    {
        // No regex hit, no file path — nothing for any tier to latch onto.
        // Must complete without throwing and just return whatever the regex
        // tier produced (Unknown / null).
        var result = await _parser.ParseWithInspectionAsync("randomname.mkv", filePath: null);

        result.Should().NotBeNull();
    }

    // ============================================================
    // Release-group rejection: trailing tokens that are actually
    // quality / resolution / source / codec markers must NOT be
    // returned as the release group. The naive regex captures the
    // last "-XXX" run, so files like "Show.WEBDL-2160p" fall through
    // to "2160p" without these guards.
    // ============================================================

    [Theory]
    [InlineData("Formula 1 - S2015E03 - Malaysian Grand Prix Qualifying - WEBDL-2160p")]
    [InlineData("EPL.match.1080p.WEB-DL")]
    [InlineData("Show.S01E01.HDTV-720p")]
    [InlineData("Game.S2025E01.BluRay-2160p")]
    [InlineData("Show.S01E01.x265")]
    [InlineData("Show.S01E01.HEVC")]
    [InlineData("Show.S01E01.AAC")]
    public void Parse_ShouldNotReturnQualityTokenAsReleaseGroup(string filename)
    {
        var result = _parser.Parse(filename);

        result.ReleaseGroup.Should().BeNull(
            because: "trailing quality / resolution / codec / audio tokens are not release groups");
    }

    [Theory]
    [InlineData("EPL.2026.05.02.Arsenal.vs.Fulham.1080p.WEB.H264-BILLIE", "BILLIE")]
    [InlineData("UFC.300.2024.04.13.1080p.WEB-DL.x264-DARKSPORT", "DARKSPORT")]
    [InlineData("Show.S01E01.1080p.WEB.x264-NTb", "NTb")]
    [InlineData("Game.S2025E01.WEBDL-1080p-FLUX", "FLUX")]
    public void Parse_ShouldExtractGenuineReleaseGroups(string filename, string expectedGroup)
    {
        var result = _parser.Parse(filename);

        result.ReleaseGroup.Should().Be(expectedGroup);
    }
}
