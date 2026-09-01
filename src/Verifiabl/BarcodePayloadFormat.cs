namespace Verifiabl;

/// <summary>Printed barcode and XMP payload format.</summary>
public enum BarcodePayloadFormat
{
    /// <summary>Permanent legacy base64url format, retained for rollback.</summary>
    V1 = 1,

    /// <summary>Default short-host format with RFC 4648 Base32 ciphertext.</summary>
    V2 = 2,
}
