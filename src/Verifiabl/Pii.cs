using System.Collections.ObjectModel;
using System.Globalization;
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
    private const int MaxFieldLength = 256;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>Maximum UTF-8 size of the optional P2 address.</summary>
    public const int AddressMaxBytes = 320;

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

        return V2Prefix + string.Join("|", segments);
    }

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

        if (value.Length > MaxFieldLength)
        {
            throw new ArgumentException($"{name} exceeds {MaxFieldLength} characters.", name);
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
        value = ValidateField(value, name);
        ValidateStrictUtf8(value, name);
        if (ContainsFormatCharacter(value))
        {
            throw new ArgumentException($"{name} must not contain format characters.", name);
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
        int byteCount = ValidateStrictUtf8(value, name);
        if (!IsPrintableWithoutPipe(value) || ContainsFormatCharacter(value))
        {
            throw new ArgumentException(
                $"{name} must not contain '|', control, or format characters.",
                name);
        }

        if (byteCount > AddressMaxBytes)
        {
            throw new ArgumentException($"{name} exceeds {AddressMaxBytes} UTF-8 bytes.", name);
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

    private static bool ContainsFormatCharacter(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(value, index) == UnicodeCategory.Format)
            {
                return true;
            }

            if (char.IsHighSurrogate(value[index]))
            {
                index++;
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
        return !value.Any(c => c == '|' || char.IsControl(c));
    }
}
