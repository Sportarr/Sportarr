using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Guides that declare a legacy encoding, which is most of the European ones,
/// were decoded as UTF-8 and every accented channel name and title came out
/// mangled. Team and event matching then had nothing to match against. The
/// guide is parsed as a stream now, so the encodings are proven through the
/// streaming entry point on the channel names that come out of it.
/// </summary>
public class XmltvEncodingTests
{
    private const string Body =
        "<tv><channel id=\"1\"><display-name>Canal Olímpico Español</display-name></channel></tv>";

    private static async Task<string?> FirstChannelNameAsync(byte[] bytes, string url, string? charSet)
    {
        var parser = new XmltvParserService(
            NullLogger<XmltvParserService>.Instance,
            new PassthroughHttpClientFactory());

        string? name = null;
        using var stream = new MemoryStream(bytes);
        var result = await parser.StreamParseAsync(
            stream, epgSourceId: 1, url, charSet,
            onChannels: batch => { name ??= batch.FirstOrDefault()?.DisplayName; return Task.CompletedTask; },
            onPrograms: _ => Task.CompletedTask);

        result.Success.Should().BeTrue(result.Error);
        return name;
    }

    private sealed class PassthroughHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Fact]
    public async Task The_declared_encoding_is_used_when_the_server_does_not_say()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var latin1 = Encoding.GetEncoding("ISO-8859-1");
        var document = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>" + Body;

        var name = await FirstChannelNameAsync(latin1.GetBytes(document), "http://example.com/guide.xml", null);

        name.Should().Be("Canal Olímpico Español");
    }

    [Fact]
    public async Task The_servers_charset_wins_over_the_declaration()
    {
        var document = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>" + Body;

        var name = await FirstChannelNameAsync(Encoding.UTF8.GetBytes(document), "http://example.com/guide.xml", "utf-8");

        name.Should().Be("Canal Olímpico Español");
    }

    [Fact]
    public async Task A_byte_order_mark_settles_it()
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>" + Body))
            .ToArray();

        var name = await FirstChannelNameAsync(bytes, "http://example.com/guide.xml", null);

        name.Should().Be("Canal Olímpico Español");
    }

    [Fact]
    public async Task Gzipped_guides_honour_their_declaration_too()
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

        var name = await FirstChannelNameAsync(buffer.ToArray(), "http://example.com/guide.xml.gz", null);

        name.Should().Be("Canal Olímpico Español");
    }
}
