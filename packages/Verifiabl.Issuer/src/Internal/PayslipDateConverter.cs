#if NET6_0_OR_GREATER
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Verifiabl.Internal;

/// <summary>Reads and writes an AU2/NZ2 date as the YYYY-MM-DD JSON string the v2 wire contract requires.</summary>
internal sealed class PayslipDateConverter : JsonConverter<DateOnly>
{
    private const string WireFormat = "yyyy-MM-dd";

    internal static string Format(DateOnly value) => value.ToString(WireFormat, CultureInfo.InvariantCulture);

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (text is null
            || !DateOnly.TryParseExact(text, WireFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly value))
        {
            throw new JsonException("A payslip date must be a YYYY-MM-DD string.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Format(value));
}
#endif
