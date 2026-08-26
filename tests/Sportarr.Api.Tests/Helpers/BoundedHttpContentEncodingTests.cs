using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// The bounded reader replaced HttpContent.ReadAsStringAsync, which honours
/// the charset the response declares. Indexer feeds still go out as
/// ISO-8859-1, and decoding those as UTF-8 mangles accented release titles or
/// breaks the XML parse outright.
/// </summary>
public class BoundedHttpContentEncodingTests
{
    private static HttpContent Content(byte[] bytes, string? contentType)
    {
        var content = new ByteArrayContent(bytes);
        if (contentType != null)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }
        return content;
    }

    [Fact]
    public async Task A_declared_legacy_charset_is_honoured()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var latin1 = Encoding.GetEncoding("ISO-8859-1");
        const string title = "Grand Prix de Monaco Qualifié";

        var content = Content(latin1.GetBytes(title), "application/xml; charset=ISO-8859-1");

        var read = await BoundedHttpContent.ReadAsStringAsync(content, "feed");

        read.Should().Be(title);
    }

    [Fact]
    public async Task Utf8_is_used_when_nothing_is_declared()
    {
        const string title = "Grand Prix de Monaco Qualifié";
        var content = Content(Encoding.UTF8.GetBytes(title), "application/xml");

        var read = await BoundedHttpContent.ReadAsStringAsync(content, "feed");

        read.Should().Be(title);
    }

    [Fact]
    public async Task An_unknown_charset_falls_back_rather_than_throwing()
    {
        const string title = "Race";
        var content = Content(Encoding.UTF8.GetBytes(title), "application/xml; charset=not-a-charset");

        var read = await BoundedHttpContent.ReadAsStringAsync(content, "feed");

        read.Should().Be(title);
    }

    [Fact]
    public async Task The_byte_ceiling_still_applies()
    {
        var content = Content(new byte[2048], "text/plain");

        var act = async () => await BoundedHttpContent.ReadAsStringAsync(content, "feed", maxBytes: 1024);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
