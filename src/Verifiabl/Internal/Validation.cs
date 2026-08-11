using System.Globalization;
using System.Text.RegularExpressions;

namespace Verifiabl.Internal;

internal static class Validation
{
    /// <summary>Payslip schema identifier: <c>xx.type.vN</c>, e.g. "au.payslip.v1".</summary>
    internal static readonly Regex SchemaRegex = new(
        "^[a-z]{2}\\.[a-z]+\\.v[0-9]+$",
        RegexOptions.CultureInvariant);

    internal const int MaxCiphertextLength = 10_000;

    internal const int MaxExternalIdLength = 255;

    /// <summary>Printable ASCII only, so an external_id is safe to place in logs.</summary>
    internal static readonly Regex ExternalIdRegex = new(
        "^[\\x20-\\x7e]+$",
        RegexOptions.CultureInvariant);

    internal static string ValidateSchema(string? schema, string name)
    {
        if (schema is null || !SchemaRegex.IsMatch(schema))
        {
            throw new ArgumentException(
                $"{name} must be in format 'xx.type.vN' (e.g. 'au.payslip.v1').",
                name);
        }

        return schema;
    }

    internal static string ValidateCiphertext(string? ciphertext, string name)
    {
        if (ciphertext is null || ciphertext.Length == 0)
        {
            throw new ArgumentException($"{name} must not be empty.", name);
        }

        if (ciphertext.Length > MaxCiphertextLength)
        {
            throw new ArgumentException($"{name} exceeds the maximum allowed length.", name);
        }

        if (!Base64Url.IsBase64Url(ciphertext))
        {
            throw new ArgumentException($"{name} must be base64url encoded.", name);
        }

        return ciphertext;
    }

    internal static string ValidateExternalId(string? externalId, string name)
    {
        if (externalId is null || externalId.Length == 0)
        {
            throw new ArgumentException($"{name} must not be empty.", name);
        }

        if (externalId.Length > MaxExternalIdLength)
        {
            throw new ArgumentException(
                $"{name} must be at most {MaxExternalIdLength} characters.",
                name);
        }

        if (!ExternalIdRegex.IsMatch(externalId))
        {
            throw new ArgumentException(
                $"{name} must contain only printable ASCII characters.",
                name);
        }

        return externalId;
    }

    /// <summary>Validates a YYYY-MM-DD date string, rejecting impossible dates.</summary>
    internal static string ValidateIsoDate(string? value, string name)
    {
        if (value is null
            || value.Length != 10
            || !DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw new ArgumentException($"{name} must be a YYYY-MM-DD date.", name);
        }

        return value;
    }

    internal static void ValidateEncryptionMetadata(EncryptionMetadata? metadata, string name)
    {
        if (metadata is null)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        if (metadata.Iv is null || metadata.Iv.Length != 16 || !Base64Url.IsBase64Url(metadata.Iv))
        {
            throw new ArgumentException(
                $"{name}.Iv must be exactly 16 base64url characters (96-bit IV).",
                name);
        }

        if (metadata.Tag is null || metadata.Tag.Length != 22 || !Base64Url.IsBase64Url(metadata.Tag))
        {
            throw new ArgumentException(
                $"{name}.Tag must be exactly 22 base64url characters (128-bit GCM tag).",
                name);
        }
    }
}
