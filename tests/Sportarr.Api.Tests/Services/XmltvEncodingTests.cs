using System.Text;
using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Guides that declare a legacy encoding, which is most of the European ones,
/// were decoded as UTF-8 and every accented channel name and title came out
/// mangled. Team and event matching then had nothing to match against.
/// </summary>
public class XmltvEncodingTests
{
    private const string Body =
        "<tv><channel id=\"1\"><display-name>Canal Olímpico Español</display-name></channel></tv>";

    [Fact]
    public void The_declared_encoding_is_used_when_the_server_does_not_say()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var latin1 = Encoding.GetEncoding("ISO-8859-1");
        var document = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>" + Body;
        var bytes = latin1.GetBytes(document);

        var decoded = XmltvParserService.DecodeEpgContent(bytes, "http://example.com/guide.xml", null);

        decoded.Should().Contain("Canal Olímpico Español");
    }

    [Fact]
    public void The_servers_charset_wins_over_the_declaration()
    {
        var document = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>" + Body;
        var bytes = Encoding.UTF8.GetBytes(document);

        var decoded = XmltvParserService.DecodeEpgContent(bytes, "http://example.com/guide.xml", "utf-8");

        decoded.Should().Contain("Canal Olímpico Español");
    }

    [Fact]
    public void A_byte_order_mark_settles_it_and_is_not_left_in_the_text()
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>" + Body))
            .ToArray();

        var decoded = XmltvParserService.DecodeEpgContent(bytes, "http://example.com/guide.xml", null);

        decoded.Should().StartWith("<?xml");
        decoded.Should().Contain("Canal Olímpico Español");
    }

    [Fact]
    public void Gzipped_guides_honour_their_declaration_too()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var latin1 = Encoding.GetEncoding("ISO-8859-1");
        var document = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>" + Body;

        using var buffer = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(buffer, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            var raw = latin1.GetBytes(document);
            gzip.Write(raw, 0, raw.Length);
        }

        var decoded = XmltvParserService.DecodeEpgContent(buffer.ToArray(), "http://example.com/guide.xml.gz", null);

        decoded.Should().Contain("Canal Olímpico Español");
    }
}
