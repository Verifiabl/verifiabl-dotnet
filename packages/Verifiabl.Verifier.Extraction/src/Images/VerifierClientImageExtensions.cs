namespace Verifiabl.Verifier.Extraction.Images;

/// <summary>Verifies a payslip from an image file, such as a photo or screenshot of its QR code.</summary>
public static class VerifierClientImageExtensions
{
    /// <summary>
    /// Read the Verifiabl QR code in a PNG, JPEG or WebP file and verify it with
    /// <see cref="IVerifierClient.VerifyAsync"/>.
    /// </summary>
    /// <param name="client">Any verifier client, including a fake or decorator.</param>
    /// <param name="file">The image file's bytes, at most <see cref="PayslipImageReader.MaxFileBytes"/>.</param>
    /// <param name="options">Optional settings, such as the time budget.</param>
    /// <param name="idempotencyKey">
    /// The key of an earlier attempt to verify this image, from
    /// <see cref="VerifierException.IdempotencyKey"/> or one you saved before the
    /// first attempt, or null for a new verification. See
    /// <see cref="VerificationRequest.IdempotencyKey"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels reading and verification.</param>
    /// <returns>The verified payslip and the QR code it was read from.</returns>
    /// <remarks>
    /// <para>
    /// Each call without <paramref name="idempotencyKey"/> is a new verification
    /// with a new key. To retry a failed call without a second charge, pass the
    /// failure's <see cref="VerifierException.IdempotencyKey"/> within 1 hour.
    /// </para>
    /// <para>
    /// Reading takes up to <see cref="ImageReadOptions.Timeout"/> and verification
    /// up to the client's own timeout, so the call can take their sum.
    /// </para>
    /// </remarks>
    /// <exception cref="PayloadExtractionException">
    /// No single Verifiabl QR code could be read. Nothing was sent to Verifiabl.
    /// </exception>
    /// <exception cref="VerifierException">The payload could not be verified, or verification could not be completed; see <see cref="IVerifierClient.VerifyAsync"/>.</exception>
    /// <exception cref="InvalidOperationException">Wasmtime's native library could not be loaded.</exception>
    public static async Task<ImageVerificationResult> VerifyImageAsync(
        this IVerifierClient client,
        byte[] file,
        ImageReadOptions? options = null,
        Guid? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        ImagePayload payload = await PayslipImageReader.ReadAsync(file, options, cancellationToken).ConfigureAwait(false);
        return await VerifyAsync(client, payload, idempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read the Verifiabl QR code in an image stream and verify it. The stream is
    /// read to its end and not disposed.
    /// </summary>
    /// <inheritdoc cref="VerifyImageAsync(IVerifierClient, byte[], ImageReadOptions?, Guid?, CancellationToken)"/>
    public static async Task<ImageVerificationResult> VerifyImageAsync(
        this IVerifierClient client,
        Stream file,
        ImageReadOptions? options = null,
        Guid? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        ImagePayload payload = await PayslipImageReader.ReadAsync(file, options, cancellationToken).ConfigureAwait(false);
        return await VerifyAsync(client, payload, idempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ImageVerificationResult> VerifyAsync(
        IVerifierClient client,
        ImagePayload payload,
        Guid? idempotencyKey,
        CancellationToken cancellationToken)
    {
        string qrCode = payload.QrCode ?? throw new PayloadExtractionException(
            PayloadExtractionFailure.NotFound,
            "The image has no readable Verifiabl QR code.");
        VerificationRequest request = idempotencyKey is Guid key
            ? new VerificationRequest(qrCode) { IdempotencyKey = key }
            : new VerificationRequest(qrCode);
        VerificationResult verified = await client.VerifyAsync(request, cancellationToken).ConfigureAwait(false);
        return new ImageVerificationResult(verified, payload);
    }
}
