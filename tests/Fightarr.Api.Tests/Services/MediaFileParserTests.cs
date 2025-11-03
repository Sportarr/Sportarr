using Fightarr.Api.Services;
using Fightarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Fightarr.Api.Tests.Services;

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
    [InlineData("UFC.300.1080p.WEB-DL.x264-FIGHTARR", "FIGHTARR")]
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
        var filename = "UFC.300.Main.Card.2024.04.13.EXTENDED.1080p.BluRay.x265.DTS-HD.MA.5.1-FIGHTARR";

        // Act
        var result = _parser.Parse(filename);

        // Assert
        result.EventTitle.Should().Be("UFC 300 Main Card");
        result.Resolution.Should().Be("1080P");
        result.Source.Should().Be("BLURAY");
        result.VideoCodec.Should().Be("x265");
        result.AudioCodec.Should().Be("DTS-HD");
        result.ReleaseGroup.Should().Be("FIGHTARR");
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
        var parsed = new ParsedFileInfo();

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
}
