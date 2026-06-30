using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Regression for the 2026 Austrian GP duplicate-download incident: word-ordinal sessions
/// ("Practice Three") must resolve to "Practice 3", not collapse to "Practice 1". When they
/// don't, the release fails to match its event, the event stays missing, and it is re-grabbed
/// every RSS cycle. The match gate (EventPartDetector) already handled this; the import-side
/// parser (SportsFileNameParser) did not — both are pinned here.
/// </summary>
public class AustrianGpPracticeThreeTests
{
    private const string P3Billie = "Formula1.2026.Austrian.Grand.Prix.Practice.Three.1080p.WEB.h264-BILLIE";
    private const string P3Darksport = "Formula1.2026.Austrian.Grand.Prix.Practice.Three.1080p.AHDTV.x264-DARKSPORT";

    [Theory]
    [InlineData(P3Billie, "Practice 3")]
    [InlineData(P3Darksport, "Practice 3")]
    [InlineData("Formula1.2026.Austrian.Grand.Prix.Practice.Two.1080p.WEB.h264-BILLIE", "Practice 2")]
    [InlineData("Formula1.2026.Austrian.Grand.Prix.Practice.One.1080p.WEB.h264-BILLIE", "Practice 1")]
    public void MatchGate_DetectsWordOrdinalSession(string filename, string expected)
    {
        EventPartDetector.DetectMotorsportSessionFromFilename(filename)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(P3Billie, "Practice 3")]
    [InlineData("Formula1.2026.Austrian.Grand.Prix.Practice.Two.1080p.WEB.h264-BILLIE", "Practice 2")]
    [InlineData("Formula1.2026.Austrian.Grand.Prix.Practice.One.1080p.WEB.h264-BILLIE", "Practice 1")]
    public void ImportParser_DetectsWordOrdinalSession(string filename, string expected)
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        parser.Parse(filename).Session.Should().Be(expected);
    }
}
