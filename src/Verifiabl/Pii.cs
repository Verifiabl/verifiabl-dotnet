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
/// Layout (8 segments, "P1" prefix + 7 fields, in this exact order):
///
///   P1|employeeName|position|department|employerAbn|bsb|accountNumber|accountName
///
/// Example:
///
///   P1|Jane A. Doe|Senior Developer|Engineering|12345678901|062-000|12345678|Jane A Doe
/// </remarks>
public static class Pii
{
    private const string Prefix = "P1|";
    private const int MaxFieldLength = 256;

    /// <summary>Maximum UTF-8 size of the optional P2 address.</summary>
    public const int AddressMaxBytes = 320;

    /// <summary>Field order is the wire contract. Never reorder.</summary>
    public static readonly IReadOnlyList<string> FieldOrder = new ReadOnlyCollection<string>(
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
    /// Format employee PII into Verifiabl's compact plaintext wire format.
    /// The result is what you encrypt with <see cref="VerifiablCrypto.EncryptPii"/>
    /// before embedding it in a barcode.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A field contains a pipe or control character, or exceeds 256 characters.
    /// </exception>
    public static string Format(PiiFields fields)
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

        return Prefix + string.Join("|", segments);
    }

    /// <summary>
    /// Opt-in P2 writer. The address is preserved verbatim and represented by
    /// the final field, which is empty when no address is supplied.
    /// </summary>
    public static string FormatV2(PiiV2Fields fields)
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

        return "P2|" + string.Join("|", segments);
    }

    /// <summary>
    /// Parse Verifiabl's compact PII wire format back into named fields. Empty
    /// segments are left <c>null</c>, mirroring Verifiabl's scan-time behaviour.
    /// </summary>
    /// <remarks>
    /// Useful for round-trip testing your integration; not needed in the normal
    /// issuance flow.
    /// </remarks>
    /// <exception cref="FormatException">The input is not a valid P1 PII string.</exception>
    public static PiiFields Parse(string plaintext)
    {
        if (plaintext is null)
        {
            throw new ArgumentNullException(nameof(plaintext));
        }

        if (!plaintext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new FormatException("Invalid PII format: expected 'P1|' prefix.");
        }

        string[] values = plaintext.Substring(Prefix.Length).Split('|');
        if (values.Length != FieldOrder.Count)
        {
            throw new FormatException(
                $"Expected {FieldOrder.Count} PII fields but got {values.Length}.");
        }

        var fields = new PiiFields
        {
            EmployeeName = NormalizeSegment(values[0], "employeeName"),
            Position = NormalizeSegment(values[1], "position"),
            Department = NormalizeSegment(values[2], "department"),
            EmployerAbn = NormalizeSegment(values[3], "employerAbn"),
            Bsb = NormalizeSegment(values[4], "bsb"),
            AccountNumber = NormalizeSegment(values[5], "accountNumber"),
            AccountName = NormalizeSegment(values[6], "accountName"),
        };

        return fields;
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

        const string name = nameof(PiiV2Fields.Address);
        if (!IsPrintableWithoutPipe(value) || ContainsFormatCharacter(value))
        {
            throw new ArgumentException(
                $"{name} must not contain '|', control, or format characters.",
                name);
        }

        if (Encoding.UTF8.GetByteCount(value) > AddressMaxBytes)
        {
            throw new ArgumentException($"{name} exceeds {AddressMaxBytes} UTF-8 bytes.", name);
        }

        return value;
    }

    private static bool ContainsFormatCharacter(string value) =>
        value.Any(c => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format);

    private static string? NormalizeSegment(string value, string name)
    {
        if (value.Length == 0)
        {
            return null;
        }

        if (value.Length > MaxFieldLength || !IsPrintableWithoutPipe(value))
        {
            throw new FormatException($"PII field '{name}' is not a valid field value.");
        }

        return value;
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
