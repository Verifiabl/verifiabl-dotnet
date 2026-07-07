namespace Verifiabl;

/// <summary>Result of <see cref="VerifiablCrypto.EncryptPii"/>.</summary>
public sealed class EncryptedPii
{
    internal EncryptedPii(string ciphertext, EncryptionMetadata metadata)
    {
        Ciphertext = ciphertext;
        Metadata = metadata;
    }

    /// <summary>
    /// Base64url ciphertext to embed in the barcode or send with
    /// <see cref="VerifiablClient.CreateBarcodeAsync"/>.
    /// </summary>
    public string Ciphertext { get; }

    /// <summary>Server-side decryption metadata for the registration endpoints.</summary>
    public EncryptionMetadata Metadata { get; }
}
