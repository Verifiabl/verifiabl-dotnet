namespace Verifiabl.Verifier;

/// <summary>
/// Base type for every failure a <see cref="VerifierClient"/> reports, so one
/// <c>catch (VerifierException)</c> covers them all.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="VerifierRejectedException"/> means the barcode itself failed
/// verification. Every other subtype is an operational failure; check
/// <see cref="IsRetryable"/> to decide whether to try again later. Do not put
/// payloads, payslip data or credentials in custom messages or causes. A null
/// barcode throws <see cref="ArgumentNullException"/>, and a cancellation you
/// requested throws <see cref="OperationCanceledException"/>.
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

    /// <summary>
    /// Whether the same call may succeed later, such as after an outage or rate
    /// limit. This does not mean the previous attempt did not complete or that
    /// replay cannot produce additional verification events. Verification requests
    /// may already have retried up to <see cref="VerifierClientOptions.MaxRetries"/>
    /// times; token-endpoint failures are not automatically retried.
    /// </summary>
    public abstract bool IsRetryable { get; }

    /// <summary>Request ID to quote to Verifiabl support, when a response carried one.</summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Server-requested delay before another attempt, measured when the response
    /// was handled. Null when no positive, valid Retry-After was supplied.
    /// This is not permission to retry: also check <see cref="IsRetryable"/> and
    /// your deadline.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }
}
