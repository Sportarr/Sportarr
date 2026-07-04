using System.Net;
using System.Text;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Regression coverage for issue #135: a previous implementation of
/// AddTorrentAsync had no way to know rTorrent's hash for a just-added
/// torrent (rTorrent's load.* methods don't echo one back), so it guessed
/// by picking whichever torrent in rTorrent's ENTIRE list had the most
/// recent TimeAdded - with no check that it was actually the one just
/// submitted. On a shared rTorrent instance (another *arr app, manual
/// activity), this could return a completely unrelated pre-existing
/// torrent's hash, which Sportarr would then track and import as if it
/// were the requested release (the reported case: an F1 race grab ended up
/// importing an unrelated "Below Deck" episode).
///
/// The fix (AddTorrentWithHashAsync) computes the hash client-side before
/// submitting, then polls rTorrent for that EXACT hash before returning
/// success - and returns null (refusing to track anything) if it can't be
/// confirmed, rather than falling back to a guess. These tests pin that
/// contract using a fake HTTP handler standing in for rTorrent's XML-RPC
/// endpoint.
/// </summary>
public class RTorrentClientAddTorrentTests
{
    private const string ExpectedHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string UnrelatedHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private static DownloadClient MakeConfig() => new()
    {
        Name = "Test rTorrent",
        Type = DownloadClientType.RTorrent,
        Host = "localhost",
        Port = 8080,
    };

    private class FakeRTorrentHandler : HttpMessageHandler
    {
        private readonly Func<string, string> _multicallResponder;

        public FakeRTorrentHandler(Func<string, string> multicallResponder)
        {
            _multicallResponder = multicallResponder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            string responseXml;

            if (body.Contains("d.multicall2"))
            {
                responseXml = _multicallResponder(body);
            }
            else
            {
                // load.raw_start / load.raw / load.start / load.normal - any
                // non-null body means "the add command was accepted".
                responseXml = "<?xml version=\"1.0\"?><methodResponse><params><param><value><i4>0</i4></value></param></params></methodResponse>";
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseXml, Encoding.UTF8, "text/xml")
            };
        }
    }

    private static string BuildMulticallResponse(params (string hash, string name)[] torrents)
    {
        var entries = string.Join("", torrents.Select(t => $@"
                <value><array><data>
                    <value><string>{t.hash}</string></value>
                    <value><string>{t.name}</string></value>
                    <value><i8>0</i8></value>
                    <value><i8>0</i8></value>
                    <value><i8>0</i8></value>
                    <value><i4>1</i4></value>
                    <value><i8>0</i8></value>
                    <value><i8>0</i8></value>
                    <value><string>/downloads</string></value>
                    <value><string>sportarr</string></value>
                    <value><i8>0</i8></value>
                </data></array></value>"));

        return $@"<?xml version=""1.0""?>
<methodResponse><params><param><value><array><data>{entries}
</data></array></value></param></params></methodResponse>";
    }

    private static RTorrentClient CreateClient(Func<string, string> multicallResponder)
    {
        var httpClient = new HttpClient(new FakeRTorrentHandler(multicallResponder));
        return new RTorrentClient(httpClient, NullLogger<RTorrentClient>.Instance);
    }

    [Fact]
    public async Task ReturnsConfirmedHash_WhenRTorrentRegistersTheExpectedHash()
    {
        var client = CreateClient(_ => BuildMulticallResponse((ExpectedHash, "Formula1.2026.Round04.USA.Miami.Race")));

        var result = await client.AddTorrentWithHashAsync(
            MakeConfig(), torrentBytes: new byte[] { 1, 2, 3 }, magnetUrl: null, knownHash: ExpectedHash, category: "sportarr");

        result.Should().Be(ExpectedHash);
    }

    [Fact]
    public async Task ReturnsNull_WhenOnlyAnUnrelatedTorrentIsPresent()
    {
        // The exact failure mode from issue #135: rTorrent's torrent list
        // contains a completely unrelated pre-existing download (different
        // hash) and never registers the one Sportarr just submitted. The
        // client must refuse to track anything rather than falling back to
        // whatever else happens to be in the list.
        var client = CreateClient(_ => BuildMulticallResponse((UnrelatedHash, "Below.Deck.Down.Under.S04E14.1080p.WEB.h264-EDITH")));

        var result = await client.AddTorrentWithHashAsync(
            MakeConfig(), torrentBytes: new byte[] { 1, 2, 3 }, magnetUrl: null, knownHash: ExpectedHash, category: "sportarr");

        result.Should().BeNull();
        result.Should().NotBe(UnrelatedHash);
    }
}
