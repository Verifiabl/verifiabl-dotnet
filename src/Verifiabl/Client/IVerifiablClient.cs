namespace Verifiabl.Client;

/// <summary>
/// The Verifiabl issuer API. Implemented by <see cref="VerifiablClient"/>; take a
/// dependency on this interface so tests can substitute a fake.
/// </summary>
/// <remarks>
/// <para>
/// Every failure these methods report derives from <see cref="VerifiablException"/>,
/// so one <c>catch (VerifiablException)</c> covers API errors, auth failures,
/// timeouts, and transport faults. Invalid arguments still throw
/// <see cref="ArgumentException"/> before anything is sent, and cancelling the
/// token you passed still throws <see cref="OperationCanceledException"/>.
/// </para>
/// <para>
/// Implementations cache OAuth tokens, so register a single instance and reuse it.
/// </para>
/// </remarks>
public interface IVerifiablClient
{
    /// <summary>
    /// Register non-PII payslip data and decryption metadata. Returns the
    /// Verifiabl reference to embed in a locally generated barcode.
    /// </summary>
    /// <param name="request">The payslip registration.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException">The request is incomplete or malformed.</exception>
    /// <exception cref="VerifiablApiException">The API returned a non-2xx response.</exception>
    /// <exception cref="VerifiablAuthException">An OAuth token could not be obtained.</exception>
    /// <exception cref="VerifiablTimeoutException">The call exceeded the configured timeout.</exception>
    /// <exception cref="VerifiablTransportException">A network fault prevented a response, or the response was not usable JSON.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<RegisterNonPiiResponse> RegisterNonPiiAsync(
        RegisterNonPiiRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Register non-PII payslip data and have the API build the barcode. Sends the
    /// encrypted PII alongside the non-PII data.
    /// </summary>
    /// <param name="request">The payslip registration, including the ciphertext.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException">The request is incomplete or malformed.</exception>
    /// <exception cref="VerifiablApiException">The API returned a non-2xx response.</exception>
    /// <exception cref="VerifiablAuthException">An OAuth token could not be obtained.</exception>
    /// <exception cref="VerifiablTimeoutException">The call exceeded the configured timeout.</exception>
    /// <exception cref="VerifiablTransportException">A network fault prevented a response, or the response was not usable JSON.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<RegisterAndBuildBarcodeResponse> RegisterAndBuildBarcodeAsync(
        RegisterAndBuildBarcodeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Register a batch of non-PII payslip records in a single request, up to
    /// <see cref="VerifiablClient.MaxBatchRecords"/> records. Each record carries a
    /// provider-generated Verifiabl reference (from
    /// <see cref="Verifiabl.VerifiablReference.Generate"/>) and the same fields as
    /// <see cref="RegisterNonPiiAsync"/>. The response contains a per-record
    /// result index-aligned to the input: one bad record never fails the batch.
    /// </summary>
    /// <param name="records">The records to register.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="records"/> is null.</exception>
    /// <exception cref="ArgumentException">The batch is empty, oversized, or a record is malformed.</exception>
    /// <exception cref="VerifiablApiException">The API returned a non-2xx response.</exception>
    /// <exception cref="VerifiablAuthException">An OAuth token could not be obtained.</exception>
    /// <exception cref="VerifiablTimeoutException">The call exceeded the configured timeout.</exception>
    /// <exception cref="VerifiablTransportException">A network fault prevented a response, or the response was not usable JSON.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<RegisterNonPiiBatchResponse> RegisterNonPiiBatchAsync(
        IEnumerable<BatchRecord> records,
        CancellationToken cancellationToken = default);
}
