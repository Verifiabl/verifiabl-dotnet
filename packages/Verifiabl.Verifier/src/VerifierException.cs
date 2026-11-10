namespace Verifiabl.Verifier;

/// <summary>
/// Base type for every failure a <see cref="VerifierClient"/> or a Verifiabl
/// extraction package reports, so one <c>catch (VerifierException)</c> covers them all.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="VerifierRejectedException"/> means the payload could not be
/// verified. <see cref="PayloadExtractionException"/> means no payload could
/// be read from a document, so nothing was verified. Every other subtype is an
/// operational failure; check <see cref="IsRetryable"/> to decide whether to try
/// again later. Do not put payloads, payslip data or credentials in custom
/// messages or causes. A null request throws <see cref="ArgumentNullException"/>,
/// and a cancellation you requested throws <see cref="OperationCanceledException"/>.
/// </para>
/// <para>
/// The set of subtypes is closed: extraction packages throw
/// <see cref="PayloadExtractionException"/> rather than defining their own, so
/// the catch sequence in the README stays complete.
/// </para>
/// <para>
/// Public constructors take only the values that define a failure. Set optional
/// diagnostics such as <see cref="RequestId"/> with an object initializer, so new
/// diagnostics never change a constructor signature.
/// </para>
/// </remarks>
public abstract class VerifierException : Exception
{
    private protected VerifierException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    private Guid? _idempotencyKey;

    /// <summary>
    /// Whether the same call may succeed later, such as after an outage or rate
    /// limit. It does not show whether the previous attempt completed. To retry,
    /// send the same <see cref="VerificationRequest"/> again, so Verifiabl does
    /// not charge twice. Verification requests may already have retried up to
    /// <see cref="VerifierClientOptions.MaxRetries"/> times; token-endpoint
    /// failures are not automatically retried.
    /// </summary>
    public abstract bool IsRetryable { get; }

    /// <summary>
    /// The API response's <c>X-Request-Id</c>, from the last attempt when the call
    /// was retried. Quote it to Verifiabl support. Null when no API response was
    /// handled, such as for token-endpoint and network failures.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Server-requested delay before another attempt, measured when the response
    /// was handled. Null when no positive, valid Retry-After was supplied.
    /// This is not permission to retry: also check <see cref="IsRetryable"/> and
    /// your deadline.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>
    /// The <see cref="VerificationRequest.IdempotencyKey"/> of the failed call.
    /// The verification can complete even when the call fails. Quote the key
    /// when you ask Verifiabl about the verification or its charge. Null when
    /// the SDK did not start a verification call: for a
    /// <see cref="PayloadExtractionException"/>, and when
    /// <see cref="VerifierClient"/> rejects empty or over-long text.
    /// </summary>
    public Guid? IdempotencyKey
    {
        get => _idempotencyKey;
        init => _idempotencyKey = value;
    }

    // The transport creates exceptions without the request, so the client adds the key.
    internal void RecordIdempotencyKey(Guid key) => _idempotencyKey = key;
}
