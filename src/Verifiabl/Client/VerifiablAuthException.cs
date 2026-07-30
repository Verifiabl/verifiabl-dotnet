namespace Verifiabl.Client;

/// <summary>
/// Thrown when an OAuth access token cannot be obtained. <see cref="Status"/> is
/// the HTTP status returned by the token endpoint, or null when the request
/// itself failed or the response was unparseable.
/// </summary>
public sealed class VerifiablAuthException : VerifiablException
{
    internal VerifiablAuthException(string message, int? status = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Status = status;
    }

    /// <summary>HTTP status returned by the token endpoint, when one was received.</summary>
    public int? Status { get; }
}
