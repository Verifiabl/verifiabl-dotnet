namespace Verifiabl;

/// <summary>
/// Decryption metadata stored server-side at registration time: the AES-GCM IV
/// and authentication tag. Verifiabl finds the decryption key at verification
/// time; no key identifier is sent.
/// </summary>
public sealed class EncryptionMetadata
{
    /// <summary>96-bit IV, exactly 16 base64url characters.</summary>
    public required string Iv { get; set; }

    /// <summary>128-bit GCM authentication tag, exactly 22 base64url characters.</summary>
    public required string Tag { get; set; }
}
