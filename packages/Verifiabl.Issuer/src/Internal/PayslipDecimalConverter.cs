using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Verifiabl.Internal;

/// <summary>Reads and writes an AU2/NZ2 decimal as the exact JSON string the v2 wire contract requires.</summary>
internal sealed class PayslipDecimalConverter : JsonConverter<decimal>
{
    // \z, not $: .NET's $ also matches before a trailing "\n", which the API rejects.
    private static readonly Regex DecimalValue = new(
        "\\A-?[0-9]+(?:\\.[0-9]+)?\\z",
        RegexOptions.CultureInvariant);

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (text is null || !DecimalValue.IsMatch(text))
        {
            throw new JsonException("A payslip amount must be a plain decimal string.");
        }

        // decimal.Parse rounds beyond 28 decimal places, so require an exact round trip.
        decimal value = decimal.Parse(
            text,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture);
        if (value.ToString(CultureInfo.InvariantCulture) != text)
        {
            throw new JsonException("A payslip amount cannot be represented exactly as a decimal.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        // The invariant form keeps the value's scale, so 1.50m is sent as "1.50".
        string text = value.ToString(CultureInfo.InvariantCulture);
        if (!DecimalValue.IsMatch(text))
        {
            throw new JsonException("A payslip amount could not be written as a plain decimal.");
        }

        writer.WriteStringValue(text);
    }
}
