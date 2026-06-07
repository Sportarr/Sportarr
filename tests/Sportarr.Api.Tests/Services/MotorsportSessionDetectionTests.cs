using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for non-English motorsport session detection. Field reports showed
/// French-language releases ("Essais qualificatifs" = Qualifying, "La Course" =
/// Race) parsing to an unknown session, which let the matcher fall back to
/// permissive behaviour and grab the wrong event. The vocabulary lives in one
/// shared table (EventPartDetector.MultilingualSessionPatterns) consumed by both
/// matchers; these tests pin the French terms and guard the English path against
/// regression.
/// </summary>
public class MotorsportSessionDetectionTests
{
    // --- French (the reported cases) ---

    [Fact]
    public void FrenchQualifying_IsDetectedAsQualifying()
    {
        // The exact reported release that was mis-grabbed as "Dutch GP Practice 1".
        var filename = "Formula.1.1950.S2026E38.Monaco.Essais.qualificatifs.2026-06-06.FRENCH.1080p.WEB.x264-THESYNDiCATE";

        EventPartDetector.DetectMotorsportSessionFromFilename(filename)
            .Should().Be("Qualifying");
    }

    [Theory]
    [InlineData("Formula1.2026.Monaco.Essais.Libres.1.FRENCH.1080p", "Practice 1")]
    [InlineData("Formula1.2026.Monaco.Essais.Libres.2.FRENCH.1080p", "Practice 2")]
    [InlineData("Formula1.2026.Monaco.Essais.Libres.3.FRENCH.1080p", "Practice 3")]
    [InlineData("Formula1.2026.Monaco.Essais.Libres.FRENCH.1080p", "Practice 1")] // bare -> Practice 1
    [InlineData("Formula1.2026.Monaco.Qualifications.FRENCH.1080p", "Qualifying")]
    [InlineData("Formula1.2026.Monaco.La.Course.FRENCH.1080p", "Race")]
    [InlineData("Formula1.2026.Monaco.Course.Sprint.FRENCH.1080p", "Sprint")]
    public void FrenchSessions_AreDetected(string filename, string expected)
    {
        EventPartDetector.DetectMotorsportSessionFromFilename(filename)
            .Should().Be(expected);
    }

    // --- English regression guard (must keep working unchanged) ---

    [Theory]
    [InlineData("Formula1.2025.Abu.Dhabi.FP1.1080p-GROUP", "Practice 1")]
    [InlineData("Formula1.2025.Abu.Dhabi.FP3.1080p-GROUP", "Practice 3")]
    [InlineData("Formula1.2025.Monaco.Qualifying.1080p-GROUP", "Qualifying")]
    [InlineData("Formula1.2025.Monaco.Sprint.Qualifying.1080p-GROUP", "Sprint Qualifying")]
    public void EnglishSessions_StillDetected(string filename, string expected)
    {
        EventPartDetector.DetectMotorsportSessionFromFilename(filename)
            .Should().Be(expected);
    }

    [Fact]
    public void NonSessionFrenchText_ReturnsNull()
    {
        // No session vocabulary present — should not be force-mapped to anything.
        EventPartDetector.DetectMultilingualSession("formula 1 2026 monaco 1080p web")
            .Should().BeNull();
    }
}
