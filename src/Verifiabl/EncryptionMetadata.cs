namespace Verifiabl;

/// <summary>
/// Decryption metadata stored server-side at registration time: the AES-GCM IV,
/// authentication tag, and the provider key version used to encrypt the PII.
/// </summary>
public sealed class EncryptionMetadata
{
    /// <summary>96-bit IV, exactly 16 base64url characters.</summary>
    public required string Iv { get; set; }

    /// <summary>128-bit GCM authentication tag, exactly 22 base64url characters.</summary>
    public required string Tag { get; set; }

    /// <summary>
    /// Provider key version in <c>&lt;provider-id&gt;.&lt;n&gt;</c> format, where
    /// provider-id is your lowercase provider ID and n increments on each key
    /// rotation, starting at 1. Verifiabl looks up the matching encryption key by
    /// this value at verification time. Note this provider ID is distinct from
    /// your OAuth client ID.
    /// </summary>
    public required string KeyVersion { get; set; }
}
