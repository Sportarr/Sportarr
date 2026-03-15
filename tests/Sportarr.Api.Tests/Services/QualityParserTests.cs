using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public  class QualityParserTests
{
    [Theory]
    //common release names
    [InlineData("Some.Show.S01E06.480p.BluRay.X264-iNGOT", "Bluray-480p")]
    [InlineData("Some.Show.S01E06.480p.WEBDL.X264-iNGOT", "WEBDL-480p")]
    [InlineData("Some.Show.S01E06.480p.WEBRIP.X264-iNGOT", "WEBRip-480p")]
    [InlineData("Some.Show.S01E06.720p.BluRay.X264-iNGOT", "Bluray-720p")]
    [InlineData("Some.Show.S01E06.720p.WEBDL.X264-iNGOT", "WEBDL-720p")]
    [InlineData("Some.Show.S01E06.720p.WEBRIP.X264-iNGOT", "WEBRip-720p")]
    [InlineData("Some.Show.S01E06.1080p.BluRay.X264-iNGOT", "Bluray-1080p")]
    [InlineData("Some.Show.S01E06.1080p.WEBDL.X264-iNGOT", "WEBDL-1080p")]
    [InlineData("Some.Show.S01E06.1080p.WEBRIP.X264-iNGOT", "WEBRip-1080p")]
    [InlineData("Some.Show.S01E06.2160p.BluRay.X264-iNGOT", "Bluray-2160p")]
    [InlineData("Some.Show.S01E06.2160p.WEBDL.X264-iNGOT", "WEBDL-2160p")]
    [InlineData("Some.Show.S01E06.2160p.WEBRIP.X264-iNGOT", "WEBRip-2160p")]
    //release names with fps info
    [InlineData("Some.Show.S01E06.480p50.BluRay.X264-iNGOT", "Bluray-480p")]
    [InlineData("Some.Show.S01E06.480p60.BluRay.X264-iNGOT", "Bluray-480p")]
    [InlineData("Some.Show.S01E06.480pEN60fps.BluRay.X264-iNGOT", "Bluray-480p")]
    [InlineData("Some.Show.S01E06.480p50fps.BluRay.X264-iNGOT", "Bluray-480p")]
    [InlineData("Some.Show.S01E06.4K50fps.BluRay.X264-iNGOT", "Bluray-2160p")]
    [InlineData("Guinness.Men s.Six.Nations.Rugby.14-03-2026.Ireland.vs.Scotland.1080p50.HDTV.x264.24-bit.WAV-CREATiVE24", "HDTV-1080p")]
    //release names with WEB only in the name (NOT WEBDL OR WEBRip)
    [InlineData("Some.Show.S01E06.480p.WEB.X264-iNGOT", "WEBDL-480p")]
    [InlineData("Some.Show.S01E06.720p.WEB.X264-iNGOT", "WEBDL-720p")]
    [InlineData("Some.Show.S01E06.1080p.WEB.X264-iNGOT", "WEBDL-1080p")]
    [InlineData("Some.Show.S01E06.2160p.WEB.X264-iNGOT", "WEBDL-2160p")]
    [InlineData("Some.Show.S01E06.4K.WEB.X264-iNGOT", "WEBDL-2160p")]
    public void QualityNames_ShouldBeParsedCorrectly(string releaseName, string expectedQuality)
    {
        //Act
        var qualityModel = QualityParser.ParseQuality(releaseName);

        //Assert
        qualityModel.QualityName.Should().Be(expectedQuality);
    }
}
