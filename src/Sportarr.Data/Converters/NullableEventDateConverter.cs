using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sportarr.Api.Converters;

/// <summary>
/// JSON converter for DateTime? that tolerates the shapes the upstream
/// API actually emits — full ISO timestamps, date-only strings
/// ("2026-05-04"), and null. System.Text.Json's default DateTime?
/// converter is strict about ISO 8601 and rejects "yyyy-MM-dd" without
/// a time component, which the broadcastDate field uses.
///
/// Parses with AdjustToUniversal + AssumeUniversal so offset-bearing
/// inputs collapse to UTC and offset-less inputs are treated as UTC.
/// See EventDateConverter for the timezone-correctness rationale; the
/// same shift-by-container-offset bug would otherwise apply here.
/// Date-only strings still round-trip cleanly because forcing UTC
/// preserves the .Date portion the callers actually read.
/// </summary>
public class NullableEventDateConverter : JsonConverter<DateTime?>
{
    private const DateTimeStyles UtcStyles =
        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s))
            {
                return null;
            }
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, UtcStyles, out var dt))
            {
                return dt;
            }
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            // Emit as date-only — BroadcastDate has no meaningful time.
            writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd"));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
