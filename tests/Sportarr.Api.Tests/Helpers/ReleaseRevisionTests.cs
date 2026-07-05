using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

public class ReleaseRevisionTests
{
    [Theory]
    [InlineData("UFC.300.PROPER.1080p.WEB.h264-GRP", 1)]
    [InlineData("UFC.300.proper.1080p.WEB.h264-GRP", 1)]
    [InlineData("NBA.2026.01.01.REAL.720p.HDTV.x264", 1)]
    [InlineData("EPL.2026.Round.30.REPACK.1080p.WEB", 2)]
    [InlineData("EPL.2026.Round.30.RERIP.1080p.WEB", 2)]
    public void RevisionMarkers_AreRanked(string title, int expected)
    {
        ReleaseRevision.Parse(title).Should().Be(expected);
    }

    [Theory]
    [InlineData("La.Liga.Real.Madrid.vs.Barcelona.1080p.WEB")]
    [InlineData("MLS.Real.Salt.Lake.vs.LAFC.720p.HDTV")]
    [InlineData("UFC.300.1080p.WEB.h264-GRP")]
    [InlineData("")]
    [InlineData(null)]
    public void TeamNamesAndPlainReleases_AreNotRevisions(string? title)
    {
        ReleaseRevision.Parse(title).Should().Be(0);
    }

    [Fact]
    public void RepackOutranksProper()
    {
        ReleaseRevision.Parse("X.REPACK.1080p").Should()
            .BeGreaterThan(ReleaseRevision.Parse("X.PROPER.1080p"));
    }
}
