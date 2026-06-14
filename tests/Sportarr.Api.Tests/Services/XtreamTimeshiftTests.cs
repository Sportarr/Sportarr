using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Tests for the Xtream catchup/timeshift URL builder and the stream-id
/// parser that CatchupDownloadService uses to recover the id from a
/// channel's stored /live/ URL. Both are pure static functions on
/// XtreamCodesClient, so no DI or HTTP is needed.
///
/// Catchup/timeshift download method ported from timeshifter by
/// scottrobertson (github.com/scottrobertson/timeshifter).
/// </summary>
public class XtreamTimeshiftTests
{
    [Fact]
    public void BuildTimeshiftUrl_PathMode_ShouldUseTimeshiftPathFormat()
    {
        var url = XtreamCodesClient.BuildTimeshiftUrl(
            "http://provider.example:8080", "user", "pass",
            streamId: 12345,
            serverLocalStart: new DateTime(2026, 6, 11, 19, 55, 0),
            durationMinutes: 215);

        url.Should().Be("http://provider.example:8080/timeshift/user/pass/215/2026-06-11:19-55/12345.ts");
    }

    [Fact]
    public void BuildTimeshiftUrl_PhpMode_ShouldUseQueryParameters()
    {
        var url = XtreamCodesClient.BuildTimeshiftUrl(
            "http://provider.example:8080/", "user", "pass",
            streamId: 12345,
            serverLocalStart: new DateTime(2026, 6, 11, 19, 55, 0),
            durationMinutes: 215,
            phpMode: true);

        url.Should().StartWith("http://provider.example:8080/streaming/timeshift.php?");
        url.Should().Contain("username=user");
        url.Should().Contain("password=pass");
        url.Should().Contain("stream=12345");
        url.Should().Contain("duration=215");
        // ':' in the start segment is escaped in query-parameter form.
        url.Should().Contain("start=2026-06-11%3A19-55");
    }

    [Fact]
    public void BuildTimeshiftUrl_ShouldEscapeCredentials()
    {
        var url = XtreamCodesClient.BuildTimeshiftUrl(
            "http://provider.example:8080", "user@mail", "p&ss",
            streamId: 7,
            serverLocalStart: new DateTime(2026, 1, 2, 3, 4, 0),
            durationMinutes: 60);

        url.Should().Contain("/timeshift/user%40mail/p%26ss/60/2026-01-02:03-04/7.ts");
    }

    [Fact]
    public void BuildTimeshiftUrl_ShouldZeroPadDateAndTime()
    {
        var url = XtreamCodesClient.BuildTimeshiftUrl(
            "http://provider.example:8080", "u", "p",
            streamId: 1,
            serverLocalStart: new DateTime(2026, 3, 5, 8, 5, 0),
            durationMinutes: 90);

        url.Should().Contain("/2026-03-05:08-05/");
    }

    [Theory]
    [InlineData("http://provider.example:8080/live/user/pass/12345.ts", 12345)]
    [InlineData("http://provider.example:8080/live/user/pass/7.m3u8", 7)]
    [InlineData("https://cdn.example/live/u%40m/p/99.ts", 99)]
    public void TryParseStreamId_ShouldRecoverIdFromLiveUrl(string streamUrl, int expected)
    {
        XtreamCodesClient.TryParseStreamId(streamUrl, out var id).Should().BeTrue();
        id.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://provider.example/playlist.m3u8")] // no numeric segment
    [InlineData("http://provider.example/live/user/pass/")] // trailing slash, no id
    [InlineData("not a url")]
    public void TryParseStreamId_ShouldRejectNonXtreamShapes(string? streamUrl)
    {
        XtreamCodesClient.TryParseStreamId(streamUrl, out _).Should().BeFalse();
    }

    // --- timeshift URL style resolution (auto-detection order) ---

    [Theory]
    [InlineData("path", null, new[] { "path" })]
    [InlineData("php", null, new[] { "php" })]
    [InlineData("path", "php", new[] { "path" })] // explicit setting overrides detection
    public void TimeshiftModesToTry_ExplicitSetting_ShouldPinSingleStyle(
        string configMode, string? detected, string[] expected)
    {
        CatchupDownloadService.TimeshiftModesToTry(configMode, detected).Should().Equal(expected);
    }

    [Theory]
    [InlineData(null)] // unset config behaves like auto
    [InlineData("auto")]
    [InlineData("AUTO")]
    public void TimeshiftModesToTry_Auto_NoDetection_ShouldTryPathThenPhp(string? configMode)
    {
        CatchupDownloadService.TimeshiftModesToTry(configMode, null).Should().Equal("path", "php");
    }

    [Fact]
    public void TimeshiftModesToTry_Auto_ShouldLeadWithDetectedStyleAndKeepFallback()
    {
        // Detected style first, but the other stays as fallback so a
        // stale detection self-heals instead of hard-failing.
        CatchupDownloadService.TimeshiftModesToTry("auto", "php").Should().Equal("php", "path");
        CatchupDownloadService.TimeshiftModesToTry("auto", "path").Should().Equal("path", "php");
    }
}
