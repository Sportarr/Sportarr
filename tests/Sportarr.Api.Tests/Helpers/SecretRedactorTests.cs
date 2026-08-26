using FluentAssertions;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// Logs travel in support bundles and issue reports. A key written into one
/// is a key the user has to rotate, so these paths must never carry it.
/// </summary>
public class SecretRedactorTests
{
    [Theory]
    [InlineData("http://idx/api?t=search&apikey=abc123", "http://idx/api?t=search&apikey=***")]
    [InlineData("http://idx/dl?passkey=deadbeef&id=7", "http://idx/dl?passkey=***&id=7")]
    [InlineData("http://idx/rss?rsskey=zzz", "http://idx/rss?rsskey=***")]
    [InlineData("http://user:hunter2@nzbget:6789/jsonrpc", "http://***@nzbget:6789/jsonrpc")]
    public void Url_MasksCredentials(string input, string expected)
    {
        SecretRedactor.Url(input).Should().Be(expected);
    }

    [Fact]
    public void Url_LeavesAPlainUrlAlone()
    {
        SecretRedactor.Url("http://idx/api?t=caps&cat=5060").Should().Be("http://idx/api?t=caps&cat=5060");
    }

    [Fact]
    public void Json_MasksANamedProperty()
    {
        SecretRedactor.Json("{\"name\":\"Nzbs\",\"apiKey\":\"abc123\"}")
            .Should().Be("{\"name\":\"Nzbs\",\"apiKey\":\"***\"}");
    }

    [Fact]
    public void Json_MasksTheProwlarrFieldsShape()
    {
        SecretRedactor.Json("{\"fields\":[{\"name\":\"apiKey\",\"value\":\"abc123\"}]}")
            .Should().Be("{\"fields\":[{\"name\":\"apiKey\",\"value\":\"***\"}]}");
    }

    [Fact]
    public void Json_KeepsTheIndexerNameReadable()
    {
        SecretRedactor.Json("{\"name\":\"My Tracker\",\"baseUrl\":\"http://idx\"}")
            .Should().Be("{\"name\":\"My Tracker\",\"baseUrl\":\"http://idx\"}");
    }
}
