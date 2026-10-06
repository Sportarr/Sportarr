using FluentAssertions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Services;

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
    [InlineData("https://tv.example/live/viewer/shortpass/42.ts", "https://tv.example/live/***/***/42.ts")]
    [InlineData("https://tv.example/timeshift/viewer/shortpass/60/2026-10-06:12-00/42.ts", "https://tv.example/timeshift/***/***/60/2026-10-06:12-00/42.ts")]
    [InlineData("https://tv.example/streaming/timeshift.php?username=viewer&password=shortpass", "https://tv.example/streaming/timeshift.php?username=***&password=***")]
    [InlineData("https://tv.example/viewer/shortpass/42.ts", "https://tv.example/***/***/42.ts")]
    [InlineData("https://tv.example/viewer/shortpass/42.m3u", "https://tv.example/***/***/42.m3u")]
    [InlineData("https://tv.example/viewer/shortpass/", "https://tv.example/***/***/")]
    [InlineData("(https://tv.example/viewer/shortpass/42.ts)", "(https://tv.example/***/***/42.ts)")]
    [InlineData("[https://tv.example/viewer/shortpass/42.m3u]", "[https://tv.example/***/***/42.m3u]")]
    public void Url_MasksCredentials(string input, string expected)
    {
        SecretRedactor.Url(input).Should().Be(expected);
    }

    [Fact]
    public void Url_LeavesAPlainUrlAlone()
    {
        SecretRedactor.Url("http://idx/api?t=caps&cat=5060").Should().Be("http://idx/api?t=caps&cat=5060");
        SecretRedactor.Url("https://example.com/api/v1/").Should().Be("https://example.com/api/v1/");
    }

    [Fact]
    public void Url_MasksAmbiguousBareBaseForPrivacy()
    {
        SecretRedactor.Url("https://example.com/settings/general/")
            .Should().Be("https://example.com/***/***/");
    }

    [Fact]
    public void LogSanitizer_MasksXtreamCredentialsBeforeWritingTheLog()
    {
        LogSanitizer.Sanitize("Recording https://tv.example/live/viewer/shortpass/42.ts")
            .Should().Be("Recording https://tv.example/live/***/***/42.ts");
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
