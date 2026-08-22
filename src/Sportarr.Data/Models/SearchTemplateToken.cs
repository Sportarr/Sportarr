using System.Text.Json.Serialization;

namespace Sportarr.Api.Models;

/// <summary>
/// Metadata describing one search query template token: the literal string
/// a user types into a custom template (e.g. "{Round:00}"), a human-readable
/// description, and an example of the value it expands to. This is the
/// shared shape returned by the "GET /api/search/available-tokens" endpoint
/// and consumed by the frontend token picker.
/// </summary>
public sealed class SearchTemplateToken
{
    public SearchTemplateToken(string token, string description, string example)
    {
        Token = token;
        Description = description;
        Example = example;
    }

    [JsonPropertyName("token")]
    public string Token { get; }

    [JsonPropertyName("description")]
    public string Description { get; }

    [JsonPropertyName("example")]
    public string Example { get; }
}
