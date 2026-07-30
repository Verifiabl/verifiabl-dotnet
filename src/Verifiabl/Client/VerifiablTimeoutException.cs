namespace Verifiabl.Client;

/// <summary>
/// Thrown when an API call exceeds <see cref="VerifiablClientOptions.Timeout"/>.
/// The deadline covers the whole operation: token fetch, request, the 401
/// refresh, and every retry with its backoff.
/// </summary>
/// <remarks>
/// A cancellation you requested surfaces as <see cref="OperationCanceledException"/>
/// instead, so the two are never confused.
/// </remarks>
public sealed class VerifiablTimeoutException : VerifiablException
{
    internal VerifiablTimeoutException(string message, TimeSpan timeout)
        : base(message)
    {
        Timeout = timeout;
    }

    /// <summary>The configured per-call timeout that was exceeded.</summary>
    public TimeSpan Timeout { get; }
}
