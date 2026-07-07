using System.Security.Cryptography;
using System.Text;
using Verifiabl.Internal;

namespace Verifiabl;

/// <summary>
/// PII encryption helper.
/// </summary>
/// <remarks>
/// <para>
/// Verifiabl decrypts barcode ciphertext with AES-256-GCM using a 96-bit IV and a
/// 128-bit authentication tag. The IV, tag, and ciphertext are base64url encoded
/// without padding. <see cref="EncryptPii"/> produces exactly that shape from a
/// formatted PII string and your provider key.
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
/// On .NET Framework (via the netstandard2.0 build) AES-GCM is provided by
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
    /// <param name="plaintext">The formatted string from <see cref="Pii.Format"/>.</param>
    /// <param name="key">Your 32-byte provider encryption key.</param>
    /// <param name="keyVersion">
    /// The key version assigned during onboarding: <c>&lt;provider-id&gt;.&lt;n&gt;</c>,
    /// where provider-id is your provider ID and n starts at 1 and increments each
    /// time you rotate your key (e.g. "0f8fad5b-d9cb-469f-a165-70867728950e.1").
    /// </param>
    public static EncryptedPii EncryptPii(string plaintext, byte[] key, string keyVersion)
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

        Validation.ValidateKeyVersion(keyVersion, nameof(keyVersion));

        byte[] iv = new byte[IvBytes];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(iv);
        }

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[TagBytes];

        using (var aes = new AesGcm(key, TagBytes))
        {
            aes.Encrypt(iv, plaintextBytes, ciphertext, tag);
        }

        var metadata = new EncryptionMetadata
        {
            Iv = Base64Url.Encode(iv),
            Tag = Base64Url.Encode(tag),
            KeyVersion = keyVersion,
        };

        return new EncryptedPii(Base64Url.Encode(ciphertext), metadata);
    }
}
