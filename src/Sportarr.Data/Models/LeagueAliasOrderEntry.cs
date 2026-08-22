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
public class LeagueAliasOrderEntry
{
    public LeagueNameFormSource Source { get; set; }

    public string Value { get; set; } = string.Empty;
}
