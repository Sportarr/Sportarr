using Sportarr.Api.Services;
using FluentAssertions;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Regression for issue #461. Since 2023 a MotoGP weekend holds two practice events,
/// "Free Practice 1" and a separate "Practice". A bare "Practice" used to read as Practice 1,
/// so one release matched both events, one session never downloaded and the same video was
/// imported twice. Other leagues keep reading bare "Practice" as Practice 1.
/// </summary>
public class MotoGpBarePracticeTests
{
    private const string Fp1Release = "MotoGP.2026.Round01.Thailand.FP1.WEB-DL.1080p.H264.English-MWR";
    private const string PracticeRelease = "MotoGP.2026.Round02.Brazil.Practice.WEB-DL.1080p.H264.English-MWR";

    [Theory]
    [InlineData("Thailand Practice", "Practice")]
    [InlineData("Thailand - Free Practice 1", "Practice 1")]
    [InlineData("Brazil Practice", "Practice")]
    [InlineData("Brazil - Free Practice 2", "Practice 2")]
    public void EventTitle_KeepsPracticeAndFreePractice1Apart(string title, string expected)
    {
        EventPartDetector.DetectMotorsportSessionIdentity(title, "MotoGP", releaseTitle: false)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(Fp1Release, "Practice 1")]
    [InlineData(PracticeRelease, "Practice")]
    public void ReleaseName_KeepsPracticeAndFp1Apart(string release, string expected)
    {
        EventPartDetector.DetectMotorsportSessionIdentity(release, "MotoGP", releaseTitle: true)
            .Should().Be(expected);
    }

    [Fact]
    public void PracticeRelease_DoesNotMatchTheFreePractice1Event()
    {
        var release = EventPartDetector.DetectMotorsportSessionIdentity(PracticeRelease, "MotoGP", releaseTitle: true);
        var fp1Event = EventPartDetector.DetectMotorsportSessionIdentity("Brazil - Free Practice 1", "MotoGP", releaseTitle: false);

        EventPartDetector.NormalizeMotorsportSession(release, keepBarePractice: true)
            .Should().NotBe(EventPartDetector.NormalizeMotorsportSession(fp1Event, keepBarePractice: true));
    }

    [Fact]
    public void Fp1Release_DoesNotMatchThePracticeEvent()
    {
        var release = EventPartDetector.DetectMotorsportSessionIdentity(Fp1Release, "MotoGP", releaseTitle: true);
        var practiceEvent = EventPartDetector.DetectMotorsportSessionIdentity("Thailand Practice", "MotoGP", releaseTitle: false);

        EventPartDetector.NormalizeMotorsportSession(release, keepBarePractice: true)
            .Should().NotBe(EventPartDetector.NormalizeMotorsportSession(practiceEvent, keepBarePractice: true));
    }

    [Fact]
    public void Normalize_FoldsBarePracticeIntoPractice1ByDefault()
    {
        EventPartDetector.NormalizeMotorsportSession("Practice").Should().Be("Practice 1");
        EventPartDetector.NormalizeMotorsportSession("Practice", keepBarePractice: true).Should().Be("Practice");
        EventPartDetector.NormalizeMotorsportSession("Practice 1", keepBarePractice: true).Should().Be("Practice 1");
    }

    [Fact]
    public void Formula1_StillReadsBarePracticeAsPractice1()
    {
        EventPartDetector.DetectMotorsportSessionIdentity("Thailand Grand Prix Practice", "Formula 1", releaseTitle: false)
            .Should().Be("Practice 1");
    }
}
