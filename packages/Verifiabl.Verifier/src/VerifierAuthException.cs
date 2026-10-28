using Verifiabl.Verifier.Internal;

namespace Verifiabl.Verifier;

/// <summary>
/// Thrown when authentication could not be completed: the OAuth token request
/// failed, or the verification API answered 401 or 403. Check
/// <see cref="VerifierException.IsRetryable"/> first; for non-retryable
/// failures, check credentials, environment and permissions.
/// </summary>
public sealed class VerifierAuthException : VerifierException
{
    /// <summary>Create an instance for an adapter or test fake. Keep messages free of sensitive data.</summary>
    public VerifierAuthException(string message)
        : base(message)
    {
    }

    /// <summary>Create an instance for an adapter or test fake. Keep messages and causes free of sensitive data.</summary>
    public VerifierAuthException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>HTTP status of the failed response, when there was one.</summary>
    public int? Status { get; init; }

    /// <summary>
    /// <see cref="VerifierErrorCodes.Unauthorized"/> or <see cref="VerifierErrorCodes.Forbidden"/>
    /// when the verification API refused the call. Null for token endpoint failures.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// True when the token endpoint was unreachable, sent an unreadable response
    /// or returned 408, 429 or 5xx. Other statuses mean the credentials,
    /// permissions or token URL need fixing.
    /// </summary>
    public override bool IsRetryable => Status is not int status || ApiTransport.IsRetryableStatus(status);
}
