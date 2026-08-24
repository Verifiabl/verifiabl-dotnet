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
/// <see cref="VerifiablCrypto.EncryptPii"/> draws a fresh IV on every call, so
/// this occurs when stored encryption metadata is sent again with new content.
/// </para>
/// <para>
/// The SDK does not re-encrypt and retry for you. Encrypt the payslip again,
/// resend the record with the new encryption metadata, and rebuild any barcode
/// that you rendered from the previous ciphertext. Resending the record
/// unchanged gives the same result.
/// </para>
/// </remarks>
public sealed class VerifiablIvReuseException : VerifiablApiException
{
    private const string RemedyMessage =
        "The iv in the record's encryption metadata is already registered to your " +
        "issuer. Encrypt the payslip again with VerifiablCrypto.EncryptPii to get a " +
        "new iv, then resend the record with the new encryption metadata. Rebuild " +
        "any barcode that you rendered from the previous ciphertext. Resending the " +
        "record unchanged gives the same result.";

    internal VerifiablIvReuseException(int status, VerifiablErrorBody? body, string? requestId)
        : base(status, body, requestId, RemedyMessage)
    {
    }
}
