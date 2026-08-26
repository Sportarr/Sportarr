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
    public void Ordinary_values_are_left_alone()
    {
        var json = "{\"name\":\"My Indexer\",\"url\":\"https://example.test/feed\"}";
        SecretRedactor.Json(json).Should().Contain("My Indexer");
    }
}
