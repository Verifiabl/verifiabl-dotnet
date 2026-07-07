namespace Verifiabl;

/// <summary>
/// One record in a <see cref="VerifiablClient.RegisterNonPiiBatchAsync"/> request:
/// the same fields as <see cref="RegisterNonPiiRequest"/> plus a provider-generated
/// Verifiabl reference from <see cref="VerifiablReference.Generate"/>.
/// </summary>
public sealed class BatchRecord
{
    /// <summary>Provider-generated Verifiabl reference for this record.</summary>
    public string? VerifiablReference { get; set; }

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
}
