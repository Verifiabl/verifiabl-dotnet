using System.Security.Cryptography;
using Verifiabl.Internal;

namespace Verifiabl;

/// <summary>
/// Helpers for Verifiabl references: the 22-character base64url identifiers
/// embedded in every barcode. References are issued by the API and must be
/// used verbatim.
/// </summary>
public static class VerifiablReference
{
    /// <summary>Exact length of a Verifiabl reference in base64url characters.</summary>
    public const int Length = 22;

    /// <summary>
    /// Generate a fresh Verifiabl reference: 16 cryptographically random bytes
    /// (128 bits) encoded as 22 base64url characters without padding. Matches the
    /// server's algorithm, so a provider-generated reference is indistinguishable
    /// from one issued by the API.
    /// </summary>
    /// <remarks>
    /// Use this for <see cref="VerifiablClient.RegisterNonPiiBatchAsync"/>, where
    /// providers generate their own references up-front so a whole pay run can be
    /// submitted in one request. Single-record registration does not need it; the
    /// API generates a reference for you and returns it.
    /// </remarks>
    public static string Generate()
    {
        byte[] bytes = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }

        return Base64Url.Encode(bytes);
    }

    /// <summary>True when <paramref name="value"/> is a well-formed Verifiabl reference.</summary>
    public static bool IsValid(string? value) =>
        value is not null && value.Length == Length && Base64Url.IsBase64Url(value);

    internal static string Validate(string? value, string name)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                $"{name} must be exactly {Length} base64url characters.",
                name);
        }

        return value!;
    }
}
