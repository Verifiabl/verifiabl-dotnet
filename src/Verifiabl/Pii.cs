using System.Collections.ObjectModel;
using System.Globalization;

namespace Verifiabl;

/// <summary>
/// Formats employee PII into Verifiabl's compact plaintext wire format and back.
/// </summary>
/// <remarks>
/// The wire format is a pipe-delimited plaintext string. It is encrypted before
/// being embedded in the barcode and is never sent to the Verifiabl API in
/// plaintext.
///
/// Layout (9 segments, "P2" prefix + 8 fields, in this exact order):
///
///   P2|employeeName|position|department|employerAbn|bsb|accountNumber|accountName|address
///
/// Example:
///
///   P2|Jane A. Doe|Senior Developer|Engineering|12345678901|062-000|12345678|Jane A Doe|12 Example St, Sydney NSW 2000
///
/// P2 is what this SDK emits. P1 is the same layout without the trailing
/// address field; <see cref="Parse"/> still reads it, because documents issued
/// before P2 carry it and cannot be reissued.
/// </remarks>
public static class Pii
{
    private const string P1Prefix = "P1|";
    private const string P2Prefix = "P2|";
    private const int P1FieldCount = 7;
    private const int MaxFieldLength = 256;

    /// <summary>Field order is the wire contract. Never reorder; append only, as a new version.</summary>
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
            ValidateField(fields.Address, nameof(fields.Address)),
        ];

        return P2Prefix + string.Join("|", segments);
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

        bool isP2 = plaintext.StartsWith(P2Prefix, StringComparison.Ordinal);
        if (!isP2 && !plaintext.StartsWith(P1Prefix, StringComparison.Ordinal))
        {
            throw new FormatException("Invalid PII format: expected 'P1|' or 'P2|' prefix.");
        }

        string prefix = isP2 ? P2Prefix : P1Prefix;
        string version = prefix.TrimEnd('|');
        int expectedCount = isP2 ? FieldOrder.Count : P1FieldCount;

        string[] values = plaintext.Substring(prefix.Length).Split('|');
        if (values.Length != expectedCount)
        {
            throw new FormatException(
                $"Expected {expectedCount} {version} fields but got {values.Length}.");
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
            Address = isP2 ? NormalizeSegment(values[7], "address") : null,
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
                $"{name} must not contain '|', control characters or line separators.",
                name);
        }

        return value;
    }

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
    /// pipe delimiter, control characters and line separators. Pipes would corrupt
    /// the positional layout; the rest have no place in PII fields.
    /// </summary>
    private static bool IsPrintableWithoutPipe(string value)
    {
        return !value.Any(c => c == '|' || char.IsControl(c) || IsLineSeparator(c));
    }

    // U+2028 and U+2029 are separators, not control characters, so char.IsControl
    // misses them even though they break a field just as a newline would.
    private static bool IsLineSeparator(char c)
    {
        UnicodeCategory category = char.GetUnicodeCategory(c);
        return category == UnicodeCategory.LineSeparator
            || category == UnicodeCategory.ParagraphSeparator;
    }
}
