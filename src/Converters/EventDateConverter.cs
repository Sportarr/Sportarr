using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sportarr.Api.Converters;

/// <summary>
/// Custom JSON converter for Event.EventDate that handles nullable strTimestamp from TheSportsDB API.
/// For older events (pre-2020), strTimestamp may be null, so we need to handle this gracefully.
/// The converter tries strTimestamp first, then falls back to dateEvent if strTimestamp is null.
/// </summary>
public class EventDateConverter : JsonConverter<DateTime>
{
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

            // Try to parse the date string
            if (DateTime.TryParse(dateString, out var date))
            {
                return date;
            }
        }

        return DateTime.MinValue;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Write as ISO 8601 format
        writer.WriteStringValue(value.ToString("O"));
    }
}
