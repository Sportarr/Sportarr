using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class QualityParserTests
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
    [InlineData("Some.Show.S01E06.1080p60.BluRay.X264-iNGOT", "Bluray-1080p")]
    [InlineData("Some.Show.S01E06.1080pEN60fps.BluRay.X264-iNGOT", "Bluray-1080p")]
    [InlineData("Guinness.Men s.Six.Nations.Rugby.14-03-2026.Ireland.vs.Scotland.1080p50.HDTV.x264.24-bit.WAV-CREATiVE24", "HDTV-1080p")]
    //streaming-service capture tags - MLB.TV per the tracker release naming
    //standard; without the indicator these fell back to HDTV-1080p and lost
    //profile scoring against explicitly WEB-DL-tagged competitors
    [InlineData("MLB.2026.07.12.New.York.Yankees.vs.Washington.Nationals.ev-483957.MLBTV.1080p.HFR.AAC.2.0.H.264-UMBR3LLA", "WEBDL-1080p")]
    [InlineData("MLB.2026.07.12.Athletics.vs.Chicago.White.Sox.MLB.TV.720p.AAC.2.0.H.264-GRP", "WEBDL-720p")]
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
