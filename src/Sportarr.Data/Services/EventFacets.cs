using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// One dimension of a facet schema, for example gender. Values are in display order.
/// </summary>
public sealed record FacetDimension(string Key, string Label, IReadOnlyList<string> Values);

/// <summary>
/// The ordered dimensions a league's event titles carry, for example gender and discipline.
/// </summary>
public sealed record FacetSchema(string Id, IReadOnlyList<FacetDimension> Dimensions);

/// <summary>
/// One stored rule. A null value list is a wildcard. A dimension the rule
/// does not name is a wildcard too.
/// </summary>
public sealed class FacetRule
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>?> Dimensions { get; }

    public FacetRule(IReadOnlyDictionary<string, IReadOnlyList<string>?> dimensions)
    {
        Dimensions = dimensions;
    }
}

/// <summary>
/// Event facets per league, read from the event title. The first adopter is
/// the FIS Alpine Ski World Cup (gender and discipline).
///
/// League.MonitoredFacets holds the selection as a JSON list of rules. Each
/// rule maps a dimension to a list of values or "*". An event is monitored
/// when any rule matches. Each dimension is checked on its own. A dimension
/// the title does not show passes. A shown value that a rule does not list
/// fails that rule.
///
/// This parser is separate from LeagueReleaseNamePolicy on purpose, so a
/// change here cannot change release matching. Facet values never go into
/// the motorsport session tables, so the release filename sweep in
/// EventPartDetector cannot return them.
/// </summary>
public static class EventFacets
{
    public const string Wildcard = "*";

    /// <summary>Most rules one league can store.</summary>
    public const int MaxRules = 200;

    // The hub league id stays the same when upstream renames the league.
    private const string FisAlpineExternalId = "lg-001231";
    private const string FisAlpineLeagueName = "FIS Alpine Ski World Cup";
    private const string SkiingSport = "Skiing";
    private const string FisAlpineSchemaId = "fis-alpine";

    private static readonly FacetSchema FisAlpineSchema = new FacetSchema(
        FisAlpineSchemaId,
        new FacetDimension[]
        {
            new FacetDimension("gender", "Gender", new[] { "Men", "Women", "Mixed" }),
            new FacetDimension("discipline", "Discipline", new[] { "Downhill", "Super G", "Giant Slalom", "Slalom", "Combined", "Parallel" })
        });

    private static readonly IReadOnlyDictionary<string, Func<string, Dictionary<string, string>>> TitleParsers =
        new Dictionary<string, Func<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase)
        {
            { FisAlpineSchemaId, ParseFisAlpineTitle }
        };

    private static readonly Regex GenderMixedRegex = new Regex(
        @"\bmixed\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex GenderWomenRegex = new Regex(
        @"\b(?:women(?:['’]s)?|womens|ladies)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex GenderMenRegex = new Regex(
        @"\b(?:mens|men(?:['’]s)?|men)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DisciplineParallelRegex = new Regex(
        @"\bparallel\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DisciplineCombinedRegex = new Regex(
        @"\bcombined\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DisciplineGiantSlalomRegex = new Regex(
        @"\bgiant[\s._\-\u2010\u2011]+slalom\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DisciplineSuperGRegex = new Regex(
        @"\bsuper[\s._\-\u2010\u2011]?g(?:iant)?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DisciplineDownhillRegex = new Regex(
        @"\bdownhill\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DisciplineSlalomRegex = new Regex(
        @"\bslalom\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Finds the schema by the hub league id first. The name and sport are
    /// the fallback for a league that has no id.
    /// </summary>
    public static FacetSchema? GetSchema(string? sport, string? leagueName, string? externalId = null)
    {
        if (string.Equals(externalId?.Trim(), FisAlpineExternalId, StringComparison.OrdinalIgnoreCase))
        {
            return FisAlpineSchema;
        }

        if (string.IsNullOrWhiteSpace(sport) || string.IsNullOrWhiteSpace(leagueName))
        {
            return null;
        }

        return string.Equals(leagueName.Trim(), FisAlpineLeagueName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(sport.Trim(), SkiingSport, StringComparison.OrdinalIgnoreCase)
            ? FisAlpineSchema
            : null;
    }

    public static IReadOnlyDictionary<string, string> ParseTitle(FacetSchema schema, string? title)
    {
        if (TitleParsers.TryGetValue(schema.Id, out var parser))
        {
            return parser(title ?? string.Empty);
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<FacetRule>? ParseRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var rules = new List<FacetRule>();

            foreach (var ruleElement in doc.RootElement.EnumerateArray())
            {
                if (ruleElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var dimensions = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.OrdinalIgnoreCase);

                foreach (var property in ruleElement.EnumerateObject())
                {
                    var key = property.Name.Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(key))
                    {
                        return null;
                    }

                    switch (property.Value.ValueKind)
                    {
                        case JsonValueKind.String:
                        {
                            var value = property.Value.GetString();
                            if (value is null)
                            {
                                return null;
                            }

                            value = value.Trim();
                            if (value == Wildcard)
                            {
                                dimensions[key] = null;
                            }
                            else
                            {
                                dimensions[key] = new List<string> { value };
                            }

                            break;
                        }

                        case JsonValueKind.Array:
                        {
                            var values = new List<string>();
                            bool hasWildcard = false;

                            foreach (var item in property.Value.EnumerateArray())
                            {
                                if (item.ValueKind != JsonValueKind.String)
                                {
                                    return null;
                                }

                                var value = item.GetString();
                                if (value is null)
                                {
                                    return null;
                                }

                                value = value.Trim();
                                if (value == Wildcard)
                                {
                                    hasWildcard = true;
                                    break;
                                }

                                values.Add(value);
                            }

                            if (hasWildcard)
                            {
                                dimensions[key] = null;
                            }
                            else if (values.Count == 0)
                            {
                                return null;
                            }
                            else
                            {
                                dimensions[key] = values;
                            }

                            break;
                        }

                        default:
                            return null;
                    }
                }

                rules.Add(new FacetRule(dimensions));
            }

            return rules;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool Matches(IReadOnlyList<FacetRule> rules, IReadOnlyDictionary<string, string> facets)
    {
        foreach (var rule in rules)
        {
            if (RuleMatches(rule, facets))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsMonitored(string? sport, string? leagueName, string? monitoredFacets, string? title) =>
        IsMonitored(sport, leagueName, null, monitoredFacets, title);

    public static bool IsMonitored(string? sport, string? leagueName, string? externalId, string? monitoredFacets, string? title)
    {
        var schema = GetSchema(sport, leagueName, externalId);

        if (schema is null)
        {
            return true;
        }

        if (monitoredFacets is null)
        {
            return true;
        }

        // A stored value that does not parse monitors everything, like null.
        var rules = ParseRules(monitoredFacets);
        if (rules is null)
        {
            return true;
        }

        var facets = ParseTitle(schema, title);
        return Matches(rules, facets);
    }

    public static bool IsMonitored(League league, string? title) =>
        IsMonitored(league.Sport, league.Name, league.ExternalId, league.MonitoredFacets, title);

    /// <summary>
    /// Checks and normalizes a value sent for League.MonitoredFacets. A league
    /// without a schema accepts null, or the value it already stores. The
    /// edit dialog sends the stored value back, so a league that lost its
    /// schema must still save its other settings.
    /// </summary>
    public static string? NormalizeForLeague(string? sport, string? leagueName, string? externalId,
        string? json, string? storedValue, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var schema = GetSchema(sport, leagueName, externalId);
        if (schema is null)
        {
            if (string.Equals(json, storedValue, StringComparison.Ordinal))
            {
                return storedValue;
            }
            error = "This league has no event filters.";
            return null;
        }

        return NormalizeForStorage(schema, json, out error);
    }

    /// <summary>
    /// Returns compact JSON with lowercase dimension keys and schema value
    /// spelling. A dimension that lists every known value is stored as "*",
    /// so a value added later is monitored too.
    /// </summary>
    public static string? NormalizeForStorage(FacetSchema schema, string? json, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "Expected a JSON array.";
                return null;
            }

            if (doc.RootElement.GetArrayLength() > MaxRules)
            {
                error = $"A league can store at most {MaxRules} event filter rules.";
                return null;
            }

            var normalizedRules = new JsonArray();

            foreach (var ruleElement in doc.RootElement.EnumerateArray())
            {
                if (ruleElement.ValueKind != JsonValueKind.Object)
                {
                    error = "Each rule must be a JSON object.";
                    return null;
                }

                var selected = new Dictionary<string, HashSet<int>?>(StringComparer.OrdinalIgnoreCase);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var property in ruleElement.EnumerateObject())
                {
                    var rawKey = property.Name.Trim();
                    var dimension = FindDimension(schema, rawKey);

                    if (dimension is null)
                    {
                        error = $"Unknown dimension '{rawKey}'.";
                        return null;
                    }

                    seen.Add(dimension.Key);

                    switch (property.Value.ValueKind)
                    {
                        case JsonValueKind.String:
                        {
                            var rawValue = property.Value.GetString();
                            if (rawValue is null)
                            {
                                error = "Rule values must be strings.";
                                return null;
                            }

                            if (!TryApplyValue(dimension, rawValue.Trim(), selected, out error))
                            {
                                return null;
                            }

                            break;
                        }

                        case JsonValueKind.Array:
                        {
                            bool hasValue = false;

                            foreach (var item in property.Value.EnumerateArray())
                            {
                                if (item.ValueKind != JsonValueKind.String)
                                {
                                    error = "Rule values must be strings.";
                                    return null;
                                }

                                var rawValue = item.GetString();
                                if (rawValue is null)
                                {
                                    error = "Rule values must be strings.";
                                    return null;
                                }

                                hasValue = true;

                                if (!TryApplyValue(dimension, rawValue.Trim(), selected, out error))
                                {
                                    return null;
                                }

                                if (selected[dimension.Key] is null)
                                {
                                    break;
                                }
                            }

                            if (!hasValue)
                            {
                                error = "Value arrays must contain at least one value.";
                                return null;
                            }

                            break;
                        }

                        default:
                            error = "Rule values must be a string or an array of strings.";
                            return null;
                    }
                }

                var normalizedRule = new JsonObject();

                foreach (var dimension in schema.Dimensions)
                {
                    if (!seen.Contains(dimension.Key))
                    {
                        continue;
                    }

                    if (!selected.TryGetValue(dimension.Key, out var indices))
                    {
                        normalizedRule[dimension.Key] = JsonValue.Create(Wildcard);
                        continue;
                    }

                    if (indices is null || indices.Count == dimension.Values.Count)
                    {
                        normalizedRule[dimension.Key] = JsonValue.Create(Wildcard);
                        continue;
                    }

                    if (indices.Count == 0)
                    {
                        error = "Rule values must contain at least one value.";
                        return null;
                    }

                    var values = new JsonArray();
                    for (int i = 0; i < dimension.Values.Count; i++)
                    {
                        if (indices.Contains(i))
                        {
                            values.Add(JsonValue.Create(dimension.Values[i]));
                        }
                    }

                    normalizedRule[dimension.Key] = values;
                }

                normalizedRules.Add(normalizedRule);
            }

            return normalizedRules.ToJsonString();
        }
        catch (JsonException)
        {
            error = "JSON is invalid.";
            return null;
        }
    }

    private static bool RuleMatches(FacetRule rule, IReadOnlyDictionary<string, string> facets)
    {
        foreach (var dimension in rule.Dimensions)
        {
            // An unparsed dimension passes, like an undetected motorsport session.
            if (!facets.TryGetValue(dimension.Key, out var eventValue) || dimension.Value is null)
            {
                continue;
            }

            bool matched = false;
            foreach (var allowedValue in dimension.Value)
            {
                if (string.Equals(allowedValue, eventValue, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, string> ParseFisAlpineTitle(string title)
    {
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        bool mixed = GenderMixedRegex.IsMatch(title);
        bool women = GenderWomenRegex.IsMatch(title);
        bool men = GenderMenRegex.IsMatch(title);

        if (mixed)
        {
            parsed["gender"] = "Mixed";
        }
        else if (women && men)
        {
            // Both words without "Mixed" is not clear. Leave the gender unparsed.
        }
        else if (women)
        {
            parsed["gender"] = "Women";
        }
        else if (men)
        {
            parsed["gender"] = "Men";
        }

        if (DisciplineParallelRegex.IsMatch(title))
        {
            parsed["discipline"] = "Parallel";
        }
        else if (DisciplineCombinedRegex.IsMatch(title))
        {
            parsed["discipline"] = "Combined";
        }
        // Super G runs first, so "Super Giant Slalom" is never Giant Slalom.
        else if (DisciplineSuperGRegex.IsMatch(title))
        {
            parsed["discipline"] = "Super G";
        }
        else if (DisciplineGiantSlalomRegex.IsMatch(title))
        {
            parsed["discipline"] = "Giant Slalom";
        }
        else if (DisciplineDownhillRegex.IsMatch(title))
        {
            parsed["discipline"] = "Downhill";
        }
        else if (DisciplineSlalomRegex.IsMatch(title))
        {
            parsed["discipline"] = "Slalom";
        }

        return parsed;
    }

    private static FacetDimension? FindDimension(FacetSchema schema, string key)
    {
        foreach (var dimension in schema.Dimensions)
        {
            if (string.Equals(dimension.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return dimension;
            }
        }

        return null;
    }

    private static int FindValueIndex(FacetDimension dimension, string value)
    {
        for (int i = 0; i < dimension.Values.Count; i++)
        {
            if (string.Equals(dimension.Values[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryApplyValue(
        FacetDimension dimension,
        string rawValue,
        Dictionary<string, HashSet<int>?> selected,
        out string? error)
    {
        error = null;

        if (rawValue == Wildcard)
        {
            selected[dimension.Key] = null;
            return true;
        }

        var valueIndex = FindValueIndex(dimension, rawValue);
        if (valueIndex < 0)
        {
            error = $"Unknown value '{rawValue}' for dimension '{dimension.Key}'.";
            return false;
        }

        if (!selected.TryGetValue(dimension.Key, out var indices))
        {
            selected[dimension.Key] = new HashSet<int> { valueIndex };
            return true;
        }

        if (indices is null)
        {
            return true;
        }

        indices.Add(valueIndex);
        return true;
    }
}
