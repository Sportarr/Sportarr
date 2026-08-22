using System.Text.Json.Serialization;

namespace Sportarr.Api.Models;

/// <summary>
/// Where a league name form came from. Diagnostic provenance for a saved
/// alias ordering - the entry's normalized value is the identity key, not
/// the source, so an alias that moves between sources (a user alias the hub
/// later publishes as an upstream alternate) keeps its saved position.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LeagueNameFormSource
{
    /// <summary>Shipped with Sportarr (built-in abbreviation tables).</summary>
    BuiltIn,

    /// <summary>The league's own canonical name.</summary>
    Canonical,

    /// <summary>An alias the user typed into the league's alias field.</summary>
    UserAlias,

    /// <summary>An alternate name published by the upstream metadata service.</summary>
    UpstreamAlias
}

/// <summary>
/// One position in a league's user-customized alias search order. Persisted
/// as JSON on League.AliasSearchOrder; a null list means the user never
/// customized the order.
/// </summary>
/// <remarks>
/// A record, not a class: value equality is what makes "the normalized value
/// is the identity key" enforceable, and it lets the EF ValueComparer use
/// SequenceEqual exactly like the League.Tags configuration next to it. The
/// properties stay mutable and non-positional, so the JSON storage shape is
/// an ordinary { "source": ..., "value": ... } object - unchanged from the
/// class form and unchanged for System.Text.Json in both directions.
/// </remarks>
public record LeagueAliasOrderEntry
{
    public LeagueNameFormSource Source { get; set; }

    public string Value { get; set; } = string.Empty;
}
