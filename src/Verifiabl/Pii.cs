using System.Collections.ObjectModel;
using System.Text;

namespace Verifiabl;

/// <summary>
/// Formats employee PII into Verifiabl's compact plaintext wire format and back.
/// </summary>
/// <remarks>
/// The wire format is a pipe-delimited plaintext string. It is encrypted before
/// being embedded in the barcode and is never sent to the Verifiabl API in
/// plaintext.
///
/// <see cref="Format(PiiFields)"/> retains the existing P2 compatibility layout.
/// <see cref="FormatAustralian(AustralianPiiFields)"/> and
/// <see cref="FormatNewZealand(NewZealandPiiFields)"/> write the AU2 and NZ2
/// jurisdiction profiles.
///
/// Legacy P1 plaintext remains readable through <see cref="Parse"/> for existing
/// documents, but cannot be generated.
/// </remarks>
public static class Pii
{
    private const string V1Prefix = "P1|";
    private const string V2Prefix = "P2|";
    private const string AustralianPrefix = JurisdictionPiiProfiles.AustralianMarker + "|";
    private const string NewZealandPrefix = JurisdictionPiiProfiles.NewZealandMarker + "|";
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>Versioned identifier for the P2 plaintext validation contract.</summary>
    internal const string TextProfileId = "io.verifiabl.p2-pii-text.v1";

    /// <summary>Versioned identifier for the AU2 plaintext validation contract.</summary>
    internal const string AustralianTextProfileId = JurisdictionPiiProfiles.AustralianTextProfileId;

    /// <summary>Versioned identifier for the NZ2 plaintext validation contract.</summary>
    internal const string NewZealandTextProfileId = JurisdictionPiiProfiles.NewZealandTextProfileId;

    /// <summary>Unicode version used by the P2 format-character table.</summary>
    internal const string TextProfileUnicodeVersion = PiiTextProfile.UnicodeVersion;

    private const int V1FieldMaxUtf16CodeUnits = 256;

    /// <summary>Maximum UTF-8 size of complete newly written P2, AU2, and NZ2 plaintext, including framing.</summary>
    internal const int PayloadMaxBytes = 1024;

    /// <summary>Current P2 field order. Never reorder.</summary>
    internal static readonly IReadOnlyList<string> FieldOrder = new ReadOnlyCollection<string>(
    [
        "employeeName",
        "position",
        "department",
        "employerAbn",
        "bsb",
        "accountNumber",
        "accountName",
        "address",
    ]);

    /// <summary>AU2 field order. Never reorder.</summary>
    internal static readonly IReadOnlyList<string> AustralianFieldOrder = new ReadOnlyCollection<string>(
        JurisdictionPiiProfiles.AustralianFieldOrder);

    /// <summary>NZ2 field order. Never reorder.</summary>
    internal static readonly IReadOnlyList<string> NewZealandFieldOrder = new ReadOnlyCollection<string>(
        JurisdictionPiiProfiles.NewZealandFieldOrder);

    // Permanent legacy P1 field order used only when reading existing documents.
    private static readonly IReadOnlyList<string> V1FieldOrder = new ReadOnlyCollection<string>(
    [
        "employeeName",
        "position",
        "department",
        "employerAbn",
        "bsb",
        "accountNumber",
        "accountName",
    ]);

    /// <summary>
    /// Format employee PII as the current P2 plaintext. The result is what you
    /// encrypt with <see cref="VerifiablCrypto.EncryptPii"/> before embedding it
    /// in a barcode.
    /// </summary>
    public static string Format(PiiFields fields)
    {
        if (fields is null)
        {
            throw new ArgumentNullException(nameof(fields));
        }

        string[] segments =
        [
            ValidateV2Field(fields.EmployeeName, nameof(fields.EmployeeName)),
            ValidateV2Field(fields.Position, nameof(fields.Position)),
            ValidateV2Field(fields.Department, nameof(fields.Department)),
            ValidateV2Field(fields.EmployerAbn, nameof(fields.EmployerAbn)),
            ValidateV2Field(fields.Bsb, nameof(fields.Bsb)),
            ValidateV2Field(fields.AccountNumber, nameof(fields.AccountNumber)),
            ValidateV2Field(fields.AccountName, nameof(fields.AccountName)),
            ValidateAddress(fields.Address),
        ];

        string plaintext = V2Prefix + string.Join("|", segments);
        if (StrictUtf8.GetByteCount(plaintext) > PayloadMaxBytes)
        {
            throw new ArgumentException(
                $"P2 plaintext exceeds {PayloadMaxBytes} UTF-8 bytes.",
                nameof(fields));
        }

        return plaintext;
    }

    /// <summary>
    /// Format Australian employee PII as the fixed-arity AU2 plaintext paired
    /// with <c>au.payslip.v2</c>.
    /// </summary>
    public static string FormatAustralian(AustralianPiiFields fields)
    {
        if (fields is null)
        {
            throw new ArgumentNullException(nameof(fields));
        }

        string employerIdentity = string.IsNullOrEmpty(fields.EmployerAbn)
            ? ValidateV2Field(fields.EmployerName, nameof(fields.EmployerName))
            : ValidateV2Field(fields.EmployerAbn, nameof(fields.EmployerAbn));
        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            ["employeeName"] = ValidateV2Field(fields.EmployeeName, nameof(fields.EmployeeName)),
            ["position"] = ValidateV2Field(fields.Position, nameof(fields.Position)),
            ["department"] = ValidateV2Field(fields.Department, nameof(fields.Department)),
            ["employerIdentity"] = employerIdentity,
            ["bsb"] = ValidateV2Field(fields.Bsb, nameof(fields.Bsb)),
            ["accountNumber"] = ValidateV2Field(fields.AccountNumber, nameof(fields.AccountNumber)),
            ["accountName"] = ValidateV2Field(fields.AccountName, nameof(fields.AccountName)),
            ["address"] = FormatAustralianAddress(fields.Address),
        };
        return FormatCurrentProfile(AustralianPrefix, AustralianFieldOrder.Select(name => values[name]).ToArray(),
            JurisdictionPiiProfiles.AustralianMarker, nameof(fields));
    }

    /// <summary>
    /// Format New Zealand employee PII as the fixed-arity NZ2 plaintext paired
    /// with <c>nz.payslip.v2</c>.
    /// </summary>
    public static string FormatNewZealand(NewZealandPiiFields fields)
    {
        if (fields is null)
        {
            throw new ArgumentNullException(nameof(fields));
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            ["employeeName"] = ValidateV2Field(fields.EmployeeName, nameof(fields.EmployeeName)),
            ["irdNumber"] = ValidateV2Field(fields.IrdNumber, nameof(fields.IrdNumber)),
            ["position"] = ValidateV2Field(fields.Position, nameof(fields.Position)),
            ["department"] = ValidateV2Field(fields.Department, nameof(fields.Department)),
            ["employerName"] = ValidateV2Field(fields.EmployerName, nameof(fields.EmployerName)),
            ["accountNumber"] = ValidateV2Field(fields.AccountNumber, nameof(fields.AccountNumber)),
            ["accountName"] = ValidateV2Field(fields.AccountName, nameof(fields.AccountName)),
            ["address"] = FormatNewZealandAddress(fields.Address),
        };
        return FormatCurrentProfile(NewZealandPrefix, NewZealandFieldOrder.Select(name => values[name]).ToArray(),
            JurisdictionPiiProfiles.NewZealandMarker, nameof(fields));
    }

    /// <summary>
    /// Parse Verifiabl's compact PII wire format back into named fields. Empty
    /// segments are left <c>null</c>, mirroring Verifiabl's scan-time behaviour.
    /// </summary>
    /// <remarks>
    /// Useful for round-trip testing your integration; not needed in the normal
    /// issuance flow.
    /// </remarks>
    /// <exception cref="FormatException">The input is not a valid P1 or P2 PII string.</exception>
    public static PiiFields Parse(string plaintext)
    {
        if (plaintext is null)
        {
            throw new ArgumentNullException(nameof(plaintext));
        }

        bool isV2 = plaintext.StartsWith(V2Prefix, StringComparison.Ordinal);
        bool isV1 = plaintext.StartsWith(V1Prefix, StringComparison.Ordinal);
        if (!isV1 && !isV2)
        {
            throw new FormatException("Invalid PII format: expected 'P1|' or 'P2|' prefix.");
        }

        int prefixLength = isV2 ? V2Prefix.Length : V1Prefix.Length;
        int expectedCount = isV2 ? FieldOrder.Count : V1FieldOrder.Count;
        string[] values = plaintext.Substring(prefixLength).Split('|');
        if (values.Length != expectedCount)
        {
            throw new FormatException(
                $"Expected {expectedCount} PII fields but got {values.Length}.");
        }

        return new PiiFields
        {
            EmployeeName = NormalizeSegment(values[0], "employeeName", isV2),
            Position = NormalizeSegment(values[1], "position", isV2),
            Department = NormalizeSegment(values[2], "department", isV2),
            EmployerAbn = NormalizeSegment(values[3], "employerAbn", isV2),
            Bsb = NormalizeSegment(values[4], "bsb", isV2),
            AccountNumber = NormalizeSegment(values[5], "accountNumber", isV2),
            AccountName = NormalizeSegment(values[6], "accountName", isV2),
            Address = isV2 ? NormalizeAddress(values[7]) : null,
        };
    }

    private static string ValidateField(string? value, string name)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value.Length > V1FieldMaxUtf16CodeUnits)
        {
            throw new ArgumentException(
                $"{name} exceeds {V1FieldMaxUtf16CodeUnits} UTF-16 code units.",
                name);
        }

        if (!IsPrintableWithoutPipe(value))
        {
            throw new ArgumentException(
                $"{name} must not contain '|' or control characters.",
                name);
        }

        return value;
    }

    private static string ValidateV2Field(string? value, string name)
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

    private static string ValidateAddress(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        const string name = nameof(PiiFields.Address);
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

        return ValidateV2Field(value, name);
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

    private static string? NormalizeSegment(string value, string name, bool isV2)
    {
        if (value.Length == 0)
        {
            return null;
        }

        try
        {
            return isV2 ? ValidateV2Field(value, name) : ValidateField(value, name);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException($"PII field '{name}' is not a valid field value.", exception);
        }
    }

    private static string? NormalizeAddress(string value)
    {
        if (value.Length == 0)
        {
            return null;
        }

        try
        {
            return ValidateAddress(value);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException("PII field 'address' is not a valid field value.", exception);
        }
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
