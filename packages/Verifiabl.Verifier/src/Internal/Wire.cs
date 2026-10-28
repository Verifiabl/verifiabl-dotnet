using System.Globalization;
using System.Text.Json;

namespace Verifiabl.Verifier.Internal;

/// <summary>
/// Parses verification API responses. Parsing checks the fields this SDK uses
/// and ignores fields the API adds later.
/// </summary>
internal static class Wire
{
    internal sealed class TokenResponse
    {
        internal TokenResponse(string accessToken, double expiresInSeconds)
        {
            AccessToken = accessToken;
            ExpiresInSeconds = expiresInSeconds;
        }

        internal string AccessToken { get; }

        internal double ExpiresInSeconds { get; }
    }

    /// <summary>Parse a 200 verification response. Throws <see cref="FormatException"/> on an unexpected shape.</summary>
    internal static VerificationResult ResultFromWire(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("verified", out JsonElement verified)
            || verified.ValueKind != JsonValueKind.True)
        {
            throw UnexpectedShape("verified");
        }

        string reference = ReadString(root, "verifiabl_reference") ?? throw UnexpectedShape("verifiabl_reference");
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw UnexpectedShape("verifiabl_reference");
        }

        JsonElement payslip = ReadObject(root, "payslip");
        JsonElement employee = ReadObject(root, "employee");
        string decryptedAtText = ReadString(root, "decrypted_at") ?? throw UnexpectedShape("decrypted_at");
        if (!DateTimeOffset.TryParse(
            decryptedAtText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset decryptedAt))
        {
            throw UnexpectedShape("decrypted_at");
        }

        // Clone so the result outlives the response document.
        return new VerificationResult(
            reference,
            ReadString(payslip, "schema"),
            payslip,
            employee,
            decryptedAt);
    }

    internal static VerifierErrorBody? ErrorBodyFromWire(JsonElement root)
    {
        string? error = ReadString(root, "error");
        string? code = ReadString(root, "code");
        if (error is null || code is null)
        {
            return null;
        }

        var fieldErrors = new List<VerifierFieldError>();
        if (root.TryGetProperty("field_errors", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                string? path = ReadString(item, "path");
                string? message = ReadString(item, "message");
                if (path is not null && message is not null)
                {
                    fieldErrors.Add(new VerifierFieldError(path, message));
                }
            }
        }

        return new VerifierErrorBody(
            error,
            code,
            ReadString(root, "reason"),
            ReadString(root, "detail"),
            fieldErrors.AsReadOnly());
    }

    internal static TokenResponse? TokenFromWire(JsonElement root)
    {
        string? accessToken = ReadString(root, "access_token");
        string? tokenType = ReadString(root, "token_type");
        if (string.IsNullOrEmpty(accessToken)
            // RFC 6749 §7.1: token_type is case-insensitive.
            || !string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("expires_in", out JsonElement expiresElement)
            || expiresElement.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        double expiresIn = expiresElement.GetDouble();
        if (double.IsNaN(expiresIn) || double.IsInfinity(expiresIn) || expiresIn <= 0)
        {
            return null;
        }

        return new TokenResponse(accessToken!, expiresIn);
    }

    private static JsonElement ReadObject(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        throw UnexpectedShape(name);
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        return null;
    }

    private static FormatException UnexpectedShape(string field) =>
        new($"Verifiabl verification response had an unexpected shape (field '{field}').");
}
