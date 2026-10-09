using System.Collections.ObjectModel;
using System.Text;

namespace Verifiabl;

/// <summary>
/// Formats employee PII into Verifiabl's compact plaintext wire format.
/// </summary>
/// <remarks>
/// The wire format is a pipe-delimited plaintext string. It is encrypted before
/// being embedded in the barcode and is never sent to the Verifiabl API in
/// plaintext.
///
/// <see cref="FormatAustralian(AustralianPiiFields)"/> and
/// <see cref="FormatNewZealand(NewZealandPiiFields)"/> write the AU2 and NZ2
/// jurisdiction profiles.
/// </remarks>
public static class Pii
{
    private const string AustralianPrefix = JurisdictionPiiProfiles.AustralianMarker + "|";
    private const string NewZealandPrefix = JurisdictionPiiProfiles.NewZealandMarker + "|";
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>Versioned identifier for the AU2 plaintext validation contract.</summary>
    internal const string AustralianTextProfileId = JurisdictionPiiProfiles.AustralianTextProfileId;

    /// <summary>Versioned identifier for the NZ2 plaintext validation contract.</summary>
    internal const string NewZealandTextProfileId = JurisdictionPiiProfiles.NewZealandTextProfileId;

    /// <summary>Unicode version used by the shared PII text profile's format-character table.</summary>
    internal const string TextProfileUnicodeVersion = PiiTextProfile.UnicodeVersion;

    /// <summary>AU2 field order. Never reorder.</summary>
    internal static readonly IReadOnlyList<string> AustralianFieldOrder = new ReadOnlyCollection<string>(
        JurisdictionPiiProfiles.AustralianFieldOrder);

    /// <summary>NZ2 field order. Never reorder.</summary>
    internal static readonly IReadOnlyList<string> NewZealandFieldOrder = new ReadOnlyCollection<string>(
        JurisdictionPiiProfiles.NewZealandFieldOrder);

    /// <summary>
    /// Format Australian employee PII as the fixed-arity AU2 plaintext paired
    /// with <c>au.payslip.v2</c>. The result is what you encrypt with
    /// <see cref="VerifiablCrypto.EncryptPii"/> before embedding it in a barcode.
    /// </summary>
    public static string FormatAustralian(AustralianPiiFields fields)
    {
        if (fields is null)
        {
            throw new ArgumentNullException(nameof(fields));
        }

        string employerIdentity = string.IsNullOrEmpty(fields.EmployerAbn)
            ? ValidateField(fields.EmployerName, nameof(fields.EmployerName))
            : ValidateField(fields.EmployerAbn, nameof(fields.EmployerAbn));
        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            ["employeeName"] = ValidateField(fields.EmployeeName, nameof(fields.EmployeeName)),
            ["position"] = ValidateField(fields.Position, nameof(fields.Position)),
            ["department"] = ValidateField(fields.Department, nameof(fields.Department)),
            ["employerIdentity"] = employerIdentity,
            ["bsb"] = ValidateField(fields.Bsb, nameof(fields.Bsb)),
            ["accountNumber"] = ValidateField(fields.AccountNumber, nameof(fields.AccountNumber)),
            ["accountName"] = ValidateField(fields.AccountName, nameof(fields.AccountName)),
            ["address"] = FormatAustralianAddress(fields.Address),
        };
        return FormatCurrentProfile(AustralianPrefix, AustralianFieldOrder.Select(name => values[name]).ToArray(),
            JurisdictionPiiProfiles.AustralianMarker, nameof(fields));
    }

    /// <summary>
    /// Format New Zealand employee PII as the fixed-arity NZ2 plaintext paired
    /// with <c>nz.payslip.v2</c>. The result is what you encrypt with
    /// <see cref="VerifiablCrypto.EncryptPii"/> before embedding it in a barcode.
    /// </summary>
    public static string FormatNewZealand(NewZealandPiiFields fields)
    {
        if (fields is null)
        {
            throw new ArgumentNullException(nameof(fields));
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            ["employeeName"] = ValidateField(fields.EmployeeName, nameof(fields.EmployeeName)),
            ["irdNumber"] = ValidateField(fields.IrdNumber, nameof(fields.IrdNumber)),
            ["position"] = ValidateField(fields.Position, nameof(fields.Position)),
            ["department"] = ValidateField(fields.Department, nameof(fields.Department)),
            ["employerName"] = ValidateField(fields.EmployerName, nameof(fields.EmployerName)),
            ["accountNumber"] = ValidateField(fields.AccountNumber, nameof(fields.AccountNumber)),
            ["accountName"] = ValidateField(fields.AccountName, nameof(fields.AccountName)),
            ["address"] = FormatNewZealandAddress(fields.Address),
        };
        return FormatCurrentProfile(NewZealandPrefix, NewZealandFieldOrder.Select(name => values[name]).ToArray(),
            JurisdictionPiiProfiles.NewZealandMarker, nameof(fields));
    }

    private static string ValidateField(string? value, string name)
    {
        if (value is null)
        {
            return string.Empty;
        }

        ValidateStrictUtf8(value, name);
        if (
            !IsPrintableWithoutPipe(value)
            || ContainsFormatCharacter(value)
            || ContainsLineOrParagraphSeparator(value))
        {
            throw new ArgumentException(
                $"{name} must not contain '|', control or format characters, or line or paragraph separators.",
                name);
        }

        return value;
    }

    private static string FormatAustralianAddress(AustralianAddress? address)
    {
        if (address is null)
        {
            return string.Empty;
        }

        var segments = ValidatedAddressLines(address.Lines, nameof(address.Lines));
        string localityLine = JoinAddressParts(
            (address.Suburb, nameof(address.Suburb)),
            (address.StateOrTerritory, nameof(address.StateOrTerritory)),
            (address.Postcode, nameof(address.Postcode)));
        if (localityLine.Length > 0)
        {
            segments.Add(localityLine);
        }

        return string.Join(", ", segments);
    }

    private static string FormatNewZealandAddress(NewZealandAddress? address)
    {
        if (address is null)
        {
            return string.Empty;
        }

        var segments = ValidatedAddressLines(address.Lines, nameof(address.Lines));
        string? suburb = ValidateAddressPart(address.Suburb, nameof(address.Suburb));
        if (suburb is not null)
        {
            segments.Add(suburb);
        }

        string cityLine = JoinAddressParts(
            (address.City, nameof(address.City)),
            (address.Postcode, nameof(address.Postcode)));
        if (cityLine.Length > 0)
        {
            segments.Add(cityLine);
        }

        return string.Join(", ", segments);
    }

    private static List<string> ValidatedAddressLines(
        IReadOnlyList<string>? lines,
        string name)
    {
        var result = new List<string>();
        if (lines is null)
        {
            return result;
        }

        for (int index = 0; index < lines.Count; index++)
        {
            string? line = ValidateAddressPart(lines[index], $"{name}[{index}]");
            if (line is not null)
            {
                result.Add(line);
            }
        }

        return result;
    }

    private static string JoinAddressParts(params (string? Value, string Name)[] parts)
    {
        return string.Join(
            " ",
            parts
                .Select(part => ValidateAddressPart(part.Value, part.Name))
                .Where(part => part is not null));
    }

    private static string? ValidateAddressPart(string? value, string name)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return ValidateField(value, name);
    }

    private static string FormatCurrentProfile(
        string prefix,
        IReadOnlyList<string> segments,
        string discriminator,
        string parameterName)
    {
        string plaintext = prefix + string.Join("|", segments);
        if (StrictUtf8.GetByteCount(plaintext) > JurisdictionPiiProfiles.PayloadMaxBytes)
        {
            throw new ArgumentException(
                $"{discriminator} plaintext exceeds {JurisdictionPiiProfiles.PayloadMaxBytes} UTF-8 bytes.",
                parameterName);
        }

        return plaintext;
    }

    private static int ValidateStrictUtf8(string value, string name)
    {
        try
        {
            return StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException($"{name} must contain valid Unicode.", name, exception);
        }
    }

    private static bool ContainsLineOrParagraphSeparator(string value) =>
        value.IndexOf('\u2028') >= 0 || value.IndexOf('\u2029') >= 0;

    private static bool ContainsFormatCharacter(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            int codePoint = value[index];
            if (char.IsHighSurrogate(value[index]))
            {
                codePoint = char.ConvertToUtf32(value[index], value[++index]);
            }

            if (PiiTextProfile.IsFormatCharacter(codePoint))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Allow-list for a single PII field value: any printable character except the
    /// pipe delimiter and control characters. Pipes would corrupt the positional
    /// layout; control characters have no place in PII fields.
    /// </summary>
    private static bool IsPrintableWithoutPipe(string value)
    {
        // Unicode Cc is permanently assigned to these C0 and C1 ranges.
        return !value.Any(
            c => c == '|' || c <= '\u001F' || c is >= '\u007F' and <= '\u009F');
    }
}
