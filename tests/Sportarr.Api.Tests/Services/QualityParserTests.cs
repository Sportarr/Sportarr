using FluentAssertions;
using Sportarr.Api.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sportarr.Api.Tests.Services;

public  class QualityParserTests
{
    //Guinness.Men s.Six.Nations.Rugby.14-03-2026.Ireland.vs.Scotland.1080p50.HDTV.x264.24-bit.WAV-CREATiVE24

    [Theory]
    [InlineData("Breaking.Bad.S01E06.480p.BluRay.X264-iNGOT", "Bluray-480p")]
    [InlineData("Breaking.Bad.S01E06.480p.WEBDL.X264-iNGOT", "WEBDL-480p")]
    [InlineData("Breaking.Bad.S01E06.480p.WEBRIP.X264-iNGOT", "WEBRip-480p")]
    [InlineData("Breaking.Bad.S01E06.720p.BluRay.X264-iNGOT", "Bluray-720p")]
    [InlineData("Breaking.Bad.S01E06.720p.WEBDL.X264-iNGOT", "WEBDL-720p")]
    [InlineData("Breaking.Bad.S01E06.720p.WEBRIP.X264-iNGOT", "WEBRip-720p")]
    [InlineData("Breaking.Bad.S01E06.1080p.BluRay.X264-iNGOT", "Bluray-1080p")]
    [InlineData("Breaking.Bad.S01E06.1080p.WEBDL.X264-iNGOT", "WEBDL-1080p")]
    [InlineData("Breaking.Bad.S01E06.1080p.WEBRIP.X264-iNGOT", "WEBRip-1080p")]
    [InlineData("Breaking.Bad.S01E06.2160p.BluRay.X264-iNGOT", "Bluray-2160p")]
    [InlineData("Breaking.Bad.S01E06.2160p.WEBDL.X264-iNGOT", "WEBDL-2160p")]
    [InlineData("Breaking.Bad.S01E06.2160p.WEBRIP.X264-iNGOT", "WEBRip-2160p")]
    public void NormalQualityNames_ShouldBeParsedCorrectly(string releaseName, string expectedQuality)
    {
        //Act
        var qualityModel = QualityParser.ParseQuality(releaseName);

        //Assert
        qualityModel.QualityName.Should().Be(expectedQuality);
    }
}
