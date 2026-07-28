namespace Verifiabl;

/// <summary>
/// Base type for every failure a Verifiabl API call can report, so one
/// <c>catch (VerifiablException)</c> covers the whole surface.
/// </summary>
/// <remarks>
/// Caller mistakes still surface as <see cref="ArgumentException"/> before
/// anything is sent, and a cancellation you requested still surfaces as
/// <see cref="OperationCanceledException"/>; neither is a Verifiabl failure.
/// </remarks>
public abstract class VerifiablException : Exception
{
    private protected VerifiablException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
