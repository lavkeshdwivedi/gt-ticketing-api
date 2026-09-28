using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Ticketing.Api.Http;

/// <summary>
/// Rejects timestamps without an explicit offset. By default "2026-10-01T19:00:00" is read in the
/// server's local time zone, which silently moves an event depending on where the API is hosted.
/// </summary>
internal sealed partial class StrictDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (value is null || !ExplicitOffset().IsMatch(value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
        {
            throw new JsonException("Timestamps must be ISO 8601 with an explicit offset, e.g. 2026-10-01T19:00:00-04:00 or 2026-10-01T23:00:00Z.");
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value);
    }

    [GeneratedRegex(@"T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitOffset();
}
