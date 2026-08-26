using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// Playlist references were resolved by hand from the scheme, host and a
/// guessed port, which produced upstream URLs that could not be fetched.
/// </summary>
public class HlsRewriterTests
{
    private static string ProxiedTarget(string rewrittenLine)
    {
        var marker = "?url=";
        var start = rewrittenLine.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the line should have been rewritten to a proxy URL");
        var tail = rewrittenLine[(start + marker.Length)..];
        // On a tag line the proxy URL sits inside quotes.
        var quote = tail.IndexOf('"');
        if (quote >= 0) tail = tail[..quote];
        return Uri.UnescapeDataString(tail);
    }

    [Fact]
    public void A_protocol_relative_segment_keeps_its_own_host()
    {
        var playlist = "#EXTM3U\n#EXTINF:6,\n//cdn.example.net/live/seg1.ts\n";
        var rewritten = HlsRewriter.RewritePlaylist(playlist, new Uri("https://provider.example/live/index.m3u8"));

        ProxiedTarget(rewritten.Split('\n')[2])
            .Should().Be("https://cdn.example.net/live/seg1.ts");
    }

    [Fact]
    public void A_non_standard_port_survives_a_root_relative_segment()
    {
        var playlist = "#EXTM3U\n#EXTINF:6,\n/live/seg1.ts\n";
        var rewritten = HlsRewriter.RewritePlaylist(playlist, new Uri("http://provider.example:8443/hls/index.m3u8"));

        ProxiedTarget(rewritten.Split('\n')[2])
            .Should().Be("http://provider.example:8443/live/seg1.ts");
    }

    [Fact]
    public void A_dot_segment_reference_is_normalised()
    {
        var playlist = "#EXTM3U\n#EXTINF:6,\n../segments/seg1.ts\n";
        var rewritten = HlsRewriter.RewritePlaylist(playlist, new Uri("https://provider.example/hls/live/index.m3u8"));

        ProxiedTarget(rewritten.Split('\n')[2])
            .Should().Be("https://provider.example/hls/segments/seg1.ts");
    }

    [Fact]
    public void A_key_uri_resolves_the_same_way()
    {
        var playlist = "#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"//keys.example.net/k1\"\n#EXTINF:6,\nseg1.ts\n";
        var rewritten = HlsRewriter.RewritePlaylist(playlist, new Uri("https://provider.example/hls/index.m3u8"));

        ProxiedTarget(rewritten.Split('\n')[1])
            .Should().Be("https://keys.example.net/k1");
    }

    [Fact]
    public void An_absolute_segment_is_left_pointing_where_it_pointed()
    {
        var playlist = "#EXTM3U\n#EXTINF:6,\nhttps://other.example/a/seg1.ts\n";
        var rewritten = HlsRewriter.RewritePlaylist(playlist, new Uri("https://provider.example/hls/index.m3u8"));

        ProxiedTarget(rewritten.Split('\n')[2])
            .Should().Be("https://other.example/a/seg1.ts");
    }
}
