namespace Verifiabl.Verifier;

/// <summary>
/// Verifies an already-extracted payload with the Verifiabl API. Depend on this
/// interface to substitute a fake or decorate the client.
/// </summary>
public interface IVerifierClient
{
    /// <summary>Return issuer-authenticated payslip data for an extracted payload.</summary>
    /// <param name="barcode">
    /// Text already extracted from a QR code or PDF metadata: a scan link such as
    /// <c>https://v.verifiabl.io/v/…#2.…</c>, or a bare payload such as <c>2|…|…</c>.
    /// Pass it unchanged; do not trim, decode or fetch the link.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The verified data, not an assessment of the document's visible contents.
    /// Your integration decides how to interpret, compare or map it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> is null.</exception>
    /// <exception cref="VerifierRejectedException">
    /// The barcode failed verification, including empty or over-long text. Treat
    /// the document as unverified.
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
    Task<VerificationResult> VerifyAsync(string barcode, CancellationToken cancellationToken = default);
}
