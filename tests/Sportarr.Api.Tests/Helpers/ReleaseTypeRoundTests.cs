using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// A round number alone was read as a pack, but a motorsport weekend ships one
/// file per session and every one carries the round number too, so single
/// sessions were classified as packs and scored against the wrong rules.
/// </summary>
public class ReleaseTypeRoundTests
{
    [Theory]
    [InlineData("Formula1.2026.Round05.Belgian.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("MotoGP.2026.Round.11.Qualifying.1080p.WEB.h264")]
    [InlineData("Formula1.2026.Round13.Sprint.Shootout.1080p")]
    [InlineData("WRC.2026.Round.04.Shakedown.1080p")]
    public void One_named_session_with_a_round_number_is_a_single_event(string title)
    {
        ReleaseTypeDetector.Detect(title).Should().Be(ReleaseType.SingleEvent);
    }

    [Theory]
    [InlineData("Premier.League.2025-26.Matchday.32.Match.Pack.1080p")]
    [InlineData("NFL.2026.Week.5.Complete.1080p")]
    public void An_explicit_pack_is_still_a_pack(string title)
    {
        ReleaseTypeDetector.Detect(title).Should().Be(ReleaseType.Pack);
    }

    [Fact]
    public void A_bare_round_number_with_no_session_named_stays_a_pack()
    {
        ReleaseTypeDetector.Detect("Premier.League.2026.Matchday.32.1080p")
            .Should().Be(ReleaseType.Pack);
    }
}
