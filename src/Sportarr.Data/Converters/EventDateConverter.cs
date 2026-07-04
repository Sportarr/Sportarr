using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sportarr.Api.Converters;

/// <summary>
/// Converts an inbound API timestamp into a UTC DateTime regardless of
/// whether the source string carries an offset (e.g. "+00:00") or not.
///
/// The previous implementation used DateTime.TryParse without styles,
/// which converts offset-bearing inputs to the SERVER'S local timezone
/// and returns Kind=Local. On a non-UTC container (any user not running
/// in UTC) the round-trip through SQLite (which strips Kind on
/// persistence) left the underlying value shifted by the container's
/// UTC offset, then the iCal exporter and other consumers re-tagged the
/// shifted value as UTC, surfacing as a Jerusalem-+3 user seeing the
/// Canadian GP 17:00 UTC race as 20:00 UTC. AdjustToUniversal +
/// AssumeUniversal forces both offset-bearing and offset-less inputs
/// to canonical UTC, so persisted values and downstream serialization
/// are timezone-correct from the moment of ingest.
///
/// Also handles the nullable strTimestamp case (older events pre-2020)
/// by returning DateTime.MinValue so the caller can fall back to
/// dateEvent.
/// </summary>
public class EventDateConverter : JsonConverter<DateTime>
{
    private const DateTimeStyles UtcStyles =
        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            // Return DateTime.MinValue for null values - this will be handled by the caller
            // who should check for invalid dates and try the fallback field
            return DateTime.MinValue;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var dateString = reader.GetString();
            if (string.IsNullOrEmpty(dateString))
            {
                return DateTime.MinValue;
            }

            if (DateTime.TryParse(dateString, CultureInfo.InvariantCulture, UtcStyles, out var date))
            {
                return date;
            }
        }

        return DateTime.MinValue;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Round-trip in ISO 8601. Values produced by Read are Kind=Utc;
        // values loaded from SQLite are Kind=Unspecified but represent
        // the same UTC clock-time, so we force the Z suffix explicitly
        // instead of letting "O" emit a bare timestamp.
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        writer.WriteStringValue(utc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"));
    }
}
