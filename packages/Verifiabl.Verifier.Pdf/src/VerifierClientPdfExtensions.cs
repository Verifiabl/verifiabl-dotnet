namespace Verifiabl.Verifier.Pdf;

/// <summary>Verifies a payslip PDF from its metadata payload, or its QR code when it has none.</summary>
public static class VerifierClientPdfExtensions
{
    /// <summary>
    /// Read the PDF's metadata payload and verify it with
    /// <see cref="IVerifierClient.VerifyAsync"/>. The PDF's images are read for a
    /// QR code only when there is no metadata payload.
    /// </summary>
    /// <param name="client">Any verifier client, including a fake or decorator.</param>
    /// <param name="pdf">The PDF bytes, at most <see cref="PayslipPdfReader.MaxPdfBytes"/>.</param>
    /// <param name="options">Optional settings, such as a JPEG decoder or time budget.</param>
    /// <param name="idempotencyKey">
    /// The key of an earlier attempt to verify this PDF, from
    /// <see cref="VerifierException.IdempotencyKey"/> or one you saved before the
    /// first attempt, or null for a new verification. See
    /// <see cref="VerificationRequest.IdempotencyKey"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels reading and verification.</param>
    /// <returns>The verified payslip and the payload it was read from.</returns>
    /// <remarks>
    /// <para>
    /// Each call without <paramref name="idempotencyKey"/> is a new verification
    /// with a new key. To retry a failed call without a second charge, pass the
    /// failure's <see cref="VerifierException.IdempotencyKey"/> within 1 hour.
    /// </para>
    /// <para>
    /// Reading takes up to <see cref="PdfReadOptions.Timeout"/> and verification
    /// up to the client's own timeout, so the call can take their sum.
    /// </para>
    /// </remarks>
    /// <exception cref="PayloadExtractionException">
    /// No single payload could be read. Nothing was sent to Verifiabl.
    /// </exception>
    /// <exception cref="VerifierException">The payload could not be verified, or verification could not be completed; see <see cref="IVerifierClient.VerifyAsync"/>.</exception>
    public static async Task<PdfVerificationResult> VerifyPdfAsync(
        this IVerifierClient client,
        byte[] pdf,
        PdfReadOptions? options = null,
        Guid? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        PdfPayloads payloads = await PayslipPdfReader.ReadAsync(pdf, options, cancellationToken).ConfigureAwait(false);
        return await VerifyAsync(client, payloads, idempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read a PDF stream's metadata payload, or its QR code when it has none, and
    /// verify it. The stream is read to its end and not disposed.
    /// </summary>
    /// <inheritdoc cref="VerifyPdfAsync(IVerifierClient, byte[], PdfReadOptions?, Guid?, CancellationToken)"/>
    public static async Task<PdfVerificationResult> VerifyPdfAsync(
        this IVerifierClient client,
        Stream pdf,
        PdfReadOptions? options = null,
        Guid? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        PdfPayloads payloads = await PayslipPdfReader.ReadAsync(pdf, options, cancellationToken).ConfigureAwait(false);
        return await VerifyAsync(client, payloads, idempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PdfVerificationResult> VerifyAsync(
        IVerifierClient client,
        PdfPayloads payloads,
        Guid? idempotencyKey,
        CancellationToken cancellationToken)
    {
        string payload = payloads.Payload ?? throw NotFound(payloads);
        VerificationRequest request = idempotencyKey is Guid key
            ? new VerificationRequest(payload) { IdempotencyKey = key }
            : new VerificationRequest(payload);
        VerificationResult verified = await client.VerifyAsync(request, cancellationToken).ConfigureAwait(false);
        return new PdfVerificationResult(verified, payloads);
    }

    private static PayloadExtractionException NotFound(PdfPayloads payloads)
    {
        if (!payloads.Opened)
        {
            return new PayloadExtractionException(
                PayloadExtractionFailure.Unreadable,
                "The PDF could not be parsed and has no Verifiabl payload in its metadata.");
        }

        string message = payloads.SkippedImages > 0
            ? $"The PDF has no Verifiabl payload in its metadata or readable images; {payloads.SkippedImages} image(s) could not be read. Set PdfReadOptions.JpegDecoder to read JPEG images."
            : "The PDF has no Verifiabl payload in its metadata or images.";
        return new PayloadExtractionException(PayloadExtractionFailure.NotFound, message);
    }
}
