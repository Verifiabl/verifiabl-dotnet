namespace Verifiabl.Verifier;

/// <summary>Conveniences for any <see cref="IVerifierClient"/>, including fakes and decorators.</summary>
public static class VerifierClientExtensions
{
    /// <summary>Verify barcode text without building a <see cref="VerificationRequest"/>.</summary>
    /// <param name="client">Any verifier client.</param>
    /// <param name="barcode">
    /// Text already extracted from a QR code or PDF metadata. See
    /// <see cref="VerificationRequest(string)"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The verified data. See <see cref="IVerifierClient.VerifyAsync"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="barcode"/> is null.</exception>
    /// <exception cref="VerifierException">Verification failed. See <see cref="IVerifierClient.VerifyAsync"/>.</exception>
    public static Task<VerificationResult> VerifyAsync(
        this IVerifierClient client,
        string barcode,
        CancellationToken cancellationToken = default)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        return client.VerifyAsync(new VerificationRequest(barcode), cancellationToken);
    }
}
