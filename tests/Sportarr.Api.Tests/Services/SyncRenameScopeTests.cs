using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The league sync renames files on its own only when their season or
/// episode marker no longer matches. A naming format change is left to a
/// manual rename. An edit to the format used to rewrite every file in a
/// library the moment it was saved.
/// </summary>
public class SyncRenameScopeTests
{
    [Theory]
    [InlineData("/m/EPL/Season 2026/EPL - S2026E13 - Bournemouth vs Everton - HDTV-1080p - DARKFLiX.mkv",
                "/m/EPL/Season 2026/EPL - S2026E13 - Bournemouth vs Everton - HDTV-1080p - sportarr-ev-2301113 - DARKFLiX.mkv")]
    [InlineData("/m/F1/Season 2026/F1 - S2026E12 - British Grand Prix - Race.mkv",
                "/m/F1/Season 2026/Formula 1 - s2026e12 - British Grand Prix - Race - WEBDL-1080p.mkv")]
    [InlineData("/m/EPL/Season 2026/EPL - S2026.E13 - Bournemouth vs Everton.mkv",
                "/m/EPL/Season 2026/EPL - S2026.E13 - Bournemouth vs Everton - sportarr-ev-2301113.mkv")]
    public void A_format_change_alone_is_not_a_numbering_change(string current, string expected)
    {
        FileRenameService.NumberingChanged(current, expected).Should().BeFalse();
    }

    [Theory]
    [InlineData("/m/MLB/Season 2026/MLB - S2026E276 - Newcastle vs Everton.mkv",
                "/m/MLB/Season 2026/MLB - S2026E275 - Newcastle vs Everton.mkv")]
    [InlineData("/m/EPL/Season 2025/EPL - S2025E13 - Bournemouth vs Everton.mkv",
                "/m/EPL/Season 2026/EPL - S2026E13 - Bournemouth vs Everton.mkv")]
    [InlineData("/m/EPL/Season 2026/Bournemouth vs Everton.mkv",
                "/m/EPL/Season 2026/EPL - S2026E13 - Bournemouth vs Everton.mkv")]
    [InlineData("/m/EPL/Season 2026/EPL - S2026.E12 - Bournemouth vs Everton.mkv",
                "/m/EPL/Season 2026/EPL - S2026 E13 - Bournemouth vs Everton.mkv")]
    [InlineData("/m/EPL/Season 2026/EPL - S2026 - E12 - Bournemouth vs Everton.mkv",
                "/m/EPL/Season 2026/EPL - S2026 - E13 - Bournemouth vs Everton.mkv")]
    public void A_renumbered_or_refiled_file_is_renamed(string current, string expected)
    {
        FileRenameService.NumberingChanged(current, expected).Should().BeTrue();
    }

    [Fact]
    public void A_format_without_a_marker_cannot_signal_a_numbering_change()
    {
        FileRenameService.NumberingChanged("/m/UFC/UFC 300 - S2026E04.mkv", "/m/UFC/UFC 300 - Bluray-1080p.mkv")
            .Should().BeFalse();
    }
}
