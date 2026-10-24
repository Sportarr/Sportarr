using System.Text.Json;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public sealed class WebhookAddressTests
{
    private static Dictionary<string, JsonElement> Config(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void TheSettingsPageFieldIsRead()
    {
        Assert.Equal("http://hook.example/a", NotificationService.GetWebhookAddress(Config("""{"webhook":"http://hook.example/a"}""")));
    }

    [Theory]
    [InlineData("""{"url":"http://hook.example/b","method":"POST"}""")]
    [InlineData("""{"Url":"http://hook.example/b"}""")]
    [InlineData("""{"webhookUrl":"http://hook.example/b"}""")]
    public void AnApiCreatedConnectionIsRead(string json)
    {
        Assert.Equal("http://hook.example/b", NotificationService.GetWebhookAddress(Config(json)));
    }

    [Fact]
    public void TheSettingsPageFieldWinsAndEmptyValuesAreSkipped()
    {
        Assert.Equal("http://hook.example/a",
            NotificationService.GetWebhookAddress(Config("""{"url":"http://hook.example/b","webhook":"http://hook.example/a"}""")));
        Assert.Equal("http://hook.example/b",
            NotificationService.GetWebhookAddress(Config("""{"webhook":"  ","url":"http://hook.example/b"}""")));
    }

    [Fact]
    public void NoAddressGivesAnEmptyString()
    {
        Assert.Equal(string.Empty, NotificationService.GetWebhookAddress(Config("""{"method":"POST","url":5}""")));
        Assert.Equal(string.Empty, NotificationService.GetWebhookAddress(new Dictionary<string, JsonElement>()));
    }
}
