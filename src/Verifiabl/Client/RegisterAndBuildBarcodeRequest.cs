namespace Verifiabl.Client;

/// <summary>
/// Request for <see cref="IVerifiablClient.RegisterAndBuildBarcodeAsync"/>. This API-managed
/// flow also sends the ciphertext, and the server returns a ready-made barcode image.
/// </summary>
public sealed class RegisterAndBuildBarcodeRequest
{
    /// <summary>Payslip schema identifier, e.g. "au.payslip.v1".</summary>
    public required string Schema { get; set; }

    /// <summary>
    /// When the payslip was issued. Sent to the API as an ISO 8601 UTC timestamp;
    /// values with an offset are converted to UTC first.
    /// </summary>
    public required DateTimeOffset IssuedAt { get; set; }

    /// <summary>Non-PII payslip data.</summary>
    public required PayslipNonPii PayslipNonPii { get; set; }

    /// <summary>Decryption metadata from <see cref="VerifiablCrypto.EncryptPii"/>.</summary>
    public required EncryptionMetadata EncryptionMetadata { get; set; }

    /// <summary>Base64url AES-256-GCM ciphertext of the formatted PII plaintext.</summary>
    public required string EncryptedPii { get; set; }
}
