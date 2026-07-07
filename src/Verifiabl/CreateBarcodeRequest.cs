namespace Verifiabl;

/// <summary>
/// Request for <see cref="VerifiablClient.CreateBarcodeAsync"/>. This API-managed
/// flow also sends the ciphertext, and the server returns a ready-made barcode image.
/// </summary>
public sealed class CreateBarcodeRequest
{
    /// <summary>Payslip schema identifier, e.g. "au.payslip.v1".</summary>
    public string? Schema { get; set; }

    /// <summary>
    /// When the payslip was issued. Sent to the API as an ISO 8601 UTC timestamp;
    /// values with an offset are converted to UTC first.
    /// </summary>
    public DateTimeOffset? IssuedAt { get; set; }

    /// <summary>Non-PII payslip data.</summary>
    public PayslipNonPii? PayslipNonPii { get; set; }

    /// <summary>Decryption metadata from <see cref="VerifiablCrypto.EncryptPii"/>.</summary>
    public EncryptionMetadata? EncryptionMetadata { get; set; }

    /// <summary>Base64url AES-256-GCM ciphertext of the formatted PII plaintext.</summary>
    public string? EncryptedPii { get; set; }
}
