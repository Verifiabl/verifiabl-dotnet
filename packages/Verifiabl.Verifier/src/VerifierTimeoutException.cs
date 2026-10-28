namespace Verifiabl.Verifier;

/// <summary>
/// Thrown when a verification exceeds <see cref="VerifierClientOptions.Timeout"/>,
/// including token fetches and retries.
/// </summary>
public sealed class VerifierTimeoutException : VerifierException
{
    /// <summary>Create an instance for an adapter or test fake. Keep messages and causes free of sensitive data.</summary>
    public VerifierTimeoutException(string message, TimeSpan timeout)
        : base(message)
    {
        Timeout = timeout;
    }

    /// <summary>The deadline that was exceeded.</summary>
    public TimeSpan Timeout { get; }

    /// <inheritdoc />
    public override bool IsRetryable => true;
}
