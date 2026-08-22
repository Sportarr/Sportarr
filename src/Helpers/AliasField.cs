namespace Sportarr.Api.Helpers;

/// <summary>
/// Shared parsing and storage-normalization for user-supplied alias fields
/// (team and league aliases). Splits on comma, pipe, or slash so that any of
/// "Man Utd, MUFC", "Man Utd | MUFC", or "Man Utd / MUFC" produce the same
/// alias list, and stores the canonical comma-and-space joined form.
/// </summary>
public static class AliasField
{
    public const int MaxUserAliasesLength = 512;
    private static readonly char[] Separators = [',', '|', '/'];

    public static IReadOnlyList<string> Parse(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    public static string? Normalize(string? value)
    {
        var aliases = Parse(value);
        return aliases.Count == 0 ? null : string.Join(", ", aliases);
    }
}
