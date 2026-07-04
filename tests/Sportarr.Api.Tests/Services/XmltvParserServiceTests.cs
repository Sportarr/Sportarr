using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class XmltvParserServiceTests
{
    private static byte[] Gzip(string text)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
        using (var sw = new StreamWriter(gz, Encoding.UTF8))
        {
            sw.Write(text);
        }
        return ms.ToArray();
    }

    [Fact]
    public void DecodeEpgContent_GzipWithoutGzExtension_IsDecompressed()
    {
        const string xml = "<tv><programme/></tv>";
        var gz = Gzip(xml);

        // URL has no .gz suffix; must still decompress via the gzip magic bytes,
        // rather than passing 0x1F 0x8B... through to the XML parser.
        var result = XmltvParserService.DecodeEpgContent(gz, "http://provider.example/epg", null);

        result.Should().Be(xml);
    }

    [Fact]
    public void DecodeEpgContent_PlainXml_IsPassedThrough()
    {
        const string xml = "<tv></tv>";
        var bytes = Encoding.UTF8.GetBytes(xml);

        var result = XmltvParserService.DecodeEpgContent(bytes, "http://provider.example/epg.xml", "utf-8");

        result.Should().Be(xml);
    }
}
