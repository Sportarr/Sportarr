namespace Sportarr.Api.Services;

public static class SeasonNumberParser
{
    public static int? Parse(string? season)
    {
        if (string.IsNullOrWhiteSpace(season)) return null;
        if (int.TryParse(season, out var number)) return number;

        var parts = season.Split(new[] { '-', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && int.TryParse(parts[0], out var startYear)
            ? startYear
            : null;
    }
}
