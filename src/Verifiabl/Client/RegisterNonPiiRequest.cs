namespace Verifiabl.Client;

/// <summary>
/// Request for <see cref="IVerifiablClient.RegisterNonPiiAsync"/>. The encrypted
/// PII stays with you and goes into a locally generated barcode; only non-PII
/// data and decryption metadata are sent.
/// </summary>
public sealed class RegisterNonPiiRequest
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
}
