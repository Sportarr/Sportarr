using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// Credentials reach the log through payloads the app did not author, so the
/// masking cannot depend on how a serializer happened to order its properties
/// or on the secret containing no quote of its own.
/// </summary>
public class SecretRedactorJsonTests
{
    private const string Secret = "abcd1234SECRETVALUE";

    [Fact]
    public void A_named_property_is_masked()
    {
        SecretRedactor.Json($"{{\"apiKey\":\"{Secret}\"}}").Should().NotContain(Secret);
    }

    [Fact]
    public void A_fields_array_entry_is_masked()
    {
        SecretRedactor.Json($"{{\"name\":\"apiKey\",\"value\":\"{Secret}\"}}").Should().NotContain(Secret);
    }

    [Fact]
    public void A_fields_array_entry_written_the_other_way_round_is_masked()
    {
        // Which order a serializer emits depends on the type's declaration
        // order, and only one of the two was recognised.
        SecretRedactor.Json($"{{\"value\":\"{Secret}\",\"name\":\"apiKey\"}}").Should().NotContain(Secret);
    }

    [Fact]
    public void A_secret_containing_an_escaped_quote_is_masked_whole()
    {
        // The value used to end at the first quote, escaped or not, so the
        // tail was written out beside the mask.
        var withQuote = "aaa\\\"bbb" + Secret;
        SecretRedactor.Json($"{{\"apiKey\":\"{withQuote}\"}}").Should().NotContain(Secret);
    }

    [Fact]
    public void A_prowlarr_field_with_properties_between_name_and_value_is_masked()
    {
        // Prowlarr's indexer test sends each field with its label and help
        // text between "name" and "value", so the two are never side by side.
        var json = "{\"fields\":[{\"order\":2,\"name\":\"apiKey\",\"label\":\"API Key\",\"helpText\":null," +
                   $"\"helpLink\":null,\"value\":\"{Secret}\",\"type\":\"textbox\",\"privacy\":\"apiKey\"," +
                   "\"advanced\":false,\"hidden\":\"false\"}]}";
        SecretRedactor.Json(json).Should().NotContain(Secret);
    }

    [Fact]
    public void A_field_with_value_first_and_properties_between_is_masked()
    {
        var json = $"{{\"value\":\"{Secret}\",\"type\":\"textbox\",\"order\":2,\"name\":\"apiKey\"}}";
        SecretRedactor.Json(json).Should().NotContain(Secret);
    }

    [Fact]
    public void A_pretty_printed_field_is_masked()
    {
        var json = "{\n  \"name\": \"apiKey\",\n  \"label\": \"API Key\",\n  \"helpText\": null,\n" +
                   $"  \"value\": \"{Secret}\"\n}}";
        SecretRedactor.Json(json).Should().NotContain(Secret);
    }

    [Fact]
    public void A_field_with_an_array_between_name_and_value_is_masked()
    {
        var json = $"{{\"name\":\"passkey\",\"selectOptions\":[\"a\",\"b]\"],\"order\":[1,2],\"value\":\"{Secret}\"}}";
        SecretRedactor.Json(json).Should().NotContain(Secret);
    }

    [Fact]
    public void Other_fields_keep_their_values()
    {
        var json = "{\"fields\":[{\"name\":\"baseUrl\",\"label\":\"URL\",\"value\":\"http://prowlarr:9696/1/\"}," +
                   $"{{\"name\":\"apiKey\",\"label\":\"API Key\",\"value\":\"{Secret}\"}}]}}";
        var masked = SecretRedactor.Json(json);
        masked.Should().NotContain(Secret);
        masked.Should().Contain("http://prowlarr:9696/1/");
    }

    [Fact]
    public void A_mask_does_not_reach_into_the_next_field()
    {
        // A credential field with no value of its own leaves its neighbours
        // alone, whichever side of it they sit.
        var after = "[{\"name\":\"apiKey\",\"label\":\"API Key\"},{\"name\":\"baseUrl\",\"value\":\"http://keep.test/\"}]";
        var before = "[{\"value\":\"http://keep.test/\",\"label\":\"URL\"},{\"label\":\"API Key\",\"name\":\"apiKey\"}]";
        SecretRedactor.Json(after).Should().Contain("http://keep.test/");
        SecretRedactor.Json(before).Should().Contain("http://keep.test/");
    }

    [Fact]
    public void Ordinary_values_are_left_alone()
    {
        var json = "{\"name\":\"My Indexer\",\"url\":\"https://example.test/feed\"}";
        SecretRedactor.Json(json).Should().Contain("My Indexer");
    }
}
