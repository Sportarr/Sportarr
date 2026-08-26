using System.Text.Json;

namespace Sportarr.Api.Data;

/// <summary>
/// Round-trips a list of strings through a single database column.
///
/// The list used to be joined with commas, which silently destroyed any value
/// containing one. Image URLs routinely do, and a single URL came back as
/// several invalid ones. JSON has no such problem. Reading still accepts the
/// old comma-joined form so existing rows keep working.
/// </summary>
public static class StringListStorage
{
    public static string Serialize(List<string>? values)
    {
        return JsonSerializer.Serialize(values ?? new List<string>());
    }

    public static List<string> Deserialize(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return new List<string>();

        var trimmed = stored.TrimStart();
        if (trimmed.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(stored) ?? new List<string>();
            }
            catch (JsonException)
            {
                // Fall through and treat it as the legacy form.
            }
        }

        return stored.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
