namespace Verifiabl.Verifier;

/// <summary>
/// Verifies an already-extracted payload with the Verifiabl API. Depend on this
/// interface to substitute a fake or decorate the client.
/// </summary>
/// <remarks>
/// The interface has one method, so fakes and decorators stay small. Later
/// versions add per-verification details to <see cref="VerificationRequest"/>,
/// not new members here. To retry a verification, send the same
/// <see cref="VerificationRequest"/> again, so every attempt carries its
/// idempotency key.
/// </remarks>
public interface IVerifierClient
{
    /// <summary>Return the payslip data the issuer registered for an extracted payload.</summary>
    /// <param name="request">The extracted payload to verify.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The verified data, not an assessment of the document's visible contents.
    /// Your integration decides how to interpret, compare or map it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="VerifierRejectedException">
    /// The barcode could not be verified, including empty or over-long text.
    /// </exception>
    /// <exception cref="VerifierAuthException">
    /// Authentication could not be completed. Check
    /// <see cref="VerifierException.IsRetryable"/> before changing credentials.
    /// </exception>
    /// <exception cref="VerifierServiceException">Verifiabl returned an error, such as an outage or rate limit.</exception>
    /// <exception cref="VerifierException">
    /// Any failure, including timeouts and transport faults. Check
    /// <see cref="VerifierException.IsRetryable"/>.
    /// </exception>
    Task<VerificationResult> VerifyAsync(VerificationRequest request, CancellationToken cancellationToken = default);
}
