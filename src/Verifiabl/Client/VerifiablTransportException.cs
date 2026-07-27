namespace Verifiabl.Client;

/// <summary>
/// Thrown when a call never produced a usable response: a network fault (with
/// the underlying <see cref="System.Net.Http.HttpRequestException"/> as
/// <see cref="Exception.InnerException"/>), or a 2xx response whose body was
/// empty, not JSON, or not the shape this SDK version expects.
/// </summary>
/// <remarks>
/// For a retryable call this is only raised once the retries are exhausted.
/// </remarks>
public sealed class VerifiablTransportException : VerifiablException
{
    internal VerifiablTransportException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
