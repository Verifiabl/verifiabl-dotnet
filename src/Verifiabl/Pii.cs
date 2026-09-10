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
/// Current layout (9 segments, "P2" prefix + 8 fields, in this exact order):
///
///   P2|employeeName|position|department|employerAbn|bsb|accountNumber|accountName|address
///
/// P1 remains available through <see cref="FormatV1"/> for rollback and is parsed permanently.
/// </remarks>
public static class Pii
{
    private const string V1Prefix = "P1|";
    private const string V2Prefix = "P2|";
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>Versioned identifier for the P2 plaintext validation contract.</summary>
    public const string TextProfileId = "io.verifiabl.p2-pii-text.v1";

    /// <summary>Unicode version used by the P2 format-character table.</summary>
    public const string TextProfileUnicodeVersion = PiiTextProfile.UnicodeVersion;

    /// <summary>Legacy P1 field limit, retained for API compatibility.</summary>
    /// <remarks>P2 does not have a per-field limit.</remarks>
    public const int FieldMaxUtf16CodeUnits = 256;

    /// <summary>Former P2 address limit, retained for API compatibility.</summary>
    /// <remarks>This value is not enforced for P2, which has no address-specific limit.</remarks>
    public const int AddressMaxBytes = 320;

    /// <summary>Maximum UTF-8 size of complete newly written P2 plaintext, including framing.</summary>
    public const int PayloadMaxBytes = 1024;

    /// <summary>Current P2 field order. Never reorder.</summary>
    public static readonly IReadOnlyList<string> FieldOrder = new ReadOnlyCollection<string>(
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

    /// <summary>Permanent legacy P1 field order. Never reorder.</summary>
    public static readonly IReadOnlyList<string> V1FieldOrder = new ReadOnlyCollection<string>(
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
    /// Compatibility alias for the P2 writer that was introduced before P2
    /// became the default. New code should use <see cref="Format(PiiFields)"/>.
    /// </summary>
    public static string FormatV2(PiiV2Fields fields) => Format(fields);

    /// <summary>
    /// Format the permanent legacy P1 plaintext for rollback. New documents use
    /// <see cref="Format(PiiFields)"/>.
    /// </summary>
    public static string FormatV1(PiiFields fields)
    {
        if (fields is null)
        {
            throw new ArgumentNullException(nameof(fields));
        }

        string[] segments =
        [
            ValidateField(fields.EmployeeName, nameof(fields.EmployeeName)),
            ValidateField(fields.Position, nameof(fields.Position)),
            ValidateField(fields.Department, nameof(fields.Department)),
            ValidateField(fields.EmployerAbn, nameof(fields.EmployerAbn)),
            ValidateField(fields.Bsb, nameof(fields.Bsb)),
            ValidateField(fields.AccountNumber, nameof(fields.AccountNumber)),
            ValidateField(fields.AccountName, nameof(fields.AccountName)),
        ];

        return V1Prefix + string.Join("|", segments);
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

        if (value.Length > FieldMaxUtf16CodeUnits)
        {
            throw new ArgumentException(
                $"{name} exceeds {FieldMaxUtf16CodeUnits} UTF-16 code units.",
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
