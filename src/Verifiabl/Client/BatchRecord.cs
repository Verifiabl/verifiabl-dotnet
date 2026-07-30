namespace Verifiabl.Client;

/// <summary>
/// One record in a <see cref="IVerifiablClient.RegisterNonPiiBatchAsync"/> request:
/// the same fields as <see cref="RegisterNonPiiRequest"/> plus a provider-generated
/// Verifiabl reference from <see cref="Verifiabl.VerifiablReference.Generate"/>.
/// </summary>
public sealed class BatchRecord
{
    /// <summary>Provider-generated Verifiabl reference for this record.</summary>
    public required string VerifiablReference { get; set; }

    /// <summary>
    /// Optional caller-supplied correlation id. The API echoes it back on the
    /// matching result and never stores it, so you can line up results (and
    /// error logs) with your own payslip records by your own id.
    /// </summary>
    public string? ExternalId { get; set; }

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
