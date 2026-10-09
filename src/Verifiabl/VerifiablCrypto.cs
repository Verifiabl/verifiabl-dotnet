using System.Security.Cryptography;
using System.Text;

namespace Verifiabl;

/// <summary>
/// PII encryption helper.
/// </summary>
/// <remarks>
/// <para>
/// Verifiabl decrypts barcode ciphertext with AES-256-GCM using a 96-bit IV and a
/// 128-bit authentication tag. <see cref="EncryptPii"/> returns the IV, tag, and
/// ciphertext as bytes; the SDK applies the required encoding when it sends an API
/// request or builds a barcode.
/// </para>
/// <para>
/// Each provider has its own encryption key, so a ciphertext can only be
/// decrypted with the key of the provider that issued it.
/// </para>
/// <para>
/// Key handling rules (ISO 27001-aligned, these are your obligations as a
/// provider): the 32-byte key must come from a KMS or secrets manager — never
/// hard-code it, commit it, or log it. The formatted plaintext string is PII;
/// keep it in memory only, never log it or persist it.
/// </para>
/// <para>
/// On .NET Framework (the net472 build) AES-GCM is provided by
/// Microsoft.Bcl.Cryptography and is supported on Windows only.
/// </para>
/// </remarks>
public static class VerifiablCrypto
{
    private const int IvBytes = 12; // 96-bit IV, the NIST-recommended size for GCM
    private const int KeyBytes = 32; // AES-256
    private const int TagBytes = 16; // 128-bit authentication tag

    /// <summary>
    /// Encrypt a formatted PII string with AES-256-GCM. The GCM authentication
    /// tag, returned in the metadata, lets the verifier detect any tampering with
    /// the ciphertext at scan time.
    /// </summary>
    /// <remarks>
    /// Every call draws a fresh random IV, which is what keeps AES-256-GCM safe
    /// under one key. Never store the returned
    /// <see cref="EncryptedPii.Metadata"/> and send it again with different
    /// content: registration rejects a repeated IV as
    /// <see cref="Client.VerifiablIvReuseException"/>, or in a batch as an error
    /// result matched by <see cref="Client.BatchRecordResult.IsIvReused"/>.
    /// </remarks>
    /// <param name="plaintext">
    /// The AU2 or NZ2 plaintext from <see cref="Pii.FormatAustralian(AustralianPiiFields)"/> or
    /// <see cref="Pii.FormatNewZealand(NewZealandPiiFields)"/>.
    /// </param>
    /// <param name="key">Your 32-byte provider encryption key.</param>
    public static EncryptedPii EncryptPii(string plaintext, byte[] key)
    {
        if (plaintext is null)
        {
            throw new ArgumentNullException(nameof(plaintext));
        }

        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (key.Length != KeyBytes)
        {
            throw new ArgumentException(
                $"Encryption key must be exactly {KeyBytes} bytes (AES-256).",
                nameof(key));
        }

        byte[] iv = new byte[IvBytes];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(iv);
        }

        return EncryptPiiCore(plaintext, key, iv);
    }

    /// <summary>Deterministic test seam for shared cryptographic conformance vectors.</summary>
    internal static EncryptedPii EncryptPiiWithIv(string plaintext, byte[] key, byte[] iv)
    {
        if (plaintext is null)
        {
            throw new ArgumentNullException(nameof(plaintext));
        }

        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (key.Length != KeyBytes)
        {
            throw new ArgumentException(
                $"Encryption key must be exactly {KeyBytes} bytes (AES-256).",
                nameof(key));
        }

        if (iv is null)
        {
            throw new ArgumentNullException(nameof(iv));
        }

        if (iv.Length != IvBytes)
        {
            throw new ArgumentException(
                $"Encryption IV must be exactly {IvBytes} bytes.",
                nameof(iv));
        }

        return EncryptPiiCore(plaintext, key, iv);
    }

    private static EncryptedPii EncryptPiiCore(string plaintext, byte[] key, byte[] iv)
    {
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[TagBytes];

        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(iv, plaintextBytes, ciphertext, tag);
        }
        finally
        {
            // Best-effort: don't leave the PII plaintext copy lingering in the heap.
#if NET8_0_OR_GREATER
            CryptographicOperations.ZeroMemory(plaintextBytes);
#else
            Array.Clear(plaintextBytes, 0, plaintextBytes.Length);
#endif
        }

        var metadata = new EncryptionMetadata
        {
            Iv = iv,
            Tag = tag,
        };

        return new EncryptedPii(ciphertext, metadata);
    }
}
