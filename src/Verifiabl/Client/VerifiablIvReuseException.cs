namespace Verifiabl.Client;

/// <summary>
/// Thrown when a single registration is rejected because the record's
/// encryption IV is already registered to your issuer. Derives from
/// <see cref="VerifiablApiException"/> with <see cref="VerifiablApiException.Code"/>
/// of <see cref="VerifiablErrorCodes.IvReused"/>, so an existing
/// <c>catch (VerifiablApiException)</c> still covers it.
/// </summary>
/// <remarks>
/// <para>
/// AES-256-GCM requires a unique IV for every record encrypted under one key, so
/// this reports a fault in the calling integration rather than a transient
/// failure. <see cref="VerifiablCrypto.EncryptPii"/> draws a fresh IV on every
/// call, so reaching this means the integration replayed encryption metadata
/// across records, for example by storing it and sending it again with new
/// content.
/// </para>
/// <para>
/// The SDK deliberately does not re-encrypt and retry for you. Resending the same
/// request cannot succeed, and re-encrypting behind your back would hide a broken
/// integration that keeps producing colliding IVs. Encrypt the payslip again,
/// resend the record with the new encryption metadata, and rebuild any barcode
/// already rendered from the previous ciphertext.
/// </para>
/// </remarks>
public sealed class VerifiablIvReuseException : VerifiablApiException
{
    private const string RemedyMessage =
        "The iv in the record's encryption metadata is already registered to your " +
        "issuer. AES-256-GCM requires a unique iv for every record encrypted under " +
        "one key, so encrypt the payslip again with VerifiablCrypto.EncryptPii, " +
        "which draws a fresh iv on every call, and resend the record with the new " +
        "encryption metadata. A barcode already rendered from the previous " +
        "ciphertext must be rebuilt from the new one. Resending the record " +
        "unchanged gives the same result.";

    internal VerifiablIvReuseException(int status, VerifiablErrorBody? body, string? requestId)
        : base(status, body, requestId, RemedyMessage)
    {
    }
}
