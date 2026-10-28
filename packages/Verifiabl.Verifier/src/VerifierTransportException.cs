namespace Verifiabl.Verifier;

/// <summary>
/// Thrown when the request fails before a response arrives, or a 2xx response
/// is not the shape the SDK expects. The cause is in <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class VerifierTransportException : VerifierException
{
    /// <summary>Create a retryable failure for an adapter or test fake. Keep messages free of sensitive data.</summary>
    public VerifierTransportException(string message)
        : this(message, null, isRetryable: true)
    {
    }

    /// <summary>Create a retryable failure for an adapter or test fake. Keep messages and causes free of sensitive data.</summary>
    public VerifierTransportException(string message, Exception? innerException)
        : this(message, innerException, isRetryable: true)
    {
    }

    /// <summary>Create an instance for an adapter or test fake. Keep messages and causes free of sensitive data.</summary>
    public VerifierTransportException(string message, Exception? innerException, bool isRetryable)
        : base(message, innerException)
    {
        IsRetryable = isRetryable;
    }

    /// <summary>HTTP status when a response arrived but could not be parsed; otherwise null.</summary>
    public int? Status { get; init; }

    /// <summary>
    /// True for network faults. False when a 2xx response was malformed: the API
    /// answered, so a retry repeats a verification and likely the same response.
    /// </summary>
    public override bool IsRetryable { get; }
}
