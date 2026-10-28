namespace Verifiabl;

/// <summary>
/// Decryption metadata stored server-side at registration time: the AES-GCM IV
/// and authentication tag. Verifiabl finds the decryption key at verification
/// time; no key identifier is sent.
/// </summary>
public sealed class EncryptionMetadata
{
    /// <summary>96-bit (12-byte) IV.</summary>
    public required byte[] Iv { get; set; }

    /// <summary>128-bit (16-byte) GCM authentication tag.</summary>
    public required byte[] Tag { get; set; }
}
