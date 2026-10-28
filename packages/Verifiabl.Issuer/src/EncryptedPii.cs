namespace Verifiabl;

/// <summary>Result of <see cref="VerifiablCrypto.EncryptPii"/>.</summary>
public sealed class EncryptedPii
{
    internal EncryptedPii(byte[] ciphertext, EncryptionMetadata metadata)
    {
        Ciphertext = ciphertext;
        Metadata = metadata;
    }

    /// <summary>
    /// AES-256-GCM ciphertext bytes to store or pass to the barcode and client APIs.
    /// The SDK applies Base32 or base64url encoding at the relevant output boundary.
    /// </summary>
    public byte[] Ciphertext { get; }

    /// <summary>Server-side decryption metadata for the registration endpoints.</summary>
    public EncryptionMetadata Metadata { get; }
}
